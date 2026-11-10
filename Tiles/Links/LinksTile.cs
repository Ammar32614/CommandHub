using System.Windows;
using CommandHub.Models;

namespace CommandHub.Tiles.Links;

public sealed class LinksTile : ITileType
{
    public string TypeId => "links";
    public string DisplayName => "Quick links";
    public string Icon => "";
    public IReadOnlyList<TileSize> AllowedSizes { get; } = [TileSize.Small, TileSize.Wide, TileSize.Tall, TileSize.Large];
    public TileSize DefaultSize => TileSize.Wide;
    public string AccentKey => "Accent3Brush";
    public FrameworkElement CreateView(TileContext ctx) => new LinksTileView(ctx);
}
