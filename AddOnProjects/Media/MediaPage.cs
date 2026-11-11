using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using CommandHub.Home;
using static CommandHub.Home.PageUi;

namespace CommandHub.AddOn.Media;

/// <summary>The Media tab: Watching, Watchlist, Completed, Discover and Stats, with posters.</summary>
public sealed class MediaPage : UserControl
{
    private enum View { Watching, Watchlist, Completed, Discover, Stats }

    private readonly Library _library;
    private readonly Border _base = new(), _glow = new() { IsHitTestVisible = false };
    private readonly StackPanel _content = new() { Margin = new Thickness(33, 0, 33, 40) };
    private View _view;
    private readonly TextBox _search = Field("Search anime, movies and shows", "", "Search titles");
    private MediaKind? _searchKind;   // null = anime; Movie/Show = IMDb
    private List<Found>? _results, _airing;
    private string _status = "";
    private bool _busy;

    public MediaPage(Library library)
    {
        _library = library;
        _view = library.Data.Items.Any(i => i.Status == WatchStatus.Watching) ? View.Watching : View.Discover;
        var scroll = new ScrollViewer { Content = _content, Focusable = false, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        WindowChrome.SetIsHitTestVisibleInChrome(_content, true);
        _ = new SmoothScroll(scroll);
        var layers = new Grid();
        layers.Children.Add(_base);
        layers.Children.Add(_glow);
        layers.Children.Add(scroll);
        Content = layers;
        _search.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await RunSearch(); } };
        Loaded += async (_, _) =>
        {
            App.ThemeChanged += OnTheme;
            _library.Changed += OnChanged;
            Backdrop.Paint(_base, _glow);
            Render();
            if (_view == View.Discover) await LoadAiring();
        };
        Unloaded += (_, _) => { App.ThemeChanged -= OnTheme; _library.Changed -= OnChanged; };
        Backdrop.Paint(_base, _glow);
    }

    private void OnTheme() { Backdrop.Paint(_base, _glow); Render(); }
    private void OnChanged() => Dispatcher.BeginInvoke(Render);
    private List<MediaItem> Items => _library.Data.Items;
    private static MainWindow? Main => Application.Current.MainWindow as MainWindow;

    // ---------------- Frame ----------------

    private void Render()
    {
        _content.Children.Clear();
        var top = new DockPanel { Height = 40 };
        var crumb = T($"MEDIA   /   {DateTime.Now.ToString("dddd, MMMM d").ToUpperInvariant()}", 11, "Muted");
        crumb.VerticalAlignment = VerticalAlignment.Center;
        crumb.Margin = new Thickness(0, 6, 0, 0);
        top.Children.Add(crumb);
        _content.Children.Add(top);

        var stats = MediaLogic.Stats(Items, DateOnly.FromDateTime(DateTime.Today));
        var head = new DockPanel { Margin = new Thickness(0, 30, 0, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
        var pick = Styled("Pick for tonight", "GhostButton", (_, _) => PickForTonight(), new Thickness(14, 5, 14, 5));
        actions.Children.Add(pick);
        var import = Styled("Import MyAnimeList", "GhostButton", async (_, _) => await ImportMal(), new Thickness(14, 5, 14, 5));
        import.Margin = new Thickness(8, 0, 0, 0);
        actions.Children.Add(import);
        DockPanel.SetDock(actions, Dock.Right);
        head.Children.Add(actions);
        var left = new StackPanel();
        left.Children.Add(Label("ANIME, SHOWS AND MOVIES"));
        var big = Big(stats.Watching > 0 ? $"Watching {stats.Watching}" : "What to watch", 46);
        big.Margin = new Thickness(-2, 10, 0, 6);
        left.Children.Add(big);
        var line = new List<string> { $"{stats.EpisodesThisWeek} {(stats.EpisodesThisWeek == 1 ? "episode" : "episodes")} this week" };
        if (stats.Planned > 0) line.Add($"{stats.Planned} on your watchlist");
        if (stats.Completed > 0) line.Add($"{stats.Completed} finished");
        if (_library.Refreshing) line.Add("checking for new episodes…");
        left.Children.Add(T(string.Join("   ·   ", line), 13, "Muted"));
        head.Children.Add(left);
        _content.Children.Add(head);

        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 24, 0, 18) };
        foreach (var (v, text) in new[]
        {
            (View.Watching, $"Watching  {stats.Watching}"), (View.Watchlist, $"Watchlist  {stats.Planned}"),
            (View.Completed, $"Completed  {stats.Completed}"), (View.Discover, "Discover"), (View.Stats, "Stats"),
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
            b.Click += async (_, _) =>
            {
                _view = captured;
                Render();
                if (captured == View.Discover && _airing is null) await LoadAiring();
            };
            tabs.Children.Add(b);
        }
        _content.Children.Add(tabs);

        switch (_view)
        {
            case View.Watching:
                ComingUp();
                Grid(Items.Where(i => i.Status == WatchStatus.Watching).OrderByDescending(i => i.Log.Count > 0 ? i.Log.Max(d => d.Date) : DateOnly.MinValue).ToList(),
                    "Nothing on the go", "Start something from your watchlist, or find a title in Discover.");
                Grid(Items.Where(i => i.Status == WatchStatus.OnHold).ToList(), "", "", "ON HOLD");
                break;
            case View.Watchlist:
                Grid(Items.Where(i => i.Status == WatchStatus.PlanToWatch).ToList(), "Your watchlist is empty", "Add titles from Discover, or import your MyAnimeList list.");
                break;
            case View.Completed:
                Grid(Items.Where(i => i.Status == WatchStatus.Completed).OrderByDescending(i => i.FinishedUtc).ToList(), "Nothing finished yet", "Titles you complete show up here with your rating.");
                Grid(Items.Where(i => i.Status == WatchStatus.Dropped).ToList(), "", "", "DROPPED");
                break;
            case View.Discover: Discover(); break;
            case View.Stats: Stats(stats); break;
        }
    }

    // ---------------- Lists ----------------

    private void ComingUp()
    {
        var soon = MediaLogic.ComingUp(Items, DateTime.UtcNow);
        if (soon.Count == 0) return;
        _content.Children.Add(Spaced(Label("COMING UP THIS WEEK"), 0, 10));
        var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 18) };
        foreach (var i in soon)
        {
            var s = new StackPanel();
            s.Children.Add(T(i.Title, 13, "Text", FontWeights.SemiBold));
            s.Children.Add(T($"Episode {i.NextEpisodeNumber} · {MediaLogic.When(i.NextEpisodeUtc!.Value, DateTime.Now)}", 11, "AccentBrush"));
            var card = Card(s, new Thickness(14, 10, 14, 10));
            card.Margin = new Thickness(0, 0, 10, 10);
            card.MaxWidth = 300;
            row.Children.Add(card);
        }
        _content.Children.Add(row);
    }

    private void Grid(List<MediaItem> items, string emptyTitle, string emptyText, string heading = "")
    {
        if (items.Count == 0)
        {
            if (emptyTitle.Length == 0) return;
            var e = new StackPanel { Margin = new Thickness(0, 20, 0, 0), MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left };
            e.Children.Add(T(emptyTitle, 18));
            e.Children.Add(Spaced(T(emptyText, 13, "Muted", wrap: true), 6, 0));
            _content.Children.Add(e);
            return;
        }
        if (heading.Length > 0) _content.Children.Add(Spaced(Label(heading, "Muted"), 10, 10));
        var grid = new WrapPanel();
        foreach (var i in items) grid.Children.Add(ItemCard(i));
        _content.Children.Add(grid);
    }

    private const double PosterW = 156, PosterH = 234;

    private static Border Poster(string url, double w = PosterW, double h = PosterH)
    {
        var b = new Border { Width = w, Height = h, CornerRadius = new CornerRadius(10) };
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
        return b;
    }

    private FrameworkElement ItemCard(MediaItem i)
    {
        var s = new StackPanel { Width = PosterW };
        var art = new Grid();
        art.Children.Add(Poster(i.Image));
        var kind = new Border { CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 1, 6, 2), Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x10, 0x12, 0x18)) };
        kind.Child = new TextBlock { Text = MediaLogic.KindName(i.Kind), FontSize = 10, Foreground = Brushes.White };
        art.Children.Add(kind);
        if (i.Rating > 0)
        {
            var r = new Border { CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 1, 6, 2), Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
            r.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            r.Child = new TextBlock { Text = $"★ {i.Rating}", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Brushes.Black };
            art.Children.Add(r);
        }
        var open = Bare(art, $"Open {i.Title}");
        open.Click += (_, _) => Details(i);
        s.Children.Add(open);
        var title = T(i.Title, 13, "Text", FontWeights.SemiBold);
        title.TextWrapping = TextWrapping.Wrap;
        title.MaxHeight = 36;
        title.Margin = new Thickness(0, 8, 0, 2);
        s.Children.Add(title);

        if (i.Kind != MediaKind.Movie && i.Status is WatchStatus.Watching or WatchStatus.OnHold)
        {
            var total = i.TotalEpisodes is > 0 ? $" / {i.TotalEpisodes}" : "";
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            var plus = Styled("+1", "GhostButton", (_, _) => _library.SetProgress(i, i.Progress + 1), new Thickness(9, 2, 9, 2));
            System.Windows.Automation.AutomationProperties.SetName(plus, $"Watched an episode of {i.Title}");
            DockPanel.SetDock(plus, Dock.Right);
            row.Children.Add(plus);
            row.Children.Add(T($"Ep {i.Progress}{total}", 12, "Muted"));
            s.Children.Add(row);
            if (i.TotalEpisodes is > 0) s.Children.Add(Bar(i.Progress / (double)i.TotalEpisodes.Value));
        }
        else
        {
            var sub = string.Join(" · ", new[] { i.Year?.ToString() ?? "", i.TotalEpisodes is > 1 ? $"{i.TotalEpisodes} episodes" : "" }.Where(x => x.Length > 0));
            if (sub.Length > 0) s.Children.Add(T(sub, 11, "Muted"));
        }
        if (i.NextEpisodeUtc is { } next && next > DateTime.UtcNow.AddHours(-12) && i.Status != WatchStatus.Completed)
            s.Children.Add(T($"Ep {i.NextEpisodeNumber}: {MediaLogic.When(next, DateTime.Now)}", 11, "AccentBrush"));
        var holder = new Border { Child = s, Margin = new Thickness(0, 0, 18, 22), VerticalAlignment = VerticalAlignment.Top };
        return holder;
    }

    private static Grid Bar(double fraction)
    {
        var g = new Grid { Height = 4 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, fraction), GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, 1 - fraction), GridUnitType.Star) });
        var track = new Border { CornerRadius = new CornerRadius(2) };
        track.SetResourceReference(Border.BackgroundProperty, "Raised");
        System.Windows.Controls.Grid.SetColumnSpan(track, 2);
        g.Children.Add(track);
        var fill = new Border { CornerRadius = new CornerRadius(2) };
        fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        g.Children.Add(fill);
        return g;
    }

    private static FrameworkElement Spaced(FrameworkElement e, double top, double bottom)
    {
        e.Margin = new Thickness(0, top, 0, bottom);
        return e;
    }

    // ---------------- Discover ----------------

    private async Task LoadAiring()
    {
        try { _airing = await _library.Sources.AiringNow(); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException) { _airing = []; _status = "Couldn't reach AniList right now."; }
        if (_view == View.Discover) Render();
    }

    private async Task RunSearch()
    {
        var term = _search.Text.Trim();
        if (term.Length == 0 || _busy) return;
        _busy = true;
        _status = "";
        Render();
        try
        {
            _results = _searchKind is null ? await _library.Sources.SearchAnime(term) : await _library.Sources.SearchImdb(term);
            if (_searchKind is { } k) _results = _results.Where(r => r.Kind == k).ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException) { _status = "Couldn't search right now. Check your connection."; }
        finally { _busy = false; Render(); }
    }

    private void Discover()
    {
        var kinds = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        foreach (var (k, text) in new (MediaKind?, string)[] { (null, "Anime"), (MediaKind.Show, "Shows"), (MediaKind.Movie, "Movies") })
        {
            bool on = k == _searchKind;
            var chip = new Border { Padding = new Thickness(12, 5, 12, 6), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 8, 0), Child = T(text, 12, on ? "Text" : "Muted", on ? FontWeights.SemiBold : null) };
            chip.SetResourceReference(Border.BorderBrushProperty, on ? "AccentBrush" : "Hairline");
            var b = Bare(chip, $"Search {text}");
            var captured = k;
            b.Click += async (_, _) => { _searchKind = captured; _results = null; if (_search.Text.Trim().Length > 0) await RunSearch(); else Render(); };
            kinds.Children.Add(b);
        }
        _content.Children.Add(kinds);
        var line = new DockPanel { Width = 640, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 18) };
        var go = Styled("Search", "AccentButton", async (_, _) => await RunSearch(), new Thickness(16, 6, 16, 6));
        go.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(go, Dock.Right);
        line.Children.Add(go);
        (_search.Parent as Panel)?.Children.Remove(_search);
        line.Children.Add(_search);
        _content.Children.Add(line);
        _content.Children.Add(T(_searchKind is null ? "Anime from AniList, linked to MyAnimeList." : "Movies and shows from IMDb; new episodes from TVmaze.", 11, "Muted"));

        if (_busy) { _content.Children.Add(Spaced(T("Searching…", 13, "Muted"), 12, 0)); return; }
        if (_status.Length > 0) { _content.Children.Add(Spaced(T(_status, 13, "Danger", wrap: true), 12, 0)); return; }
        var list = _results ?? (_searchKind is null ? _airing : null);
        if (list is null) return;
        _content.Children.Add(Spaced(Label(_results is null ? "AIRING THIS SEASON" : "RESULTS"), 18, 12));
        if (list.Count == 0) { _content.Children.Add(T("Nothing found.", 13, "Muted")); return; }
        var grid = new WrapPanel();
        foreach (var f in list) grid.Children.Add(FoundCard(f));
        _content.Children.Add(grid);
    }

    private FrameworkElement FoundCard(Found f)
    {
        var s = new StackPanel { Width = PosterW };
        var art = new Grid();
        art.Children.Add(Poster(f.Image));
        if (f.Score.Length > 0)
        {
            var sc = new Border { CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 1, 6, 2), Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x10, 0x12, 0x18)) };
            sc.Child = new TextBlock { Text = f.Score, FontSize = 10, Foreground = Brushes.White };
            art.Children.Add(sc);
        }
        s.Children.Add(art);
        var title = T(f.Title, 13, "Text", FontWeights.SemiBold);
        title.TextWrapping = TextWrapping.Wrap;
        title.MaxHeight = 36;
        title.Margin = new Thickness(0, 8, 0, 2);
        s.Children.Add(title);
        var sub = string.Join(" · ", new[] { MediaLogic.KindName(f.Kind), f.Year?.ToString() ?? "", f.Episodes is > 1 ? $"{f.Episodes} ep" : "" }.Where(x => x.Length > 0));
        s.Children.Add(T(sub, 11, "Muted"));
        var existing = _library.Find(f.Key);
        if (existing is null)
        {
            var add = Styled("+ Watchlist", "GhostButton", async (_, _) => await _library.Add(f, WatchStatus.PlanToWatch), new Thickness(10, 3, 10, 3));
            System.Windows.Automation.AutomationProperties.SetName(add, $"Add {f.Title} to watchlist");
            add.HorizontalAlignment = HorizontalAlignment.Left;
            add.Margin = new Thickness(0, 6, 0, 0);
            s.Children.Add(add);
        }
        else s.Children.Add(Spaced(T("✓ " + MediaLogic.StatusName(existing.Status), 11, "AccentBrush"), 6, 0));
        return new Border { Child = s, Margin = new Thickness(0, 0, 18, 22), VerticalAlignment = VerticalAlignment.Top };
    }

    // ---------------- Stats ----------------

    private void Stats(MediaStats s)
    {
        var row = new WrapPanel();
        void Tile(string label, string value, string sub)
        {
            var st = new StackPanel { Width = 180 };
            st.Children.Add(Label(label));
            var big = Big(value, 36);
            big.Margin = new Thickness(-1, 8, 0, 2);
            st.Children.Add(big);
            st.Children.Add(T(sub, 11, "Muted"));
            var card = Card(st);
            card.Margin = new Thickness(0, 0, 12, 12);
            row.Children.Add(card);
        }
        Tile("THIS WEEK", s.EpisodesThisWeek.ToString(), "episodes watched");
        Tile("THIS MONTH", s.EpisodesThisMonth.ToString(), "episodes watched");
        Tile("TIME", $"{s.HoursTotal:0}h", "spent watching, about");
        Tile("FINISHED", s.Completed.ToString(), $"{s.CompletedByKind[MediaKind.Anime]} anime · {s.CompletedByKind[MediaKind.Show]} shows · {s.CompletedByKind[MediaKind.Movie]} movies");
        Tile("YOUR RATINGS", s.AverageRating is { } a ? $"{a:0.0}" : "–", "average out of 10");
        _content.Children.Add(row);

        var top = Items.Where(i => i.Rating > 0).OrderByDescending(i => i.Rating).ThenByDescending(i => i.FinishedUtc).Take(10).ToList();
        if (top.Count > 0)
        {
            _content.Children.Add(Spaced(Label("YOUR TOP RATED"), 12, 10));
            var list = new StackPanel();
            foreach (var i in top)
            {
                var r = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                var v = T($"★ {i.Rating}", 12, "AccentBrush", FontWeights.SemiBold);
                DockPanel.SetDock(v, Dock.Right);
                r.Children.Add(v);
                r.Children.Add(T($"{i.Title}  ·  {MediaLogic.KindName(i.Kind)}", 13));
                list.Children.Add(r);
            }
            var card = Card(list);
            card.MaxWidth = 640;
            card.HorizontalAlignment = HorizontalAlignment.Left;
            _content.Children.Add(card);
        }
    }

    // ---------------- Actions ----------------

    private void PickForTonight()
    {
        var pick = MediaLogic.Pick(Items, Random.Shared);
        if (pick is null) { Main?.ShowNotice("Your watchlist is empty: add something first."); return; }
        Details(pick, "HOW ABOUT THIS TONIGHT?");
    }

    private async Task ImportMal()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose your MyAnimeList export",
            Filter = "MyAnimeList export (*.xml;*.gz)|*.xml;*.gz|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog() != true) return;
        Main?.ShowNotice("Importing your MyAnimeList list…");
        try
        {
            await using var file = File.OpenRead(dialog.FileName);
            var added = await _library.ImportMal(file);
            Main?.ShowNotice(added == 0 ? "Everything in that list was already here." : $"Imported {added} {(added == 1 ? "title" : "titles")} from MyAnimeList.");
            _view = View.Watching;
            Render();
        }
        catch (InvalidDataException ex) { Main?.ShowNotice(ex.Message); }
        catch (IOException ex) { Main?.ShowNotice("Couldn't read that file: " + ex.Message); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            Main?.ShowNotice("The import couldn't finish: " + ex.Message);
        }
    }

    // ---------------- Details ----------------

    private void Details(MediaItem item, string eyebrow = "")
    {
        var form = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var close = Bare(Glyph("", 11, "Muted"), "Close");
        close.Click += (_, _) => Main?.HideSheet();
        close.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(close, Dock.Right);
        head.Children.Add(close);
        var poster = Poster(item.Image, 110, 165);
        poster.Margin = new Thickness(0, 0, 18, 0);
        DockPanel.SetDock(poster, Dock.Left);
        head.Children.Add(poster);
        var title = new StackPanel();
        title.Children.Add(Label(eyebrow.Length > 0 ? eyebrow : MediaLogic.KindName(item.Kind).ToUpperInvariant()));
        var name = Big(item.Title, 28);
        name.TextWrapping = TextWrapping.Wrap;
        name.Margin = new Thickness(-1, 6, 0, 4);
        title.Children.Add(name);
        var facts = new[] { item.Subtitle, item.Year?.ToString() ?? "", item.TotalEpisodes is > 1 ? $"{item.TotalEpisodes} episodes" : "", item.Minutes is { } m ? $"{m} min" : "", item.AiringStatus switch { "RELEASING" or "Running" => "airing", "FINISHED" or "Ended" => "finished", "NOT_YET_RELEASED" => "not out yet", _ => "" } };
        title.Children.Add(T(string.Join(" · ", facts.Where(x => x.Length > 0)), 12, "Muted", wrap: true));
        var link = MediaLogic.LinkFor(item);
        if (link.Length > 0)
        {
            var open = Styled(item.MalId is not null ? "Open on MyAnimeList" : item.ImdbId.Length > 0 ? "Open on IMDb" : "Open on AniList", "GhostButton",
                (_, _) => { try { Process.Start(new ProcessStartInfo(link) { UseShellExecute = true }); } catch (System.ComponentModel.Win32Exception) { } }, new Thickness(12, 4, 12, 4));
            open.HorizontalAlignment = HorizontalAlignment.Left;
            open.Margin = new Thickness(0, 10, 0, 0);
            title.Children.Add(open);
        }
        head.Children.Add(title);
        form.Children.Add(head);

        // Status
        form.Children.Add(Spaced(Label("STATUS", "Muted"), 0, 6));
        var statuses = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var st in Enum.GetValues<WatchStatus>())
        {
            var rb = new RadioButton { Content = MediaLogic.StatusName(st), GroupName = "MediaStatus", IsChecked = item.Status == st, MinWidth = 86, Padding = new Thickness(8, 0, 8, 0) };
            rb.SetResourceReference(StyleProperty, "Segment");
            var captured = st;
            rb.Checked += (_, _) => { if (item.Status != captured) _library.SetStatus(item, captured); };
            statuses.Children.Add(rb);
        }
        var statusBox = new Border { Padding = new Thickness(3), CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), Child = statuses, HorizontalAlignment = HorizontalAlignment.Left };
        statusBox.SetResourceReference(Border.BackgroundProperty, "Bg");
        statusBox.SetResourceReference(Border.BorderBrushProperty, "Hairline");
        form.Children.Add(statusBox);

        // Progress
        if (item.Kind != MediaKind.Movie)
        {
            form.Children.Add(Spaced(Label("EPISODES WATCHED", "Muted"), 16, 6));
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var field = Field("0", item.Progress.ToString(), "Episodes watched");
            field.Width = 90;
            var minus = Styled("−", "GhostButton", (_, _) => { _library.SetProgress(item, item.Progress - 1); field.Text = item.Progress.ToString(); }, new Thickness(12, 4, 12, 4));
            var plus = Styled("+1", "GhostButton", (_, _) => { _library.SetProgress(item, item.Progress + 1); field.Text = item.Progress.ToString(); }, new Thickness(12, 4, 12, 4));
            minus.Margin = plus.Margin = new Thickness(8, 0, 0, 0);
            field.LostFocus += (_, _) => { if (int.TryParse(field.Text, out var v) && v != item.Progress) _library.SetProgress(item, v); field.Text = item.Progress.ToString(); };
            field.KeyDown += (_, e) => { if (e.Key == Key.Enter && int.TryParse(field.Text, out var v)) { _library.SetProgress(item, v); field.Text = item.Progress.ToString(); } };
            row.Children.Add(field);
            row.Children.Add(minus);
            row.Children.Add(plus);
            row.Children.Add(Spaced(T(item.TotalEpisodes is > 0 ? $"of {item.TotalEpisodes}" : "so far", 12, "Muted"), 8, 0));
            ((FrameworkElement)row.Children[^1]).Margin = new Thickness(10, 7, 0, 0);
            form.Children.Add(row);
            if (item.NextEpisodeUtc is { } next && next > DateTime.UtcNow.AddHours(-12))
                form.Children.Add(Spaced(T($"Episode {item.NextEpisodeNumber} airs {MediaLogic.When(next, DateTime.Now)}", 12, "AccentBrush"), 6, 0));
        }

        // Rating
        form.Children.Add(Spaced(Label("YOUR RATING", "Muted"), 16, 6));
        var stars = new StackPanel { Orientation = Orientation.Horizontal };
        for (int r = 1; r <= 10; r++)
        {
            var n = r;
            var on = item.Rating >= r;
            var b = Bare(T(on ? "★" : "☆", 20, on ? "AccentBrush" : "Muted"), $"Rate {r} out of 10");
            b.Margin = new Thickness(0, 0, 3, 0);
            b.Click += (_, _) => { _library.SetRating(item, item.Rating == n ? 0 : n); Details(item, eyebrow); };
            stars.Children.Add(b);
        }
        stars.Children.Add(Spaced(T(item.Rating > 0 ? $"{item.Rating}/10" : "not rated", 12, "Muted"), 0, 0));
        ((FrameworkElement)stars.Children[^1]).Margin = new Thickness(10, 6, 0, 0);
        form.Children.Add(stars);

        // Note
        form.Children.Add(Spaced(Label("NOTE", "Muted"), 16, 6));
        var note = Field("Thoughts, where to watch, who recommended it…", item.Note, "Note");
        note.TextChanged += (_, _) => _library.SetNote(item, note.Text);
        form.Children.Add(note);

        var footer = new DockPanel { Margin = new Thickness(0, 18, 0, 0) };
        var remove = Styled("Remove from my lists", "GhostButton", (_, _) => { _library.Remove(item); Main?.HideSheet(); }, new Thickness(12, 5, 12, 5));
        DockPanel.SetDock(remove, Dock.Left);
        footer.Children.Add(remove);
        var done = Styled("Done", "AccentButton", (_, _) => Main?.HideSheet(), new Thickness(18, 6, 18, 6));
        done.HorizontalAlignment = HorizontalAlignment.Right;
        footer.Children.Add(done);
        form.Children.Add(footer);

        var card = new Border
        {
            Width = 640, CornerRadius = new CornerRadius(16), Padding = new Thickness(24, 20, 24, 22), BorderThickness = new Thickness(1), Child = form,
            BorderBrush = Backdrop.Wash("TextColor", 0x24), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
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
