using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CommandHub.Models;

namespace CommandHub.Tiles.Countdown;

public sealed class CountdownTile : ITileType
{
    public string TypeId => "countdown";
    public string DisplayName => "Countdowns";
    public string Icon => "";
    public IReadOnlyList<TileSize> AllowedSizes { get; } = [TileSize.Small, TileSize.Wide, TileSize.Tall, TileSize.Large];
    public TileSize DefaultSize => TileSize.Wide;
    public string AccentKey => "Accent3Brush";
    public FrameworkElement CreateView(TileContext ctx) => new CountdownView(ctx);
}

/// <summary>Days until the things you're looking forward to. The soonest gets the big number.</summary>
public sealed class CountdownView : UserControl
{
    private readonly TileContext _ctx;
    private List<CountdownEntry> _events;
    private StackPanel? _rows;
    private TextBlock? _error;
    private readonly DispatcherTimer _midnight = new() { Interval = TimeSpan.FromMinutes(10) };

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    public CountdownView(TileContext ctx)
    {
        _ctx = ctx;
        _events = CountdownContent.Read(ctx.Content, Today);
        _midnight.Tick += (_, _) => { if (_rows is null) Show(); };
        Loaded += (_, _) => { App.CommittingEdits += CommitIfEditing; _midnight.Start(); };
        Unloaded += (_, _) => { _midnight.Stop(); App.CommittingEdits -= CommitIfEditing; CommitIfEditing(); };
        Show();
    }

    private void Show()
    {
        _rows = null;
        var root = new Grid();
        var edit = TileUi.EditButton("Edit countdowns");
        edit.Click += (_, _) => Edit();

        var upcoming = CountdownContent.Upcoming(_events, Today);
        if (upcoming.Count == 0)
        {
            var empty = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var hint = TileUi.Text("Count down to a trip, an exam or a birthday.", 12, "Muted");
            hint.TextWrapping = TextWrapping.Wrap;
            empty.Children.Add(hint);
            var add = TileUi.Ghost("Add a countdown", (_, _) => Edit());
            add.HorizontalAlignment = HorizontalAlignment.Left;
            add.Margin = new Thickness(0, 8, 0, 0);
            empty.Children.Add(add);
            root.Children.Add(empty);
            root.Children.Add(edit);
            Content = root;
            return;
        }

        var first = upcoming[0];
        int days = first.Date.DayNumber - Today.DayNumber;
        var hero = new StackPanel { Orientation = Orientation.Horizontal };
        var number = TileUi.Display(days == 0 ? "Today" : days.ToString(), 40, "Accent3Brush", FontWeights.SemiBold);
        hero.Children.Add(number);
        var heroText = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        heroText.Children.Add(TileUi.Text(first.Name, 14, "Text", semibold: true));
        heroText.Children.Add(TileUi.Text(days == 0 ? first.Date.ToString("dddd, MMM d") : (days == 1 ? "day to go" : "days to go") + " · " + first.Date.ToString("MMM d"), 11, "Muted"));
        hero.Children.Add(heroText);

        var list = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        foreach (var e in upcoming.Skip(1))
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 5) };
            var label = TileUi.Text(CountdownContent.Label(e.Date, Today), 12, "Accent3Brush");
            DockPanel.SetDock(label, Dock.Right);
            row.Children.Add(label);
            row.Children.Add(TileUi.Text(e.Name, 12, "Text"));
            list.Children.Add(row);
        }

        var stack = new StackPanel();
        stack.Children.Add(hero);
        stack.Children.Add(list);
        root.Children.Add(new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false });
        root.Children.Add(edit);
        Content = root;
    }

    // ---------------- Editing ----------------

    private void Edit()
    {
        _rows = new StackPanel();
        foreach (var e in _events.OrderBy(e => e.Date)) AddRow(e.Name, e.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (_events.Count == 0) AddRow("", "");

        _error = TileUi.Text("", 11, "Danger");
        _error.TextWrapping = TextWrapping.Wrap;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        buttons.Children.Add(TileUi.Ghost("Add", (_, _) => AddRow("", "")));
        var done = TileUi.Ghost("Done", (_, _) => { if (CommitEdits()) Show(); }, accent: true);
        done.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(done);

        var dock = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        DockPanel.SetDock(_error, Dock.Bottom);
        dock.Children.Add(buttons);
        dock.Children.Add(_error);
        dock.Children.Add(new ScrollViewer { Content = _rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false });
        Content = dock;
    }

    private void AddRow(string name, string date)
    {
        if (_rows is null) return;
        var nameBox = TileUi.Field("What", name, "Countdown name");
        var dateBox = TileUi.Field("2026-12-31", date, "Countdown date");
        dateBox.Width = 96;
        dateBox.Margin = new Thickness(6, 0, 0, 0);
        var remove = TileUi.IconButton("", "Remove countdown", 24);
        remove.FontSize = 9;
        remove.Margin = new Thickness(4, 0, 0, 0);
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(remove, Dock.Right);
        DockPanel.SetDock(dateBox, Dock.Right);
        row.Children.Add(remove);
        row.Children.Add(dateBox);
        row.Children.Add(nameBox);
        remove.Click += (_, _) => _rows.Children.Remove(row);
        _rows.Children.Add(row);
    }

    private static bool TryDate(string text, out DateOnly date) =>
        DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
        || DateOnly.TryParse(text.Trim(), CultureInfo.CurrentCulture, DateTimeStyles.None, out date);

    /// <summary>Saves the rows. Returns false (and keeps editing) when a filled-in row has a date it can't read.</summary>
    private bool CommitEdits()
    {
        if (_rows is null) return true;
        var list = new List<CountdownEntry>();
        foreach (var boxes in _rows.Children.OfType<DockPanel>().Select(r => r.Children.OfType<TextBox>().ToArray()))
        {
            var dateBox = boxes.First(b => (string)b.Tag == "2026-12-31");
            var nameBox = boxes.First(b => b != dateBox);
            if (nameBox.Text.Trim().Length == 0 && dateBox.Text.Trim().Length == 0) continue;
            if (!TryDate(dateBox.Text, out var d))
            {
                if (_error != null) _error.Text = "Use a date like 2026-12-31.";
                dateBox.Focus();
                return false;
            }
            list.Add(new CountdownEntry(nameBox.Text.Trim().Length > 0 ? nameBox.Text.Trim() : "Countdown", d));
        }
        _events = list;
        CountdownContent.Write(_ctx.Content, _events);
        _ctx.SaveNow();
        return true;
    }

    /// <summary>On rebuild or app close: keep what can be saved (rows with bad dates are dropped rather than blocking).</summary>
    private void CommitIfEditing()
    {
        // Save and close: an editor left open would later write its (now stale) rows over newer content.
        if (_rows is null) return;
        if (CommitEdits()) { Show(); return; }
        foreach (var row in _rows.Children.OfType<DockPanel>().ToList())
        {
            var boxes = row.Children.OfType<TextBox>().ToArray();
            if (boxes.Any(b => (string)b.Tag == "2026-12-31" && b.Text.Trim().Length > 0 && !TryDate(b.Text, out _)))
                _rows.Children.Remove(row);
        }
        CommitEdits();
        Show();
    }
}
