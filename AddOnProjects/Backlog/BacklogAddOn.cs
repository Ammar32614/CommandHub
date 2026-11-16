using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using CommandHub.AddOns;
using CommandHub.Home;
using static CommandHub.Home.PageUi;

namespace CommandHub.AddOn.Backlog;

public sealed class BacklogAddOn : IAddOn
{
    private Library? _library;

    public void Start(IAddOnHost host)
    {
        _library = new Library(host);
        _library.Load();
    }

    public UIElement CreatePage() => new BacklogPage(_library!);
}

public sealed record Hit(string Title, string Image, string Platforms);

public sealed class Library(IAddOnHost host)
{
    public BacklogData Data { get; private set; } = new();
    public event Action? Changed;
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    public void Load()
    {
        Data = host.Load<BacklogData>("backlog");
        Data.Games ??= [];
        foreach (var g in Data.Games) { g.Sessions ??= []; g.Notes ??= ""; g.Image ??= ""; g.ConceptId ??= ""; }
    }

    public void Save(bool notify = true)
    {
        try { host.Save("backlog", Data); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { host.Notify("Game backlog: couldn't save. " + ex.Message); }
        if (notify) Changed?.Invoke();
    }

    /// <summary>The PlayStation add-on's wishlist, if it's installed and has one.</summary>
    public List<Wished> PlayStationWishlist()
    {
        try
        {
            var file = Path.Combine(Path.GetDirectoryName(host.DataFolder)!, "playstation", "playstation.json");
            return File.Exists(file) ? BacklogLogic.ReadPlayStationWishlist(File.ReadAllText(file), Data.Games) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    /// <summary>Searches the US PlayStation Store for titles and cover art (the same web API the store site uses).</summary>
    public async Task<List<Hit>> Search(string term)
    {
        const string hash = "4df6284f982e57bec70f23c77e2c219dc792eb19af7fb3d3a81767aa3f1958aa";
        var vars = JsonSerializer.Serialize(new { countryCode = "US", languageCode = "en", nextCursor = "", pageOffset = 0, pageSize = 24, searchTerm = term.Trim() });
        var url = "https://web.np.playstation.com/api/graphql/v1/op?operationName=getSearchResults&variables=" + Uri.EscapeDataString(vars)
            + "&extensions=" + Uri.EscapeDataString("{\"persistedQuery\":{\"version\":1,\"sha256Hash\":\"" + hash + "\"}}");
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("x-psn-store-locale-override", "en-US");
        req.Headers.Add("x-apollo-operation-name", "getSearchResults");
        using var res = await host.Http.SendAsync(req);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var list = new List<Hit>();
        if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("universalSearch", out var us) && us.ValueKind == JsonValueKind.Object
            && us.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in results.EnumerateArray())
            {
                if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("name", out var n) || n.ValueKind != JsonValueKind.String) continue;
                var cls = r.TryGetProperty("storeDisplayClassification", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : "";
                if (cls is not ("FULL_GAME" or "PREMIUM_EDITION" or "GAME_BUNDLE")) continue; // games, not add-ons and items
                string image = "";
                foreach (var holder in new[] { "media", "personalizedMeta" })
                {
                    if (!r.TryGetProperty(holder, out var h)) continue;
                    var media = holder == "media" ? h : h.ValueKind == JsonValueKind.Object && h.TryGetProperty("media", out var pm) ? pm : default;
                    if (media.ValueKind != JsonValueKind.Array) continue;
                    foreach (var m in media.EnumerateArray())
                        if (m.TryGetProperty("role", out var role) && role.GetString() == "MASTER" && m.TryGetProperty("url", out var u)) { image = u.GetString() ?? ""; break; }
                    if (image.Length > 0) break;
                }
                var platforms = r.TryGetProperty("platforms", out var p) && p.ValueKind == JsonValueKind.Array ? string.Join(" / ", p.EnumerateArray().Select(x => x.GetString())) : "";
                list.Add(new Hit(n.GetString()!, image, platforms));
            }
        }
        return list.DistinctBy(h => h.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }
}

/// <summary>The Game backlog tab.</summary>
public sealed class BacklogPage : UserControl
{
    private enum View { Playing, Backlog, Finished, Shelved, Add }

    private readonly Library _lib;
    private readonly Border _base = new(), _glow = new() { IsHitTestVisible = false };
    private readonly StackPanel _content = new() { Margin = new Thickness(33, 0, 33, 40) };
    private readonly TextBox _search = Field("Search for a game", "", "Search games");
    private View _view;
    private List<Hit>? _hits;
    private string _status = "";
    private bool _busy;

    public BacklogPage(Library lib)
    {
        _lib = lib;
        _view = lib.Data.Games.Count == 0 ? View.Add : lib.Data.Games.Any(g => g.Status == GameStatus.Playing) ? View.Playing : View.Backlog;
        var scroll = new ScrollViewer { Content = _content, Focusable = false, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        WindowChrome.SetIsHitTestVisibleInChrome(_content, true);
        _ = new SmoothScroll(scroll);
        var layers = new Grid();
        layers.Children.Add(_base);
        layers.Children.Add(_glow);
        layers.Children.Add(scroll);
        Content = layers;
        _search.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await RunSearch(); } };
        Loaded += (_, _) => { App.ThemeChanged += OnTheme; _lib.Changed += OnChanged; Backdrop.Paint(_base, _glow); Render(); };
        Unloaded += (_, _) => { App.ThemeChanged -= OnTheme; _lib.Changed -= OnChanged; };
        Backdrop.Paint(_base, _glow);
    }

    private void OnTheme() { Backdrop.Paint(_base, _glow); Render(); }
    private void OnChanged() => Dispatcher.BeginInvoke(Render);
    private List<Game> Games => _lib.Data.Games;
    internal static MainWindow? Main => Application.Current.MainWindow as MainWindow;

    private void Render()
    {
        _content.Children.Clear();
        var s = BacklogLogic.Stats(Games, Library.Today);
        var top = new DockPanel { Height = 40 };
        var crumb = T($"GAME BACKLOG   /   {DateTime.Now.ToString("dddd, MMMM d").ToUpperInvariant()}", 11, "Muted");
        crumb.VerticalAlignment = VerticalAlignment.Center;
        crumb.Margin = new Thickness(0, 6, 0, 0);
        top.Children.Add(crumb);
        _content.Children.Add(top);

        var head = new DockPanel { Margin = new Thickness(0, 30, 0, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
        actions.Children.Add(Styled("What next?", "GhostButton", (_, _) => PickNext(false), new Thickness(14, 5, 14, 5)));
        var shortest = Styled("Something short", "GhostButton", (_, _) => PickNext(true), new Thickness(14, 5, 14, 5));
        shortest.Margin = new Thickness(8, 0, 0, 0);
        actions.Children.Add(shortest);
        DockPanel.SetDock(actions, Dock.Right);
        head.Children.Add(actions);
        var left = new StackPanel();
        left.Children.Add(Label("YOUR GAMES"));
        var big = Big(s.Backlog == 0 ? (s.Owned == 1 ? "1 game" : $"{s.Owned} games") : $"{s.Backlog} in the backlog", 46);
        big.Margin = new Thickness(-2, 10, 0, 6);
        left.Children.Add(big);
        var line = new List<string>();
        if (s.BacklogHours is { } bh) line.Add(s.Estimated == s.Backlog ? $"about {bh:0} hours to clear it" : $"at least {bh:0} hours to clear it ({s.Estimated} of {s.Backlog} estimated)");
        line.Add($"{BacklogLogic.Hours(s.HoursThisWeek)} played this week");
        line.Add($"{s.FinishedThisYear} finished this year");
        if (s.Platinums > 0) line.Add($"{s.Platinums} platinum{(s.Platinums == 1 ? "" : "s")}");
        line.Add($"{s.HoursTotal:0} hours in all");
        left.Children.Add(T(string.Join("   ·   ", line), 13, "Muted"));
        head.Children.Add(left);
        _content.Children.Add(head);

        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 24, 0, 18) };
        foreach (var (v, text) in new[]
        {
            (View.Playing, $"Playing  {s.Playing}"), (View.Backlog, $"Backlog  {s.Backlog}"),
            (View.Finished, $"Finished  {Games.Count(g => g.Status is GameStatus.Finished or GameStatus.Platinum)}"),
            (View.Shelved, $"Shelved  {Games.Count(g => g.Status == GameStatus.Shelved)}"), (View.Add, "Add games"),
        })
        {
            bool on = v == _view;
            var st = new StackPanel { Margin = new Thickness(0, 0, 26, 0) };
            st.Children.Add(T(text, 14, on ? "Text" : "Muted", on ? FontWeights.SemiBold : null));
            var bar = new Border { Height = 3, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 7, 0, 0) };
            if (on) bar.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            st.Children.Add(bar);
            var b = Bare(st, $"Show {v}");
            var captured = v;
            b.Click += (_, _) => { _view = captured; Render(); };
            tabs.Children.Add(b);
        }
        _content.Children.Add(tabs);

        switch (_view)
        {
            case View.Playing: Shelf(Games.Where(g => g.Status == GameStatus.Playing).OrderByDescending(g => g.Sessions.Count > 0 ? g.Sessions.Max(x => x.Date) : DateOnly.MinValue).ToList(), "Nothing on the go", "Start something from your backlog."); break;
            case View.Backlog: Shelf(Games.Where(g => g.Status == GameStatus.Backlog).OrderBy(g => g.EstimateHours ?? 9999).ToList(), "Backlog's empty", "Add the games you own but haven't played."); break;
            case View.Finished: Shelf(Games.Where(g => g.Status is GameStatus.Finished or GameStatus.Platinum).OrderByDescending(g => g.Finished).ToList(), "Nothing finished yet", "Games you finish (or platinum) show up here."); break;
            case View.Shelved: Shelf(Games.Where(g => g.Status == GameStatus.Shelved).ToList(), "Nothing shelved", "Games you've put aside go here."); break;
            case View.Add: Add(); break;
        }
    }

