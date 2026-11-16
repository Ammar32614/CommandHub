using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using CommandHub.AddOns;
using CommandHub.Home;
using static CommandHub.Home.PageUi;

namespace CommandHub.AddOn.Flashcards;

public sealed class FlashcardsAddOn : IAddOn
{
    private Store? _store;

    public void Start(IAddOnHost host)
    {
        _store = new Store(host);
        _store.Load();
    }

    public UIElement CreatePage() => new FlashcardsPage(_store!);
}

public sealed class Store(IAddOnHost host)
{
    public FlashData Data { get; private set; } = new();
    public event Action? Changed;
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    public void Load()
    {
        Data = host.Load<FlashData>("flashcards");
        Data.Decks ??= [];
        Data.Log ??= [];
        foreach (var d in Data.Decks) d.Cards ??= [];
        if (Data.NewPerDay is < 1 or > 200) Data.NewPerDay = 10;
        if (Data.Decks.Count == 0) { Data.Decks.Add(Cards.Hiragana(Today)); Save(false); }
    }

    public void Save(bool notify = true)
    {
        try { host.Save("flashcards", Data); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { host.Notify("Flashcards: couldn't save. " + ex.Message); }
        if (notify) Changed?.Invoke();
    }
}

/// <summary>The Flashcards tab: decks with what's due, a streak, and study sessions.</summary>
public sealed class FlashcardsPage : UserControl
{
    private readonly Store _store;
    private readonly Border _base = new(), _glow = new() { IsHitTestVisible = false };
    private readonly StackPanel _content = new() { Margin = new Thickness(33, 0, 33, 40) };

