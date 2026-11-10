using System.Windows;
using System.Windows.Controls;
using CommandHub.Models;

namespace CommandHub.Views;

/// <summary>The rounded card around every tile, plus its edit-mode controls.</summary>
public partial class TileFrame : UserControl
{
    public PlacedTile Tile { get; }

    public TileFrame(PlacedTile tile, string title, bool showTitle, FrameworkElement view)
    {
        InitializeComponent();
        Tile = tile;
        TitleText.Text = title;
        TitleText.Visibility = showTitle ? Visibility.Visible : Visibility.Collapsed;
        Body.Content = view;
        System.Windows.Automation.AutomationProperties.SetName(this, title);
        ResizeBtn.Click += (_, _) => ResizeRequested?.Invoke(this);
        RemoveBtn.Click += (_, _) => RemoveRequested?.Invoke(this);
    }

    public event Action<TileFrame>? ResizeRequested;
    public event Action<TileFrame>? RemoveRequested;

    /// <summary>The element drag gestures start from (the edit layer covers the whole tile).</summary>
    public UIElement DragSurface => EditLayer;

    public bool IsEditing
    {
        get => EditLayer.Visibility == Visibility.Visible;
        set => EditLayer.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }
}
