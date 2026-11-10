using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;
using CommandHub.Home;

namespace CommandHub.Study;

/// <summary>
/// The Study tab: analytics over Stint's log. Reads only (Stint owns the file) and refreshes when Stint saves.
/// Course = Stint subject; task = what was typed in "What are you working on?".
/// </summary>
public sealed class StudyPage : UserControl
{
    private readonly Border _base = new(), _glow = new() { IsHitTestVisible = false };
    private readonly ScrollViewer _scroll;
    private readonly StackPanel _content = new() { Margin = new Thickness(33, 0, 33, 40) };
    private readonly DispatcherTimer _reload = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private FileSystemWatcher? _watcher;
    private StudyLog? _log;
    private StudyRange _range = StudyRange.Week;
    private string? _course; // filter for "What you worked on"

    public StudyPage()
    {
        _scroll = new ScrollViewer
        {
            Content = _content, Focusable = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        WindowChrome.SetIsHitTestVisibleInChrome(_content, true);
        _ = new SmoothScroll(_scroll);

        var layers = new Grid();
        layers.Children.Add(_base);
        layers.Children.Add(_glow);
        layers.Children.Add(_scroll);
        Content = layers;

        _reload.Tick += (_, _) => { _reload.Stop(); Load(); };
        Loaded += (_, _) => { App.ThemeChanged += OnTheme; Watch(); Load(); };
        Unloaded += (_, _) =>
        {
            App.ThemeChanged -= OnTheme;
            _reload.Stop();
            _watcher?.Dispose();
            _watcher = null;
        };
        Backdrop.Paint(_base, _glow);
    }

    private void OnTheme()
    {
        Backdrop.Paint(_base, _glow);
        Render();
    }

    /// <summary>Re-read when Stint saves (debounced: Stint writes a temp file then moves it).</summary>
    private void Watch()
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(StudyLog.DefaultPath)!;
            if (!Directory.Exists(dir)) return;
            _watcher = new FileSystemWatcher(dir, "data.json") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
            FileSystemEventHandler changed = (_, _) => Dispatcher.BeginInvoke(() => { _reload.Stop(); _reload.Start(); });
            _watcher.Changed += changed;
            _watcher.Created += changed;
            _watcher.Renamed += (_, _) => Dispatcher.BeginInvoke(() => { _reload.Stop(); _reload.Start(); });
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { _watcher = null; }
    }

    private void Load()
    {
        // A read during Stint's save can fail; keep showing the last good log rather than blanking.
        _log = StudyLog.ReadFile(StudyLog.DefaultPath) ?? _log;
        Render();
    }

    // ---------------- Small building blocks ----------------

    private static TextBlock T(string text, double size, string brush = "Text", FontWeight? weight = null, bool wrap = false)
    {
        var t = new TextBlock { Text = text, FontSize = size, TextTrimming = TextTrimming.CharacterEllipsis };
        if (weight is { } w) t.FontWeight = w;
        if (wrap) { t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; }
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    private static TextBlock Label(string text) => T(text, 9, "AccentBrush", FontWeights.SemiBold);

    private static TextBlock Big(string text, double size)
    {
        var t = T(text, size, "Text", FontWeights.Normal);
        t.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        return t;
    }

    private static Brush ColorBrush(string hex)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch (FormatException) { return Brushes.Gray; }
    }

