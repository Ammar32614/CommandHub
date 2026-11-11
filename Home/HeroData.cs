using System.IO;
using CommandHub.Layout;
using CommandHub.Models;
using CommandHub.Tiles;
using CommandHub.Tiles.Links;
using CommandHub.Tiles.Todo;
using CommandHub.Tiles.Weather;

namespace CommandHub.Home;

public record HeroWeather(string City, int Temperature, string Description, Sky Sky, bool IsDay);

/// <summary>The words in the Home header.</summary>
public static class HeroText
{
    public static string Eyebrow(int hour)
    {
        var part = hour switch
        {
            >= 5 and < 12 => "MORNING",
            >= 12 and < 17 => "AFTERNOON",
            >= 17 and < 22 => "EVENING",
            _ => "NIGHT",
        };
        return $"YOUR {part}, AT YOUR PACE";
    }

    public static string Breadcrumb(string page, DateTime now) =>
        $"{page.ToUpperInvariant()}   /   {now.ToString("dddd, MMMM d").ToUpperInvariant()}";

    public static string SubLine(string time, DateTime now, HeroWeather? weather) =>
        weather is null
            ? $"{time}   ·   {now:dddd, MMMM d}"
            : $"{time}   ·   {weather.City}   ·   {weather.Temperature}° and {weather.Description.ToLowerInvariant()}";
}

/// <summary>
/// The Home header borrows from the page's own tiles (the first weather, links and to-do tile),
/// so there is one source of truth and nothing new to set up.
/// </summary>
public static class HeroData
{
    private static PlacedTile? First(Page page, string type) => page.Tiles.FirstOrDefault(t => t.TypeId == type);

    public static HeroWeather? Weather(Page page)
    {
        foreach (var tile in page.Tiles.Where(t => t.TypeId == "weather"))
        {
            var report = WeatherApi.ParseForecast(Json.Str(tile.Content["report"]));
            if (report is null) continue;
            var (sky, text) = WeatherApi.Describe(report.Code);
            var city = Json.Str(tile.Content["place"]).Split(',')[0].Trim();
            return new HeroWeather(city, (int)Math.Round(report.Temperature), text, sky, report.IsDay);
        }
        return null;
    }

    public static List<QuickLink> Links(Page page, int max) =>
        First(page, "links") is { } t
            ? LinksContent.Read(t.Content).Where(l => l.Target.Trim().Length > 0).Take(max).ToList()
            : [];

    /// <summary>Every link on the page's first quick links tile, with groups gathered together.</summary>
    public static List<LinkEntry> LinkEntries(Page page) =>
        First(page, "links") is { } t
            ? LinksContent.Arrange(LinksContent.Read(t.Content).Where(l => l.Target.Trim().Length > 0))
            : [];

    /// <summary>Unfinished to-dos from the first to-do tile, with their index in that tile's list.</summary>
    public static List<(int Index, string Text)> OpenTodos(Page page) =>
        First(page, "todo") is { } t
            ? TodoContent.Read(t.Content).Select((item, i) => (item, i)).Where(x => !x.item.Done).Select(x => (x.i, x.item.Text)).ToList()
            : [];

    public static bool TickTodo(Page page, int index)
    {
        if (First(page, "todo") is not { } t) return false;
        var items = TodoContent.Read(t.Content);
        if (index < 0 || index >= items.Count) return false;
        items[index] = items[index] with { Done = true };
        TodoContent.Write(t.Content, items);
        return true;
    }

    /// <summary>Adds a link to the first quick links tile, creating that tile in a free spot if the page has none.</summary>
    public static bool PinLink(Page page, string name, string target, string group = "")
    {
        target = (target ?? "").Trim();
        if (target.Length == 0) return false;
        name = (name ?? "").Trim();
        if (name.Length == 0) name = NameFor(target);

        var tile = First(page, "links");
        if (tile is null)
        {
            var (col, row) = BoardGrid.FindFreeSpot(page.Tiles, TileSize.Wide);
            tile = new PlacedTile { TypeId = "links", Column = col, Row = row, Size = TileSize.Wide };
            page.Tiles.Add(tile);
        }
        var links = LinksContent.Read(tile.Content);
        links.Add(new QuickLink(name, target, (group ?? "").Trim()));
        LinksContent.Write(tile.Content, links);
        return true;
    }

    /// <summary>"https://www.example.com/x" → "example.com"; "C:\Apps\Stint.exe" → "Stint.exe".</summary>
    private static string NameFor(string target)
    {
        var t = LinkTarget.Normalize(target);
        if (Uri.TryCreate(t, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            return uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        var file = Path.GetFileName(t.TrimEnd('\\', '/'));
        return file.Length > 0 ? file : t;
    }
}