    internal static Border Cover(string url, double size)
    {
        var b = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(10) };
        b.SetResourceReference(Border.BackgroundProperty, "Raised");
        if (url.Length > 0 && Uri.TryCreate(url + (url.Contains('?') ? "&" : "?") + "w=" + (int)(size * 1.5), UriKind.Absolute, out var uri))
        {
            try
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.UriSource = uri;
                img.DecodePixelWidth = (int)(size * 1.5);
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                b.Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
            }
            catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException) { }
        }
        else
        {
            var g = Glyph("", 30, "Muted");
            g.HorizontalAlignment = HorizontalAlignment.Center;
            g.VerticalAlignment = VerticalAlignment.Center;
            b.Child = g;
        }
        return b;
    }

    private void Shelf(List<Game> games, string emptyTitle, string emptyText)
    {
        if (games.Count == 0)
        {
            var e = new StackPanel { MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left };
            e.Children.Add(T(emptyTitle, 18));
            e.Children.Add(T(emptyText, 13, "Muted", wrap: true));
            _content.Children.Add(e);
            return;
        }
        var grid = new WrapPanel();
        foreach (var g in games)
        {
            var st = new StackPanel { Width = 180 };
            var art = new Grid();
            art.Children.Add(Cover(g.Image, 180));
            if (g.Status == GameStatus.Platinum || g.Rating > 0)
            {
                var badge = new Border { CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 1, 6, 2), Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
                badge.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                badge.Child = new TextBlock { Text = g.Status == GameStatus.Platinum ? "🏆 Platinum" : $"★ {g.Rating}", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Brushes.Black };
                art.Children.Add(badge);
            }
            var open = Bare(art, $"Open {g.Title}");
            var captured = g;
            open.Click += (_, _) => new GameSheet(_lib, captured).Show();
            st.Children.Add(open);
            var title = T(g.Title, 13, "Text", FontWeights.SemiBold);
            title.TextWrapping = TextWrapping.Wrap;
            title.MaxHeight = 36;
            title.Margin = new Thickness(0, 8, 0, 1);
            st.Children.Add(title);
            var sub = $"{g.Platform} · {BacklogLogic.Hours(g.Hours)} played" + (g.EstimateHours is { } est && g.Status == GameStatus.Backlog ? $" · ~{est:0} h" : "");
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };
            if (g.Status is GameStatus.Playing or GameStatus.Backlog)
            {
                var plus = Styled("+1 h", "GhostButton", (_, _) => { BacklogLogic.AddHours(captured, 1, Library.Today); _lib.Save(); }, new Thickness(8, 2, 8, 2));
                System.Windows.Automation.AutomationProperties.SetName(plus, $"Played an hour of {g.Title}");
                DockPanel.SetDock(plus, Dock.Right);
                row.Children.Add(plus);
            }
            row.Children.Add(T(sub, 11, "Muted"));
            st.Children.Add(row);
            if (g.EstimateHours is { } e2 && e2 > 0 && g.Status == GameStatus.Playing)
            {
                var g2 = new Grid { Height = 4, Margin = new Thickness(0, 6, 0, 0) };
                var frac = (double)Math.Min(1, g.Hours / e2);
                g2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, frac), GridUnitType.Star) });
                g2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, 1 - frac), GridUnitType.Star) });
                var track = new Border { CornerRadius = new CornerRadius(2) };
                track.SetResourceReference(Border.BackgroundProperty, "Raised");
                Grid.SetColumnSpan(track, 2);
                g2.Children.Add(track);
                var fill = new Border { CornerRadius = new CornerRadius(2) };
                fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                g2.Children.Add(fill);
                st.Children.Add(g2);
            }
            grid.Children.Add(new Border { Child = st, Margin = new Thickness(0, 0, 18, 22), VerticalAlignment = VerticalAlignment.Top });
        }
        _content.Children.Add(grid);
    }

    private void PickNext(bool shortest)
    {
        var g = BacklogLogic.Pick(Games, Random.Shared, shortest);
        if (g is null) { Main?.ShowNotice("Your backlog is empty: nothing to pick from."); return; }
        new GameSheet(_lib, g, shortest ? "SHORTEST IN YOUR BACKLOG" : "HOW ABOUT THIS NEXT?").Show();
    }

    private async Task RunSearch()
    {
        if (_search.Text.Trim().Length == 0 || _busy) return;
        _busy = true;
        _status = "";
        Render();
        try { _hits = await _lib.Search(_search.Text); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { _status = "Couldn't search the PlayStation Store right now. You can still add a game by hand."; }
        finally { _busy = false; Render(); }
    }

    private Game AddGame(string title, string image, string platform, string conceptId, GameStatus status)
    {
        var g = new Game { Title = title, Image = image, Platform = platform.Length > 0 ? platform : "PS5", ConceptId = conceptId };
        BacklogLogic.SetStatus(g, status, Library.Today);
        Games.Insert(0, g);
        _lib.Save();
        return g;
    }

    private void Add()
    {
        // From the PlayStation add-on's wishlist
        var wished = _lib.PlayStationWishlist();
        if (wished.Count > 0)
        {
            _content.Children.Add(Label("FROM YOUR PLAYSTATION WISHLIST"));
            _content.Children.Add(T("Bought one? Move it into your backlog.", 12, "Muted"));
            var row = new WrapPanel { Margin = new Thickness(0, 10, 0, 18) };
            foreach (var w in wished)
            {
                var st = new StackPanel { Orientation = Orientation.Horizontal };
                st.Children.Add(Cover(w.Image, 44));
                var name = T(w.Name, 12, "Text", FontWeights.SemiBold);
                name.VerticalAlignment = VerticalAlignment.Center;
                name.Margin = new Thickness(10, 0, 10, 0);
                name.MaxWidth = 200;
                st.Children.Add(name);
                var bought = Styled("I bought it", "GhostButton", (_, _) => AddGame(w.Name, w.Image, "PS5", w.ConceptId, GameStatus.Backlog), new Thickness(10, 3, 10, 3));
                System.Windows.Automation.AutomationProperties.SetName(bought, $"Bought {w.Name}");
                bought.VerticalAlignment = VerticalAlignment.Center;
                st.Children.Add(bought);
                var c = Card(st, new Thickness(10, 8, 12, 8));
                c.Margin = new Thickness(0, 0, 10, 10);
                row.Children.Add(c);
            }
            _content.Children.Add(row);
        }

        var line = new DockPanel { Width = 640, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) };
        var go = Styled("Search", "AccentButton", async (_, _) => await RunSearch(), new Thickness(16, 6, 16, 6));
        go.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(go, Dock.Right);
        line.Children.Add(go);
        (_search.Parent as Panel)?.Children.Remove(_search);
        line.Children.Add(_search);
        _content.Children.Add(line);
        var manual = Bare(T("Add one by hand (any platform)", 12, "AccentBrush"), "Add a game by hand");
        manual.HorizontalAlignment = HorizontalAlignment.Left;
        manual.Click += (_, _) => new GameSheet(_lib, AddGame(_search.Text.Trim().Length > 0 ? _search.Text.Trim() : "New game", "", "PS5", "", GameStatus.Backlog)).Show();
        _content.Children.Add(manual);
        _content.Children.Add(T("Titles and covers from the PlayStation Store.", 11, "Muted"));
        if (_busy) { _content.Children.Add(T("Searching…", 13, "Muted")); return; }
        if (_status.Length > 0) { _content.Children.Add(T(_status, 13, "Danger", wrap: true)); return; }
        if (_hits is null) return;
        var grid = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        foreach (var h in _hits)
        {
            var st = new StackPanel { Width = 170 };
            st.Children.Add(Cover(h.Image, 170));
            var title = T(h.Title, 13, "Text", FontWeights.SemiBold);
            title.TextWrapping = TextWrapping.Wrap;
            title.MaxHeight = 36;
            title.Margin = new Thickness(0, 8, 0, 1);
            st.Children.Add(title);
            st.Children.Add(T(h.Platforms, 11, "Muted"));
            var have = Games.FirstOrDefault(g => g.Title.Equals(h.Title, StringComparison.OrdinalIgnoreCase));
            if (have is not null)
            {
                st.Children.Add(T("✓ " + BacklogLogic.StatusName(have.Status), 11, "AccentBrush"));
            }
            else
            {
                var picks = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
                foreach (var status in new[] { GameStatus.Backlog, GameStatus.Playing, GameStatus.Finished })
                {
                    var chip = new Border { Padding = new Thickness(8, 2, 8, 3), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 4, 4) };
                    chip.SetResourceReference(Border.BorderBrushProperty, status == GameStatus.Backlog ? "AccentBrush" : "Hairline");
                    chip.Child = T((status == GameStatus.Backlog ? "+ " : "") + BacklogLogic.StatusName(status), 11, status == GameStatus.Backlog ? "Text" : "Muted");
                    var b = Bare(chip, $"Add {h.Title} as {BacklogLogic.StatusName(status)}");
                    var platform = h.Platforms.Contains("PS5") ? "PS5" : h.Platforms.Split(" / ").FirstOrDefault() ?? "PS5";
                    var captured = status;
                    b.Click += (_, _) =>
                    {
                        var g = AddGame(h.Title, h.Image, platform, "", captured);
                        if (captured == GameStatus.Finished) new GameSheet(_lib, g, "FINISHED: HOW WAS IT?").Show();
                    };
                    picks.Children.Add(b);
                }
                st.Children.Add(picks);
            }
            grid.Children.Add(new Border { Child = st, Margin = new Thickness(0, 0, 18, 22), VerticalAlignment = VerticalAlignment.Top });
        }
        _content.Children.Add(grid);
    }
}