    public FlashcardsPage(Store store)
    {
        _store = store;
        var scroll = new ScrollViewer { Content = _content, Focusable = false, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        WindowChrome.SetIsHitTestVisibleInChrome(_content, true);
        _ = new SmoothScroll(scroll);
        var layers = new Grid();
        layers.Children.Add(_base);
        layers.Children.Add(_glow);
        layers.Children.Add(scroll);
        Content = layers;
        Loaded += (_, _) => { App.ThemeChanged += OnTheme; _store.Changed += OnChanged; Backdrop.Paint(_base, _glow); Render(); };
        Unloaded += (_, _) => { App.ThemeChanged -= OnTheme; _store.Changed -= OnChanged; };
        Backdrop.Paint(_base, _glow);
    }

    private void OnTheme() { Backdrop.Paint(_base, _glow); Render(); }
    private void OnChanged() => Dispatcher.BeginInvoke(Render);
    private FlashData D => _store.Data;
    internal static MainWindow? Main => Application.Current.MainWindow as MainWindow;

    private void Render()
    {
        _content.Children.Clear();
        var today = Store.Today;
        var top = new DockPanel { Height = 40 };
        var crumb = T($"FLASHCARDS   /   {DateTime.Now.ToString("dddd, MMMM d").ToUpperInvariant()}", 11, "Muted");
        crumb.VerticalAlignment = VerticalAlignment.Center;
        crumb.Margin = new Thickness(0, 6, 0, 0);
        top.Children.Add(crumb);
        _content.Children.Add(top);

        int due = D.Decks.Sum(d => Cards.Queue(d, today, D.NewPerDay).Count);
        int streak = Cards.Streak(D.Log, today);
        var todayLog = D.Log.FirstOrDefault(l => l.Date == today);
        var head = new DockPanel { Margin = new Thickness(0, 30, 0, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
        actions.Children.Add(Styled("+ New deck", "GhostButton", (_, _) => new DeckSheet(_store, null).Show(), new Thickness(14, 5, 14, 5)));
        DockPanel.SetDock(actions, Dock.Right);
        head.Children.Add(actions);
        var left = new StackPanel();
        left.Children.Add(Label("SPACED REPETITION"));
        var big = Big(due == 0 ? "All caught up" : $"{due} {(due == 1 ? "card" : "cards")} to study", 46);
        big.Margin = new Thickness(-2, 10, 0, 6);
        left.Children.Add(big);
        var line = new List<string> { streak > 0 ? $"{streak}-day streak" : "Study today to start a streak" };
        if (todayLog is { Reviewed: > 0 } l) line.Add($"{l.Reviewed} reviewed today, {Math.Round(100.0 * l.Remembered / l.Reviewed)}% remembered");
        line.Add($"{D.NewPerDay} new cards a day per deck");
        left.Children.Add(T(string.Join("   ·   ", line), 13, "Muted"));
        head.Children.Add(left);
        _content.Children.Add(head);

        var grid = new WrapPanel { Margin = new Thickness(0, 24, 0, 0) };
        foreach (var deck in D.Decks) grid.Children.Add(DeckCard(deck, today));
        _content.Children.Add(grid);

        var tip = T("Tip: paste a word list into a deck (one per line, like  猫 ; cat ; neko) to make dozens of cards at once. Keys while studying: Space shows the answer, 1–4 answer.", 12, "Muted", wrap: true);
        tip.Margin = new Thickness(0, 10, 0, 0);
        tip.MaxWidth = 760;
        tip.HorizontalAlignment = HorizontalAlignment.Left;
        _content.Children.Add(tip);
    }

    private FrameworkElement DeckCard(Deck deck, DateOnly today)
    {
        var queue = Cards.Queue(deck, today, D.NewPerDay);
        int reviews = queue.Count(c => c.Due is not null), fresh = queue.Count - reviews;
        var s = new StackPanel { Width = 280 };
        var title = T(deck.Name, 16, "Text", FontWeights.SemiBold);
        title.TextWrapping = TextWrapping.Wrap;
        s.Children.Add(title);
        s.Children.Add(T($"{deck.Cards.Count} cards · {Cards.Learned(deck)} learned · {Cards.Mature(deck)} well known", 11, "Muted"));
        var counts = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 12) };
        counts.Children.Add(Big(reviews.ToString(), 30));
        counts.Children.Add(Spaced(T("to review", 11, "Muted"), 6, 0, 18));
        counts.Children.Add(Big(fresh.ToString(), 30));
        counts.Children.Add(Spaced(T("new", 11, "Muted"), 6, 0, 0));
        s.Children.Add(counts);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var study = Styled(queue.Count == 0 ? "Done for today" : "Study", "AccentButton", (_, _) => new StudySheet(_store, deck).Show(), new Thickness(18, 6, 18, 6));
        study.IsEnabled = queue.Count > 0;
        System.Windows.Automation.AutomationProperties.SetName(study, $"Study {deck.Name}");
        buttons.Children.Add(study);
        var edit = Styled("Cards", "GhostButton", (_, _) => new DeckSheet(_store, deck).Show(), new Thickness(14, 6, 14, 6));
        edit.Margin = new Thickness(8, 0, 0, 0);
        System.Windows.Automation.AutomationProperties.SetName(edit, $"Edit {deck.Name}");
        buttons.Children.Add(edit);
        s.Children.Add(buttons);
        var card = Card(s, new Thickness(18, 16, 18, 16));
        card.Margin = new Thickness(0, 0, 14, 14);
        card.VerticalAlignment = VerticalAlignment.Top;
        return card;
    }

    private static FrameworkElement Spaced(FrameworkElement e, double left, double top, double right)
    {
        e.Margin = new Thickness(left, top, right, 0);
        e.VerticalAlignment = VerticalAlignment.Bottom;
        return e;
    }

    internal static void ShowCard(UIElement content, double width)
    {
        var card = new Border
        {
            MaxWidth = width, Margin = new Thickness(24), MaxHeight = 820, CornerRadius = new CornerRadius(16), Padding = new Thickness(24, 20, 24, 22), BorderThickness = new Thickness(1),
            BorderBrush = Backdrop.Wash("TextColor", 0x24), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center,
            Child = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false },
        };
        card.SetResourceReference(Border.BackgroundProperty, "Surface");
        card.MouseLeftButtonDown += (_, e) => e.Handled = true;
        var scrim = new Grid { Focusable = true };
        scrim.SetResourceReference(Panel.BackgroundProperty, "Scrim");
        scrim.MouseLeftButtonDown += (_, _) => Main?.HideSheet();
        scrim.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Main?.HideSheet(); };
        scrim.Children.Add(card);
        Main?.ShowSheet(scrim);
    }
}

/// <summary>A study session: the front, then the answer and four buttons that say when you'll see it again.</summary>
internal sealed class StudySheet(Store store, Deck deck)
{
    private readonly StackPanel _root = new() { Focusable = true };
    private List<Card> _queue = [];
    private bool _revealed;
    private int _done;

