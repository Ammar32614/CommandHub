using System.Windows;
using System.Windows.Controls;
using CommandHub.Models;

namespace CommandHub.Tiles.Clock;

public sealed class ClockTile : ITileType
{
    public string TypeId => "clock";
    public string DisplayName => "Clock and date";
    public string Icon => "";
    public IReadOnlyList<TileSize> AllowedSizes { get; } = [TileSize.Small, TileSize.Wide, TileSize.Large];
    public TileSize DefaultSize => TileSize.Large;
    public FrameworkElement CreateView(TileContext ctx) => new TextBlock { Text = "Clock" };
}