/// <summary>A game: status, time played, estimate, rating and notes.</summary>
internal sealed class GameSheet(Library lib, Game game, string eyebrow = "")
{
    private readonly StackPanel _root = new();

    public void Show()
    {
        _root.Unloaded += (_, _) => lib.Save();
        Render();
        var card = new Border
        {
            MaxWidth = 640, Margin = new Thickness(24), MaxHeight = 820, CornerRadius = new CornerRadius(16), Padding = new Thickness(24, 20, 24, 22), BorderThickness = new Thickness(1),
            BorderBrush = Backdrop.Wash("TextColor", 0x24), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center,
            Child = new ScrollViewer { Content = _root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false },
        };
        card.SetResourceReference(Border.BackgroundProperty, "Surface");
        card.MouseLeftButtonDown += (_, e) => e.Handled = true;
        var scrim = new Grid();
        scrim.SetResourceReference(Panel.BackgroundProperty, "Scrim");
        scrim.MouseLeftButtonDown += (_, _) => BacklogPage.Main?.HideSheet();
        scrim.KeyDown += (_, e) => { if (e.Key == Key.Escape) BacklogPage.Main?.HideSheet(); };
        scrim.Children.Add(card);
        BacklogPage.Main?.ShowSheet(scrim);
    }

    private void Quiet() => lib.Save(false);

