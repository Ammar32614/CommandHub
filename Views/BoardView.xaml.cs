using System.Windows;
using System.Windows.Controls;
using CommandHub.Layout;
using CommandHub.Models;
using CommandHub.Tiles;
using Page = CommandHub.Models.Page;

namespace CommandHub.Views;

/// <summary>One page's tile board. Tiles sit on a fixed grid; the board scrolls rather than reflowing.</summary>
public partial class BoardView : UserControl
{
    private readonly Page _page;

    public BoardView(Page page)
    {
        InitializeComponent();
        _page = page;
        PageTitleText.Text = page.Name;
        Render();
    }

    private void Render()
    {
        Board.Children.Clear();
        foreach (var tile in _page.Tiles)
            Board.Children.Add(CreateFrame(tile));
        SizeBoard();
    }

    private TileFrame CreateFrame(PlacedTile tile)
    {
        var type = TileRegistry.Find(tile.TypeId);
        var ctx = new TileContext(tile, App.SaveNow, App.SaveSoon);
        var view = type?.CreateView(ctx) ?? new UnknownTileView();
        var frame = new TileFrame(tile, type?.DisplayName ?? "Unknown tile", showTitle: type?.TypeId != "clock", view);
        Place(frame);
        return frame;
    }

    private static void Place(TileFrame frame)
    {
        var r = BoardGrid.CellRect(frame.Tile.Column, frame.Tile.Row, frame.Tile.Size);
        Canvas.SetLeft(frame, r.X);
        Canvas.SetTop(frame, r.Y);
        frame.Width = r.Width;
        frame.Height = r.Height;
    }

    private void SizeBoard()
    {
        int rows = _page.Tiles.Count == 0 ? 1 : _page.Tiles.Max(t => t.Row + TileSizes.Span(t.Size).Rows);
        var full = BoardGrid.CellRect(0, 0, TileSize.Small);
        Board.Width = BoardGrid.Columns * (full.Width + BoardGrid.Gap) - BoardGrid.Gap;
        Board.Height = rows * (full.Height + BoardGrid.Gap) - BoardGrid.Gap;
    }

    private void Edit_Click(object sender, RoutedEventArgs e) { }

    private void Undo_Click(object sender, RoutedEventArgs e) { }
}
