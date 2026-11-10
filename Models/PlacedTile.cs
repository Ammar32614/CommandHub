using System.Text.Json.Nodes;

namespace CommandHub.Models;

/// <summary>One tile on a page's board. <see cref="Content"/> is owned and read only by the tile's type.</summary>
public class PlacedTile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TypeId { get; set; } = "";
    public int Column { get; set; }
    public int Row { get; set; }
    public TileSize Size { get; set; } = TileSize.Small;
    public JsonObject Content { get; set; } = new();
}
