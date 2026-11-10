using System.Text.Json.Nodes;

namespace CommandHub.Tiles.Links;

public record QuickLink(string Name, string Target);

/// <summary>Quick links tile content: <c>{ "links": [ { "name", "target" } ] }</c>.</summary>
public static class LinksContent
{
    public static List<QuickLink> Read(JsonObject c) =>
        Json.Objects(c["links"]).Select(o => new QuickLink(Json.Str(o["name"]), Json.Str(o["target"]))).ToList();

    public static void Write(JsonObject c, IEnumerable<QuickLink> links) =>
        c["links"] = new JsonArray(links.Select(l => (JsonNode)new JsonObject { ["name"] = l.Name, ["target"] = l.Target }).ToArray());
}
