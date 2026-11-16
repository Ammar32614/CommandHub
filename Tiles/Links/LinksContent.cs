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

    private static List<QuickLink> Flatten(IEnumerable<LinkEntry> entries) => entries.SelectMany(e => e.Links).ToList();

    /// <summary>Moves one entry (a link, or a whole group) to another place in the row.</summary>
    public static List<QuickLink> MoveEntry(IEnumerable<QuickLink> links, int from, int to)
    {
        var entries = Arrange(links);
        if (from < 0 || from >= entries.Count) return Flatten(entries);
        var e = entries[from];
        entries.RemoveAt(from);
        entries.Insert(Math.Clamp(to, 0, entries.Count), e);
        return Flatten(entries);
    }

    /// <summary>Drops a single link (the entry at <paramref name="entry"/>) into a group, at the end.</summary>
    public static List<QuickLink> MoveIntoGroup(IEnumerable<QuickLink> links, int entry, string group)
    {
        var entries = Arrange(links);
        var target = entries.FirstOrDefault(e => e.Group != null && e.Group.Equals(group.Trim(), StringComparison.OrdinalIgnoreCase));
        if (entry < 0 || entry >= entries.Count || entries[entry].Group != null || target is null) return Flatten(entries);
        var link = entries[entry].Links[0];
        entries.RemoveAt(entry);
        target.Links.Add(link with { Group = target.Group! });
        return Flatten(entries);
    }

    /// <summary>Reorders the links inside a group.</summary>
    public static List<QuickLink> MoveWithinGroup(IEnumerable<QuickLink> links, string group, int from, int to)
    {
        var entries = Arrange(links);
        var g = entries.FirstOrDefault(e => e.Group != null && e.Group.Equals(group, StringComparison.OrdinalIgnoreCase));
        if (g is null || from < 0 || from >= g.Links.Count) return Flatten(entries);
        var link = g.Links[from];
        g.Links.RemoveAt(from);
        g.Links.Insert(Math.Clamp(to, 0, g.Links.Count), link);
        return Flatten(entries);
    }

    /// <summary>Takes a link out of its group; it sits right after the group (which goes once it's empty).</summary>
    public static List<QuickLink> TakeOutOfGroup(IEnumerable<QuickLink> links, string group, int index)
    {
        var entries = Arrange(links);
        int at = entries.FindIndex(e => e.Group != null && e.Group.Equals(group, StringComparison.OrdinalIgnoreCase));
        if (at < 0 || index < 0 || index >= entries[at].Links.Count) return Flatten(entries);
        var g = entries[at];
        var link = g.Links[index];
        g.Links.RemoveAt(index);
        entries.Insert(at + 1, new LinkEntry(null, [link with { Group = "" }]));
        if (g.Links.Count == 0) entries.RemoveAt(at);
        return Flatten(entries);
    }

    /// <summary>The group names in use, first spelling wins.</summary>
    public static List<string> Groups(IEnumerable<QuickLink> links) =>
        Arrange(links).Where(e => e.Group != null).Select(e => e.Group!).ToList();
}