    private static FrameworkElement Gap(FrameworkElement e, double top, double bottom)
    {
        e.Margin = new Thickness(0, top, 0, bottom);
        return e;
    }

    private void Render()
    {
        _root.Children.Clear();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var close = Bare(Glyph("", 11, "Muted"), "Close");
        close.Click += (_, _) => BacklogPage.Main?.HideSheet();
        close.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(close, Dock.Right);
        head.Children.Add(close);
        var cover = BacklogPage.Cover(game.Image, 120);
        cover.Margin = new Thickness(0, 0, 18, 0);
        DockPanel.SetDock(cover, Dock.Left);
        head.Children.Add(cover);
        var info = new StackPanel();
        if (eyebrow.Length > 0) info.Children.Add(Label(eyebrow));
        var title = Field("Title", game.Title, "Game title");
        title.FontSize = 18;
        title.Margin = new Thickness(0, 6, 0, 0);
        title.LostFocus += (_, _) => { if (title.Text.Trim().Length > 0) { game.Title = title.Text.Trim(); Quiet(); } };
        info.Children.Add(title);
        var platforms = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var p in new[] { "PS5", "PS4", "PC", "Switch", "Xbox", "Mobile" })
        {
            bool on = game.Platform == p;
            var chip = new Border { Padding = new Thickness(9, 2, 9, 3), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 4, 4), Child = T(p, 11, on ? "Text" : "Muted", on ? FontWeights.SemiBold : null) };
            chip.SetResourceReference(Border.BorderBrushProperty, on ? "AccentBrush" : "Hairline");
            var b = Bare(chip, $"Platform {p}");
            var captured = p;
            b.Click += (_, _) => { game.Platform = captured; Quiet(); Render(); };
            platforms.Children.Add(b);
        }
        info.Children.Add(platforms);
        head.Children.Add(info);
        _root.Children.Add(head);

        // Status
        var statuses = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var st in Enum.GetValues<GameStatus>())
        {
            var rb = new RadioButton { Content = BacklogLogic.StatusName(st), GroupName = "GameStatus", IsChecked = game.Status == st, MinWidth = 90, Padding = new Thickness(8, 0, 8, 0) };
            rb.SetResourceReference(FrameworkElement.StyleProperty, "Segment");
            var captured = st;
            rb.Checked += (_, _) => { if (game.Status != captured) { BacklogLogic.SetStatus(game, captured, Library.Today); Quiet(); Render(); } };
            statuses.Children.Add(rb);
        }
        var statusBox = new Border { Padding = new Thickness(3), CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), Child = statuses, HorizontalAlignment = HorizontalAlignment.Left };
        statusBox.SetResourceReference(Border.BackgroundProperty, "Bg");
        statusBox.SetResourceReference(Border.BorderBrushProperty, "Hairline");
        _root.Children.Add(statusBox);

        // Time
        _root.Children.Add(Gap(Label("TIME PLAYED", "Muted"), 16, 6));
        var time = new StackPanel { Orientation = Orientation.Horizontal };
        time.Children.Add(Big(BacklogLogic.Hours(game.Hours), 30));
        foreach (var (label, h) in new[] { ("+30 min", 0.5m), ("+1 h", 1m), ("+2 h", 2m), ("−30 min", -0.5m) })
        {
            var b = Styled(label, "GhostButton", (_, _) => { BacklogLogic.AddHours(game, h, Library.Today); Quiet(); Render(); }, new Thickness(10, 4, 10, 4));
            b.Margin = new Thickness(10, 0, 0, 0);
            b.VerticalAlignment = VerticalAlignment.Center;
            time.Children.Add(b);
        }
        _root.Children.Add(time);
        var est = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        est.Children.Add(Gap(T("Takes about", 12, "Muted"), 7, 0));
        var estBox = Field("e.g. 30", game.EstimateHours?.ToString("0.#") ?? "", "Estimated hours");
        estBox.Width = 70;
        estBox.Margin = new Thickness(8, 0, 8, 0);
        estBox.LostFocus += (_, _) => { game.EstimateHours = decimal.TryParse(estBox.Text.Trim(), System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0 ? v : null; Quiet(); };
        est.Children.Add(estBox);
        est.Children.Add(Gap(T("hours to finish (helps pick what's next)", 12, "Muted"), 7, 0));
        _root.Children.Add(est);

        // Rating
        _root.Children.Add(Gap(Label("YOUR RATING", "Muted"), 16, 6));
        var stars = new StackPanel { Orientation = Orientation.Horizontal };
        for (int r = 1; r <= 10; r++)
        {
            var n = r;
            var on = game.Rating >= r;
            var b = Bare(T(on ? "★" : "☆", 20, on ? "AccentBrush" : "Muted"), $"Rate {r} out of 10");
            b.Margin = new Thickness(0, 0, 3, 0);
            b.Click += (_, _) => { game.Rating = game.Rating == n ? 0 : n; Quiet(); Render(); };
            stars.Children.Add(b);
        }
        _root.Children.Add(stars);

        _root.Children.Add(Gap(Label("NOTES", "Muted"), 16, 6));
        var notes = Field("Where you're up to, trophies left, thoughts…", game.Notes, "Game notes");
        notes.AcceptsReturn = true;
        notes.TextWrapping = TextWrapping.Wrap;
        notes.MinHeight = 60;
        notes.TextChanged += (_, _) => game.Notes = notes.Text;
        notes.LostFocus += (_, _) => Quiet(); // saved when you leave the box, not on every key
        _root.Children.Add(notes);

        var footer = new DockPanel { Margin = new Thickness(0, 18, 0, 0) };
        bool armed = false;
        Button? remove = null;
        remove = Styled("Remove game", "GhostButton", (_, _) =>
        {
            if (!armed) { armed = true; remove!.Content = "Remove it and its hours? Click again"; return; }
            lib.Data.Games.Remove(game);
            lib.Save();
            BacklogPage.Main?.HideSheet();
        }, new Thickness(12, 5, 12, 5));
        DockPanel.SetDock(remove, Dock.Left);
        footer.Children.Add(remove);
        var done = Styled("Done", "AccentButton", (_, _) => BacklogPage.Main?.HideSheet(), new Thickness(18, 6, 18, 6));
        done.HorizontalAlignment = HorizontalAlignment.Right;
        footer.Children.Add(done);
        _root.Children.Add(footer);
    }
}
