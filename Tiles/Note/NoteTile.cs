using System.Windows;
using System.Windows.Controls;
using CommandHub.Models;

namespace CommandHub.Tiles.Note;

public sealed class NoteTile : ITileType
{
    public string TypeId => "note";
    public string DisplayName => "Quick note";
    public string Icon => "";
    public IReadOnlyList<TileSize> AllowedSizes { get; } = [TileSize.Small, TileSize.Wide, TileSize.Tall, TileSize.Large];
    public TileSize DefaultSize => TileSize.Wide;
    public FrameworkElement CreateView(TileContext ctx) => new TextBlock { Text = "Note" };
}
