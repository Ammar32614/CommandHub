using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;
using CommandHub.Views;
using Page = CommandHub.Models.Page;

namespace CommandHub.Home;

/// <summary>
/// Home: the welcome header fills the window; scroll down (or click "Your tiles") for the tile board.
/// Painted with a soft gradient and an accent glow in the top-right corner, from the current theme.
/// </summary>
public sealed class HomeView : UserControl
{
    private readonly HeroView _hero;
    private readonly BoardView _board;
    private readonly ScrollViewer _scroll;
    private readonly Border _base = new();
    private readonly Border _glow = new() { IsHitTestVisible = false };

    public HomeView(Page page)
    {
        _hero = new HeroView(page);
        _board = new BoardView(page, embedded: true);
        // Home paints under the 40 px title strip; once scrolled, tiles must still take clicks there.
        WindowChrome.SetIsHitTestVisibleInChrome(_board, true);

        var stack = new StackPanel();
        stack.Children.Add(_hero);
        stack.Children.Add(_board);
        _scroll = new ScrollViewer
        {
            Content = stack, Focusable = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            // The page never scrolls sideways: the header always fits the window. The tile board
            // scrolls sideways on its own when the window is narrower than the grid.
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        // The header is exactly one screen tall, so the tiles start just below the fold.
        // ScrollChanged (not SizeChanged): the viewport size is only known after layout, and is 0 before it.
        _scroll.ScrollChanged += (_, e) =>
        {
            if (e.ViewportHeightChange != 0 && _scroll.ViewportHeight > 0)
                _hero.MinHeight = Math.Max(560, _scroll.ViewportHeight); // MinHeight: grows instead of clipping in small windows
            UpdateForScroll();
        };
        _smooth = new SmoothScroll(_scroll);

        _hero.ContentChanged += _board.Refresh;
        _hero.ScrollToTilesRequested += ScrollToTiles;
        _hero.PersonalizeRequested += () => { ScrollToTiles(); _board.StartEditing(); };

        // One floating button that always offers the other half of the page.
        _jump = new Button
        {
            Cursor = System.Windows.Input.Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 16), Padding = new Thickness(16, 7, 16, 7),
        };
        _jump.SetResourceReference(StyleProperty, "ChipButton");
        _jump.Click += (_, _) => { if (_inTiles) _smooth.ScrollTo(0); else ScrollToTiles(); };
        UpdateJump();

        var layers = new Grid();
        layers.Children.Add(_base);
        layers.Children.Add(_glow);
        layers.Children.Add(_scroll);
        layers.Children.Add(_jump);
        Content = layers;

        Loaded += (_, _) => { App.ThemeChanged += Paint; Paint(); };
        Unloaded += (_, _) => App.ThemeChanged -= Paint;
        Paint();
    }

    private readonly SmoothScroll _smooth;
    private readonly Button _jump;
    private bool _inTiles;

    private void ScrollToTiles()
    {
        var top = _board.TranslatePoint(new Point(0, 0), (UIElement)_scroll.Content).Y;
        _smooth.ScrollTo(top); // the tiles header has its own top gap, clear of the window buttons
    }

    /// <summary>Fade the header as it scrolls away, and flip the floating button between "down" and "up".</summary>
    private void UpdateForScroll()
    {
        double header = _hero.ActualHeight;
        _hero.Opacity = SmoothScroll.HeaderOpacity(_scroll.VerticalOffset, header);
        bool inTiles = SmoothScroll.InTiles(_scroll.VerticalOffset, header);
        if (inTiles == _inTiles) return;
        _inTiles = inTiles;
        UpdateJump();
    }

    private void UpdateJump()
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        var glyph = new TextBlock { Text = _inTiles ? "" : "", FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        glyph.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        var label = new TextBlock { Text = _inTiles ? "Back to top" : "Your tiles", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(glyph);
        content.Children.Add(label);
        _jump.Content = content;
        System.Windows.Automation.AutomationProperties.SetName(_jump, label.Text);
    }

    private void Paint() => Backdrop.Paint(_base, _glow);
}
