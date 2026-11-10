using System.Text.Json.Nodes;

namespace CommandHub.Tiles.Note;

/// <summary>Note tile content: <c>{ "text": string }</c>.</summary>
public static class NoteContent
{
    public static string Read(JsonObject c) => Json.Str(c["text"]);

    public static void Write(JsonObject c, string text) => c["text"] = text;
}
