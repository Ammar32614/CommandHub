using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CommandHub.Models;
using CommandHub.Planner;

namespace CommandHub.Tiles.UpNext;

public sealed class UpNextTile : ITileType
{
    public string TypeId => "upnext";
    public string DisplayName => "Up next";
    public string Icon => "";
    public IReadOnlyList<TileSize> AllowedSizes { get; } = [TileSize.Wide, TileSize.Tall, TileSize.Large];
    public TileSize DefaultSize => TileSize.Wide;
    public string AccentKey => "AccentBrush";
    public FrameworkElement CreateView(TileContext ctx) => new UpNextView();
}

/// <summary>The next few things from the Planner (overdue first). Click to open the Planner.</summary>
public sealed class UpNextView : UserControl
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(5) };

    public UpNextView()
    {
        _timer.Tick += (_, _) => Render();
        Loaded += (_, _) => { App.Saved += Render; _timer.Start(); Render(); };
        Unloaded += (_, _) => { App.Saved -= Render; _timer.Stop(); };
        Render();
    }

    private void Render()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var next = PlannerLogic.UpNext(App.Data?.Planner ?? [], today, 21);
        var root = new Grid();
        var open = TileUi.EditButton("Open planner");
        open.Content = "";
        open.Click += (_, _) => (Application.Current.MainWindow as MainWindow)?.ShowPlanner();

        var list = new StackPanel();
        if (next.Count == 0)
        {
            var empty = TileUi.Text("Nothing coming up. Add classes, deadlines and exams in the Planner.", 12, "Muted");
            empty.TextWrapping = TextWrapping.Wrap;
            empty.TextTrimming = TextTrimming.None;
            list.Children.Add(empty);
        }
        foreach (var o in next.Take(6))
        {
            bool overdue = PlannerLogic.IsOverdue(o, today);
            var bar = new Border { Width = 3, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 1, 8, 1) };
            bar.SetResourceReference(Border.BackgroundProperty, overdue ? "Danger" : Planner.PlannerPage.KindBrush(o.Item.Kind));
            var right = TileUi.Text(overdue ? "Overdue" : PlannerLogic.When(o.Date, today), 11, overdue ? "Danger" : "Muted");
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };
            DockPanel.SetDock(bar, Dock.Left);
            DockPanel.SetDock(right, Dock.Right);
            row.Children.Add(bar);
            row.Children.Add(right);
            var title = o.Item.Start is { } s ? $"{o.Item.Title}  ·  {PlannerLogic.FormatTime(s)}" : o.Item.Title;
            row.Children.Add(TileUi.Text(title, 12));
            list.Children.Add(row);
        }
        root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false });
        root.Children.Add(open);
        Content = root;
    }
}
