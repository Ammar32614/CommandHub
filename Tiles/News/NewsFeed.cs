using System.Globalization;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace CommandHub.Tiles.News;

public record NewsItem(string Title, string Link, string Source, DateTimeOffset? Published);

public record ParsedFeed(string Title, List<NewsItem> Items);

/// <summary>A news source offered in the tile's settings.</summary>
public record NewsSource(string Name, string Url, string Topic);

/// <summary>Reads RSS 2.0 and Atom feeds. Never throws: anything unreadable comes back as null.</summary>
public static partial class NewsFeed
{
    public static readonly NewsSource[] Suggested =
    [
        new("BBC News", "https://feeds.bbci.co.uk/news/rss.xml", "World"),
        new("Dawn", "https://www.dawn.com/feeds/home", "Pakistan"),
        new("Express Tribune", "https://tribune.com.pk/feed/home", "Pakistan"),
        new("Geo News", "https://www.geo.tv/rss/1/1", "Pakistan"),
        new("ESPNcricinfo", "https://www.espncricinfo.com/rss/content/story/feeds/0.xml", "Cricket"),
        new("The Verge", "https://www.theverge.com/rss/index.xml", "Tech"),
        new("Ars Technica", "https://feeds.arstechnica.com/arstechnica/index", "Tech"),
        new("Hacker News", "https://hnrss.org/frontpage", "Tech"),
        new("PlayStation Blog", "https://blog.playstation.com/feed/", "Games"),
    ];

    public static ParsedFeed? Parse(string xml, string fallbackTitle)
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;
        XDocument doc;
        try
        {
            // No DTDs: a feed can't make us fetch or expand anything.
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
            doc = XDocument.Load(reader);
        }
        catch (XmlException) { return null; }
        var root = doc.Root;
        if (root is null) return null;

        if (root.Name.LocalName == "rss" || root.Name.LocalName == "RDF")
        {
            var channel = root.Elements().FirstOrDefault(e => e.Name.LocalName == "channel");
            var title = Clean(Child(channel, "title")) is { Length: > 0 } t ? t : fallbackTitle;
            var items = root.Descendants().Where(e => e.Name.LocalName == "item")
                .Select(i => Item(Clean(Child(i, "title")), Child(i, "link").Trim(), title, ParseDate(Child(i, "pubDate") is { Length: > 0 } p ? p : Child(i, "date"))))
                .OfType<NewsItem>().ToList();
            return new ParsedFeed(title, items);
        }
        if (root.Name.LocalName == "feed")
        {
            var title = Clean(Child(root, "title")) is { Length: > 0 } t ? t : fallbackTitle;
            var items = root.Elements().Where(e => e.Name.LocalName == "entry").Select(e =>
            {
                var links = e.Elements().Where(l => l.Name.LocalName == "link").ToList();
                var link = links.FirstOrDefault(l => (string?)l.Attribute("rel") is null or "alternate") ?? links.FirstOrDefault();
                var date = Child(e, "published") is { Length: > 0 } p ? p : Child(e, "updated");
                return Item(Clean(Child(e, "title")), ((string?)link?.Attribute("href") ?? "").Trim(), title, ParseDate(date));
            }).OfType<NewsItem>().ToList();
            return new ParsedFeed(title, items);
        }
        return null;
    }

    private static NewsItem? Item(string title, string link, string source, DateTimeOffset? published) =>
        title.Length == 0 || !Uri.TryCreate(link, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            ? null
            : new NewsItem(title, uri.ToString(), source, published);

    private static string Child(XElement? parent, string name) =>
        parent?.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value ?? "";

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <summary>Plain text: no HTML tags or entities, single spaces.</summary>
    private static string Clean(string text) => Spaces().Replace(WebUtility.HtmlDecode(Tags().Replace(text ?? "", "")), " ").Trim();

    private static readonly Dictionary<string, string> Zones = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GMT"] = "+00:00", ["UT"] = "+00:00", ["UTC"] = "+00:00", ["Z"] = "+00:00",
        ["EST"] = "-05:00", ["EDT"] = "-04:00", ["CST"] = "-06:00", ["CDT"] = "-05:00",
        ["MST"] = "-07:00", ["MDT"] = "-06:00", ["PST"] = "-08:00", ["PDT"] = "-07:00",
    };

    [GeneratedRegex(@"^(?:[A-Za-z]{3},\s*)?(\d{1,2}\s+[A-Za-z]{3}\s+\d{2,4}\s+\d{1,2}:\d{2}(?::\d{2})?)\s*([+-]\d{2}:?\d{2}|[A-Za-z]{1,4})?$")]
    private static partial Regex Rfc822();

    /// <summary>RFC 822 dates as feeds write them ("Wed, 11 Nov 26 05:20:02 +0500", "… GMT") or ISO 8601.</summary>
    public static DateTimeOffset? ParseDate(string text)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0) return null;
        var m = Rfc822().Match(t);
        if (m.Success)
        {
            var zone = m.Groups[2].Value;
            string offset = zone.Length == 0 ? "+00:00"
                : Zones.TryGetValue(zone, out var z) ? z
                : zone[0] is '+' or '-' ? (zone.Contains(':') ? zone : zone[..3] + ":" + zone[3..])
                : "+00:00";
            string[] formats = ["d MMM yyyy HH:mm:ss", "d MMM yyyy HH:mm", "d MMM yy HH:mm:ss", "d MMM yy HH:mm"];
            if (DateTime.TryParseExact(m.Groups[1].Value, formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowInnerWhite, out var local)
                && TimeSpan.TryParse(offset.TrimStart('+'), CultureInfo.InvariantCulture, out var span))
                return new DateTimeOffset(local, span);
        }
        return DateTimeOffset.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var iso) ? iso : null;
    }

    /// <summary>All feeds' items newest first (undated last), each link once, at most <paramref name="max"/>.</summary>
    public static List<NewsItem> Merge(IEnumerable<IEnumerable<NewsItem>> feeds, int max) =>
        feeds.SelectMany(f => f)
            .DistinctBy(i => i.Link.TrimEnd('/'), StringComparer.OrdinalIgnoreCase) // the same story twice: the first source listed keeps it
            .OrderByDescending(i => i.Published ?? DateTimeOffset.MinValue)
            .Take(max).ToList();

    /// <summary>"just now", "5 min", "3 h", "2 d".</summary>
    public static string Ago(DateTimeOffset when, DateTimeOffset now)
    {
        var d = now - when;
        if (d.TotalMinutes < 1) return "just now";
        if (d.TotalHours < 1) return $"{(int)d.TotalMinutes} min";
        if (d.TotalDays < 1) return $"{(int)d.TotalHours} h";
        return $"{(int)d.TotalDays} d";
    }
}
