namespace CommandHub.Models;

/// <summary>A sidebar page with its own tile board. Home is just the first page.</summary>
public class Page
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    /// <summary>A Segoe Fluent Icons glyph.</summary>
    public string Icon { get; set; } = "";
    public List<PlacedTile> Tiles { get; set; } = [];
}