    public void Show()
    {
        _queue = Cards.Queue(deck, Store.Today, store.Data.NewPerDay);
        _root.PreviewKeyDown += (_, e) =>
        {
            if (e.OriginalSource is TextBox) return;
            if (!_revealed && (e.Key == Key.Space || e.Key == Key.Enter)) { _revealed = true; Render(); e.Handled = true; }
            else if (_revealed && e.Key is >= Key.D1 and <= Key.D4) { Answer((Answer)(e.Key - Key.D1)); e.Handled = true; }
            else if (_revealed && e.Key is >= Key.NumPad1 and <= Key.NumPad4) { Answer((Answer)(e.Key - Key.NumPad1)); e.Handled = true; }
        };
        _root.Unloaded += (_, _) => store.Save();
        Render();
        FlashcardsPage.ShowCard(_root, 640);
        _root.Dispatcher.BeginInvoke(() => _root.Focus(), DispatcherPriority.Input);
    }

    private void Answer(Answer a)
    {
        if (_queue.Count == 0) return;
        var card = _queue[0];
        _queue.RemoveAt(0);
        Cards.Answer(card, a, Store.Today);
        Cards.LogAnswer(store.Data, Store.Today, a != Flashcards.Answer.Again);
        if (a == Flashcards.Answer.Again) _queue.Insert(Math.Min(_queue.Count, 3), card); // see it again in a moment
        else _done++;
        _revealed = false;
        store.Save(notify: false);
        Render();
    }

    private void Render()
    {
        _root.Children.Clear();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
        var close = Bare(Glyph("", 11, "Muted"), "Close");
        close.Click += (_, _) => FlashcardsPage.Main?.HideSheet();
        DockPanel.SetDock(close, Dock.Right);
        head.Children.Add(close);
        head.Children.Add(T($"{deck.Name.ToUpperInvariant()}   ·   {_done} done, {_queue.Count} to go", 10, "Muted", FontWeights.SemiBold));
        _root.Children.Add(head);

        if (_queue.Count == 0)
        {
            _root.Children.Add(Big("Done for today", 38));
            _root.Children.Add(T($"You studied {_done} {(_done == 1 ? "card" : "cards")}. New ones unlock tomorrow.", 13, "Muted"));
            var fin = Styled("Close", "AccentButton", (_, _) => FlashcardsPage.Main?.HideSheet(), new Thickness(18, 6, 18, 6));
            fin.HorizontalAlignment = HorizontalAlignment.Left;
            fin.Margin = new Thickness(0, 18, 0, 0);
            _root.Children.Add(fin);
            return;
        }
        var card = _queue[0];
        var front = T(card.Front, card.Front.Length <= 4 ? 72 : 36, "Text");
        front.TextWrapping = TextWrapping.Wrap;
        front.HorizontalAlignment = HorizontalAlignment.Center;
        front.TextAlignment = TextAlignment.Center;
        front.Margin = new Thickness(0, 10, 0, 10);
        _root.Children.Add(front);
        if (card.Due is null)
        {
            var n = T("NEW", 9, "AccentBrush", FontWeights.SemiBold);
            n.HorizontalAlignment = HorizontalAlignment.Center;
            _root.Children.Add(n);
        }
        var rule = new Border { Height = 1, Margin = new Thickness(0, 16, 0, 16) };
        rule.SetResourceReference(Border.BackgroundProperty, "Hairline");
        _root.Children.Add(rule);

        if (!_revealed)
        {
            var show = Styled("Show answer   (Space)", "AccentButton", (_, _) => { _revealed = true; Render(); }, new Thickness(22, 8, 22, 8));
            System.Windows.Automation.AutomationProperties.SetName(show, "Show answer");
            show.HorizontalAlignment = HorizontalAlignment.Center;
            _root.Children.Add(show);
            return;
        }
        var back = T(card.Back, 26, "Text", FontWeights.SemiBold);
        back.TextWrapping = TextWrapping.Wrap;
        back.HorizontalAlignment = HorizontalAlignment.Center;
        back.TextAlignment = TextAlignment.Center;
        _root.Children.Add(back);
        foreach (var extra in new[] { card.Reading, card.Example }.Where(x => x.Length > 0))
        {
            var t = T(extra, 14, "Muted", wrap: true);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.TextAlignment = TextAlignment.Center;
            t.Margin = new Thickness(0, 6, 0, 0);
            _root.Children.Add(t);
        }
        var row = new UniformGrid4();
        foreach (var a in Enum.GetValues<Answer>())
        {
            var label = new StackPanel();
            var name = T($"{(int)a + 1}  {a}", 13, "Text", FontWeights.SemiBold);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            label.Children.Add(name);
            var when = T(Cards.Spell(Cards.Preview(card, a)), 11, "Muted");
            when.HorizontalAlignment = HorizontalAlignment.Center;
            label.Children.Add(when);
            var b = new Button { Content = label, Margin = new Thickness(4), Padding = new Thickness(6, 8, 6, 8) };
            b.SetResourceReference(FrameworkElement.StyleProperty, a == Flashcards.Answer.Good ? "AccentButton" : "GhostButton");
            System.Windows.Automation.AutomationProperties.SetName(b, $"Answer {a}");
            var captured = a;
            b.Click += (_, _) => Answer(captured);
            row.Children.Add(b);
        }
        row.Margin = new Thickness(0, 22, 0, 0);
        _root.Children.Add(row);
    }