    private static Border Card(string title, UIElement body, FrameworkElement? corner = null, double minHeight = 0)
    {
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        if (corner != null) { DockPanel.SetDock(corner, Dock.Right); head.Children.Add(corner); }
        head.Children.Add(Label(title));
        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(body);
        var card = new Border
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(18, 16, 18, 18), BorderThickness = new Thickness(1),
            BorderBrush = Backdrop.Wash("TextColor", 0x20), Child = stack, MinHeight = minHeight,
        };
        card.SetResourceReference(Border.BackgroundProperty, "Surface");
        return card;
    }

    private static Grid Columns(UIElement left, UIElement right, double leftStar = 1, double rightStar = 1)
    {
        var g = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(leftStar, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(rightStar, GridUnitType.Star) });
        g.Children.Add(left);
        Grid.SetColumn((UIElement)right, 2);
        g.Children.Add(right);
        return g;
    }

    /// <summary>A thin horizontal bar: <paramref name="fraction"/> of the width filled with <paramref name="fill"/>.</summary>
    private static Grid Bar(double fraction, Brush fill, double height = 6)
    {
        var g = new Grid { Height = height };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, fraction), GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, 1 - fraction), GridUnitType.Star) });
        var track = new Border { CornerRadius = new CornerRadius(height / 2) };
        track.SetResourceReference(Border.BackgroundProperty, "Raised");
        Grid.SetColumnSpan(track, 2);
        g.Children.Add(track);
        g.Children.Add(new Border { CornerRadius = new CornerRadius(height / 2), Background = fill });
        return g;
    }

    private static readonly ControlTemplate Bare = CreateBare();

    private static ControlTemplate CreateBare()
    {
        var t = new ControlTemplate(typeof(Button));
        t.VisualTree = new FrameworkElementFactory(typeof(ContentPresenter));
        return t;
    }

    // ---------------- Page ----------------

    private void Render()
    {
        _content.Children.Clear();
        var today = DateOnly.FromDateTime(DateTime.Today);

        // Top bar (sits in the title strip).
        var top = new DockPanel { Height = 40 };
        var source = T("From your Stint log", 11, "Muted");
        source.Margin = new Thickness(0, 6, 118, 0);
        source.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(source, Dock.Right);
        top.Children.Add(source);
        var crumb = T($"STUDY   /   {DateTime.Now.ToString("dddd, MMMM d").ToUpperInvariant()}", 11, "Muted");
        crumb.VerticalAlignment = VerticalAlignment.Center;
        crumb.Margin = new Thickness(0, 6, 0, 0);
        top.Children.Add(crumb);
        _content.Children.Add(top);

        var all = _log?.Sessions ?? [];
        if (all.Count == 0) { _content.Children.Add(EmptyState()); return; }

        int goal = _log!.GoalMinutes;
        var (from, to) = StudyStats.Bounds(_range, today, all);
        var inRange = StudyStats.InRange(all, from, to);
        var sum = StudyStats.Summarise(all, from, to, goal);
        var (current, best) = StudyStats.Streaks(all, today);

        _content.Children.Add(Headline(sum, StudyStats.ChangePercent(all, from, to)));
        _content.Children.Add(StatCards(sum, goal, current, best));
        _content.Children.Add(DailyChart(StudyStats.PerDay(all, from, to), goal));
        _content.Children.Add(Columns(CourseCard(inRange), TaskCard(inRange), 1, 1.35));
        _content.Children.Add(Columns(HeatmapCard(inRange), InsightsCard(all, from, to, goal), 1.35, 1));
        _content.Children.Add(RecentCard(all));
    }

    private UIElement EmptyState()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 90, 0, 0), MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left };
        stack.Children.Add(Label("STUDY"));
        var h = Big("Your study story starts in Stint.", 40);
        h.TextWrapping = TextWrapping.Wrap;
        h.Margin = new Thickness(0, 12, 0, 12);
        stack.Children.Add(h);
        stack.Children.Add(T("Log a session in Stint: pick a course and type what you're working on, like \"Assignment\" or \"Lecture 8\". " +
                             "This page will show where your time goes, by course and by task, and when you study best.", 13, "Muted", wrap: true));
        return stack;
    }

    private UIElement Headline(StudySummary sum, int? change)
    {
        var dock = new DockPanel { Margin = new Thickness(0, 30, 0, 0) };

        // Range switch (top-right).
        var seg = new UniformGrid { Columns = 3, Width = 264 };
        foreach (var (range, text) in new[] { (StudyRange.Week, "7 days"), (StudyRange.Month, "30 days"), (StudyRange.AllTime, "All time") })
        {
            var rb = new RadioButton { Content = text, GroupName = "StudyRange", IsChecked = range == _range };
            rb.SetResourceReference(StyleProperty, "Segment");
            rb.Checked += (_, _) => { if (_range == range) return; _range = range; _course = null; Render(); };
            seg.Children.Add(rb);
        }
        var segBox = new Border { Padding = new Thickness(3), CornerRadius = new CornerRadius(9), Child = seg, VerticalAlignment = VerticalAlignment.Top, BorderThickness = new Thickness(1), BorderBrush = Backdrop.Wash("TextColor", 0x20) };
        segBox.SetResourceReference(Border.BackgroundProperty, "Surface");
        DockPanel.SetDock(segBox, Dock.Right);
        dock.Children.Add(segBox);

        var left = new StackPanel();
        string span = _range switch { StudyRange.Week => "in the last 7 days", StudyRange.Month => "in the last 30 days", _ => "since you started" };
        left.Children.Add(Label("TIME STUDIED"));
        var big = Big(StudyStats.Format(sum.TotalSeconds), 52);
        big.Margin = new Thickness(-2, 10, 0, 6);
        left.Children.Add(big);
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(T($"{span}   ·   {sum.Sessions} {(sum.Sessions == 1 ? "session" : "sessions")}", 13, "Muted"));
        if (change is { } c && _range != StudyRange.AllTime)
        {
            var delta = T($"   ·   {(c >= 0 ? "↑" : "↓")} {Math.Abs(c)}% vs the {sum.Days} days before", 13, c >= 0 ? "AccentBrush" : "Accent3Brush");
            line.Children.Add(delta);
        }
        left.Children.Add(line);
        dock.Children.Add(left);
        return dock;
    }

    private static UIElement StatCards(StudySummary sum, int goal, int current, int best)
    {
        var grid = new UniformGrid { Columns = 4, Margin = new Thickness(-6, 24, -6, 0) };
        void Add(string label, string value, string caption)
        {
            var stack = new StackPanel();
            stack.Children.Add(Label(label));
            var v = Big(value, 26);
            v.Margin = new Thickness(0, 10, 0, 4);
            stack.Children.Add(v);
            stack.Children.Add(T(caption, 11, "Muted"));
            var card = new Border
            {
                CornerRadius = new CornerRadius(10), Padding = new Thickness(16), Margin = new Thickness(6, 0, 6, 0),
                BorderThickness = new Thickness(1), BorderBrush = Backdrop.Wash("TextColor", 0x20), Child = stack,
            };
            card.SetResourceReference(Border.BackgroundProperty, "Surface");
            grid.Children.Add(card);
        }
        Add("DAILY AVERAGE", StudyStats.Format(sum.DailyAverageSeconds), $"across {sum.Days} {(sum.Days == 1 ? "day" : "days")}");
        Add("GOAL DAYS", $"{sum.GoalDays} / {sum.Days}", $"days with {StudyStats.Format(goal * 60)} or more");
        Add("STREAK", current == 1 ? "1 day" : $"{current} days", $"best {best} {(best == 1 ? "day" : "days")}");
        if (sum.Longest is { } l) Add("LONGEST SITTING", StudyStats.Format(l.Seconds), $"{l.Task} · {l.Start:ddd d MMM}");
        else Add("LONGEST SITTING", "–", "no sessions in this range");
        return grid;
    }

    // ---------------- Every day ----------------

    private UIElement DailyChart(List<DayTotal> days, int goalMinutes)
    {
        // Long ranges become weekly bars (Monday start) so they stay readable.
        bool weekly = days.Count > 45;
        var buckets = weekly
            ? days.GroupBy(d => d.Day.AddDays(-(((int)d.Day.DayOfWeek + 6) % 7)))
                  .Select(g => new DayTotal(g.Key,
                      g.SelectMany(d => d.Parts).GroupBy(p => p.Subject).Select(pg => new SubjectPart(pg.Key, pg.First().Color, pg.Sum(p => p.Seconds))).OrderByDescending(p => p.Seconds).ToList(),
                      g.Sum(d => d.TotalSeconds))).ToList()
            : days;

        const double chartH = 170;
        double goalSec = goalMinutes * 60.0;
        double max = Math.Max(buckets.Select(b => (double)b.TotalSeconds).DefaultIfEmpty(0).Max(), weekly ? 1 : goalSec * 1.15);

        var bars = new Grid { Height = chartH + 22 };
        for (int i = 0; i < buckets.Count; i++)
        {
            bars.ColumnDefinitions.Add(new ColumnDefinition());
            var b = buckets[i];
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(buckets.Count > 20 ? 1.5 : 6, 0, buckets.Count > 20 ? 1.5 : 6, 22) };
            foreach (var part in b.Parts.AsEnumerable().Reverse())
                stack.Children.Add(new Border { Height = Math.Max(2, chartH * part.Seconds / max), Background = ColorBrush(part.Color) });
            if (b.Parts.Count == 0)
            {
                var empty = new Border { Height = 2 };
                empty.SetResourceReference(Border.BackgroundProperty, "Raised");
                stack.Children.Add(empty);
            }
            var clip = new Border { Child = stack, ClipToBounds = true, VerticalAlignment = VerticalAlignment.Bottom, Background = Brushes.Transparent, MaxWidth = 64 };
            var tip = (weekly ? $"Week of {b.Day:d MMM}" : $"{b.Day:dddd d MMM}") + $"\n{StudyStats.Format(b.TotalSeconds)}" +
                      string.Concat(b.Parts.Select(p => $"\n  {p.Subject}: {StudyStats.Format(p.Seconds)}"));
            var hit = new Grid { Background = Brushes.Transparent, ToolTip = tip };
            hit.Children.Add(clip);
            Grid.SetColumn(hit, i);
            bars.Children.Add(hit);

            int every = StudyStats.LabelEvery(buckets.Count);
            bool labelIt = i % every == 0 || (i == buckets.Count - 1 && i % every >= (every + 1) / 2);
            if (labelIt)
            {
                bool overAYear = buckets[^1].Day.DayNumber - buckets[0].Day.DayNumber > 365;
                var text = buckets.Count <= 10 ? b.Day.ToString("ddd d") : b.Day.ToString(overAYear ? "MMM yy" : "d MMM");
                var lbl = T(text, 10, b.Day == DateOnly.FromDateTime(DateTime.Today) ? "Text" : "Muted");
                lbl.HorizontalAlignment = HorizontalAlignment.Center;
                lbl.VerticalAlignment = VerticalAlignment.Bottom;
                lbl.TextTrimming = TextTrimming.None;
                Grid.SetColumn(lbl, i);
                bars.Children.Add(lbl);
            }
        }

        var chart = new Grid();
        chart.Children.Add(bars);
        if (!weekly)
        {
            // Goal line.
            double y = chartH - chartH * goalSec / max;
            var line = new Line { X1 = 0, X2 = 1, Y1 = 0, Y2 = 0, Stretch = Stretch.Fill, StrokeThickness = 1, StrokeDashArray = [4, 4], VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, y, 0, 0), IsHitTestVisible = false };
            line.SetResourceReference(Shape.StrokeProperty, "Muted");
            var goalText = T($"goal {StudyStats.Format(goalMinutes * 60)}", 10, "Muted");
            goalText.HorizontalAlignment = HorizontalAlignment.Left;
            goalText.VerticalAlignment = VerticalAlignment.Top;
            goalText.Margin = new Thickness(0, Math.Max(0, y - 15), 0, 0);
            goalText.IsHitTestVisible = false;
            chart.Children.Add(line);
            chart.Children.Add(goalText);
        }

        // Legend: courses in this range.
        var legend = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var s in days.SelectMany(d => d.Parts).GroupBy(p => p.Subject).OrderByDescending(g => g.Sum(p => p.Seconds)))
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 0) };
            item.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = ColorBrush(s.First().Color), VerticalAlignment = VerticalAlignment.Center });
            var name = T(s.Key, 11, "Muted");
            name.Margin = new Thickness(6, 0, 0, 0);
            item.Children.Add(name);
            legend.Children.Add(item);
        }
        var body = new StackPanel();
        body.Children.Add(chart);
        body.Children.Add(legend);

        var card = Card(weekly ? "EVERY WEEK" : "EVERY DAY", body);
        card.Margin = new Thickness(0, 12, 0, 0);
        return card;
    }

    // ---------------- By course / by task ----------------

    private UIElement CourseCard(List<StudySession> sessions)
    {
        var list = new StackPanel();
        foreach (var c in StudyStats.BySubject(sessions))
        {
            bool selected = _course == c.Subject;
            var row = new StackPanel();
            var head = new DockPanel();
            var right = T($"{StudyStats.Format(c.Seconds)}   {Math.Round(c.Share * 100)}%", 12, "Muted");
            DockPanel.SetDock(right, Dock.Right);
            head.Children.Add(right);
            var name = new StackPanel { Orientation = Orientation.Horizontal };
            name.Children.Add(new Ellipse { Width = 9, Height = 9, Fill = ColorBrush(c.Color), VerticalAlignment = VerticalAlignment.Center });
            var n = T(c.Subject, 13, "Text", selected ? FontWeights.SemiBold : null);
            n.Margin = new Thickness(8, 0, 0, 0);
            name.Children.Add(n);
            head.Children.Add(name);
            row.Children.Add(head);
            var bar = Bar(c.Share, ColorBrush(c.Color));
            bar.Margin = new Thickness(17, 7, 0, 0);
            row.Children.Add(bar);

            var box = new Border { Child = row, Padding = new Thickness(8, 8, 8, 9), Margin = new Thickness(-8, 0, -8, 4), CornerRadius = new CornerRadius(8) };
            if (selected) box.SetResourceReference(Border.BackgroundProperty, "Raised");
            var button = new Button { Content = box, Template = Bare, Cursor = Cursors.Hand, ToolTip = selected ? "Show all courses" : $"Show only {c.Subject} tasks" };
            button.SetResourceReference(FocusVisualStyleProperty, "FocusRing");
            System.Windows.Automation.AutomationProperties.SetName(button, $"{c.Subject}, {StudyStats.Format(c.Seconds)}");
            button.Click += (_, _) => { _course = selected ? null : c.Subject; Render(); };
            list.Children.Add(button);
        }
        return Card("BY COURSE", list, T("click to filter tasks", 10, "Muted"), 240);
    }

    private UIElement TaskCard(List<StudySession> sessions)
    {
        var tasks = StudyStats.ByTask(_course is null ? sessions : sessions.Where(s => s.Subject == _course));
        var list = new StackPanel();
        int max = Math.Max(1, tasks.Select(t => t.Seconds).DefaultIfEmpty(1).Max());
        foreach (var t in tasks.Take(10))
        {
            var head = new DockPanel();
            var time = T(StudyStats.Format(t.Seconds), 13, "Text", FontWeights.SemiBold);
            DockPanel.SetDock(time, Dock.Right);
            head.Children.Add(time);
            head.Children.Add(T(t.Task, 13));
            var sub = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
            sub.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = ColorBrush(t.Color), VerticalAlignment = VerticalAlignment.Center });
            var last = t.LastStudied == DateOnly.FromDateTime(DateTime.Today) ? "today" : t.LastStudied.ToString("ddd d MMM");
            var meta = T($"{t.Subject}   ·   {t.Sessions} {(t.Sessions == 1 ? "session" : "sessions")}   ·   last {last}", 11, "Muted");
            meta.Margin = new Thickness(6, 0, 0, 0);
            sub.Children.Add(meta);
            var bar = Bar((double)t.Seconds / max, ColorBrush(t.Color), 4);
            bar.Margin = new Thickness(0, 7, 0, 0);
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            row.Children.Add(head);
            row.Children.Add(sub);
            row.Children.Add(bar);
            list.Children.Add(row);
        }
        if (tasks.Count > 10) list.Children.Add(T($"and {tasks.Count - 10} more", 11, "Muted"));
        var title = _course is null ? "WHAT YOU WORKED ON" : $"WHAT YOU WORKED ON · {_course.ToUpperInvariant()}";
        return Card(title, list, minHeight: 240);
    }

    // ---------------- When / insights / recent ----------------

    private static UIElement HeatmapCard(List<StudySession> sessions)
    {
        var grid = StudyStats.HourGrid(sessions);
        int max = 1;
        foreach (var v in grid) max = Math.Max(max, v);

        var table = new Grid();
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        for (int h = 0; h < 24; h++) table.ColumnDefinitions.Add(new ColumnDefinition());
        for (int r = 0; r < 8; r++) table.RowDefinitions.Add(new RowDefinition { Height = new GridLength(r == 7 ? 18 : 17) });

        string[] days = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
        for (int d = 0; d < 7; d++)
        {
            var name = T(days[d], 10, "Muted");
            name.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(name, d);
            table.Children.Add(name);
            for (int h = 0; h < 24; h++)
            {
                double level = grid[d, h] / (double)max;
                var cell = new Border { CornerRadius = new CornerRadius(3), Margin = new Thickness(1.5), ToolTip = $"{days[d]} {StudyStats.Hour(h)}: {StudyStats.Format(grid[d, h])}" };
                if (grid[d, h] == 0) cell.SetResourceReference(Border.BackgroundProperty, "Raised");
                else cell.Background = Backdrop.Wash("AccentColor", (byte)(60 + 195 * level));
                Grid.SetRow(cell, d);
                Grid.SetColumn(cell, h + 1);
                table.Children.Add(cell);
            }
        }
        foreach (var h in new[] { 0, 6, 12, 18 })
        {
            var lbl = T(StudyStats.Hour(h), 9, "Muted");
            lbl.TextTrimming = TextTrimming.None;
            Grid.SetRow(lbl, 7);
            Grid.SetColumn(lbl, h + 1);
            Grid.SetColumnSpan(lbl, 6);
            table.Children.Add(lbl);
        }
        var body = new StackPanel();
        body.Children.Add(table);
        var peak = StudyStats.PeakWindow(grid);
        var cap = T(peak is null ? "" : $"Your peak: {peak}", 12, "Text");
        cap.Margin = new Thickness(0, 12, 0, 0);
        body.Children.Add(cap);
        return Card("WHEN YOU STUDY", body);
    }

    private static UIElement InsightsCard(IReadOnlyList<StudySession> all, DateOnly from, DateOnly to, int goal)
    {
        var list = new StackPanel();
        foreach (var line in StudyStats.Insights(all, from, to, goal))
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var dot = T("●", 8, "AccentBrush");
            dot.Margin = new Thickness(0, 4, 10, 0);
            dot.VerticalAlignment = VerticalAlignment.Top;
            DockPanel.SetDock(dot, Dock.Left);
            row.Children.Add(dot);
            row.Children.Add(T(line, 13, "Text", wrap: true));
            list.Children.Add(row);
        }
        return Card("INSIGHTS", list);
    }

    private static UIElement RecentCard(IReadOnlyList<StudySession> all)
    {
        var list = new StackPanel();
        var recent = all.OrderByDescending(s => s.Start).Take(12).ToList();
        for (int i = 0; i < recent.Count; i++)
        {
            var s = recent[i];
            var row = new Grid { Margin = new Thickness(0, 0, 0, 0) };
            foreach (var w in new[] { 100.0, 170, 150, -1, 90, 70, 64 })
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = w < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
            void Cell(UIElement e, int col) { Grid.SetColumn(e, col); row.Children.Add(e); }

            Cell(T(s.Start.ToString("ddd d MMM"), 12, "Muted"), 0);
            Cell(T($"{s.Start:t} – {s.End:t}".ToLowerInvariant(), 12, "Muted"), 1);
            var course = new StackPanel { Orientation = Orientation.Horizontal };
            course.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = ColorBrush(s.Color), VerticalAlignment = VerticalAlignment.Center });
            var cn = T(s.Subject, 12);
            cn.Margin = new Thickness(6, 0, 0, 0);
            course.Children.Add(cn);
            Cell(course, 2);
            Cell(T(s.Task, 12), 3);
            Cell(T(s.Mode, 11, "Muted"), 4);
            Cell(T(s.Rating > 0 ? new string('★', s.Rating) : "", 11, "AccentBrush"), 5);
            var dur = T(StudyStats.Format(s.Seconds), 12, "Text", FontWeights.SemiBold);
            dur.HorizontalAlignment = HorizontalAlignment.Right;
            Cell(dur, 6);

            var line = new Border
            {
                Child = row, Padding = new Thickness(0, 9, 0, 9),
                BorderThickness = new Thickness(0, 0, 0, i == recent.Count - 1 ? 0 : 1), BorderBrush = Backdrop.Wash("TextColor", 0x12),
            };
            list.Children.Add(line);
        }
        var card = Card("RECENT SESSIONS", list);
        card.Margin = new Thickness(0, 12, 0, 0);
        return card;
    }
}
