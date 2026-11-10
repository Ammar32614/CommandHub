using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;
using CommandHub.Home;
using CommandHub.Study;
using static CommandHub.Home.PageUi;

namespace CommandHub.Planner;

/// <summary>
/// The Planner tab: classes, deadlines, exams and events by week or month, with what's coming up,
/// and (from Stint) how long you actually studied each day, so plan and reality sit side by side.
/// </summary>
public sealed class PlannerPage : UserControl
{
    private enum View { Week, Month }

    private readonly Border _base = new(), _glow = new() { IsHitTestVisible = false };
    private readonly StackPanel _content = new() { Margin = new Thickness(33, 0, 33, 40) };
    private readonly DispatcherTimer _midnight = new() { Interval = TimeSpan.FromMinutes(5) };
    private View _view = View.Week;
    private DateOnly _anchor = Today;
    private DateOnly _renderedFor = Today;
    private StudyLog? _log;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    public PlannerPage()
    {
        var scroll = new ScrollViewer
        {
            Content = _content, Focusable = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        WindowChrome.SetIsHitTestVisibleInChrome(_content, true);
        _ = new SmoothScroll(scroll);
        var layers = new Grid();
        layers.Children.Add(_base);
        layers.Children.Add(_glow);
        layers.Children.Add(scroll);
        Content = layers;

        _midnight.Tick += (_, _) => { if (Today != _renderedFor) Render(); };
        Loaded += (_, _) =>
        {
            App.ThemeChanged += OnTheme;
            App.Saved += Render;
            _midnight.Start();
            _log = StudyLog.ReadFile(StudyLog.DefaultPath);
            Backdrop.Paint(_base, _glow);
            Render();
        };
        Unloaded += (_, _) => { App.ThemeChanged -= OnTheme; App.Saved -= Render; _midnight.Stop(); };
        Backdrop.Paint(_base, _glow);
    }

    private void OnTheme() { Backdrop.Paint(_base, _glow); Render(); }

    /// <summary>Opens the editor for a new item on <paramref name="day"/> (or an existing one).</summary>
    public void OpenEditor(PlanItem? item, DateOnly day)
    {
        var editor = new PlanEditor(item, day, _log?.Courses ?? []);
        editor.Closed += _ => (Application.Current.MainWindow as MainWindow)?.HideSheet();
        (Application.Current.MainWindow as MainWindow)?.ShowSheet(editor);
    }

    public static string KindBrush(PlanKind kind) => kind switch
    {
        PlanKind.Class => "Accent2Brush",
        PlanKind.Deadline => "AccentBrush",
        PlanKind.Exam => "Accent3Brush",
        _ => "Accent4Brush",
    };

    private Brush? CourseBrush(string course)
    {
        var c = _log?.Courses.FirstOrDefault(x => string.Equals(x.Name, course, StringComparison.OrdinalIgnoreCase));
        return c is null ? null : ColorBrush(c.Color);
    }

    // ---------------- Page ----------------

    private void Render()
    {
        _renderedFor = Today;
        _content.Children.Clear();
        var items = App.Data.Planner;

        // Top bar in the title strip.
        var top = new DockPanel { Height = 40 };
        var crumb = T($"PLANNER   /   {DateTime.Now.ToString("dddd, MMMM d").ToUpperInvariant()}", 11, "Muted");
        crumb.VerticalAlignment = VerticalAlignment.Center;
        crumb.Margin = new Thickness(0, 6, 0, 0);
        top.Children.Add(crumb);
        _content.Children.Add(top);

        var (from, to) = _view == View.Week
            ? (PlannerLogic.WeekStart(_anchor), PlannerLogic.WeekStart(_anchor).AddDays(6))
            : (new DateOnly(_anchor.Year, _anchor.Month, 1), new DateOnly(_anchor.Year, _anchor.Month, 1).AddMonths(1).AddDays(-1));
        var occurrences = PlannerLogic.Occurrences(items, from, to);
        var studied = _log is null ? [] : StudyStats.PerDay(_log.Sessions, from, to);

        _content.Children.Add(Headline(from, to, occurrences, studied.Sum(d => d.TotalSeconds)));

        var body = new Grid { Margin = new Thickness(0, 20, 0, 0) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 250 });
        body.Children.Add(_view == View.Week ? Week(from, occurrences, studied) : Month(occurrences, studied));
        var coming = ComingUp(items);
        Grid.SetColumn(coming, 2);
        body.Children.Add(coming);
        _content.Children.Add(body);
    }

