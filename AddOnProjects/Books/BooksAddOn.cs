using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using CommandHub.AddOns;
using CommandHub.Home;
using static CommandHub.Home.PageUi;

namespace CommandHub.AddOn.Books;

public sealed class BooksAddOn : IAddOn
{
    private Shelf? _shelf;

    public void Start(IAddOnHost host)
    {
        _shelf = new Shelf(host);
        _shelf.Load();
    }

    public UIElement CreatePage() => new BooksPage(_shelf!);
}

public sealed class Shelf(IAddOnHost host)
{
    public BooksData Data { get; private set; } = new();
    public HttpClient Http => host.Http;
    public event Action? Changed;
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    public void Load()
    {
        Data = host.Load<BooksData>("books");
        Data.Books ??= [];
        foreach (var b in Data.Books) { b.Quotes ??= []; b.Log ??= []; b.Notes ??= ""; b.Author ??= ""; }
        if (Data.YearGoal is < 1 or > 1000) Data.YearGoal = 12;
    }

    public void Save(bool notify = true)
    {
        try { host.Save("books", Data); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { host.Notify("Books: couldn't save. " + ex.Message); }
        if (notify) Changed?.Invoke();
    }

    public async Task<List<FoundBook>> Search(string term)
    {
        var url = "https://openlibrary.org/search.json?limit=24&fields=key,title,author_name,first_publish_year,number_of_pages_median,cover_i&q=" + Uri.EscapeDataString(term.Trim());
        return BookLogic.ParseOpenLibrary(await host.Http.GetStringAsync(url));
    }
}

/// <summary>The Books tab: Reading, Want to read, Read, Quotes and Find, with a yearly goal.</summary>
public sealed class BooksPage : UserControl
{
    private enum View { Reading, Want, Read, Quotes, Find }

    private readonly Shelf _shelf;
    private readonly Border _base = new(), _glow = new() { IsHitTestVisible = false };
    private readonly StackPanel _content = new() { Margin = new Thickness(33, 0, 33, 40) };
    private readonly TextBox _search = Field("Title, author or ISBN", "", "Search books");
    private View _view;
    private List<FoundBook>? _results;
    private string _status = "";
    private bool _busy;

    public BooksPage(Shelf shelf)
    {
        _shelf = shelf;
        _view = shelf.Data.Books.Any(b => b.Status == ReadStatus.Reading) ? View.Reading : View.Find;
        var scroll = new ScrollViewer { Content = _content, Focusable = false, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        WindowChrome.SetIsHitTestVisibleInChrome(_content, true);
        _ = new SmoothScroll(scroll);
        var layers = new Grid();
        layers.Children.Add(_base);
        layers.Children.Add(_glow);
        layers.Children.Add(scroll);
        Content = layers;
        _search.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await RunSearch(); } };
        Loaded += (_, _) => { App.ThemeChanged += OnTheme; _shelf.Changed += OnChanged; Backdrop.Paint(_base, _glow); Render(); };
        Unloaded += (_, _) => { App.ThemeChanged -= OnTheme; _shelf.Changed -= OnChanged; };
        Backdrop.Paint(_base, _glow);
    }

    private void OnTheme() { Backdrop.Paint(_base, _glow); Render(); }
    private void OnChanged() => Dispatcher.BeginInvoke(Render);
    private List<Book> Books => _shelf.Data.Books;
    internal static MainWindow? Main => Application.Current.MainWindow as MainWindow;

