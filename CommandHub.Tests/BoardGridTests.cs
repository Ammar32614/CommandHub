using CommandHub.Layout;

namespace CommandHub.Tests;

public class BoardGridTests
{
    static PlacedTile T(int c, int r, TileSize s) =>
        new() { Id = Guid.NewGuid(), TypeId = "note", Column = c, Row = r, Size = s };

    [Fact]
    public void Fits_RejectsOverlap() =>
        Assert.False(BoardGrid.Fits([T(0, 0, TileSize.Large)], 1, 1, TileSize.Small));

    [Fact]
    public void Fits_RejectsPastLastColumn() =>
        Assert.False(BoardGrid.Fits([], 5, 0, TileSize.Wide));

    [Fact]
    public void Fits_RejectsNegative() =>
        Assert.False(BoardGrid.Fits([], -1, 0, TileSize.Small));

    [Fact]
    public void Fits_AcceptsAdjacent() =>
        Assert.True(BoardGrid.Fits([T(0, 0, TileSize.Large)], 2, 0, TileSize.Large));

    [Fact]
    public void Fits_IgnoresSelf()
    {
        var a = T(0, 0, TileSize.Large);
        Assert.True(BoardGrid.Fits([a], 0, 0, TileSize.Large, a.Id));
    }

    [Fact]
    public void FindFreeSpot_FirstGapRowMajor() =>
        Assert.Equal((2, 0), BoardGrid.FindFreeSpot([T(0, 0, TileSize.Large)], TileSize.Wide));

    [Fact]
    public void FindFreeSpot_FullRowGoesBelow() =>
        Assert.Equal((0, 1), BoardGrid.FindFreeSpot(
            [T(0, 0, TileSize.Wide), T(2, 0, TileSize.Wide), T(4, 0, TileSize.Wide)], TileSize.Small));

    [Fact]
    public void TryMove_OntoOtherTile_SnapsBack()
    {
        var a = T(0, 0, TileSize.Small);
        var b = T(1, 0, TileSize.Small);
        Assert.False(BoardGrid.TryMove([a, b], a.Id, 1, 0));
        Assert.Equal((0, 0), (a.Column, a.Row));
    }

    [Fact]
    public void TryMove_ToFreeCell_Moves()
    {
        var a = T(0, 0, TileSize.Small);
        Assert.True(BoardGrid.TryMove([a], a.Id, 3, 2));
        Assert.Equal((3, 2), (a.Column, a.Row));
    }

    [Fact]
    public void TryResize_BlockedKeepsSize()
    {
        var a = T(0, 0, TileSize.Small);
        var b = T(1, 0, TileSize.Small);
        Assert.False(BoardGrid.TryResize([a, b], a.Id, TileSize.Wide));
        Assert.Equal(TileSize.Small, a.Size);
    }

    [Fact]
    public void TryResize_FreeSpaceGrows()
    {
        var a = T(0, 0, TileSize.Small);
        Assert.True(BoardGrid.TryResize([a], a.Id, TileSize.Large));
        Assert.Equal(TileSize.Large, a.Size);
    }

    [Fact]
    public void Normalize_RepairsOverlapAndOutOfBounds()
    {
        var a = T(0, 0, TileSize.Large);
        var b = T(1, 1, TileSize.Small);
        var c = T(9, -3, TileSize.Wide);
        var list = new List<PlacedTile> { a, b, c };
        BoardGrid.Normalize(list);
        Assert.Equal((0, 0), (a.Column, a.Row));
        foreach (var t in list) Assert.True(BoardGrid.Fits(list, t.Column, t.Row, t.Size, t.Id));
    }

    [Fact]
    public void Snap_RoundsToNearestCell() =>
        Assert.Equal((1, 1), BoardGrid.Snap(160 + 12 - 40, 140 + 12 + 30));

    [Fact]
    public void Snap_ClampsToBoard() =>
        Assert.Equal((5, 0), BoardGrid.Snap(5000, -200));

    [Fact]
    public void CellRect_LargeSpansTwoCellsAndOneGap()
    {
        var r = BoardGrid.CellRect(1, 1, TileSize.Large);
        Assert.Equal(172, r.X);
        Assert.Equal(152, r.Y);
        Assert.Equal(332, r.Width);
        Assert.Equal(292, r.Height);
    }
}