    private UIElement Headline(DateOnly from, DateOnly to, List<PlanOccurrence> occ, int studiedSeconds)
    {
        var dock = new DockPanel { Margin = new Thickness(0, 30, 0, 0) };

        // Controls (right): view switch, navigation, add.
        var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
        var seg = new UniformGrid { Columns = 2, Width = 150 };
        foreach (var (v, text) in new[] { (View.Week, "Week"), (View.Month, "Month") })
        {
            var rb = new RadioButton { Content = text, GroupName = "PlannerView", IsChecked = v == _view };
            rb.SetResourceReference(StyleProperty, "Segment");
            rb.Checked += (_, _) => { if (_view != v) { _view = v; Render(); } };
            seg.Children.Add(rb);
        }
        var segBox = new Border { Padding = new Thickness(3), CornerRadius = new CornerRadius(9), Child = seg, BorderThickness = new Thickness(1), BorderBrush = Backdrop.Wash("TextColor", 0x20), Margin = new Thickness(0, 0, 10, 0) };
        segBox.SetResourceReference(Border.BackgroundProperty, "Surface");
        controls.Children.Add(segBox);

        Button Nav(string glyph, string name, int step)
        {
            var b = new Button { Content = glyph, Width = 34, Height = 34, ToolTip = name };
            b.SetResourceReference(StyleProperty, "IconButton");
            System.Windows.Automation.AutomationProperties.SetName(b, name);
            b.Click += (_, _) => { _anchor = _view == View.Week ? _anchor.AddDays(7 * step) : _anchor.AddMonths(step); Render(); };
            return b;
        }
        controls.Children.Add(Nav("", _view == View.Week ? "Previous week" : "Previous month", -1));
        var today = Styled("Today", "GhostButton", (_, _) => { _anchor = Today; Render(); }, new Thickness(12, 5, 12, 5));
        today.Margin = new Thickness(4, 0, 4, 0);
        controls.Children.Add(today);
        controls.Children.Add(Nav("", _view == View.Week ? "Next week" : "Next month", 1));
        var add = Styled("+  Add", "AccentButton", (_, _) => OpenEditor(null, DefaultDay(from, to)), new Thickness(16, 6, 16, 6));
        add.Margin = new Thickness(12, 0, 0, 0);
        System.Windows.Automation.AutomationProperties.SetName(add, "Add to planner");
        controls.Children.Add(add);
        DockPanel.SetDock(controls, Dock.Right);
        dock.Children.Add(controls);

        var left = new StackPanel();
        left.Children.Add(Label(_view == View.Week ? (from <= Today && Today <= to ? "THIS WEEK" : "WEEK") : "MONTH"));
        var title = _view == View.Week
            ? (from.Month == to.Month ? $"{from:MMM d} – {to.Day}" : $"{from:MMM d} – {to:MMM d}")
            : from.ToString("MMMM yyyy");
        var big = Big(title, 44);
        big.Margin = new Thickness(-2, 10, 0, 6);
        left.Children.Add(big);
        var sub = PlannerLogic.Summary(occ) + (studiedSeconds > 0 ? $"   ·   studied {StudyStats.Format(studiedSeconds)}" : "");
        left.Children.Add(T(sub, 13, "Muted"));
        dock.Children.Add(left);
        return dock;
    }

    /// <summary>New items default to today when it's on screen, otherwise the first day shown.</summary>
    private static DateOnly DefaultDay(DateOnly from, DateOnly to) => Today >= from && Today <= to ? Today : from;

    // ---------------- Week ----------------

