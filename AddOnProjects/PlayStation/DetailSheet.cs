using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommandHub.Home;
using static CommandHub.Home.PageUi;

namespace CommandHub.AddOn.PlayStation;

/// <summary>A game's page: every edition, its price in each store and in rupees, and the wishlist button.</summary>
public sealed class DetailSheet(Tracker tracker, string productId, Region foundIn, WishItem? wished)
{
    private readonly StackPanel _body = new();
    private Dictionary<string, Concept?>? _everywhere;
    private string _error = "";

    private static MainWindow? Main => Application.Current.MainWindow as MainWindow;

    /// <summary>Shows any content in a centred card over the window (Escape or a click outside closes it).</summary>
    public static void ShowCard(UIElement content, double width)
    {
        var card = new Border
        {
            Width = width, MaxHeight = 760, CornerRadius = new CornerRadius(16), Padding = new Thickness(24, 20, 24, 22), BorderThickness = new Thickness(1),
            BorderBrush = Backdrop.Wash("TextColor", 0x24), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
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

    public async Task Show()
    {
        Render();
        ShowCard(_body, 800);
        try
        {
            await tracker.EnsureRates();
            _everywhere = await tracker.LoadEverywhere(productId, foundIn);
            if (_everywhere.GetValueOrDefault(foundIn.Code) is null) _error = "This game isn't on sale in that store any more.";
        }
        catch (StoreChangedException ex) { _error = ex.Message; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { _error = "Couldn't reach the PlayStation Store. Check your connection."; }
        Render();
    }

    private void Render()
    {
        _body.Children.Clear();
        var concept = _everywhere?.GetValueOrDefault(foundIn.Code);
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var close = Bare(Glyph("", 11, "Muted"), "Close");
        close.Click += (_, _) => Main?.HideSheet();
        close.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(close, Dock.Right);
        head.Children.Add(close);
        var image = concept?.Image ?? wished?.Image ?? "";
        if (image.Length > 0)
        {
            var art = new Border { Width = 120, Height = 120, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 18, 0) };
            try { art.Background = new ImageBrush(new BitmapImage(new Uri(image + (image.Contains('?') ? "&" : "?") + "w=240"))) { Stretch = Stretch.UniformToFill }; }
            catch (Exception ex) when (ex is UriFormatException or NotSupportedException) { }
            DockPanel.SetDock(art, Dock.Left);
            head.Children.Add(art);
        }
        var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(Label("PLAYSTATION STORE"));
        var name = Big(concept?.Name ?? wished?.Name ?? "Loading…", 30);
        name.TextWrapping = TextWrapping.Wrap;
        name.Margin = new Thickness(-1, 6, 0, 4);
        title.Children.Add(name);
        title.Children.Add(T(_everywhere is null && _error.Length == 0 ? "Getting prices from the US, Turkish and Ukrainian stores…" : "Prices now, in each store and in rupees. The cheapest is highlighted.", 12, "Muted", wrap: true));
        head.Children.Add(title);
        _body.Children.Add(head);

        if (_error.Length > 0) { _body.Children.Add(T(_error, 13, "Danger", wrap: true)); return; }
        if (concept is null) return;

        foreach (var edition in concept.Editions)
            _body.Children.Add(EditionCard(concept, edition));
    }

    private Border EditionCard(Concept concept, Edition edition)
    {
        var s = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var existing = tracker.Data.Wishlist.FirstOrDefault(w => w.ConceptId == concept.Id && w.Label.Equals(edition.Label, StringComparison.OrdinalIgnoreCase));
        var action = existing is null
            ? Styled("Add to wishlist", "AccentButton", (_, _) => { tracker.Add(concept, edition, _everywhere!); Render(); }, new Thickness(14, 5, 14, 5))
            : Styled("Remove from wishlist", "GhostButton", (_, _) => { tracker.Remove(existing); Render(); }, new Thickness(12, 5, 12, 5));
        System.Windows.Automation.AutomationProperties.SetName(action, (existing is null ? "Add " : "Remove ") + edition.Name);
        action.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(action, Dock.Right);
        head.Children.Add(action);
        var t = new StackPanel();
        t.Children.Add(T(edition.Name, 14, "Text", FontWeights.SemiBold));
        var sub = string.Join(" · ", new[] { edition.EditionName }.Concat(edition.Platforms).Where(x => x.Length > 0));
        if (sub.Length > 0) t.Children.Add(T(sub, 11, "Muted"));
        head.Children.Add(t);
        s.Children.Add(head);

        // One row per region.
        var rows = Region.All.Select(r =>
        {
            var match = _everywhere!.GetValueOrDefault(r.Code) is { } c ? WishLogic.Match(c.Editions, edition.Label, edition.Name) : null;
            var offer = match?.Offer;
            var pkr = offer is null ? null : tracker.Pkr(offer.Sale, offer.Currency);
            return (Region: r, Edition: match, Offer: offer, Pkr: pkr);
        }).ToList();
        var best = rows.Where(x => x.Pkr is not null).OrderBy(x => x.Pkr).FirstOrDefault();
        foreach (var row in rows)
        {
            bool isBest = row.Region == best.Region && row.Pkr is not null;
            var line = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            foreach (var w in new[] { 120.0, -1, 140, 70 })
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = w < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
            line.Children.Add(T(row.Region.Name, 12, isBest ? "Text" : "Muted", isBest ? FontWeights.SemiBold : null));
            var local = new StackPanel { Orientation = Orientation.Horizontal };
            if (row.Offer is { } o)
            {
                local.Children.Add(T(o.IsFree ? "Free" : WishLogic.Money(o.Sale, o.Currency), 12, "Text"));
                if (o.OnSale)
                {
                    var was = T(WishLogic.Money(o.Base, o.Currency), 11, "Muted");
                    was.TextDecorations = TextDecorations.Strikethrough;
                    was.Margin = new Thickness(8, 1, 0, 0);
                    local.Children.Add(was);
                    local.Children.Add(Spaced(T($"-{o.PercentOff}%" + (o.EndsUtc is { } e ? $" until {e.ToLocalTime():d MMM}" : ""), 11, "AccentBrush"), 8));
                }
                if (o.Plus is { } plus) local.Children.Add(Spaced(T($"PS Plus {WishLogic.Money(plus, o.Currency)}", 11, "Muted"), 8));
            }
            else local.Children.Add(T("Not sold here", 12, "Muted"));
            Grid.SetColumn(local, 1);
            line.Children.Add(local);
            var rupees = T(row.Pkr is { } p ? WishLogic.Money(p, "PKR") : "", 13, isBest ? "AccentBrush" : "Text", isBest ? FontWeights.SemiBold : null);
            rupees.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(rupees, 2);
            line.Children.Add(rupees);
            if (row.Edition is { } ed)
            {
                var open = Bare(T("Store ↗", 11, "Muted"), $"Open {row.Region.Name} store page");
                open.HorizontalAlignment = HorizontalAlignment.Right;
                var url = row.Region.ProductUrl(ed.ProductId);
                open.Click += (_, _) => { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (System.ComponentModel.Win32Exception) { } };
                Grid.SetColumn(open, 3);
                line.Children.Add(open);
            }
            s.Children.Add(line);
        }

        if (existing is not null)
        {
            var target = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var field = Field("Alert me at, e.g. 8000", existing.TargetPkr?.ToString("0") ?? "", "Target price");
            field.Width = 200;
            var save = Styled("Set alert", "GhostButton", (_, _) => { }, new Thickness(12, 4, 12, 4));
            save.Margin = new Thickness(8, 0, 0, 0);
            void Apply()
            {
                var text = new string(field.Text.Where(char.IsDigit).ToArray());
                tracker.SetTarget(existing, decimal.TryParse(text, out var v) ? v : null);
                Render();
            }
            save.Click += (_, _) => Apply();
            field.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Apply(); } };
            DockPanel.SetDock(field, Dock.Right);
            DockPanel.SetDock(save, Dock.Right);
            target.Children.Add(save);
            target.Children.Add(field);
            var lowest = existing.Lowest is { } low ? $"Lowest seen: {WishLogic.Money(low.Pkr, "PKR")} in {Region.Find(low.Region)?.Name ?? low.Region}, {low.Date:d MMM yyyy}." : "";
            target.Children.Add(T(lowest + (existing.TargetPkr is { } tp ? $" Alert at {WishLogic.Money(tp, "PKR")}." : " Set a price in rupees to be told when it drops to it."), 11, "Muted", wrap: true));
            s.Children.Add(target);
            if (existing.History.Count > 1) s.Children.Add(HistoryChart(existing));
        }

        var card = Card(s, new Thickness(16, 14, 16, 12));
        card.Margin = new Thickness(0, 0, 0, 10);
        return card;
    }

