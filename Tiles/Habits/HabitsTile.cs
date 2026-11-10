using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CommandHub.Models;

namespace CommandHub.Tiles.Habits;

public sealed class HabitsTile : ITileType
{
    public string TypeId => "habits";
    public string DisplayName => "Habits";
    public string Icon => "";
    public IReadOnlyList<TileSize> AllowedSizes { get; } = [TileSize.Wide, TileSize.Tall, TileSize.Large];
    public TileSize DefaultSize => TileSize.Tall;
    public string AccentKey => "Accent2Brush";
    public FrameworkElement CreateView(TileContext ctx) => new HabitsView(ctx);
}

/// <summary>Each habit shows its last 7 days as dots (today on the right). Click a dot to tick or untick that day.</summary>
public sealed class HabitsView : UserControl
{
    private readonly TileContext _ctx;
    private readonly List<Habit> _habits;
    private readonly StackPanel _list = new();
    private readonly TextBox _add = TileUi.Field("Add a habit");
    private readonly DispatcherTimer _midnight = new() { Interval = TimeSpan.FromMinutes(10) };
    private DateOnly _shownFor;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    public HabitsView(TileContext ctx)
    {
        _ctx = ctx;
        _habits = HabitsContent.Read(ctx.Content);
        _add.Margin = new Thickness(0, 8, 0, 0);
        _add.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            var name = _add.Text.Trim();
            if (name.Length == 0) return;
            _habits.Add(new Habit(name, []));
            _add.Clear();
            Commit();
        };

        var dock = new DockPanel();
        DockPanel.SetDock(_add, Dock.Bottom);
        dock.Children.Add(_add);
        dock.Children.Add(new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false });
        Content = dock;

        // Roll the dots over at midnight.
        _midnight.Tick += (_, _) => { if (Today != _shownFor) Rebuild(); };
        Loaded += (_, _) => _midnight.Start();
        Unloaded += (_, _) => _midnight.Stop();
        Rebuild();
    }

    private void Commit()
    {
        HabitsContent.Write(_ctx.Content, _habits, Today);
        _ctx.SaveNow();
        Rebuild();
    }

    private void Rebuild()
    {
        _shownFor = Today;
        _list.Children.Clear();
        foreach (var habit in _habits.ToList())
        {
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 12), Background = System.Windows.Media.Brushes.Transparent };

            var head = new DockPanel();
            var delete = TileUi.IconButton("", $"Delete {habit.Name}", 20);
            delete.FontSize = 8;
            delete.Opacity = 0;
            // Two clicks: a habit can hold a year of ticks, so a stray click mustn't wipe it.
            bool armed = false;
            var disarm = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            disarm.Tick += (_, _) =>
            {
                disarm.Stop();
                armed = false;
                delete.Content = "";
                delete.Width = 20;
                delete.FontSize = 8;
                delete.ClearValue(Control.ForegroundProperty);
                delete.ClearValue(Control.FontFamilyProperty);
                delete.ToolTip = $"Delete {habit.Name}";
                System.Windows.Automation.AutomationProperties.SetName(delete, $"Delete {habit.Name}");
            };
            delete.Click += (_, _) =>
            {
                if (armed) { disarm.Stop(); _habits.Remove(habit); Commit(); return; }
                armed = true;
                delete.Content = "Delete?";
                delete.Width = double.NaN;
                delete.FontSize = 11;
                delete.FontFamily = SystemFonts.MessageFontFamily;
                delete.SetResourceReference(Control.ForegroundProperty, "Danger");
                delete.ToolTip = "Click again to delete this habit and its history";
                System.Windows.Automation.AutomationProperties.SetName(delete, $"Confirm delete {habit.Name}");
                disarm.Start();
            };
            delete.Unloaded += (_, _) => disarm.Stop();
            DockPanel.SetDock(delete, Dock.Right);
            int streak = HabitsContent.Streak(habit, Today);
            var streakText = TileUi.Text(streak > 0 ? $"{streak}d" : "", 11, "Accent2Brush", semibold: true);
            streakText.Margin = new Thickness(6, 0, 4, 0);
            streakText.ToolTip = streak == 1 ? "1-day streak" : $"{streak}-day streak";
            DockPanel.SetDock(streakText, Dock.Right);
            head.Children.Add(delete);
            head.Children.Add(streakText);
            head.Children.Add(TileUi.Text(habit.Name, 13, "Text"));
            row.Children.Add(head);

            var dots = new System.Windows.Controls.Primitives.UniformGrid { Columns = 7, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            for (int i = 6; i >= 0; i--)
            {
                var day = Today.AddDays(-i);
                bool done = habit.Done.Contains(day);
                var dot = new Border
                {
                    Width = 14, Height = 14, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 6, 0),
                    BorderThickness = new Thickness(i == 0 ? 2 : 1.5),
                };
                dot.SetResourceReference(Border.BorderBrushProperty, done || i == 0 ? "Accent2Brush" : "Strong");
                if (done) dot.SetResourceReference(Border.BackgroundProperty, "Accent2Brush");
                var label = day == Today ? "today" : day.ToString("ddd d MMM");
                var button = new Button
                {
                    Content = dot, Cursor = Cursors.Hand, Template = Bare, ToolTip = (done ? "Done " : "Not done ") + label,
                    Focusable = true,
                };
                button.SetResourceReference(FrameworkElement.FocusVisualStyleProperty, "FocusRing");
                System.Windows.Automation.AutomationProperties.SetName(button, $"{habit.Name} {label}: {(done ? "done" : "not done")}");
                button.Click += (_, _) => { HabitsContent.Toggle(habit, day); Commit(); };
                dots.Children.Add(button);
            }
            row.Children.Add(dots);

            void UpdateDelete() => delete.Opacity = armed || row.IsMouseOver || row.IsKeyboardFocusWithin ? 1 : 0;
            row.MouseEnter += (_, _) => UpdateDelete();
            row.MouseLeave += (_, _) => UpdateDelete();
            row.IsKeyboardFocusWithinChanged += (_, _) => UpdateDelete();
            _list.Children.Add(row);
        }
        if (_habits.Count == 0)
            _list.Children.Add(TileUi.Text("Add something small you want to do every day.", 12, "Muted"));
    }

    private static readonly ControlTemplate Bare = CreateBare();

    private static ControlTemplate CreateBare()
    {
        var t = new ControlTemplate(typeof(Button));
        t.VisualTree = new FrameworkElementFactory(typeof(ContentPresenter));
        return t;
    }
}
