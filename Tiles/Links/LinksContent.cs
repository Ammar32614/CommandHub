using System.Text.Json.Nodes;

namespace CommandHub.Tiles.Links;

/// <summary>A link. <paramref name="Group"/> is "" for one that isn't in a group.</summary>
public record QuickLink(string Name, string Target, string Group = "");

/// <summary>One thing in a row of links: a single link (Group is null) or a named group of them.</summary>
public record LinkEntry(string? Group, List<QuickLink> Links);

/// <summary>Quick links tile content: <c>{ "links": [ { "name", "target", "group"? } ] }</c>.</summary>
public static class LinksContent
{
    public static List<QuickLink> Read(JsonObject c) =>
        Json.Objects(c["links"]).Select(o => new QuickLink(Json.Str(o["name"]), Json.Str(o["target"]), Json.Str(o["group"]).Trim())).ToList();

    public static void Write(JsonObject c, IEnumerable<QuickLink> links) =>
        c["links"] = new JsonArray(links.Select(l =>
        {
            var o = new JsonObject { ["name"] = l.Name, ["target"] = l.Target };
            if (l.Group.Trim().Length > 0) o["group"] = l.Group.Trim();
            return (JsonNode)o;
        }).ToArray());

    /// <summary>Links in order, with each group gathered at the place of its first link. Group names ignore case.</summary>
    public static List<LinkEntry> Arrange(IEnumerable<QuickLink> links)
    {
        var entries = new List<LinkEntry>();
        var groups = new Dictionary<string, LinkEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var link in links)
        {
            var g = link.Group.Trim();
            if (g.Length == 0) { entries.Add(new LinkEntry(null, [link])); continue; }
            if (!groups.TryGetValue(g, out var entry))
            {
                entry = new LinkEntry(g, []);
                groups[g] = entry;
                entries.Add(entry);
            }
            entry.Links.Add(link);
        }
        return entries;
    }

    /// <summary>The group names in use, first spelling wins.</summary>
    public static List<string> Groups(IEnumerable<QuickLink> links) =>
        Arrange(links).Where(e => e.Group != null).Select(e => e.Group!).ToList();
}
