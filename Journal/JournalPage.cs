using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;
using CommandHub.Home;
using CommandHub.Planner;
using CommandHub.Study;
using static CommandHub.Home.PageUi;

namespace CommandHub.Journal;

/// <summary>
/// The Journal tab: one entry per day with a mood, written in a box that saves as you type,
/// next to what you studied and planned that day.
/// </summary>
public sealed class JournalPage : UserControl
{
    private readonly Border _base = new(), _glow = new() { IsHitTestVisible = false };
    private readonly StackPanel _content = new() { Margin = new Thickness(33, 0, 33, 40) };
    private readonly DispatcherTimer _midnight = new() { Interval = TimeSpan.FromMinutes(1) };
    private DateOnly _day = Today, _renderedFor = Today;
    private StudyLog? _log;
    private string _query = "";

    // The writing box survives re-renders so the caret and undo history stay put.
    private readonly TextBox _text;
    private readonly TextBlock _words = T("", 11, "Muted"), _tags = T("", 11, "AccentBrush"), _saved = T("", 11, "Muted");
    private readonly TextBox _search = Field("Search your journal, or #tag", "", "Search journal");
    private readonly StackPanel _results = new();
    private bool _loadingText;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);
    private static List<JournalEntry> Entries => App.Data.Journal;

    public JournalPage()
    {
        _text = Field("Write anything. It saves as you type.", "", "Journal entry");
        _text.AcceptsReturn = true;
        _text.AcceptsTab = false;
        _text.TextWrapping = TextWrapping.Wrap;
        _text.MinHeight = 260;
        _text.VerticalContentAlignment = VerticalAlignment.Top;
        _text.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _text.FontSize = 14;
        _text.TextChanged += (_, _) => OnTextChanged();
        _search.TextChanged += (_, _) => { _query = _search.Text; ShowResults(); };

        var scroll = new ScrollViewer { Content = _content, Focusable = false, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        WindowChrome.SetIsHitTestVisibleInChrome(_content, true);
        _ = new SmoothScroll(scroll);
        var layers = new Grid();
        layers.Children.Add(_base);
        layers.Children.Add(_glow);
        layers.Children.Add(scroll);
        Content = layers;

        _midnight.Tick += (_, _) =>
        {
            if (Today == _renderedFor) return;
            if (_day == _renderedFor) _day = Today; // still on "today" when the date changed: follow it
            Render();
        };
        Loaded += (_, _) =>
        {
            App.ThemeChanged += OnTheme;
            _midnight.Start();
            _log = StudyLog.ReadFile(StudyLog.DefaultPath);
            Backdrop.Paint(_base, _glow);
            Render();
        };
        Unloaded += (_, _) => { App.ThemeChanged -= OnTheme; _midnight.Stop(); Tidy(); };
        Backdrop.Paint(_base, _glow);
    }

    private void OnTheme() { Backdrop.Paint(_base, _glow); Render(); }

    private JournalEntry? Find(DateOnly day) => Entries.FirstOrDefault(e => e.Date == day);

    private JournalEntry GetOrCreate(DateOnly day)
    {
        if (Find(day) is { } e) return e;
        e = new JournalEntry { Date = day };
        int at = Entries.FindIndex(x => x.Date > day);
        Entries.Insert(at < 0 ? Entries.Count : at, e);
        return e;
    }

    /// <summary>Drops a blank entry for the day being left, so empty days don't count as written.</summary>
    private void Tidy()
    {
        if (Find(_day) is { } e && JournalLogic.IsEmpty(e))
        {
            Entries.Remove(e);
            App.SaveSoon();
        }
    }

    private void GoTo(DateOnly day)
    {
        if (day > Today) day = Today;
        if (day == _day) return;
        Tidy();
        _day = day;
        Render();
    }

    // ---------------- Page ----------------

    private void Render()
    {
        _renderedFor = Today;
        _content.Children.Clear();
        var entry = Find(_day);

        var top = new DockPanel { Height = 40 };
        var crumb = T($"JOURNAL   /   {DateTime.Now.ToString("dddd, MMMM d").ToUpperInvariant()}", 11, "Muted");
        crumb.VerticalAlignment = VerticalAlignment.Center;
        crumb.Margin = new Thickness(0, 6, 0, 0);
        top.Children.Add(crumb);
        _content.Children.Add(top);

        _content.Children.Add(Headline());

        var row = new Grid { Margin = new Thickness(0, 22, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.7, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(Editor(entry));
        var side = new StackPanel();
        side.Children.Add(DayCard());
        if (MemoriesCard() is { } memories) side.Children.Add(memories);
        Grid.SetColumn(side, 2);
        row.Children.Add(side);
        _content.Children.Add(row);

        var row2 = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        row2.ColumnDefinitions.Add(new ColumnDefinition());
        row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        row2.Children.Add(MoodCalendar());
        var search = SearchCard();
        Grid.SetColumn(search, 2);
        row2.Children.Add(search);
        _content.Children.Add(row2);
    }

    private UIElement Headline()
    {
        var dock = new DockPanel { Margin = new Thickness(0, 30, 0, 0) };
        var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
        Button Nav(string glyph, string name, int step)
        {
            var b = new Button { Content = glyph, Width = 34, Height = 34, ToolTip = name, IsEnabled = step < 0 || _day < Today };
            b.SetResourceReference(StyleProperty, "IconButton");
            System.Windows.Automation.AutomationProperties.SetName(b, name);
            b.Click += (_, _) => GoTo(_day.AddDays(step));
            return b;
        }
        controls.Children.Add(Nav("", "Previous day", -1));
        var today = Styled("Today", "GhostButton", (_, _) => GoTo(Today), new Thickness(12, 5, 12, 5));
        today.Margin = new Thickness(4, 0, 4, 0);
        controls.Children.Add(today);
        controls.Children.Add(Nav("", "Next day", 1));
        DockPanel.SetDock(controls, Dock.Right);
        dock.Children.Add(controls);

        var left = new StackPanel();
        var (current, best) = JournalLogic.Streaks(Entries, Today);
        left.Children.Add(Label(_day == Today ? "TODAY" : PlannerLogic.When(_day, Today).ToUpperInvariant()));
        var big = Big(_day.ToString("dddd, d MMMM"), 44);
        big.Margin = new Thickness(-2, 10, 0, 6);
        left.Children.Add(big);
        int written = Entries.Count(e => !JournalLogic.IsEmpty(e));
        var parts = new List<string>
        {
            current > 0 ? $"{current}-day streak" : "No streak yet: write today to start one",
        };
        if (best > current) parts.Add($"best {best}");
        parts.Add($"{written} {(written == 1 ? "entry" : "entries")}");
        if (JournalLogic.MoodAverage(Entries.Where(e => e.Date > Today.AddDays(-30))) is { } avg)
            parts.Add($"mood lately: {JournalLogic.MoodNames[(int)Math.Round(avg)].ToLowerInvariant()}");
        left.Children.Add(T(string.Join("   ·   ", parts), 13, "Muted"));
        dock.Children.Add(left);
        return dock;
    }

    // ---------------- Editor ----------------

    private UIElement Editor(JournalEntry? entry)
    {
        var stack = new StackPanel();

        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var moods = new StackPanel { Orientation = Orientation.Horizontal };
        for (int m = 1; m <= 5; m++)
        {
            bool on = entry?.Mood == m;
            var dot = new Ellipse { Width = 10, Height = 10, VerticalAlignment = VerticalAlignment.Center };
            dot.Fill = ColorBrush(JournalLogic.MoodColors[m]);
            var name = T(JournalLogic.MoodNames[m], 12, on ? "Text" : "Muted", on ? FontWeights.SemiBold : null);
            name.Margin = new Thickness(7, 0, 0, 0);
            var inner = new StackPanel { Orientation = Orientation.Horizontal };
            inner.Children.Add(dot);
            inner.Children.Add(name);
            var chip = new Border { Child = inner, Padding = new Thickness(10, 5, 12, 5), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 6, 0) };
            if (on) chip.BorderBrush = ColorBrush(JournalLogic.MoodColors[m]); else chip.SetResourceReference(Border.BorderBrushProperty, "Hairline");
            if (on) chip.Background = Backdrop.Wash("TextColor", 0x10);
            var b = Bare(chip, $"Mood {JournalLogic.MoodNames[m]}");
            int captured = m;
            b.Click += (_, _) =>
            {
                var e = GetOrCreate(_day);
                e.Mood = e.Mood == captured ? 0 : captured; // click again to clear
                e.Updated = DateTime.Now;
                App.SaveNow();
                Render();
            };
            moods.Children.Add(b);
        }
        var moodLabel = T("MOOD", 9, "Muted", FontWeights.SemiBold);
        moodLabel.VerticalAlignment = VerticalAlignment.Center;
        moodLabel.Margin = new Thickness(0, 0, 12, 0);
        DockPanel.SetDock(moodLabel, Dock.Left);
        head.Children.Add(moodLabel);
        head.Children.Add(moods);
        stack.Children.Add(head);

        var prompt = T(JournalLogic.PromptFor(_day), 13, "Muted", wrap: true);
        prompt.FontStyle = FontStyles.Italic;
        prompt.Margin = new Thickness(2, 0, 0, 8);
        stack.Children.Add(prompt);

        (_text.Parent as Panel)?.Children.Remove(_text);
        _loadingText = true;
        var want = entry?.Text ?? "";
        if (_text.Text != want) { _text.Text = want; _text.CaretIndex = want.Length; }
        _loadingText = false;
        stack.Children.Add(_text);

        var foot = new DockPanel { Margin = new Thickness(2, 8, 0, 0) };
        foreach (var t in new[] { _words, _tags, _saved }) (t.Parent as Panel)?.Children.Remove(t);
        DockPanel.SetDock(_saved, Dock.Right);
        foot.Children.Add(_saved);
        DockPanel.SetDock(_words, Dock.Left);
        _words.Margin = new Thickness(0, 0, 14, 0);
        foot.Children.Add(_words);
        _tags.TextTrimming = TextTrimming.CharacterEllipsis;
        foot.Children.Add(_tags);
        stack.Children.Add(foot);
        UpdateFooter(entry, saved: false);
        _saved.Text = entry is { } en && !JournalLogic.IsEmpty(en) ? $"Saved {en.Updated:h:mm tt}" : "";

        return Card(stack, new Thickness(18, 16, 18, 14));
    }

    private void OnTextChanged()
    {
        if (_loadingText) return;
        var e = GetOrCreate(_day);
        e.Text = _text.Text;
        e.Updated = DateTime.Now;
        App.SaveSoon();
        UpdateFooter(e, saved: true);
    }

    private void UpdateFooter(JournalEntry? e, bool saved)
    {
        int words = JournalLogic.WordCount(e?.Text ?? "");
        _words.Text = words == 0 ? "" : $"{words} {(words == 1 ? "word" : "words")}";
        var tags = JournalLogic.Tags(e?.Text ?? "");
        _tags.Text = tags.Count == 0 ? (words == 0 ? "Tip: add #tags to find days later" : "") : string.Join("  ", tags.Select(t => "#" + t));
        _tags.SetResourceReference(TextBlock.ForegroundProperty, tags.Count == 0 ? "Muted" : "AccentBrush");
        if (saved) _saved.Text = "Saved";
    }

    // ---------------- That day ----------------

    private UIElement DayCard()
    {
        var stack = new StackPanel();
        stack.Children.Add(Spaced(Label("THAT DAY"), 0, 12));

        var studied = _log is null ? null : StudyStats.PerDay(_log.Sessions, _day, _day).FirstOrDefault();
        if (_log is null) stack.Children.Add(T("Study time shows here when Stint is installed.", 12, "Muted", wrap: true));
        else if (studied is null || studied.TotalSeconds == 0) stack.Children.Add(T("No study logged in Stint.", 12, "Muted"));
        else
        {
            var r = new DockPanel();
            var total = T(StudyStats.Format(studied.TotalSeconds), 13, "Text", FontWeights.SemiBold);
            DockPanel.SetDock(total, Dock.Right);
            r.Children.Add(total);
            r.Children.Add(T("Studied", 13));
            stack.Children.Add(r);
            foreach (var p in studied.Parts.OrderByDescending(p => p.Seconds))
            {
                var line = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
                var v = T(StudyStats.Format(p.Seconds), 11, "Muted");
                DockPanel.SetDock(v, Dock.Right);
                line.Children.Add(v);
                var dot = new Ellipse { Width = 7, Height = 7, Fill = ColorBrush(p.Color), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                DockPanel.SetDock(dot, Dock.Left);
                line.Children.Add(dot);
                line.Children.Add(T(p.Subject, 12));
                stack.Children.Add(line);
            }
        }

        var plans = PlannerLogic.Occurrences(App.Data.Planner, _day, _day);
        stack.Children.Add(Spaced(T("Planned", 13), 14, 2));
        if (plans.Count == 0) stack.Children.Add(T("Nothing in the planner.", 12, "Muted"));
        foreach (var o in plans)
        {
            var line = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var time = T(o.Item.Start is { } s ? PlannerLogic.FormatTime(s) : "All day", 11, "Muted");
            DockPanel.SetDock(time, Dock.Right);
            line.Children.Add(time);
            var dot = new Ellipse { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            dot.SetResourceReference(Shape.FillProperty, PlannerPage.KindBrush(o.Item.Kind));
            DockPanel.SetDock(dot, Dock.Left);
            line.Children.Add(dot);
            var title = T(o.Item.Title, 12, o.Item.Done && !o.Item.Weekly ? "Muted" : "Text");
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            if (o.Item.Done && !o.Item.Weekly) title.TextDecorations = TextDecorations.Strikethrough;
            line.Children.Add(title);
            stack.Children.Add(line);
        }

        var spent = App.Data.Money.Transactions.Where(t => t.Date == _day && t.Kind == Money.MoneyKind.Expense).Sum(t => t.Amount);
        if (spent > 0)
        {
            var r = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
            var v = T(Money.MoneyLogic.Format(spent, App.Data.Money.Currency), 12, "Muted");
            DockPanel.SetDock(v, Dock.Right);
            r.Children.Add(v);
            r.Children.Add(T("Spent", 13));
            stack.Children.Add(r);
        }
        return Card(stack);
    }

    private UIElement? MemoriesCard()
    {
        var memories = JournalLogic.OnThisDay(Entries, _day);
        if (memories.Count == 0) return null;
        var stack = new StackPanel();
        stack.Children.Add(Spaced(Label("ON THIS DAY"), 0, 10));
        foreach (var m in memories) stack.Children.Add(EntryRow(m.Label, m.Entry));
        var card = Card(stack);
        card.Margin = new Thickness(0, 12, 0, 0);
        return card;
    }

    /// <summary>A clickable preview of an entry that opens its day.</summary>
    private Button EntryRow(string label, JournalEntry e)
    {
        var s = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        if (e.Mood > 0)
        {
            var dot = new Ellipse { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) };
            dot.Fill = ColorBrush(JournalLogic.MoodColors[e.Mood]);
            head.Children.Add(dot);
        }
        head.Children.Add(T(label, 11, "Muted", FontWeights.SemiBold));
        s.Children.Add(head);
        var preview = string.Join(" ", e.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var text = T(preview.Length == 0 ? $"Felt {JournalLogic.MoodNames[e.Mood].ToLowerInvariant()}" : preview, 12, "Text");
        text.TextTrimming = TextTrimming.CharacterEllipsis;
        text.MaxHeight = 34;
        text.TextWrapping = TextWrapping.Wrap;
        text.Margin = new Thickness(0, 3, 0, 0);
        s.Children.Add(text);
        var b = Bare(s, $"Open {e.Date:d MMMM yyyy}");
        b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        b.Click += (_, _) => GoTo(e.Date);
        return b;
    }

    // ---------------- Mood calendar ----------------

    private UIElement MoodCalendar()
    {
        var first = new DateOnly(_day.Year, _day.Month, 1);
        var stack = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var avg = JournalLogic.MoodAverage(Entries.Where(e => e.Date.Year == first.Year && e.Date.Month == first.Month));
        var right = T(avg is { } a ? $"average {JournalLogic.MoodNames[(int)Math.Round(a)].ToLowerInvariant()}" : "", 11, "Muted");
        DockPanel.SetDock(right, Dock.Right);
        head.Children.Add(right);
        head.Children.Add(Label($"MOOD IN {first.ToString("MMMM").ToUpperInvariant()}"));
        stack.Children.Add(head);

        var grid = new UniformGrid { Columns = 7 };
        foreach (var d in new[] { "M", "T", "W", "T", "F", "S", "S" })
        {
            var l = T(d, 10, "Muted");
            l.HorizontalAlignment = HorizontalAlignment.Center;
            l.Margin = new Thickness(0, 0, 0, 6);
            grid.Children.Add(l);
        }
        int offset = ((int)first.DayOfWeek + 6) % 7;
        for (int i = 0; i < offset; i++) grid.Children.Add(new Border());
        var byDay = Entries.ToDictionary(e => e.Date);
        for (var d = first; d.Month == first.Month; d = d.AddDays(1))
        {
            byDay.TryGetValue(d, out var e);
            bool written = e is not null && !JournalLogic.IsEmpty(e);
            var cell = new Border { Height = 30, Margin = new Thickness(2), CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(d == _day ? 2 : 0) };
            cell.SetResourceReference(Border.BorderBrushProperty, "Text");
            if (e?.Mood > 0) cell.Background = ColorBrush(JournalLogic.MoodColors[e.Mood]);
            else if (written) cell.Background = Backdrop.Wash("TextColor", 0x30);
            else cell.SetResourceReference(Border.BackgroundProperty, "Raised");
            if (d > Today) cell.Opacity = 0.35;
            var num = T(d.Day.ToString(), 10, "Muted");
            if (e?.Mood > 0) num.Foreground = ColorBrush("#1C1F27");
            num.HorizontalAlignment = HorizontalAlignment.Center;
            num.VerticalAlignment = VerticalAlignment.Center;
            cell.Child = num;
            cell.ToolTip = $"{d:ddd d MMM}" + (e?.Mood > 0 ? $": {JournalLogic.MoodNames[e.Mood]}" : written ? ": written" : "");
            if (d > Today) { grid.Children.Add(cell); continue; }
            var b = Bare(cell, $"Go to {d:d MMMM}");
            var captured = d;
            b.Click += (_, _) => GoTo(captured);
            grid.Children.Add(b);
        }
        stack.Children.Add(grid);

        var legend = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        for (int m = 1; m <= 5; m++)
        {
            var dot = new Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center };
            dot.Fill = ColorBrush(JournalLogic.MoodColors[m]);
            legend.Children.Add(dot);
            legend.Children.Add(Spaced(T(JournalLogic.MoodNames[m], 10, "Muted"), 5, 12, horizontal: true));
        }
        stack.Children.Add(legend);

        var nav = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var prev = Styled("Earlier month", "GhostButton", (_, _) => GoTo(LastDayOf(first.AddMonths(-1))), new Thickness(10, 4, 10, 4));
        nav.Children.Add(prev);
        if (first.AddMonths(1) <= Today)
        {
            var next = Styled("Later month", "GhostButton", (_, _) => GoTo(LastDayOf(first.AddMonths(1))), new Thickness(10, 4, 10, 4));
            next.Margin = new Thickness(6, 0, 0, 0);
            nav.Children.Add(next);
        }
        stack.Children.Add(nav);
        return Card(stack);
    }

    private static DateOnly LastDayOf(DateOnly first) => new(first.Year, first.Month, DateTime.DaysInMonth(first.Year, first.Month));

    // ---------------- Search and recent ----------------

    private UIElement SearchCard()
    {
        var stack = new StackPanel();
        stack.Children.Add(Spaced(Label("LOOK BACK"), 0, 12));
        (_search.Parent as Panel)?.Children.Remove(_search);
        stack.Children.Add(_search);
        var tags = Entries.SelectMany(e => JournalLogic.Tags(e.Text)).GroupBy(t => t).OrderByDescending(g => g.Count()).Take(8).ToList();
        if (tags.Count > 0)
        {
            var wrap = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            foreach (var g in tags)
            {
                var chip = new Border { Padding = new Thickness(9, 3, 9, 3), CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 6, 6), Child = T($"#{g.Key}  {g.Count()}", 11, "Muted") };
                chip.SetResourceReference(Border.BorderBrushProperty, "Hairline");
                var b = Bare(chip, $"Search #{g.Key}");
                var tag = g.Key;
                b.Click += (_, _) => { _search.Text = "#" + tag; _search.CaretIndex = _search.Text.Length; };
                wrap.Children.Add(b);
            }
            stack.Children.Add(wrap);
        }
        (_results.Parent as Panel)?.Children.Remove(_results);
        _results.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(_results);
        ShowResults();
        return Card(stack);
    }

    private void ShowResults()
    {
        _results.Children.Clear();
        bool searching = _query.Trim().TrimStart('#').Length > 0;
        var list = searching
            ? JournalLogic.Search(Entries, _query)
            : Entries.Where(e => !JournalLogic.IsEmpty(e) && e.Date != _day).OrderByDescending(e => e.Date).ToList();
        _results.Children.Add(Spaced(T(searching ? (list.Count == 0 ? "Nothing found." : $"{list.Count} {(list.Count == 1 ? "day" : "days")}") : "Recent", 11, "Muted", FontWeights.SemiBold), 4, 2));
        if (!searching && list.Count == 0) _results.Children.Add(T("Your past entries show up here.", 12, "Muted"));
        foreach (var e in list.Take(8)) _results.Children.Add(EntryRow(e.Date.ToString("ddd d MMM yyyy"), e));
        if (list.Count > 8) _results.Children.Add(T($"and {list.Count - 8} more", 11, "Muted"));
    }

    private static FrameworkElement Spaced(FrameworkElement e, double top, double bottom, bool horizontal = false)
    {
        e.Margin = horizontal ? new Thickness(top, 0, bottom, 0) : new Thickness(0, top, 0, bottom);
        return e;
    }
}
