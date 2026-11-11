using System.Globalization;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CommandHub.Models;
using CommandHub.Services;

namespace CommandHub.Tiles.News;

public sealed class NewsTile : ITileType
{
    public string TypeId => "news";
    public string DisplayName => "News";
    public string Icon => "";
    public IReadOnlyList<TileSize> AllowedSizes { get; } = [TileSize.Wide, TileSize.Tall, TileSize.Large];
    public TileSize DefaultSize => TileSize.Wide;
    public string AccentKey => "Accent4Brush";
    public FrameworkElement CreateView(TileContext ctx) => new NewsView(ctx);
}

/// <summary>
/// Content: <c>{ "sources": [ { "name", "url" } ], "items": [ { "title", "link", "source", "published" } ], "updated": ISO time }</c>.
/// Starts with BBC News and Dawn. Refreshes every 30 minutes and keeps the last headlines for when you're offline.
/// </summary>
public sealed class NewsView : UserControl
{
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(30);
    private const int Keep = 40;
    private readonly TileContext _ctx;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(5) };
    private bool _busy, _editing;
    private string _status = "";

    public NewsView(TileContext ctx)
    {
        _ctx = ctx;
        if (C["sources"] is not JsonArray)
            WriteSources([NewsFeed.Suggested[0], NewsFeed.Suggested[1]]);
        _timer.Tick += async (_, _) => await RefreshIfStale();
        Loaded += async (_, _) => { _timer.Start(); await RefreshIfStale(); };
        Unloaded += (_, _) => _timer.Stop();
        Render();
    }

    private JsonObject C => _ctx.Content;

    private List<NewsSource> Sources =>
        Json.Objects(C["sources"]).Select(o => new NewsSource(Json.Str(o["name"]), Json.Str(o["url"]), ""))
            .Where(s => s.Url.Length > 0).ToList();

    private void WriteSources(IEnumerable<NewsSource> sources) =>
        C["sources"] = new JsonArray(sources.Select(s => (JsonNode)new JsonObject { ["name"] = s.Name, ["url"] = s.Url }).ToArray());

    private List<NewsItem> Items =>
        Json.Objects(C["items"]).Select(o => new NewsItem(Json.Str(o["title"]), Json.Str(o["link"]), Json.Str(o["source"]),
            DateTimeOffset.TryParse(Json.Str(o["published"]), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var p) ? p : null))
            .Where(i => i.Title.Length > 0 && i.Link.Length > 0).ToList();

    private DateTime? Updated =>
        DateTime.TryParse(Json.Str(C["updated"]), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t) ? t : null;

    private async Task RefreshIfStale()
    {
        if (_busy || _editing || Sources.Count == 0) return;
        if (Updated is { } t && DateTime.UtcNow - t.ToUniversalTime() < RefreshEvery) return;
        await Refresh();
    }

    private async Task Refresh()
    {
        var sources = Sources;
        if (sources.Count == 0) return;
        _busy = true;
        _status = "";
        if (!_editing) Render();
        try
        {
            var results = await Task.WhenAll(sources.Select(Fetch));
            var ok = results.Where(r => r != null).ToList();
            if (ok.Count == 0) { _status = "Offline"; return; }
            // Sources may have changed while this was loading.
            var now = Sources.Select(s => s.Url).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var items = NewsFeed.Merge(ok.Where(r => now.Contains(r!.Value.Url)).Select(r => r!.Value.Items), Keep);
            C["items"] = new JsonArray(items.Select(i => (JsonNode)new JsonObject
            {
                ["title"] = i.Title, ["link"] = i.Link, ["source"] = i.Source,
                ["published"] = i.Published?.ToString("o", CultureInfo.InvariantCulture) ?? "",
            }).ToArray());
            C["updated"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            _status = ok.Count < results.Length ? $"{results.Length - ok.Count} source{(results.Length - ok.Count == 1 ? "" : "s")} didn't load" : "";
            _ctx.SaveNow();
        }
        finally
        {
            _busy = false;
            if (!_editing) Render();
        }
    }

    private static async Task<(string Url, List<NewsItem> Items)?> Fetch(NewsSource source)
    {
        try
        {
            var xml = await Net.Http.GetStringAsync(source.Url);
            var feed = NewsFeed.Parse(xml, source.Name);
            if (feed is null) return null;
            // Show the name the user chose for a source, not whatever the feed calls itself.
            var name = source.Name.Length > 0 ? source.Name : feed.Title;
            return (source.Url, feed.Items.Select(i => i with { Source = name }).ToList());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or UriFormatException)
        {
            return null;
        }
    }

    // ---------------- Headlines ----------------

    private void Render()
    {
        _editing = false;
        var root = new Grid();
        var edit = TileUi.EditButton("Choose news sources");
        edit.Click += (_, _) => ShowSources();

        var items = Items;
        if (items.Count == 0)
        {
            var text = Sources.Count == 0 ? "Pick some news sources with the pencil above."
                : _busy ? "Fetching the headlines…"
                : _status == "Offline" ? "Can't reach the news right now." : "No headlines yet.";
            var wait = TileUi.Text(text, 12, "Muted");
            wait.TextWrapping = TextWrapping.Wrap;
            root.Children.Add(wait);
            root.Children.Add(edit);
            Content = root;
            return;
        }

        var list = new StackPanel();
        var now = DateTimeOffset.UtcNow;
        foreach (var item in items)
        {
            var s = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            var title = TileUi.Text(item.Title, 13, "Text");
            title.TextWrapping = TextWrapping.Wrap;
            title.MaxHeight = 36;
            s.Children.Add(title);
            var meta = item.Published is { } p ? $"{item.Source}  ·  {NewsFeed.Ago(p, now)}" : item.Source;
            s.Children.Add(TileUi.Text(meta, 10, "Muted"));
            var b = new Button { Content = s, Cursor = Cursors.Hand, ToolTip = item.Link, HorizontalContentAlignment = HorizontalAlignment.Stretch, Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0) };
            b.Template = Bare;
            b.SetResourceReference(FrameworkElement.FocusVisualStyleProperty, "FocusRing");
            System.Windows.Automation.AutomationProperties.SetName(b, item.Title);
            var link = item.Link;
            b.MouseEnter += (_, _) => title.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            b.MouseLeave += (_, _) => title.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            b.Click += (_, _) => Views.AboutPage.Open(link);
            list.Children.Add(b);
        }
        var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
        var layout = new DockPanel();
        if (_status.Length > 0 || _busy)
        {
            var status = TileUi.Text(_busy ? "Refreshing…" : Updated is { } u ? $"{_status} · updated {u.ToLocalTime():t}" : _status, 10, "Muted");
            status.Margin = new Thickness(0, 4, 0, 0);
            DockPanel.SetDock(status, Dock.Bottom);
            layout.Children.Add(status);
        }
        layout.Children.Add(scroll);
        root.Children.Add(layout);
        root.Children.Add(edit);
        Content = root;
    }

    private static readonly ControlTemplate Bare = MakeBare();

    private static ControlTemplate MakeBare()
    {
        var t = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, System.Windows.Media.Brushes.Transparent);
        border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        t.VisualTree = border;
        return t;
    }

    // ---------------- Sources ----------------

    private void ShowSources()
    {
        _editing = true;
        var chosen = Sources;
        var stack = new StackPanel();
        stack.Children.Add(TileUi.Text("Show headlines from", 12, "Muted"));
        var wrap = new WrapPanel { Margin = new Thickness(0, 8, 0, 4) };
        var picks = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);
        var all = NewsFeed.Suggested.ToList();
        foreach (var mine in chosen.Where(c => all.All(s => !s.Url.Equals(c.Url, StringComparison.OrdinalIgnoreCase))))
            all.Add(mine with { Topic = "Your feed" });
        foreach (var s in all)
        {
            var cb = new CheckBox { Content = s.Name, ToolTip = $"{s.Topic} · {s.Url}", IsChecked = chosen.Any(c => c.Url.Equals(s.Url, StringComparison.OrdinalIgnoreCase)), Margin = new Thickness(0, 0, 12, 6) };
            cb.SetResourceReference(ForegroundProperty, "Text");
            System.Windows.Automation.AutomationProperties.SetName(cb, $"News source {s.Name}");
            picks[s.Url] = cb;
            wrap.Children.Add(cb);
        }
        stack.Children.Add(wrap);

        var custom = TileUi.Field("Another feed: https://…/rss", "", "Feed address");
        var error = TileUi.Text("", 11, "Danger");
        error.TextWrapping = TextWrapping.Wrap;
        var done = TileUi.Ghost("Done", (_, _) => { }, accent: true);
        done.Margin = new Thickness(6, 0, 0, 0);
        System.Windows.Automation.AutomationProperties.SetName(done, "Save news sources");
        var line = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        DockPanel.SetDock(done, Dock.Right);
        line.Children.Add(done);
        line.Children.Add(custom);
        stack.Children.Add(line);
        stack.Children.Add(error);

        async Task Save()
        {
            var list = all.Where(s => picks[s.Url].IsChecked == true).ToList();
            var extra = custom.Text.Trim();
            if (extra.Length > 0)
            {
                if (!extra.StartsWith("http", StringComparison.OrdinalIgnoreCase)) extra = "https://" + extra;
                if (!Uri.TryCreate(extra, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
                {
                    error.Text = "That doesn't look like a web address.";
                    return;
                }
                done.IsEnabled = false;
                error.Text = "Checking the feed…";
                var check = await Fetch(new NewsSource("", uri.ToString(), ""));
                done.IsEnabled = true;
                if (check is null) { error.Text = "Couldn't read a news feed at that address."; custom.Focus(); return; }
                if (list.All(s => !s.Url.Equals(uri.ToString(), StringComparison.OrdinalIgnoreCase)))
                    list.Add(new NewsSource(check.Value.Items.FirstOrDefault()?.Source ?? uri.Host, uri.ToString(), "Your feed"));
            }
            WriteSources(list);
            C.Remove("updated"); // fetch again with the new sources
            if (list.Count == 0) C.Remove("items");
            _ctx.SaveNow();
            _editing = false;
            Render();
            await Refresh();
        }

        done.Click += async (_, _) => await Save();
        custom.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; await Save(); }
            else if (e.Key == Key.Escape) { e.Handled = true; Render(); }
        };
        Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
    }
}