    private void Render()
    {
        _content.Children.Clear();
        var today = Shelf.Today;
        var top = new DockPanel { Height = 40 };
        var crumb = T($"BOOKS   /   {DateTime.Now.ToString("dddd, MMMM d").ToUpperInvariant()}", 11, "Muted");
        crumb.VerticalAlignment = VerticalAlignment.Center;
        crumb.Margin = new Thickness(0, 6, 0, 0);
        top.Children.Add(crumb);
        _content.Children.Add(top);

        var y = BookLogic.Year(Books, _shelf.Data.YearGoal, today);
        var pace = BookLogic.PagesPerDay(Books, today);
        var head = new DockPanel { Margin = new Thickness(0, 30, 0, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
        actions.Children.Add(Styled("Yearly goal", "GhostButton", (_, _) => GoalSheet(), new Thickness(14, 5, 14, 5)));
        DockPanel.SetDock(actions, Dock.Right);
        head.Children.Add(actions);
        var left = new StackPanel();
        left.Children.Add(Label($"YOUR READING IN {today.Year}"));
        var big = Big($"{y.Finished} of {y.Goal} books", 46);
        big.Margin = new Thickness(-2, 10, 0, 6);
        left.Children.Add(big);
        var line = new List<string>
        {
            y.Finished >= y.Goal ? "goal reached" : y.Finished >= y.ExpectedByNow ? "on track for your goal" : $"{y.ExpectedByNow - y.Finished} behind your goal's pace",
            $"{y.PagesRead:N0} pages this year",
        };
        if (pace > 0) line.Add($"{pace:0.#} pages a day lately");
        if (y.AverageRating is { } r) line.Add($"average {r:0.#}★");
        left.Children.Add(T(string.Join("   ·   ", line), 13, "Muted"));
        left.Children.Add(Gap(Bar(y.Goal > 0 ? Math.Min(1, y.Finished / (double)y.Goal) : 0), 10, 0));
        left.MaxWidth = 720;
        left.HorizontalAlignment = HorizontalAlignment.Left;
        head.Children.Add(left);
        _content.Children.Add(head);

        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 24, 0, 18) };
        foreach (var (v, text) in new[]
        {
            (View.Reading, $"Reading  {Books.Count(b => b.Status == ReadStatus.Reading)}"), (View.Want, $"Want to read  {Books.Count(b => b.Status == ReadStatus.WantToRead)}"),
            (View.Read, $"Read  {Books.Count(b => b.Status == ReadStatus.Read)}"), (View.Quotes, "Quotes"), (View.Find, "Find books"),
        })
        {
            bool on = v == _view;
            var s = new StackPanel { Margin = new Thickness(0, 0, 26, 0) };
            s.Children.Add(T(text, 14, on ? "Text" : "Muted", on ? FontWeights.SemiBold : null));
            var bar = new Border { Height = 3, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 7, 0, 0) };
            if (on) bar.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            s.Children.Add(bar);
            var b = Bare(s, $"Show {v}");
            var captured = v;
            b.Click += (_, _) => { _view = captured; Render(); };
            tabs.Children.Add(b);
        }
        _content.Children.Add(tabs);

