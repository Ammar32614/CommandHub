using System.Windows;
using CommandHub.Models;

namespace CommandHub.Layout;

/// <summary>
/// Pure placement rules for a tile board: a fixed number of columns, unbounded rows,
/// tiles snap to cells and never overlap. A move or resize that would overlap is refused.
/// </summary>
public static class BoardGrid
{
    public const int Columns = 6;
    public const double CellWidth = 160;
    public const double CellHeight = 140;
    public const double Gap = 12;

    public static bool Fits(IReadOnlyList<PlacedTile> tiles, int col, int row, TileSize size, Guid? ignoreId = null)
    {
        var (cols, rows) = TileSizes.Span(size);
        if (col < 0 || row < 0 || col + cols > Columns) return false;

        foreach (var t in tiles)
        {
            if (ignoreId is { } id && t.Id == id) continue;
            var (tc, tr) = TileSizes.Span(t.Size);
            bool overlaps = col < t.Column + tc && t.Column < col + cols
                         && row < t.Row + tr && t.Row < row + rows;
            if (overlaps) return false;
        }
        return true;
    }

    public static (int Col, int Row) FindFreeSpot(IReadOnlyList<PlacedTile> tiles, TileSize size)
    {
        for (int row = 0; ; row++)
            for (int col = 0; col < Columns; col++)
                if (Fits(tiles, col, row, size)) return (col, row);
    }

    public static bool TryMove(IReadOnlyList<PlacedTile> tiles, Guid id, int col, int row)
    {
        var tile = tiles.FirstOrDefault(t => t.Id == id);
        if (tile is null || !Fits(tiles, col, row, tile.Size, id)) return false;
        tile.Column = col;
        tile.Row = row;
        return true;
    }

    public static bool TryResize(IReadOnlyList<PlacedTile> tiles, Guid id, TileSize size)
    {
        var tile = tiles.FirstOrDefault(t => t.Id == id);
        if (tile is null || !Fits(tiles, tile.Column, tile.Row, size, id)) return false;
        tile.Size = size;
        return true;
    }

    /// <summary>Keeps tiles in list order; any tile that clashes with an earlier one (or is off the board) moves to a free spot.</summary>
    public static void Normalize(List<PlacedTile> tiles)
    {
        var placed = new List<PlacedTile>(tiles.Count);
        foreach (var t in tiles)
        {
            if (!Fits(placed, t.Column, t.Row, t.Size))
                (t.Column, t.Row) = FindFreeSpot(placed, t.Size);
            placed.Add(t);
        }
    }

    /// <summary>Pixel position (top-left of a dragged tile, board-relative) to the nearest cell, clamped to the board.</summary>
    public static (int Col, int Row) Snap(double x, double y)
    {
        int col = (int)Math.Round(x / (CellWidth + Gap));
        int row = (int)Math.Round(y / (CellHeight + Gap));
        return (Math.Clamp(col, 0, Columns - 1), Math.Max(row, 0));
    }

    public static Rect CellRect(int col, int row, TileSize size)
    {
        var (cols, rows) = TileSizes.Span(size);
        return new Rect(
            col * (CellWidth + Gap),
            row * (CellHeight + Gap),
            cols * CellWidth + (cols - 1) * Gap,
            rows * CellHeight + (rows - 1) * Gap);
    }
}
