using System.Text.Json.Nodes;

namespace CommandHub.Tiles.Todo;

public record TodoItem(string Text, bool Done);

/// <summary>To-do tile content: <c>{ "items": [ { "text", "done" } ] }</c>.</summary>
public static class TodoContent
{
    public static List<TodoItem> Read(JsonObject c) =>
        Json.Objects(c["items"])
            .Select(o => new TodoItem(Json.Str(o["text"]), Json.Bool(o["done"])))
            .Where(i => i.Text.Length > 0)
            .ToList();

    public static void Write(JsonObject c, IEnumerable<TodoItem> items) =>
        c["items"] = new JsonArray(items.Select(i => (JsonNode)new JsonObject { ["text"] = i.Text, ["done"] = i.Done }).ToArray());

    /// <summary>Adds a trimmed, not-done item. Blank text is rejected.</summary>
    public static bool TryAdd(List<TodoItem> items, string text)
    {
        var t = text.Trim();
        if (t.Length == 0) return false;
        items.Add(new TodoItem(t, false));
        return true;
    }
}
