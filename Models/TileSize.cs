namespace CommandHub.Models;

/// <summary>The fixed tile sizes. Spans are in grid cells (columns × rows).</summary>
public enum TileSize { Small, Wide, Tall, Large }

public static class TileSizes
{
    public static (int Cols, int Rows) Span(TileSize s) => s switch
    {
        TileSize.Small => (1, 1),
        TileSize.Wide => (2, 1),
        TileSize.Tall => (1, 2),
        TileSize.Large => (2, 2),
        _ => (1, 1),
    };
}
