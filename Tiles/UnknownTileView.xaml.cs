using System.Windows.Controls;

namespace CommandHub.Tiles;

/// <summary>Shown for a tile type this version doesn't know. The tile and its content are kept untouched.</summary>
public partial class UnknownTileView : UserControl
{
    public UnknownTileView() => InitializeComponent();
}