    private sealed class UniformGrid4 : System.Windows.Controls.Primitives.UniformGrid { public UniformGrid4() { Columns = 4; } }
}

/// <summary>Add cards (one at a time or pasted in bulk), browse and delete them, rename or delete the deck.</summary>
internal sealed class DeckSheet(Store store, Deck? deck)
{
    private readonly StackPanel _root = new();
    private string _filter = "";

    public void Show()
    {
        _root.Unloaded += (_, _) => store.Save();
        Render();
        FlashcardsPage.ShowCard(_root, 760);
    }

    private void Render()
    {
        _root.Children.Clear();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var close = Bare(Glyph("", 11, "Muted"), "Close");
        close.Click += (_, _) => FlashcardsPage.Main?.HideSheet();
        DockPanel.SetDock(close, Dock.Right);
        head.Children.Add(close);
        head.Children.Add(Label(deck is null ? "NEW DECK" : "DECK"));
        _root.Children.Add(head);

        var name = Field("e.g. Japanese: anime words", deck?.Name ?? "", "Deck name");
        _root.Children.Add(name);
        if (deck is null)
        {
            var create = Styled("Create deck", "AccentButton", (_, _) =>
            {
                if (name.Text.Trim().Length == 0) { name.Focus(); return; }
                var d = new Deck { Name = name.Text.Trim() };
                store.Data.Decks.Add(d);
                store.Save();
                FlashcardsPage.Main?.HideSheet();
                new DeckSheet(store, d).Show();
            }, new Thickness(18, 6, 18, 6));
            create.HorizontalAlignment = HorizontalAlignment.Left;
            create.Margin = new Thickness(0, 12, 0, 0);
            _root.Children.Add(create);
            name.KeyDown += (_, e) => { if (e.Key == Key.Enter) create.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); };
            name.Dispatcher.BeginInvoke(() => name.Focus(), DispatcherPriority.Input);
            return;
        }
        name.LostFocus += (_, _) => { if (name.Text.Trim().Length > 0) { deck.Name = name.Text.Trim(); store.Save(false); } };