    private UIElement Week(DateOnly start, List<PlanOccurrence> occ, List<DayTotal> studied)
    {
        var grid = new UniformGrid { Columns = 7, Margin = new Thickness(-4, 0, -4, 0) };
        for (int i = 0; i < 7; i++)
        {
            var day = start.AddDays(i);
            bool isToday = day == Today;
            var col = new DockPanel();

            // Header
            var head = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            head.Children.Add(Label(day.ToString("ddd").ToUpperInvariant(), isToday ? "AccentBrush" : "Muted"));
            var num = Big(day.Day.ToString(), 24);
            num.Margin = new Thickness(0, 4, 0, 0);
            if (isToday) num.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            head.Children.Add(num);
            DockPanel.SetDock(head, Dock.Top);
            col.Children.Add(head);

            // Study from Stint (bottom)
            var dayStudy = studied.FirstOrDefault(d => d.Day == day);
            if (dayStudy is { TotalSeconds: > 0 })
            {
                var foot = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
                foot.Children.Add(T($"Studied {StudyStats.Format(dayStudy.TotalSeconds)}", 10, "Muted"));
                var bar = new Grid { Height = 5, Margin = new Thickness(0, 5, 0, 0) };
                foreach (var p in dayStudy.Parts)
                    bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(p.Seconds, GridUnitType.Star) });
                for (int j = 0; j < dayStudy.Parts.Count; j++)
                {
                    var seg = new Border { Background = ColorBrush(dayStudy.Parts[j].Color), CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, j < dayStudy.Parts.Count - 1 ? 1 : 0, 0) };
                    Grid.SetColumn(seg, j);
                    bar.Children.Add(seg);
                }
                foot.Children.Add(bar);
                foot.ToolTip = string.Join("\n", dayStudy.Parts.Select(p => $"{p.Subject}: {StudyStats.Format(p.Seconds)}"));
                DockPanel.SetDock(foot, Dock.Bottom);
                col.Children.Add(foot);
            }

            // Add button (bottom, above study)
            var addLabel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            addLabel.Children.Add(Glyph("", 9, "Muted"));
            var addText = T("Add", 10, "Muted");
            addText.Margin = new Thickness(5, 0, 0, 0);
            addLabel.Children.Add(addText);
            var addBox = new Border { Child = addLabel, Padding = new Thickness(0, 6, 0, 6), CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1), BorderBrush = Backdrop.Wash("TextColor", 0x14), Margin = new Thickness(0, 6, 0, 0) };
            var addBtn = PageUi.Bare(addBox, $"Add on {day:dddd d MMMM}");
            var capturedDay = day;
            addBtn.Click += (_, _) => OpenEditor(null, capturedDay);
            DockPanel.SetDock(addBtn, Dock.Bottom);
            col.Children.Add(addBtn);

            // Items
            var list = new StackPanel();
            foreach (var o in occ.Where(o => o.Date == day)) list.Children.Add(WeekItem(o));
            col.Children.Add(list);

            var card = Card(col, new Thickness(12, 12, 12, 12));
            card.Margin = new Thickness(4, 0, 4, 0);
            card.MinHeight = 380;
            if (isToday) card.BorderBrush = Backdrop.Wash("AccentColor", 0x88);
            grid.Children.Add(card);
        }
        return grid;
    }

    private UIElement WeekItem(PlanOccurrence o)
    {
        var item = o.Item;
        bool overdue = PlannerLogic.IsOverdue(o, Today);
        var stack = new StackPanel();
        if (item.Start is { } s)
            stack.Children.Add(T(PlannerLogic.FormatTime(s), 10, "Muted"));
        var title = T(item.Title, 12, item.Done ? "Muted" : "Text", FontWeights.SemiBold);
        title.TextWrapping = TextWrapping.Wrap;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.MaxHeight = 34;
        if (item.Done) title.TextDecorations = TextDecorations.Strikethrough;
        stack.Children.Add(title);
        var meta = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        if (CourseBrush(item.Course) is { } cb)
            meta.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = cb, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) });
        meta.Children.Add(T(overdue ? "Overdue" : item.Course.Length > 0 ? item.Course : item.Kind.ToString(), 10, overdue ? "Danger" : "Muted"));
        stack.Children.Add(meta);

        var bar = new Border { Width = 3, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 8, 0) };
        bar.SetResourceReference(Border.BackgroundProperty, overdue ? "Danger" : KindBrush(item.Kind));
        var row = new DockPanel();
        DockPanel.SetDock(bar, Dock.Left);
        row.Children.Add(bar);
        row.Children.Add(stack);
        var box = new Border { Child = row, Padding = new Thickness(6, 6, 6, 7), CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 0, 6), Background = Backdrop.Wash("TextColor", 0x08) };
        var tip = string.Join("\n", new[]
        {
            item.Title,
            item.Kind + (item.Course.Length > 0 ? $" · {item.Course}" : ""),
            item.Start is { } st ? PlannerLogic.FormatTime(st) + (item.End is { } en ? $" – {PlannerLogic.FormatTime(en)}" : "") : "",
            item.Notes,
        }.Where(x => x.Length > 0));
        var b = PageUi.Bare(box, $"{item.Title}, {item.Kind}, {o.Date:dddd d MMMM}", tip);
        b.Click += (_, _) => OpenEditor(item, o.Date);
        return b;
    }

    // ---------------- Month ----------------

    private UIElement Month(List<PlanOccurrence> occ, List<DayTotal> studied)
    {
        var stack = new StackPanel();
        var names = new UniformGrid { Columns = 7, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var n in new[] { "MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN" })
            names.Children.Add(Label(n, "Muted"));
        stack.Children.Add(names);

        var days = PlannerLogic.MonthGrid(_anchor.Year, _anchor.Month);
        var all = PlannerLogic.Occurrences(App.Data.Planner, days[0], days[^1]);
        var study = _log is null ? [] : StudyStats.PerDay(_log.Sessions, days[0], days[^1]);
        int maxStudy = Math.Max(1, study.Select(d => d.TotalSeconds).DefaultIfEmpty(0).Max());

        var grid = new UniformGrid { Columns = 7 };
        foreach (var day in days)
        {
            bool inMonth = day.Month == _anchor.Month, isToday = day == Today;
            var cell = new DockPanel();

            var num = T(day.Day.ToString(), 12, isToday ? "OnAccent" : inMonth ? "Text" : "Muted", isToday ? FontWeights.SemiBold : null);
            num.HorizontalAlignment = HorizontalAlignment.Center;
            num.VerticalAlignment = VerticalAlignment.Center;
            var numBox = new Border { Width = 24, Height = 22, CornerRadius = new CornerRadius(11), Child = num, HorizontalAlignment = HorizontalAlignment.Left };
            if (isToday) numBox.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            var numBtn = PageUi.Bare(numBox, $"Week of {day:d MMMM}", "Show this week");
            var captured = day;
            numBtn.HorizontalAlignment = HorizontalAlignment.Left;
            numBtn.Click += (_, _) => { _anchor = captured; _view = View.Week; Render(); };
            DockPanel.SetDock(numBtn, Dock.Top);
            cell.Children.Add(numBtn);

            if (study.FirstOrDefault(d => d.Day == day) is { TotalSeconds: > 0 } ds)
            {
                var bar = new Border { Height = 3, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(6, 120.0 * ds.TotalSeconds / maxStudy), ToolTip = $"Studied {StudyStats.Format(ds.TotalSeconds)}", Margin = new Thickness(0, 4, 0, 0) };
                bar.SetResourceReference(Border.BackgroundProperty, "Accent2Brush");
                DockPanel.SetDock(bar, Dock.Bottom);
                cell.Children.Add(bar);
            }

            var chips = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            var todays = all.Where(o => o.Date == day).ToList();
            foreach (var o in todays.Take(3))
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                var dot = new Ellipse { Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
                dot.SetResourceReference(Shape.FillProperty, PlannerLogic.IsOverdue(o, Today) ? "Danger" : KindBrush(o.Item.Kind));
                row.Children.Add(dot);
                var t = T(o.Item.Title, 10, o.Item.Done ? "Muted" : "Text");
                t.MaxWidth = 120;
                row.Children.Add(t);
                var chip = PageUi.Bare(row, $"{o.Item.Title}, {o.Item.Kind}");
                chip.HorizontalAlignment = HorizontalAlignment.Left;
                chip.Margin = new Thickness(0, 0, 0, 2);
                var item = o.Item;
                chip.Click += (_, _) => OpenEditor(item, captured);
                chips.Children.Add(chip);
            }
            if (todays.Count > 3) chips.Children.Add(T($"+{todays.Count - 3} more", 10, "Muted"));
            cell.Children.Add(chips);

            var box = new Border
            {
                Child = cell, MinHeight = 92, Padding = new Thickness(7, 6, 7, 7), Margin = new Thickness(2),
                CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
                BorderBrush = isToday ? Backdrop.Wash("AccentColor", 0x88) : Backdrop.Wash("TextColor", 0x14),
                Background = inMonth ? Backdrop.Wash("TextColor", 0x06) : Brushes.Transparent, Opacity = inMonth ? 1 : 0.55,
            };
            // Clicking empty space in a day adds something on that day.
            box.MouseLeftButtonUp += (_, e) => { if (e.OriginalSource == box || e.OriginalSource == cell || e.OriginalSource == chips) OpenEditor(null, captured); };
            box.Cursor = System.Windows.Input.Cursors.Hand;
            grid.Children.Add(box);
        }
        stack.Children.Add(grid);
        return Card(stack, new Thickness(14));
    }

    // ---------------- Coming up ----------------

    private UIElement ComingUp(List<PlanItem> items)
    {
        var list = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        head.Children.Add(Label("COMING UP"));
        list.Children.Add(head);

        var next = PlannerLogic.UpNext(items, Today, 21, TimeOnly.FromDateTime(DateTime.Now));
        if (next.Count == 0)
        {
            var empty = T("Nothing coming up. Add a class, a deadline or an exam, and it shows here.", 12, "Muted", wrap: true);
            list.Children.Add(empty);
        }
        foreach (var o in next.Take(14))
        {
            var item = o.Item;
            bool overdue = PlannerLogic.IsOverdue(o, Today);
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };

            if (item.Kind is PlanKind.Deadline or PlanKind.Event && !item.Weekly)
            {
                var check = new CheckBox { IsChecked = item.Done, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0) };
                check.SetResourceReference(StyleProperty, "TodoCheck");
                System.Windows.Automation.AutomationProperties.SetName(check, $"Mark {item.Title} done");
                check.Checked += (_, _) => { item.Done = true; App.SaveNow(); };
                DockPanel.SetDock(check, Dock.Left);
                row.Children.Add(check);
            }
            else
            {
                var bar = new Border { Width = 3, CornerRadius = new CornerRadius(2), Margin = new Thickness(5, 2, 13, 2) };
                bar.SetResourceReference(Border.BackgroundProperty, KindBrush(item.Kind));
                DockPanel.SetDock(bar, Dock.Left);
                row.Children.Add(bar);
            }

            var text = new StackPanel();
            text.Children.Add(T(item.Title, 13));
            var when = overdue ? $"Overdue · {PlannerLogic.When(o.Date, Today).ToLowerInvariant()}" : PlannerLogic.When(o.Date, Today);
            var parts = new List<string> { when };
            if (item.Start is { } s) parts.Add(PlannerLogic.FormatTime(s));
            if (item.Course.Length > 0) parts.Add(item.Course);
            parts.Add(item.Kind.ToString());
            text.Children.Add(T(string.Join("  ·  ", parts), 11, overdue ? "Danger" : "Muted"));
            var open = PageUi.Bare(text, $"Edit {item.Title}");
            open.HorizontalAlignment = HorizontalAlignment.Stretch;
            open.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            open.Click += (_, _) => OpenEditor(item, o.Date);
            row.Children.Add(open);
            list.Children.Add(row);
        }
        if (next.Count > 14) list.Children.Add(T($"and {next.Count - 14} more", 11, "Muted"));
        var card = Card(list);
        card.VerticalAlignment = VerticalAlignment.Top;
        return card;
    }
}
