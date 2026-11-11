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

namespace CommandHub.AddOn.PlayStation;

/// <summary>
/// The PlayStation tab, laid out like the PS5 store: big cover art in a grid, prices in rupees,
/// the cheapest region highlighted. Wishlist, Deals and Search.
/// </summary>
public sealed class PlayStationPage : UserControl
{
    private enum View { Wishlist, Deals, Search }

    private static readonly Brush PsBlue = ColorBrush("#0072CE");
    private static readonly Brush SaleYellow = ColorBrush("#FFC83D");

    private readonly Tracker _tracker;
    private readonly Border _base = new(), _glow = new() { IsHitTestVisible = false };
    private readonly StackPanel _content = new() { Margin = new Thickness(33, 0, 33, 40) };
    private readonly StackPanel _body = new();
    private View _view;
    private readonly TextBox _search = Field("Search the PlayStation Store", "", "Search games");
    private List<StoreHit>? _hits, _deals;
    private string _dealsRegion = "", _status = "";
    private bool _loading;

    public PlayStationPage(Tracker tracker)
    {
        _tracker = tracker;
        _view = tracker.Data.Wishlist.Count == 0 ? View.Deals : View.Wishlist;
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
            _tracker.Changed += OnChanged;
            Paint();
            Render();
            await _tracker.EnsureRates();
            if (_view == View.Deals) await LoadDeals();
        };
        Unloaded += (_, _) => { App.ThemeChanged -= OnTheme; _tracker.Changed -= OnChanged; };
        Paint();
    }

    private void Paint()
    {
        Backdrop.Paint(_base, _glow);
        // A touch of PlayStation blue in the corner glow.
        _glow.Background = new RadialGradientBrush(Color.FromArgb(0x40, 0x00, 0x72, 0xCE), Colors.Transparent) { Center = new Point(0.85, 0.0), GradientOrigin = new Point(0.85, 0.0), RadiusX = 0.7, RadiusY = 0.6 };
    }

    private void OnTheme() { Paint(); Render(); }
    private void OnChanged() => Dispatcher.BeginInvoke(Render);

    private PsData D => _tracker.Data;
    private string Pkr(decimal? v) => v is { } x ? WishLogic.Money(x, "PKR") : "–";

    // ---------------- Frame ----------------

    private void Render()
    {
        _content.Children.Clear();
        var top = new DockPanel { Height = 40 };
        var crumb = T($"PLAYSTATION   /   {DateTime.Now.ToString("dddd, MMMM d").ToUpperInvariant()}", 11, "Muted");
        crumb.VerticalAlignment = VerticalAlignment.Center;
        crumb.Margin = new Thickness(0, 6, 0, 0);
        top.Children.Add(crumb);
        _content.Children.Add(top);

        var head = new DockPanel { Margin = new Thickness(0, 30, 0, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
        var check = Styled(_tracker.Checking ? "Checking prices…" : "Check prices now", "GhostButton", async (_, _) => await _tracker.CheckAll(), new Thickness(14, 5, 14, 5));
        check.IsEnabled = !_tracker.Checking && D.Wishlist.Count > 0;
        actions.Children.Add(check);
        var rates = Styled("Rates", "GhostButton", (_, _) => OpenRates(), new Thickness(14, 5, 14, 5));
        rates.Margin = new Thickness(8, 0, 0, 0);
        actions.Children.Add(rates);
        DockPanel.SetDock(actions, Dock.Right);
        head.Children.Add(actions);

        var left = new StackPanel();
        left.Children.Add(Label("GAME PRICES IN RUPEES"));
        var big = Big("PlayStation Store", 46);
        big.Margin = new Thickness(-2, 10, 0, 6);
        left.Children.Add(big);
        left.Children.Add(T(SubLine(), 13, D.LastError.Length > 0 ? "Danger" : "Muted", wrap: true));
        head.Children.Add(left);
        _content.Children.Add(head);

        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 24, 0, 18) };
        foreach (var (v, text) in new[] { (View.Wishlist, $"Wishlist  {D.Wishlist.Count}"), (View.Deals, "Deals"), (View.Search, "Search") })
        {
            bool on = v == _view;
            var label = T(text, 14, on ? "Text" : "Muted", on ? FontWeights.SemiBold : null);
            var line = new Border { Height = 3, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 7, 0, 0), Background = on ? PsBlue : Brushes.Transparent };
            var s = new StackPanel { Margin = new Thickness(0, 0, 26, 0) };
            s.Children.Add(label);
            s.Children.Add(line);
            var b = Bare(s, $"Show {v}");
            var captured = v;
            b.Click += async (_, _) =>
            {
                _view = captured;
                Render();
                if (captured == View.Deals && _deals is null) await LoadDeals();
                if (captured == View.Search) _ = Dispatcher.BeginInvoke(() => _search.Focus(), DispatcherPriority.Input);
            };
            tabs.Children.Add(b);
        }
        _content.Children.Add(tabs);

        _body.Children.Clear();
        (_body.Parent as Panel)?.Children.Remove(_body);
        _content.Children.Add(_body);
        switch (_view)
        {
            case View.Wishlist: RenderWishlist(); break;
            case View.Deals: RenderDeals(); break;
            case View.Search: RenderSearch(); break;
        }
    }

    private string SubLine()
    {
        if (D.LastError.Length > 0) return D.LastError;
        var parts = new List<string>();
        if (_tracker.Checking) parts.Add("Checking your wishlist…");
        else if (D.LastCheckUtc is { } t) parts.Add($"Wishlist checked {Ago(t)}, again in about a day");
        else if (D.Wishlist.Count > 0) parts.Add("Wishlist prices are checked once a day");
        if (D.Rates is { } r)
        {
            var usd = D.ManualPkrPerUsd ?? r.PkrPerUsd;
            parts.Add($"$1 = Rs {usd:0.##}");
            parts.Add($"1 TL = Rs {usd / r.TryPerUsd:0.##}");
            parts.Add($"1 UAH = Rs {usd / r.UahPerUsd:0.##}");
        }
        else parts.Add("Getting exchange rates…");
        return string.Join("   ·   ", parts);
    }

    private static string Ago(DateTime utc)
    {
        var d = DateTime.UtcNow - utc;
        return d.TotalMinutes < 2 ? "just now" : d.TotalHours < 1 ? $"{(int)d.TotalMinutes} min ago" : d.TotalDays < 1 ? $"{(int)d.TotalHours} h ago" : $"{(int)d.TotalDays} d ago";
    }

    // ---------------- Cards ----------------

    private const double CardWidth = 214;

    private static Border Cover(string url, double size, double radius = 12)
    {
        var b = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(radius) };
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
            catch (Exception ex) when (ex is NotSupportedException or UriFormatException or InvalidOperationException) { }
        }
        return b;
    }

    private static Border Badge(string text, Brush background, Brush? foreground = null)
    {
        var t = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = foreground ?? Brushes.White };
        return new Border { Background = background, CornerRadius = new CornerRadius(5), Padding = new Thickness(7, 2, 7, 3), Child = t, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    }

    private Button CardButton(UIElement content, string name, Action click)
    {
        var holder = new Border { Child = content, RenderTransform = new TranslateTransform(), Padding = new Thickness(0, 0, 0, 4) };
        var b = Bare(holder, name);
        b.Margin = new Thickness(0, 0, 18, 22);
        b.VerticalAlignment = VerticalAlignment.Top;
        b.MouseEnter += (_, _) => ((TranslateTransform)holder.RenderTransform).Y = -3;
        b.MouseLeave += (_, _) => ((TranslateTransform)holder.RenderTransform).Y = 0;
        b.Click += (_, _) => click();
        return b;
    }

    private void Empty(string title, string text)
    {
        var s = new StackPanel { Margin = new Thickness(0, 30, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 520 };
        s.Children.Add(T(title, 18, "Text"));
        s.Children.Add(Top(T(text, 13, "Muted", wrap: true), 6));
        _body.Children.Add(s);
    }

    private static FrameworkElement Top(FrameworkElement e, double top)
    {
        e.Margin = new Thickness(e.Margin.Left, top, e.Margin.Right, e.Margin.Bottom);
        return e;
    }

    // ---------------- Wishlist ----------------

    private void RenderWishlist()
    {
        if (D.Wishlist.Count == 0)
        {
            Empty("Your wishlist is empty", "Find a game in Deals or Search, open it and add it to your wishlist. Its price is then checked in all three stores once a day, and the lowest it has been is kept.");
            return;
        }
        var bestTotal = D.Wishlist.Select(w => WishLogic.Best(w, D.Rates, D.ManualPkrPerUsd)?.Pkr).OfType<decimal>().Sum();
        var onSale = D.Wishlist.Count(w => w.Offers.Values.Any(o => o.OnSale));
        _body.Children.Add(Top(T($"{D.Wishlist.Count} {(D.Wishlist.Count == 1 ? "game" : "games")}   ·   {onSale} on sale somewhere   ·   all at their best price: {Pkr(bestTotal)}", 12, "Muted"), 0));
        var grid = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        foreach (var item in D.Wishlist) grid.Children.Add(WishCard(item));
        _body.Children.Add(grid);
    }

    private Button WishCard(WishItem item)
    {
        var prices = WishLogic.Prices(item, D.Rates, D.ManualPkrPerUsd);
        var best = prices.FirstOrDefault(p => p.Pkr is not null);
        var s = new StackPanel { Width = CardWidth };
        var art = new Grid();
        art.Children.Add(Cover(item.Image, CardWidth));
        if (best?.Offer.OnSale == true)
        {
            var badge = Badge($"-{best.Offer.PercentOff}%", PsBlue);
            badge.Margin = new Thickness(10);
            art.Children.Add(badge);
        }
        if (item.TargetPkr is { } target && best?.Pkr <= target)
        {
            var hit = Badge("Target hit", SaleYellow, Brushes.Black);
            hit.HorizontalAlignment = HorizontalAlignment.Right;
            hit.Margin = new Thickness(10);
            art.Children.Add(hit);
        }
        s.Children.Add(art);
        var name = T(item.Name, 14, "Text", FontWeights.SemiBold);
        name.TextWrapping = TextWrapping.Wrap;
        name.MaxHeight = 40;
        name.Margin = new Thickness(0, 10, 0, 0);
        s.Children.Add(name);
        if (item.EditionName.Length > 0) s.Children.Add(T(item.EditionName, 11, "Muted"));

        var bestLine = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 6) };
        bestLine.Children.Add(T(best is null ? "Not on sale anywhere" : Pkr(best.Pkr), 20, "Text", FontWeights.SemiBold));
        if (best is not null) bestLine.Children.Add(Top(T($"  in {best.Region.Name}", 11, "Muted"), 9));
        s.Children.Add(bestLine);

        foreach (var p in prices)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 3) };
            var right = T(p.Pkr is null ? "Not sold" : Pkr(p.Pkr), 12, p == best ? "Text" : "Muted", p == best ? FontWeights.SemiBold : null);
            DockPanel.SetDock(right, Dock.Right);
            row.Children.Add(right);
            var code = T(p.Region.Code, 10, "Muted", FontWeights.SemiBold);
            code.Width = 26;
            row.Children.Add(code);
            row.Children.Add(T(p.Offer.Available ? WishLogic.Money(p.Offer.SaleMinor / 100m, p.Offer.Currency) + (p.Offer.OnSale ? $"  -{p.Offer.PercentOff}%" : "") : "", 11, p.Offer.OnSale ? "AccentBrush" : "Muted"));
            s.Children.Add(row);
        }
        if (item.Lowest is { } low)
            s.Children.Add(Top(T($"Lowest {Pkr(low.Pkr)} · {low.Region} · {low.Date:d MMM}", 11, "Muted"), 6));
        if (best?.Offer is { OnSale: true, EndsUtc: { } ends })
            s.Children.Add(T($"Sale ends {ends.ToLocalTime():d MMM}", 11, "AccentBrush"));
        return CardButton(s, $"Open {item.Name}", () => OpenDetails(item.Offers.Values.FirstOrDefault(o => o.ProductId.Length > 0)?.ProductId ?? "", FoundIn(item), item));
    }

    private static Region FoundIn(WishItem item) =>
        Region.All.FirstOrDefault(r => item.Offers.TryGetValue(r.Code, out var o) && o.ProductId.Length > 0) ?? Region.US;

    // ---------------- Deals and search ----------------

    private async Task LoadDeals()
    {
        var region = Region.Find(D.DealsRegion) ?? Region.TR;
        if (_loading) return;
        _loading = true;
        _status = "";
        Render();
        try
        {
            _deals = await _tracker.Store.Deals(region);
            _dealsRegion = region.Code;
        }
        catch (StoreChangedException ex) { _status = ex.Message; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { _status = "Couldn't reach the PlayStation Store. Check your connection."; }
        finally { _loading = false; Render(); }
    }

    private void RegionPicker(string current, Action<Region> pick)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
        foreach (var r in Region.All)
        {
            bool on = r.Code == current;
            var chip = new Border { Padding = new Thickness(12, 5, 12, 6), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 8, 0), Child = T(r.Name, 12, on ? "Text" : "Muted", on ? FontWeights.SemiBold : null) };
            if (on) { chip.Background = PsBlue; chip.BorderBrush = PsBlue; ((TextBlock)chip.Child).Foreground = Brushes.White; }
            else chip.SetResourceReference(Border.BorderBrushProperty, "Hairline");
            var b = Bare(chip, $"Store {r.Name}");
            var captured = r;
            b.Click += (_, _) => pick(captured);
            row.Children.Add(b);
        }
        _body.Children.Add(row);
    }

    private void RenderDeals()
    {
        RegionPicker(D.DealsRegion, async r =>
        {
            D.DealsRegion = r.Code;
            _tracker.Touch();
            _deals = null;
            await LoadDeals();
        });
        if (_loading) { _body.Children.Add(T("Loading deals…", 13, "Muted")); return; }
        if (_status.Length > 0) { _body.Children.Add(T(_status, 13, "Danger", wrap: true)); return; }
        if (_deals is null || _dealsRegion != D.DealsRegion) return;
        if (_deals.Count == 0) { Empty("No deals right now", "The store isn't running any offers in this region at the moment."); return; }
        HitGrid(_deals, Region.Find(_dealsRegion)!);
    }

    private void RenderSearch()
    {
        RegionPicker(D.SearchRegion, r => { D.SearchRegion = r.Code; _tracker.Touch(); _hits = null; Render(); });
        var line = new DockPanel { Margin = new Thickness(0, 0, 0, 18), Width = 640, HorizontalAlignment = HorizontalAlignment.Left };
        var go = Styled("Search", "AccentButton", async (_, _) => await RunSearch(), new Thickness(16, 6, 16, 6));
        go.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(go, Dock.Right);
        line.Children.Add(go);
        (_search.Parent as Panel)?.Children.Remove(_search);
        _search.Width = double.NaN;
        line.Children.Add(_search);
        _body.Children.Add(line);
        if (_loading) { _body.Children.Add(T("Searching…", 13, "Muted")); return; }
        if (_status.Length > 0) { _body.Children.Add(T(_status, 13, "Danger", wrap: true)); return; }
        if (_hits is null) { _body.Children.Add(T("Type a game's name. Results show this store's price and what it is in rupees.", 13, "Muted", wrap: true)); return; }
        if (_hits.Count == 0) { _body.Children.Add(T("Nothing found.", 13, "Muted")); return; }
        HitGrid(_hits, Region.Find(D.SearchRegion) ?? Region.US);
    }

    private async Task RunSearch()
    {
        var term = _search.Text.Trim();
        if (term.Length == 0 || _loading) return;
        _loading = true;
        _status = "";
        Render();
        try { _hits = await _tracker.Store.Search(term, Region.Find(D.SearchRegion) ?? Region.US); }
        catch (StoreChangedException ex) { _status = ex.Message; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { _status = "Couldn't reach the PlayStation Store. Check your connection."; }
        finally { _loading = false; Render(); }
    }

    private void HitGrid(List<StoreHit> hits, Region region)
    {
        var grid = new WrapPanel();
        foreach (var h in hits)
        {
            var s = new StackPanel { Width = CardWidth };
            var art = new Grid();
            art.Children.Add(Cover(h.Image, CardWidth));
            if (!string.IsNullOrEmpty(h.DiscountText))
            {
                var badge = Badge(h.DiscountText, PsBlue);
                badge.Margin = new Thickness(10);
                art.Children.Add(badge);
            }
            s.Children.Add(art);
            var name = T(h.Name, 13, "Text", FontWeights.SemiBold);
            name.TextWrapping = TextWrapping.Wrap;
            name.MaxHeight = 38;
            name.Margin = new Thickness(0, 10, 0, 2);
            s.Children.Add(name);
            s.Children.Add(T(string.Join(" · ", new[] { h.Classification }.Concat(h.Platforms).Where(x => x.Length > 0)), 10, "Muted"));
            var price = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            var sale = Store.ParseShownPrice(h.SalePrice);
            price.Children.Add(T(h.IsFree ? "Free" : h.SalePrice, 13, "Text", FontWeights.SemiBold));
            if (h.BasePrice != h.SalePrice && h.BasePrice.Length > 0)
            {
                var was = T(h.BasePrice, 11, "Muted");
                was.TextDecorations = TextDecorations.Strikethrough;
                was.Margin = new Thickness(8, 2, 0, 0);
                price.Children.Add(was);
            }
            s.Children.Add(price);
            if (sale is { } v && v > 0) s.Children.Add(T("≈ " + Pkr(_tracker.Pkr(v, region.Currency)), 12, "AccentBrush"));
            var id = h.ProductId;
            grid.Children.Add(CardButton(s, $"Open {h.Name}", () => OpenDetails(id, region, null)));
        }
        _body.Children.Add(grid);
    }

    // ---------------- Details sheet ----------------

    private void OpenDetails(string productId, Region region, WishItem? wished)
    {
        if (productId.Length == 0) return;
        _ = new DetailSheet(_tracker, productId, region, wished).Show();
    }

    // ---------------- Rates ----------------

    private void OpenRates()
    {
        var form = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var close = Bare(Glyph("", 11, "Muted"), "Close");
        close.Click += (_, _) => Main?.HideSheet();
        DockPanel.SetDock(close, Dock.Right);
        head.Children.Add(close);
        head.Children.Add(Label("EXCHANGE RATES"));
        form.Children.Add(head);
        var r = D.Rates;
        if (r is null) form.Children.Add(T("Rates haven't loaded yet. Check your connection.", 12, "Muted", wrap: true));
        else
        {
            void Line(string what, string value, string source)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
                var v = T(value, 13, "Text", FontWeights.SemiBold);
                DockPanel.SetDock(v, Dock.Right);
                row.Children.Add(v);
                var t = new StackPanel();
                t.Children.Add(T(what, 13));
                t.Children.Add(T(source, 11, "Muted"));
                row.Children.Add(t);
                form.Children.Add(row);
            }
            Line("US dollar to rupee", $"Rs {r.PkrPerUsd:0.####}", r.PkrSource);
            Line("Dollar to Turkish lira", $"{r.TryPerUsd:0.####} TL", r.TrySource);
            Line("Dollar to hryvnia", $"{r.UahPerUsd:0.####} UAH", r.UahSource);
            form.Children.Add(T($"Updated {r.FetchedUtc.ToLocalTime():d MMM, h:mm tt}. Lira and hryvnia go to rupees through the dollar.", 11, "Muted", wrap: true));
        }
        form.Children.Add(Top(T("SBP RATE (OPTIONAL)", 9, "AccentBrush", FontWeights.SemiBold), 18));
        form.Children.Add(Top(T("The State Bank of Pakistan doesn't let apps read its rate. To use it, type today's USD/PKR rate from sbp.org.pk; it's used instead of the market rate until you clear it.", 12, "Muted", wrap: true), 6));
        var field = Field("e.g. 276.65", D.ManualPkrPerUsd?.ToString("0.####") ?? "", "SBP rate");
        field.Margin = new Thickness(0, 8, 0, 0);
        form.Children.Add(field);
        var error = T("", 11, "Danger");
        form.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var save = Styled("Save", "AccentButton", (_, _) =>
        {
            var text = field.Text.Trim();
            if (text.Length == 0) { _tracker.SetManualRate(null); Main?.HideSheet(); return; }
            if (!decimal.TryParse(text, System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out var v) || v < 50 || v > 5000)
            {
                error.Text = "Type the rate as a number, like 276.65.";
                return;
            }
            _tracker.SetManualRate(v);
            Main?.HideSheet();
        }, new Thickness(18, 6, 18, 6));
        System.Windows.Automation.AutomationProperties.SetName(save, "Save rate");
        buttons.Children.Add(save);
        form.Children.Add(buttons);
        field.KeyDown += (_, e) => { if (e.Key == Key.Enter) save.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); };
        DetailSheet.ShowCard(form, 440);
        Dispatcher.BeginInvoke(() => field.Focus(), DispatcherPriority.Input);
    }

    private static MainWindow? Main => Application.Current.MainWindow as MainWindow;
}