        // Add one card
        _root.Children.Add(Gap(Label("ADD A CARD", "Muted"), 16, 6));
        var one = new WrapPanel();
        var front = Field("Front (word)", "", "Card front"); front.Width = 170; front.Margin = new Thickness(0, 0, 6, 6);
        var back = Field("Back (meaning)", "", "Card back"); back.Width = 170; back.Margin = new Thickness(0, 0, 6, 6);
        var reading = Field("Reading (optional)", "", "Card reading"); reading.Width = 150; reading.Margin = new Thickness(0, 0, 6, 6);
        var example = Field("Example (optional)", "", "Card example"); example.Width = 200; example.Margin = new Thickness(0, 0, 6, 6);
        one.Children.Add(front); one.Children.Add(back); one.Children.Add(reading); one.Children.Add(example);
        void AddOne()
        {
            if (front.Text.Trim().Length == 0 || back.Text.Trim().Length == 0) { (front.Text.Trim().Length == 0 ? front : back).Focus(); return; }
            deck.Cards.Add(new Card { Front = front.Text.Trim(), Back = back.Text.Trim(), Reading = reading.Text.Trim(), Example = example.Text.Trim(), Added = Store.Today });
            store.Save(false);
            Render();
            if (FindBox("Card front") is { } f) f.Dispatcher.BeginInvoke(() => f.Focus(), DispatcherPriority.Input);
        }
        foreach (var b in new[] { front, back, reading, example }) b.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; AddOne(); } };
        var add = Styled("Add", "AccentButton", (_, _) => AddOne(), new Thickness(16, 6, 16, 6));
        System.Windows.Automation.AutomationProperties.SetName(add, "Add card");
        one.Children.Add(add);
        _root.Children.Add(one);

        // Paste many
        _root.Children.Add(Gap(Label("OR PASTE A LIST", "Muted"), 12, 6));
        var paste = Field("One card per line:  front ; back ; reading ; example   (tabs from a spreadsheet work too)", "", "Paste cards");
        paste.AcceptsReturn = true;
        paste.TextWrapping = TextWrapping.Wrap;
        paste.MinHeight = 80;
        paste.VerticalContentAlignment = VerticalAlignment.Top;
        _root.Children.Add(paste);
        var result = T("", 12, "Muted");
        var import = Styled("Add these cards", "GhostButton", (_, _) =>
        {
            var cards = Cards.Parse(paste.Text, Store.Today);
            if (cards.Count == 0) { result.Text = "No cards found: put the front and back on each line, separated by ; or a tab."; return; }
            deck.Cards.AddRange(cards);
            store.Save(false);
            Render();
        }, new Thickness(14, 5, 14, 5));
        System.Windows.Automation.AutomationProperties.SetName(import, "Import pasted cards");
        import.HorizontalAlignment = HorizontalAlignment.Left;
        import.Margin = new Thickness(0, 8, 0, 0);
        _root.Children.Add(import);
        _root.Children.Add(result);

        // Browse
        _root.Children.Add(Gap(Label($"{deck.Cards.Count} CARDS", "Muted"), 16, 6));
        var search = Field("Find a card", _filter, "Find card");
        search.TextChanged += (_, _) => { _filter = search.Text; RenderList(); };
        _root.Children.Add(search);
        _list.Margin = new Thickness(0, 8, 0, 0);
        (_list.Parent as Panel)?.Children.Remove(_list);
        _root.Children.Add(_list);
        RenderList();

        bool armed = false;
        Button? del = null;
        del = Styled("Delete deck", "GhostButton", (_, _) =>
        {
            if (!armed) { armed = true; del!.Content = $"Delete all {deck.Cards.Count} cards too? Click again"; return; }
            store.Data.Decks.Remove(deck);
            store.Save();
            FlashcardsPage.Main?.HideSheet();
        }, new Thickness(12, 5, 12, 5));
        del.HorizontalAlignment = HorizontalAlignment.Left;
        del.Margin = new Thickness(0, 16, 0, 0);
        _root.Children.Add(del);
    }

    private readonly StackPanel _list = new();

    private void RenderList()
    {
        _list.Children.Clear();
        if (deck is null) return;
        var f = _filter.Trim();
        var cards = deck.Cards.Where(c => f.Length == 0 || c.Front.Contains(f, StringComparison.CurrentCultureIgnoreCase) || c.Back.Contains(f, StringComparison.CurrentCultureIgnoreCase)).ToList();
        foreach (var c in cards.Take(60))
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var x = Bare(Glyph("", 9, "Muted"), $"Delete card {c.Front}");
            x.Click += (_, _) => { deck.Cards.Remove(c); store.Save(false); RenderList(); };
            DockPanel.SetDock(x, Dock.Right);
            row.Children.Add(x);
            var due = T(c.Due is null ? "new" : c.Due <= Store.Today ? "due" : $"in {Cards.Spell(c.Due.Value.DayNumber - Store.Today.DayNumber)}", 11, "Muted");
            due.Width = 70;
            DockPanel.SetDock(due, Dock.Right);
            row.Children.Add(due);
            var text = T($"{c.Front}   ·   {c.Back}" + (c.Reading.Length > 0 ? $"   ({c.Reading})" : ""), 13);
            row.Children.Add(text);
            _list.Children.Add(row);
        }
        if (cards.Count > 60) _list.Children.Add(T($"and {cards.Count - 60} more", 11, "Muted"));
    }

    private TextBox? FindBox(string name) =>
        Descendants(_root).OfType<TextBox>().FirstOrDefault(t => System.Windows.Automation.AutomationProperties.GetName(t) == name);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject d)
    {
        foreach (var c in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>())
        {
            yield return c;
            foreach (var x in Descendants(c)) yield return x;
        }
    }

    private static FrameworkElement Gap(FrameworkElement e, double top, double bottom)
    {
        e.Margin = new Thickness(0, top, 0, bottom);
        return e;
    }
}