    private static FrameworkElement Spaced(FrameworkElement e, double left)
    {
        e.Margin = new Thickness(left, 1, 0, 0);
        return e;
    }

    /// <summary>The best price each day, as a small line.</summary>
    private static FrameworkElement HistoryChart(WishItem item)
    {
        var days = item.History.GroupBy(h => h.Date).OrderBy(g => g.Key).Select(g => (Day: g.Key, Pkr: g.Min(h => h.Pkr))).TakeLast(90).ToList();
        const double w = 700, h = 60;
        var canvas = new Canvas { Width = w, Height = h, Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        decimal min = days.Min(d => d.Pkr), max = days.Max(d => d.Pkr);
        var range = Math.Max(1, max - min);
        var line = new System.Windows.Shapes.Polyline { StrokeThickness = 2 };
        line.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "AccentBrush");
        for (int i = 0; i < days.Count; i++)
            line.Points.Add(new Point(days.Count == 1 ? 0 : i * w / (days.Count - 1), h - 6 - (double)((days[i].Pkr - min) / range) * (h - 12)));
        canvas.Children.Add(line);
        var wrap = new StackPanel();
        wrap.Children.Add(canvas);
        wrap.Children.Add(T($"Best price each day since {days[0].Day:d MMM}: from {WishLogic.Money(min, "PKR")} to {WishLogic.Money(max, "PKR")}", 10, "Muted"));
        return wrap;
    }
}
