using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CommandHub.Models;

namespace CommandHub.Tiles.Weather;

public sealed class WeatherTile : ITileType
{
    public string TypeId => "weather";
    public string DisplayName => "Weather";
    public string Icon => "";
    public IReadOnlyList<TileSize> AllowedSizes { get; } = [TileSize.Small, TileSize.Wide, TileSize.Large];
    public TileSize DefaultSize => TileSize.Wide;
    public string AccentKey => "Accent2Brush";
    public FrameworkElement CreateView(TileContext ctx) => new WeatherView(ctx);
}

/// <summary>
/// Content: <c>{ "place", "lat", "lon", "report": cached forecast JSON, "updated": ISO time }</c>.
/// Nothing goes online until the user types a city. Refreshes every 30 minutes; shows the cached report offline.
/// </summary>
public sealed class WeatherView : UserControl
{
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(30);
    private readonly TileContext _ctx;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(5) };
    private bool _busy;
    private string _status = "";

    public WeatherView(TileContext ctx)
    {
        _ctx = ctx;
        _timer.Tick += async (_, _) => await RefreshIfStale();
        Loaded += async (_, _) => { _timer.Start(); await RefreshIfStale(); };
        Unloaded += (_, _) => _timer.Stop();
        Render();
    }

    private JsonObject C => _ctx.Content;
    private bool HasPlace => C["lat"] is JsonValue && C["lon"] is JsonValue;

    private DateTime? Updated =>
        DateTime.TryParse(Json.Str(C["updated"]), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t) ? t : null;

    private async Task RefreshIfStale()
    {
        if (!HasPlace || _busy) return;
        if (Updated is { } t && DateTime.UtcNow - t.ToUniversalTime() < RefreshEvery) return;
        await Refresh();
    }

    private async Task Refresh()
    {
        if (!HasPlace) return;
        _busy = true;
        try
        {
            double lat = (double)C["lat"]!, lon = (double)C["lon"]!;
            var result = await WeatherApi.FetchAsync(lat, lon);
            // The city may have changed while this was loading: never store one place's forecast under another.
            bool samePlace = C["lat"] is JsonValue la && la.TryGetValue<double>(out var nowLat) && nowLat == lat
                          && C["lon"] is JsonValue lo && lo.TryGetValue<double>(out var nowLon) && nowLon == lon;
            if (!samePlace) return;
            if (result is { } r)
            {
                C["report"] = r.Json;
                C["updated"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                _status = "";
                _ctx.SaveNow();
            }
            else
            {
                _status = "Offline";
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException) { _status = "Offline"; }
        finally { _busy = false; }
        Render();
    }

    private void Render()
    {
        var report = WeatherApi.ParseForecast(Json.Str(C["report"]));
        if (!HasPlace) { ShowCityPrompt(""); return; }

        var root = new Grid();
        var edit = TileUi.EditButton("Change city");
        edit.Click += (_, _) => ShowCityPrompt(Json.Str(C["place"]));

        if (report is null)
        {
            var wait = TileUi.Text(_status == "Offline" ? "Can't reach the weather service right now." : "Fetching the weather…", 12, "Muted");
            wait.TextWrapping = TextWrapping.Wrap;
            root.Children.Add(wait);
            root.Children.Add(edit);
            Content = root;
            return;
        }

        var (sky, text) = WeatherApi.Describe(report.Code);
        var main = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        main.Children.Add(TileUi.Icon(Glyph(sky, report.IsDay), 34, Brush(sky)));
        var temps = new StackPanel { Margin = new Thickness(12, -4, 0, 0) };
        temps.Children.Add(TileUi.Display($"{Math.Round(report.Temperature)}°", 34, weight: FontWeights.Normal));
        main.Children.Add(temps);

        var details = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        var place = Json.Str(C["place"]);
        var city = place.Split(',')[0];
        var condition = TileUi.Text(city.Length > 0 ? $"{text} in {city}" : text, 13, "Text");
        condition.ToolTip = place;
        details.Children.Add(condition);
        var extra = _status.Length > 0 && Updated is { } u
            ? $"{_status} · updated {u.ToLocalTime():t}"
            : $"Feels {Math.Round(report.FeelsLike)}° · wind {Math.Round(report.Wind)} km/h";
        details.Children.Add(TileUi.Text(extra, 11, "Muted"));

        var left = new StackPanel();
        left.Children.Add(main);
        left.Children.Add(details);

        var layout = new DockPanel();
        var days = Forecast(report);
        DockPanel.SetDock(days, Dock.Right);
        layout.Children.Add(days);
        layout.Children.Add(left);
        // Hide the forecast when the tile is too narrow (Small).
        layout.SizeChanged += (_, _) => days.Visibility = layout.ActualWidth > 260 ? Visibility.Visible : Visibility.Collapsed;

        root.Children.Add(layout);
        root.Children.Add(edit);
        Content = root;
    }

    private static StackPanel Forecast(WeatherReport report)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var d in report.Days.Skip(1).Take(3))
        {
            var (sky, text) = WeatherApi.Describe(d.Code);
            var col = new StackPanel { Width = 52, Margin = new Thickness(4, 0, 0, 0) };
            var name = TileUi.Text(d.Date.DayOfWeek.ToString()[..3], 11, "Muted");
            name.HorizontalAlignment = HorizontalAlignment.Center;
            var icon = TileUi.Icon(Glyph(sky, true), 18, Brush(sky));
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.Margin = new Thickness(0, 6, 0, 6);
            icon.ToolTip = text;
            var hi = TileUi.Text($"{Math.Round(d.Max)}°", 12, "Text");
            hi.HorizontalAlignment = HorizontalAlignment.Center;
            var lo = TileUi.Text($"{Math.Round(d.Min)}°", 11, "Muted");
            lo.HorizontalAlignment = HorizontalAlignment.Center;
            col.Children.Add(name);
            col.Children.Add(icon);
            col.Children.Add(hi);
            col.Children.Add(lo);
            row.Children.Add(col);
        }
        return row;
    }

    private static string Glyph(Sky sky, bool day) => sky switch
    {
        Sky.Clear => day ? "" : "",
        _ => "",
    };

    private static string Brush(Sky sky) => sky switch
    {
        Sky.Clear => "AccentBrush",
        Sky.Rain => "Accent2Brush",
        Sky.Storm => "Accent3Brush",
        Sky.Snow => "Text",
        _ => "Muted",
    };

    // ---------------- City prompt ----------------

    private void ShowCityPrompt(string current)
    {
        var stack = new StackPanel();
        var hint = TileUi.Text("Which city should I show the weather for?", 12, "Muted");
        hint.TextWrapping = TextWrapping.Wrap;
        var box = TileUi.Field("City, like Lahore", current, "City");
        var go = TileUi.Ghost("Show weather", (_, _) => { });
        go.Margin = new Thickness(6, 0, 0, 0);
        var line = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(go, Dock.Right);
        line.Children.Add(go);
        line.Children.Add(box);
        var error = TileUi.Text("", 12, "Danger");
        error.TextWrapping = TextWrapping.Wrap;
        error.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(hint);
        stack.Children.Add(line);
        stack.Children.Add(error);
        box.ToolTip = "Sends the city you type, then that place's location every 30 minutes, to Open-Meteo (free, no account).";

        async Task Submit()
        {
            if (box.Text.Trim().Length == 0) { error.Text = "Type a city name first."; return; }
            box.IsEnabled = go.IsEnabled = false;
            error.Text = "";
            var place = await WeatherApi.FindPlaceAsync(box.Text);
            box.IsEnabled = go.IsEnabled = true;
            if (place is null) { error.Text = "Couldn't find that city, or you're offline. Try again."; box.Focus(); return; }
            C["place"] = place.Label;
            C["lat"] = place.Latitude;
            C["lon"] = place.Longitude;
            C.Remove("report");
            C.Remove("updated");
            _ctx.SaveNow();
            Render();
            await Refresh();
        }

        go.Click += async (_, _) => await Submit();
        box.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; await Submit(); }
            else if (e.Key == Key.Escape && HasPlace) { e.Handled = true; Render(); }
        };
        Content = stack;
        Dispatcher.BeginInvoke(() => { if (IsLoaded && current.Length > 0) box.Focus(); });
    }
}
