using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CommandHub.Controls;
using CommandHub.Models;

namespace CommandHub.Tiles.Focus;

public sealed class FocusTile : ITileType
{
    public string TypeId => "focus";
    public string DisplayName => "Study today";
    public string Icon => "";
    public IReadOnlyList<TileSize> AllowedSizes { get; } = [TileSize.Small, TileSize.Tall, TileSize.Large];
    public TileSize DefaultSize => TileSize.Tall;
    public FrameworkElement CreateView(TileContext ctx) => new FocusView();
}

/// <summary>Reads Stint's log every minute: today's time against the goal, the streak, and the last 7 days.</summary>
public sealed class FocusView : UserControl
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(1) };

    public FocusView()
    {
        _timer.Tick += (_, _) => Update();
        Loaded += (_, _) => { Update(); _timer.Start(); };
        Unloaded += (_, _) => _timer.Stop();
        Update();
    }

    private void Update()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var stats = StintReader.ReadFile(StintReader.DefaultPath, today);
        if (stats is null)
        {
            var empty = TileUi.Text("Your study time from Stint will show up here once you log a session.", 12, "Muted");
            empty.TextWrapping = TextWrapping.Wrap;
            empty.TextTrimming = TextTrimming.None;
            Content = empty;
            return;
        }

        var stack = new StackPanel();

        double goalSeconds = stats.GoalMinutes * 60.0;
        var ring = new ProgressRing { Width = 104, Height = 104, Thickness = 9, Progress = stats.TodaySeconds / goalSeconds };
        ring.SetResourceReference(ProgressRing.ArcBrushProperty, "AccentBrush");
        ring.SetResourceReference(ProgressRing.TrackBrushProperty, "Raised");
        var center = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var time = TileUi.Display(TileUi.Duration(stats.TodaySeconds), 20, weight: FontWeights.Normal);
        time.HorizontalAlignment = HorizontalAlignment.Center;
        var goal = TileUi.Text("of " + TileUi.Duration((int)goalSeconds), 11, "Muted");
        goal.HorizontalAlignment = HorizontalAlignment.Center;
        center.Children.Add(time);
        center.Children.Add(goal);
        var ringHolder = new Grid { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 10) };
        ringHolder.Children.Add(ring);
        ringHolder.Children.Add(center);
        stack.Children.Add(ringHolder);

        var streak = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 12) };
        streak.Children.Add(TileUi.Icon("", 12, "AccentBrush"));
        var streakText = TileUi.Text(stats.Streak == 1 ? "1-day streak" : $"{stats.Streak}-day streak", 12, "Text");
        streakText.Margin = new Thickness(6, 0, 0, 0);
        streak.Children.Add(streakText);
        stack.Children.Add(streak);

        stack.Children.Add(WeekBars(stats.Week, today));
        Content = new Viewbox { Child = stack, StretchDirection = StretchDirection.DownOnly, VerticalAlignment = VerticalAlignment.Top };
    }

    /// <summary>Seven small bars, today highlighted, with day initials underneath.</summary>
    private static Grid WeekBars(IReadOnlyList<int> week, DateOnly today)
    {
        int max = Math.Max(1, week.Max());
        var grid = new Grid { Height = 64, Width = 140 };
        for (int i = 0; i < 7; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            bool isToday = i == 6;
            var bar = new Border
            {
                Width = 10, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Bottom,
                Height = Math.Max(3, 44.0 * week[i] / max), Margin = new Thickness(0, 0, 0, 16),
                Opacity = isToday ? 1 : 0.55,
            };
            bar.SetResourceReference(Border.BackgroundProperty, week[i] > 0 ? "AccentBrush" : "Raised");
            bar.ToolTip = TileUi.Duration(week[i]);
            Grid.SetColumn(bar, i);
            grid.Children.Add(bar);

            var day = TileUi.Text(today.AddDays(i - 6).DayOfWeek.ToString()[..1], 10, isToday ? "Text" : "Muted");
            day.HorizontalAlignment = HorizontalAlignment.Center;
            day.VerticalAlignment = VerticalAlignment.Bottom;
            Grid.SetColumn(day, i);
            grid.Children.Add(day);
        }
        return grid;
    }
}