        switch (_view)
        {
            case View.Reading: ShowShelf(Books.Where(b => b.Status == ReadStatus.Reading).ToList(), "Not reading anything", "Find a book and start it, or move one from Want to read.", pace); break;
            case View.Want: ShowShelf(Books.Where(b => b.Status == ReadStatus.WantToRead).OrderByDescending(b => b.AddedUtc).ToList(), "Nothing on your list", "Find books to add.", pace); break;
            case View.Read:
                foreach (var year in Books.Where(b => b.Status is ReadStatus.Read or ReadStatus.Abandoned).GroupBy(b => b.Finished?.Year ?? 0).OrderByDescending(g => g.Key))
                {
                    _content.Children.Add(Gap(Label(year.Key == 0 ? "STOPPED" : year.Key.ToString(), "Muted"), 6, 10));
                    ShowShelf(year.OrderByDescending(b => b.Finished).ToList(), "", "", pace);
                }
                if (!Books.Any(b => b.Status is ReadStatus.Read or ReadStatus.Abandoned)) Empty("Nothing finished yet", "Books you finish show up here, by year.");
                break;
            case View.Quotes: Quotes(); break;
            case View.Find: Find(); break;
        }
    }

    private void Empty(string title, string text)
    {
        var e = new StackPanel { Margin = new Thickness(0, 10, 0, 0), MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left };
        e.Children.Add(T(title, 18));
        e.Children.Add(Gap(T(text, 13, "Muted", wrap: true), 6, 0));
        _content.Children.Add(e);
    }

    internal static FrameworkElement Gap(FrameworkElement e, double top, double bottom)
    {
        e.Margin = new Thickness(0, top, 0, bottom);
        return e;
    }

    internal static Grid Bar(double fraction)
    {
        var g = new Grid { Height = 5 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, fraction), GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, 1 - fraction), GridUnitType.Star) });
        var track = new Border { CornerRadius = new CornerRadius(3) };
        track.SetResourceReference(Border.BackgroundProperty, "Raised");
        Grid.SetColumnSpan(track, 2);
        g.Children.Add(track);
        var fill = new Border { CornerRadius = new CornerRadius(3) };
        fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        g.Children.Add(fill);
        return g;
    }

    internal static Border Cover(string url, double w, double h)
    {
        var b = new Border { Width = w, Height = h, CornerRadius = new CornerRadius(6) };
        b.SetResourceReference(Border.BackgroundProperty, "Raised");
        if (url.Length > 0 && Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            try
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.UriSource = uri;
                img.DecodePixelWidth = (int)(w * 1.5);
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                b.Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
            }
            catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException) { }
        }
        else
        {
            var g = Glyph("", 26, "Muted");
            g.HorizontalAlignment = HorizontalAlignment.Center;
            g.VerticalAlignment = VerticalAlignment.Center;
            b.Child = g;
        }
        return b;
    }

    private void ShowShelf(List<Book> books, string emptyTitle, string emptyText, double pace)
    {
        if (books.Count == 0) { if (emptyTitle.Length > 0) Empty(emptyTitle, emptyText); return; }
        var grid = new WrapPanel();
        foreach (var b in books)
        {
            var s = new StackPanel { Width = 150 };
            var art = new Grid();
            art.Children.Add(Cover(b.CoverUrl(), 150, 225));
            if (b.Rating > 0)
            {
                var r = new Border { CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 1, 6, 2), Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
                r.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                r.Child = new TextBlock { Text = new string('★', b.Rating), FontSize = 10, Foreground = Brushes.Black };
                art.Children.Add(r);
            }
            var open = Bare(art, $"Open {b.Title}");
            var captured = b;
            open.Click += (_, _) => new BookSheet(_shelf, captured).Show();
            s.Children.Add(open);
            var title = T(b.Title, 13, "Text", FontWeights.SemiBold);
            title.TextWrapping = TextWrapping.Wrap;
            title.MaxHeight = 36;
            title.Margin = new Thickness(0, 8, 0, 1);
            s.Children.Add(title);
            s.Children.Add(T(b.Author, 11, "Muted"));
            if (b.Status == ReadStatus.Reading)
            {
                var row = new DockPanel { Margin = new Thickness(0, 6, 0, 4) };
                var plus = Styled("+10", "GhostButton", (_, _) => { BookLogic.SetPage(captured, captured.CurrentPage + 10, Shelf.Today); _shelf.Save(); }, new Thickness(8, 2, 8, 2));
                System.Windows.Automation.AutomationProperties.SetName(plus, $"Read 10 more pages of {b.Title}");
                DockPanel.SetDock(plus, Dock.Right);
                row.Children.Add(plus);
                row.Children.Add(T(b.Pages is > 0 ? $"p. {b.CurrentPage} / {b.Pages}" : $"p. {b.CurrentPage}", 11, "Muted"));
                s.Children.Add(row);
                if (b.Pages is > 0) s.Children.Add(Bar(BookLogic.Progress(b)));
                if (BookLogic.DaysToFinish(b, pace) is { } days) s.Children.Add(Gap(T(days <= 1 ? "Done about tomorrow at your pace" : $"Done in about {days} days at your pace", 10, "AccentBrush"), 4, 0));
            }
            else if (b.Status == ReadStatus.Read && b.Finished is { } f) s.Children.Add(T($"Finished {f:d MMM}", 11, "Muted"));
            grid.Children.Add(new Border { Child = s, Margin = new Thickness(0, 0, 18, 22), VerticalAlignment = VerticalAlignment.Top });
        }
        _content.Children.Add(grid);
    }

    private void Quotes()
    {
        var all = Books.SelectMany(b => b.Quotes.Select(q => (Book: b, Quote: q))).OrderByDescending(x => x.Quote.Added).ToList();
        if (all.Count == 0) { Empty("No quotes yet", "Open a book to save a line you liked, with its page."); return; }
        var list = new StackPanel { MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var (book, q) in all)
        {
            var s = new StackPanel();
            var text = T($"“{q.Text}”", 16, "Text", wrap: true);
            text.FontStyle = FontStyles.Italic;
            s.Children.Add(text);
            s.Children.Add(Gap(T($"{book.Title}" + (book.Author.Length > 0 ? $", {book.Author}" : "") + (q.Page is { } p ? $", p. {p}" : ""), 11, "Muted"), 6, 0));
            var c = Card(s, new Thickness(18, 14, 18, 14));
            c.Margin = new Thickness(0, 0, 0, 10);
            list.Children.Add(c);
        }
        _content.Children.Add(list);
    }

    private async Task RunSearch()
    {
        if (_search.Text.Trim().Length == 0 || _busy) return;
        _busy = true;
        _status = "";
        Render();
        try { _results = await _shelf.Search(_search.Text); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { _status = "Couldn't reach Open Library. Check your connection."; }
        finally { _busy = false; Render(); }
    }

    private void Find()
    {
        var line = new DockPanel { Width = 640, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) };
        var go = Styled("Search", "AccentButton", async (_, _) => await RunSearch(), new Thickness(16, 6, 16, 6));
        go.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(go, Dock.Right);
        line.Children.Add(go);
        (_search.Parent as Panel)?.Children.Remove(_search);
        line.Children.Add(_search);
        _content.Children.Add(line);
        var manual = Bare(T("Not there? Add a book by hand", 12, "AccentBrush"), "Add a book by hand");
        manual.HorizontalAlignment = HorizontalAlignment.Left;
        manual.Click += (_, _) =>
        {
            var b = new Book { Key = "me:" + Guid.NewGuid().ToString("N"), Title = _search.Text.Trim().Length > 0 ? _search.Text.Trim() : "New book" };
            Books.Insert(0, b);
            _shelf.Save();
            new BookSheet(_shelf, b).Show();
        };
        _content.Children.Add(manual);
        _content.Children.Add(Gap(T("Books from Open Library.", 11, "Muted"), 4, 14));
        if (_busy) { _content.Children.Add(T("Searching…", 13, "Muted")); return; }
        if (_status.Length > 0) { _content.Children.Add(T(_status, 13, "Danger", wrap: true)); return; }
        if (_results is null) return;
        if (_results.Count == 0) { _content.Children.Add(T("Nothing found.", 13, "Muted")); return; }
        var grid = new WrapPanel();
        foreach (var f in _results)
        {
            var s = new StackPanel { Width = 150 };
            s.Children.Add(Cover(f.CoverId is { } id ? $"https://covers.openlibrary.org/b/id/{id}-M.jpg" : "", 150, 225));
            var title = T(f.Title, 13, "Text", FontWeights.SemiBold);
            title.TextWrapping = TextWrapping.Wrap;
            title.MaxHeight = 36;
            title.Margin = new Thickness(0, 8, 0, 1);
            s.Children.Add(title);
            s.Children.Add(T(string.Join(" · ", new[] { f.Author, f.Year?.ToString() ?? "", f.Pages is { } p ? $"{p} pages" : "" }.Where(x => x.Length > 0)), 11, "Muted"));
            var existing = Books.FirstOrDefault(b => b.Key == f.Key);
            if (existing is null)
            {
                var picks = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
                foreach (var st in new[] { ReadStatus.WantToRead, ReadStatus.Reading, ReadStatus.Read })
                {
                    var chip = new Border { Padding = new Thickness(8, 2, 8, 3), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 4, 4) };
                    chip.SetResourceReference(Border.BorderBrushProperty, st == ReadStatus.WantToRead ? "AccentBrush" : "Hairline");
                    chip.Child = T((st == ReadStatus.WantToRead ? "+ " : "") + BookLogic.StatusName(st), 11, st == ReadStatus.WantToRead ? "Text" : "Muted");
                    var b = Bare(chip, $"Add {f.Title} as {BookLogic.StatusName(st)}");
                    var captured = st;
                    b.Click += (_, _) =>
                    {
                        var book = BookLogic.FromFound(f, captured, Shelf.Today);
                        Books.Insert(0, book);
                        _shelf.Save();
                        if (captured == ReadStatus.Read) new BookSheet(_shelf, book).Show();
                    };
                    picks.Children.Add(b);
                }
                s.Children.Add(picks);
            }
            else
            {
                var change = Bare(T("✓ " + BookLogic.StatusName(existing.Status) + "  ·  open", 11, "AccentBrush"), $"Open {f.Title}");
                change.HorizontalAlignment = HorizontalAlignment.Left;
                change.Margin = new Thickness(0, 6, 0, 0);
                change.Click += (_, _) => new BookSheet(_shelf, existing).Show();
                s.Children.Add(change);
            }
            grid.Children.Add(new Border { Child = s, Margin = new Thickness(0, 0, 18, 22), VerticalAlignment = VerticalAlignment.Top });
        }
        _content.Children.Add(grid);
    }

    private void GoalSheet()
    {
        var s = new StackPanel();
        s.Children.Add(Label("YEARLY GOAL"));
        s.Children.Add(Gap(T("How many books do you want to finish this year?", 13, "Muted", wrap: true), 6, 8));
        var field = Field("12", _shelf.Data.YearGoal.ToString(), "Yearly goal");
        s.Children.Add(field);
        void Save()
        {
            if (int.TryParse(field.Text.Trim(), out var g) && g is >= 1 and <= 1000) { _shelf.Data.YearGoal = g; _shelf.Save(); Main?.HideSheet(); }
            else field.Focus();
        }
        field.KeyDown += (_, e) => { if (e.Key == Key.Enter) Save(); };
        var save = Styled("Save", "AccentButton", (_, _) => Save(), new Thickness(18, 6, 18, 6));
        save.HorizontalAlignment = HorizontalAlignment.Right;
        save.Margin = new Thickness(0, 14, 0, 0);
        s.Children.Add(save);
        ShowCard(s, 380);
        field.Dispatcher.BeginInvoke(() => { field.Focus(); field.SelectAll(); }, DispatcherPriority.Input);
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
        var scrim = new Grid();
        scrim.SetResourceReference(Panel.BackgroundProperty, "Scrim");
        scrim.MouseLeftButtonDown += (_, _) => Main?.HideSheet();
        scrim.KeyDown += (_, e) => { if (e.Key == Key.Escape) Main?.HideSheet(); };
        scrim.Children.Add(card);
        Main?.ShowSheet(scrim);
    }
}

