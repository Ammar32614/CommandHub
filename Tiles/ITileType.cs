using System.Text.Json.Nodes;
using System.Windows;
using CommandHub.Models;

namespace CommandHub.Tiles;

/// <summary>
/// One kind of tile. Each type is self-contained: it owns its view and the shape of its
/// <see cref="PlacedTile.Content"/>. Register new types in <see cref="TileRegistry.All"/>.
/// </summary>
public interface ITileType
{
    string TypeId { get; }
    string DisplayName { get; }
    /// <summary>A Segoe Fluent Icons glyph.</summary>
    string Icon { get; }
    IReadOnlyList<TileSize> AllowedSizes { get; }
    TileSize DefaultSize { get; }
    /// <summary>Which theme accent colours this tile: AccentBrush, Accent2Brush, Accent3Brush or Accent4Brush.</summary>
    string AccentKey => "AccentBrush";
    FrameworkElement CreateView(TileContext ctx);
}

/// <summary>What a tile view gets: its tile, and the two ways to ask for a save.</summary>
public sealed class TileContext(PlacedTile tile, Action saveNow, Action saveSoon)
{
    public PlacedTile Tile { get; } = tile;
    public JsonObject Content => Tile.Content;
    /// <summary>Save immediately (discrete changes like ticking a to-do).</summary>
    public void SaveNow() => saveNow();
    /// <summary>Save after a short pause (typing).</summary>
    public void SaveSoon() => saveSoon();
}

/// <summary>Small helpers for reading tile content without ever throwing on bad JSON.</summary>
internal static class Json
{
    public static string Str(JsonNode? n) =>
        n is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

    public static bool Bool(JsonNode? n) =>
        n is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    public static IEnumerable<JsonObject> Objects(JsonNode? n) =>
        n is JsonArray a ? a.OfType<JsonObject>() : [];
}
