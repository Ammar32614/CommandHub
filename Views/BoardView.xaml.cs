using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CommandHub.Layout;
using CommandHub.Models;
using CommandHub.Tiles;
using Page = CommandHub.Models.Page;

namespace CommandHub.Views;

/// <summary>
/// One page's tile board. Tiles sit on a fixed grid; the board scrolls rather than reflowing.
/// In edit mode tiles can be dragged, resized, added and removed (with Undo).
/// </summary>
public partial class BoardView : UserControl
{
    private readonly Page _page;
    private bool _editing;

    /// <summary>Tiles that are their own headline and look better without a title row.</summary>
    private static readonly HashSet<string> NoTitle = ["clock", "greeting"];

    /// <param name="embedded">
    /// True when the board sits inside a page that scrolls (Home): no scroll area of its own,
    /// and a small "YOUR TILES" label instead of the page title.
    /// </param>
    public BoardView(Page page, bool embedded = false)
    {
        InitializeComponent();
        _page = page;
        PageTitleText.Text = page.Name;
        if (embedded)
        {
            // Sideways only; the page around it scrolls vertically, so pass the mouse wheel up to it.
            BoardScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            BoardScroll.PreviewMouseWheel += (_, e) =>
            {
                // A list or note inside a tile that can still scroll this way gets the wheel first.
                if (InnerCanScroll(e.OriginalSource as DependencyObject, e.Delta)) return;
                e.Handled = true;
                var up = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = MouseWheelEvent, Source = this };
                (VisualTreeHelper.GetParent(this) as UIElement)?.RaiseEvent(up);
            };
            PageTitleText.Style = null;
            PageTitleText.Text = "YOUR TILES";
            PageTitleText.FontSize = 10;
            PageTitleText.FontWeight = FontWeights.SemiBold;
            PageTitleText.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            Header.Margin = new Thickness(33, 52, 33, 14); // room for the window buttons when scrolled to the top
        }
        Unloaded += (_, _) => Main?.HideToast();
        Render();
    }

    private static MainWindow? Main => Application.Current.MainWindow as MainWindow;

    /// <summary>True if a scroll area between <paramref name="source"/> and the board can still move in the wheel's direction.</summary>
    private bool InnerCanScroll(DependencyObject? source, int delta)
    {
        for (var node = source; node != null && node != BoardScroll; node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
        {
            if (node is ScrollViewer sv && sv.ScrollableHeight > 0)
            {
                bool canUp = sv.VerticalOffset > 0, canDown = sv.VerticalOffset < sv.ScrollableHeight;
                if (delta > 0 ? canUp : canDown) return true;
            }
        }
        return false;
    }

    /// <summary>Rebuilds every tile (after something outside the board changed their content).</summary>
    public void Refresh() => Render();

    /// <summary>Turns edit layout on (used by "Personalize this space").</summary>
    public void StartEditing()
    {
        if (!_editing) Edit_Click(this, new RoutedEventArgs());
    }

    // ---------------- Rendering ----------------

    private IEnumerable<TileFrame> Frames => Board.Children.OfType<TileFrame>();

    private void Render()
    {
        // Old tile views only get Unloaded after the new ones are built (and have read their content),
        // so have any open editors save first. This also covers switching pages (a new board is built).
        App.CommitOpenEdits();
        Board.Children.Clear();
        foreach (var tile in _page.Tiles)
            Board.Children.Add(CreateFrame(tile));
        if (_editing) Board.Children.Add(CreateAddCell());
        SizeBoard();
    }

    private TileFrame CreateFrame(PlacedTile tile)
    {
        var type = TileRegistry.Find(tile.TypeId);
        var ctx = new TileContext(tile, App.SaveNow, App.SaveSoon);
        var view = type?.CreateView(ctx) ?? new UnknownTileView();
        var frame = new TileFrame(tile, type?.DisplayName ?? "Unknown tile", type?.Icon ?? "",
            type?.AccentKey ?? "Muted", showTitle: type is not null && !NoTitle.Contains(type.TypeId), view)
        {
            IsEditing = _editing,
            CanResize = type is not null && type.AllowedSizes.Count > 1,
        };
        frame.ResizeRequested += Resize;
        frame.RemoveRequested += Remove;
        frame.DragSurface.MouseLeftButtonDown += (_, e) => BeginDrag(frame, e);
        Place(frame);
        return frame;
    }

    private static void Place(FrameworkElement element, int col, int row, TileSize size)
    {
        var r = BoardGrid.CellRect(col, row, size);
        element.BeginAnimation(Canvas.LeftProperty, null);
        element.BeginAnimation(Canvas.TopProperty, null);
        Canvas.SetLeft(element, r.X);
        Canvas.SetTop(element, r.Y);
        element.Width = r.Width;
        element.Height = r.Height;
    }

    private static void Place(TileFrame frame) => Place(frame, frame.Tile.Column, frame.Tile.Row, frame.Tile.Size);

    /// <summary>Board size = the tiles' extent; edit mode adds an empty row so tiles can be dragged below.</summary>
    private void SizeBoard()
    {
        int rows = _page.Tiles.Count == 0 ? 1 : _page.Tiles.Max(t => t.Row + TileSizes.Span(t.Size).Rows);
        if (_editing)
        {
            var (_, addRow) = BoardGrid.FindFreeSpot(_page.Tiles, TileSize.Small);
            rows = Math.Max(rows, addRow + 1) + 1;
        }
        var cell = BoardGrid.CellRect(0, 0, TileSize.Small);
        Board.Width = BoardGrid.Columns * (cell.Width + BoardGrid.Gap) - BoardGrid.Gap;
        Board.Height = rows * (cell.Height + BoardGrid.Gap) - BoardGrid.Gap;
    }

    // ---------------- Edit mode ----------------

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        _editing = !_editing;
        EditLabel.Text = _editing ? "Done" : "Edit layout";
        EditGlyph.Text = _editing ? "" : "";
        System.Windows.Automation.AutomationProperties.SetName(EditBtn, _editing ? "Done editing" : "Edit layout");
        Render();
    }

    private Button CreateAddCell()
    {
        var (col, row) = BoardGrid.FindFreeSpot(_page.Tiles, TileSize.Small);
        var label = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        label.Children.Add(new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center });
        label.Children.Add(new TextBlock { Text = "Add tile", FontSize = 12, Margin = new Thickness(0, 6, 0, 0) });

        var border = new Border
        {
            CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1.5),
            BorderBrush = DashedBrush(), Child = label, Background = Brushes.Transparent,
        };
        var button = new Button
        {
            Content = border, Cursor = Cursors.Hand, Template = BareTemplate,
            Foreground = (Brush)FindResource("Muted"), FocusVisualStyle = (Style)FindResource("FocusRing"),
        };
        System.Windows.Automation.AutomationProperties.SetName(button, "Add tile");
        button.Click += (_, _) => ShowPicker();
        Place(button, col, row, TileSize.Small);
        return button;
    }

    /// <summary>A dashed outline in the theme's strong border colour.</summary>
    private DrawingBrush DashedBrush()
    {
        var pen = new Pen((Brush)FindResource("Strong"), 1.5) { DashStyle = new DashStyle([4, 3], 0) };
        var rect = new RectangleGeometry(new Rect(0, 0, 100, 100), 9, 9);
        return new DrawingBrush(new GeometryDrawing(null, pen, rect)) { Stretch = Stretch.Fill };
    }

    // ---------------- Add ----------------

    private void ShowPicker()
    {
        var picker = new AddTilePicker();
        picker.Closed += ClosePicker;
        picker.Picked += type =>
        {
            ClosePicker();
            var (col, row) = BoardGrid.FindFreeSpot(_page.Tiles, type.DefaultSize);
            _page.Tiles.Add(new PlacedTile { TypeId = type.TypeId, Column = col, Row = row, Size = type.DefaultSize });
            App.SaveNow();
            Render();
            TilesChanged?.Invoke();
        };
        Main?.ShowSheet(picker);
    }

    private static void ClosePicker() => Main?.HideSheet();

    // ---------------- Resize ----------------

    /// <summary>Moves to the next allowed size that fits; if none fits, flashes the outline red.</summary>
    private void Resize(TileFrame frame)
    {
        var type = TileRegistry.Find(frame.Tile.TypeId);
        if (type is null) return;
        var sizes = type.AllowedSizes;
        int start = Math.Max(0, sizes.ToList().IndexOf(frame.Tile.Size));
        for (int i = 1; i < sizes.Count; i++)
        {
            var next = sizes[(start + i) % sizes.Count];
            if (BoardGrid.TryResize(_page.Tiles, frame.Tile.Id, next))
            {
                App.SaveNow();
                Place(frame);
                SizeBoard();
                MoveAddCell();
                return;
            }
        }
        frame.FlashBlocked();
    }

    // ---------------- Remove and undo ----------------

    /// <summary>Raised after tiles are added, removed or restored, so a page around the board can refresh.</summary>
    public event Action? TilesChanged;

    private void Remove(TileFrame frame)
    {
        int index = _page.Tiles.IndexOf(frame.Tile);
        if (index < 0) return;
        var tile = frame.Tile;
        _page.Tiles.RemoveAt(index);
        App.SaveNow();
        Render();
        TilesChanged?.Invoke();
        Main?.ShowToast("Tile removed", "Undo", () => Restore(tile, index), TimeSpan.FromSeconds(6));
    }

    private void Restore(PlacedTile tile, int index)
    {
        if (!BoardGrid.Fits(_page.Tiles, tile.Column, tile.Row, tile.Size))
            (tile.Column, tile.Row) = BoardGrid.FindFreeSpot(_page.Tiles, tile.Size);
        _page.Tiles.Insert(Math.Min(index, _page.Tiles.Count), tile);
        App.SaveNow();
        Render();
        TilesChanged?.Invoke();
    }

    // ---------------- Drag ----------------

    private TileFrame? _dragging;
    private Point _dragStart;
    private Point _frameStart;
    private bool _dragMoved;
    private Border? _ghost;

    private void BeginDrag(TileFrame frame, MouseButtonEventArgs e)
    {
        if (!_editing) return;
        _dragging = frame;
        _dragStart = e.GetPosition(Board);
        _frameStart = new Point(Canvas.GetLeft(frame), Canvas.GetTop(frame));
        _dragMoved = false;
        frame.DragSurface.MouseMove += DragMove;
        frame.DragSurface.MouseLeftButtonUp += EndDrag;
        frame.DragSurface.LostMouseCapture += DragCaptureLost;
        frame.DragSurface.CaptureMouse();
        e.Handled = true;
    }

    private void DragMove(object sender, MouseEventArgs e)
    {
        if (_dragging is not { } frame) return;
        var delta = e.GetPosition(Board) - _dragStart;
        if (!_dragMoved && Math.Abs(delta.X) < 4 && Math.Abs(delta.Y) < 4) return;

        if (!_dragMoved)
        {
            _dragMoved = true;
            Panel.SetZIndex(frame, 100);
            frame.Opacity = 0.9;
            _ghost = new Border { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(2), IsHitTestVisible = false };
            Board.Children.Add(_ghost);
        }

        double x = _frameStart.X + delta.X, y = _frameStart.Y + delta.Y;
        Canvas.SetLeft(frame, x);
        Canvas.SetTop(frame, y);

        var (col, row) = BoardGrid.Snap(x, y);
        bool fits = BoardGrid.Fits(_page.Tiles, col, row, frame.Tile.Size, frame.Tile.Id);
        _ghost!.SetResourceReference(Border.BorderBrushProperty, fits ? "AccentBrush" : "Danger");
        Place(_ghost, col, row, frame.Tile.Size);
    }

    private void EndDrag(object sender, MouseButtonEventArgs e)
    {
        if (_dragging is not { } frame) return;
        frame.DragSurface.ReleaseMouseCapture(); // also runs DragCaptureLost
    }

    private void DragCaptureLost(object sender, MouseEventArgs e)
    {
        if (_dragging is not { } frame) return;
        _dragging = null;
        frame.DragSurface.MouseMove -= DragMove;
        frame.DragSurface.MouseLeftButtonUp -= EndDrag;
        frame.DragSurface.LostMouseCapture -= DragCaptureLost;
        if (_ghost != null) { Board.Children.Remove(_ghost); _ghost = null; }
        if (!_dragMoved) return;

        Panel.SetZIndex(frame, 0);
        frame.Opacity = 1;
        var (col, row) = BoardGrid.Snap(Canvas.GetLeft(frame), Canvas.GetTop(frame));
        bool moved = BoardGrid.TryMove(_page.Tiles, frame.Tile.Id, col, row);
        if (moved) App.SaveNow();
        AnimateTo(frame);
        SizeBoard();
        MoveAddCell();
    }

    /// <summary>Slides a tile to its cell (back to where it was, if the drop was refused).</summary>
    private static void AnimateTo(TileFrame frame)
    {
        var r = BoardGrid.CellRect(frame.Tile.Column, frame.Tile.Row, frame.Tile.Size);
        if (!SystemParameters.ClientAreaAnimation) { Place(frame); return; }
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(160);
        var ax = new DoubleAnimation(r.X, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        var ay = new DoubleAnimation(r.Y, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        ax.Completed += (_, _) => Place(frame);
        frame.BeginAnimation(Canvas.LeftProperty, ax);
        frame.BeginAnimation(Canvas.TopProperty, ay);
    }

    /// <summary>The Add tile cell follows the first free spot as tiles move.</summary>
    private void MoveAddCell()
    {
        if (!_editing) return;
        var add = Board.Children.OfType<Button>().FirstOrDefault();
        if (add is null) return;
        var (col, row) = BoardGrid.FindFreeSpot(_page.Tiles, TileSize.Small);
        Place(add, col, row, TileSize.Small);
    }

    private static readonly ControlTemplate BareTemplate = CreateBareTemplate();

    private static ControlTemplate CreateBareTemplate()
    {
        var t = new ControlTemplate(typeof(Button));
        t.VisualTree = new FrameworkElementFactory(typeof(ContentPresenter));
        return t;
    }
}