/// <summary>A book: status, your page, rating, dates, quotes and notes.</summary>
internal sealed class BookSheet(Shelf shelf, Book book)
{
    private readonly StackPanel _root = new();

    public void Show()
    {
        _root.Unloaded += (_, _) => shelf.Save();
        Render();
        BooksPage.ShowCard(_root, 700);
    }

    private void Quiet() => shelf.Save(false);

    private void Render()
    {
        _root.Children.Clear();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var close = Bare(Glyph("", 11, "Muted"), "Close");
        close.Click += (_, _) => BooksPage.Main?.HideSheet();
        close.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(close, Dock.Right);
        head.Children.Add(close);
        var cover = BooksPage.Cover(book.CoverUrl('L'), 110, 165);
        cover.Margin = new Thickness(0, 0, 18, 0);
        DockPanel.SetDock(cover, Dock.Left);
        head.Children.Add(cover);
        var info = new StackPanel();
        var title = Field("Title", book.Title, "Book title");
        title.FontSize = 18;
        title.LostFocus += (_, _) => { if (title.Text.Trim().Length > 0) { book.Title = title.Text.Trim(); Quiet(); } };
        info.Children.Add(title);
        var meta = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var author = Field("Author", book.Author, "Book author"); author.Width = 200; author.Margin = new Thickness(0, 0, 8, 0);
        author.LostFocus += (_, _) => { book.Author = author.Text.Trim(); Quiet(); };
        var pages = Field("Pages", book.Pages?.ToString() ?? "", "Book pages"); pages.Width = 90;
        pages.LostFocus += (_, _) => { book.Pages = int.TryParse(pages.Text.Trim(), out var p) && p > 0 ? p : book.Pages; Quiet(); Render(); };
        meta.Children.Add(author);
        meta.Children.Add(pages);
        info.Children.Add(meta);
        head.Children.Add(info);
        _root.Children.Add(head);

        // Status
        var statuses = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var st in Enum.GetValues<ReadStatus>())
        {
            var rb = new RadioButton { Content = BookLogic.StatusName(st), GroupName = "BookStatus", IsChecked = book.Status == st, MinWidth = 104, Padding = new Thickness(8, 0, 8, 0) };
            rb.SetResourceReference(FrameworkElement.StyleProperty, "Segment");
            var captured = st;
            rb.Checked += (_, _) => { if (book.Status != captured) { BookLogic.SetStatus(book, captured, Shelf.Today); Quiet(); Render(); } };
            statuses.Children.Add(rb);
        }
        var statusBox = new Border { Padding = new Thickness(3), CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), Child = statuses, HorizontalAlignment = HorizontalAlignment.Left };
        statusBox.SetResourceReference(Border.BackgroundProperty, "Bg");
        statusBox.SetResourceReference(Border.BorderBrushProperty, "Hairline");
        _root.Children.Add(statusBox);

        // Page
        if (book.Status is ReadStatus.Reading or ReadStatus.WantToRead)
        {
            _root.Children.Add(BooksPage.Gap(Label("YOUR PAGE", "Muted"), 16, 6));
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var page = Field("0", book.CurrentPage.ToString(), "Current page");
            page.Width = 90;
            void Set(int p) { BookLogic.SetPage(book, p, Shelf.Today); Quiet(); Render(); }
            page.KeyDown += (_, e) => { if (e.Key == Key.Enter && int.TryParse(page.Text.Trim(), out var p)) Set(p); };
            page.LostFocus += (_, _) => { if (int.TryParse(page.Text.Trim(), out var p) && p != book.CurrentPage) Set(p); };
            row.Children.Add(page);
            foreach (var n in new[] { 10, 25, 50 })
            {
                var b = Styled($"+{n}", "GhostButton", (_, _) => Set(book.CurrentPage + n), new Thickness(10, 4, 10, 4));
                b.Margin = new Thickness(8, 0, 0, 0);
                row.Children.Add(b);
            }
            if (book.Pages is > 0) row.Children.Add(BooksPage.Gap(T($"of {book.Pages} · {BookLogic.Progress(book):P0}", 12, "Muted"), 7, 0));
            ((FrameworkElement)row.Children[^1]).Margin = new Thickness(10, 7, 0, 0);
            _root.Children.Add(row);
        }

        // Rating
        _root.Children.Add(BooksPage.Gap(Label("YOUR RATING", "Muted"), 16, 6));
        var stars = new StackPanel { Orientation = Orientation.Horizontal };
        for (int r = 1; r <= 5; r++)
        {
            var n = r;
            var on = book.Rating >= r;
            var b = Bare(T(on ? "★" : "☆", 24, on ? "AccentBrush" : "Muted"), $"Rate {r} stars");
            b.Margin = new Thickness(0, 0, 4, 0);
            b.Click += (_, _) => { book.Rating = book.Rating == n ? 0 : n; Quiet(); Render(); };
            stars.Children.Add(b);
        }
        _root.Children.Add(stars);

        // Quotes
        _root.Children.Add(BooksPage.Gap(Label("QUOTES", "Muted"), 16, 6));
        foreach (var q in book.Quotes)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var x = Bare(Glyph("", 9, "Muted"), "Delete quote");
            x.Click += (_, _) => { book.Quotes.Remove(q); Quiet(); Render(); };
            DockPanel.SetDock(x, Dock.Right);
            row.Children.Add(x);
            var t = T($"“{q.Text}”" + (q.Page is { } p ? $"  p. {p}" : ""), 13, "Text", wrap: true);
            t.FontStyle = FontStyles.Italic;
            row.Children.Add(t);
            _root.Children.Add(row);
        }
        var add = new DockPanel();
        var qPage = Field("Page", "", "Quote page"); qPage.Width = 70;
        var qText = Field("A line you liked", "", "Quote text");
        var save = Styled("Add", "GhostButton", (_, _) => { }, new Thickness(12, 4, 12, 4));
        void AddQuote()
        {
            if (qText.Text.Trim().Length == 0) { qText.Focus(); return; }
            book.Quotes.Add(new Quote { Text = qText.Text.Trim(), Page = int.TryParse(qPage.Text.Trim(), out var p) ? p : (book.CurrentPage > 0 ? book.CurrentPage : null), Added = Shelf.Today });
            Quiet();
            Render();
        }
        save.Click += (_, _) => AddQuote();
        qText.KeyDown += (_, e) => { if (e.Key == Key.Enter) AddQuote(); };
        save.Margin = new Thickness(8, 0, 0, 0);
        qPage.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(save, Dock.Right);
        DockPanel.SetDock(qPage, Dock.Right);
        add.Children.Add(save);
        add.Children.Add(qPage);
        add.Children.Add(qText);
        _root.Children.Add(add);

        // Notes
        _root.Children.Add(BooksPage.Gap(Label("NOTES", "Muted"), 16, 6));
        var notes = Field("What you thought, who recommended it…", book.Notes, "Book notes");
        notes.AcceptsReturn = true;
        notes.TextWrapping = TextWrapping.Wrap;
        notes.MinHeight = 60;
        notes.TextChanged += (_, _) => { book.Notes = notes.Text; Quiet(); };
        _root.Children.Add(notes);

        var footer = new DockPanel { Margin = new Thickness(0, 18, 0, 0) };
        bool armed = false;
        Button? remove = null;
        remove = Styled("Remove book", "GhostButton", (_, _) =>
        {
            if (!armed) { armed = true; remove!.Content = "Remove it and its quotes? Click again"; return; }
            shelf.Data.Books.Remove(book);
            shelf.Save();
            BooksPage.Main?.HideSheet();
        }, new Thickness(12, 5, 12, 5));
        DockPanel.SetDock(remove, Dock.Left);
        footer.Children.Add(remove);
        var done = Styled("Done", "AccentButton", (_, _) => BooksPage.Main?.HideSheet(), new Thickness(18, 6, 18, 6));
        done.HorizontalAlignment = HorizontalAlignment.Right;
        footer.Children.Add(done);
        _root.Children.Add(footer);
    }
}
