using System.Windows;
using CommandHub.Models;

namespace CommandHub.Tiles.Todo;

public sealed class TodoTile : ITileType
{
    public string TypeId => "todo";
    public string DisplayName => "To-do list";
    public string Icon => "";
    public IReadOnlyList<TileSize> AllowedSizes { get; } = [TileSize.Small, TileSize.Wide, TileSize.Tall, TileSize.Large];
    public TileSize DefaultSize => TileSize.Tall;
    public string AccentKey => "Accent2Brush";
    public FrameworkElement CreateView(TileContext ctx) => new TodoTileView(ctx);
}
