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
            if (e.ViewportHeightChange == 0 || _scroll.ViewportHeight <= 0) return;
            _hero.MinHeight = Math.Max(560, _scroll.ViewportHeight); // MinHeight: grows instead of clipping in small windows
        };

        _hero.ContentChanged += _board.Refresh;
        _hero.ScrollToTilesRequested += ScrollToTiles;
        _hero.PersonalizeRequested += () => { ScrollToTiles(); _board.StartEditing(); };

        var layers = new Grid();
        layers.Children.Add(_base);
        layers.Children.Add(_glow);
        layers.Children.Add(_scroll);
        Content = layers;

        Loaded += (_, _) => { App.ThemeChanged += Paint; Paint(); };
        Unloaded += (_, _) => App.ThemeChanged -= Paint;
        Paint();
    }

    private void ScrollToTiles()
    {
        var top = _board.TranslatePoint(new Point(0, 0), (UIElement)_scroll.Content).Y;
        _scroll.ScrollToVerticalOffset(top); // the tiles header has its own top gap, clear of the window buttons
    }

    private static Color ColorOf(string key) =>
        Application.Current.TryFindResource(key) is Color c ? c : Colors.Gray;

    private static Color Blend(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    /// <summary>The page backdrop: a diagonal wash leaning toward the theme's cool accent, plus a faint warm glow top-right.</summary>
    private void Paint()
    {
        var bg = ColorOf("BgColor");
        var cool = ColorOf("Accent2Color");
        var accent = ColorOf("AccentColor");
        _base.Background = new LinearGradientBrush(
            [
                new GradientStop(Blend(bg, cool, 0.05), 0),
                new GradientStop(Blend(bg, cool, 0.10), 0.53),
                new GradientStop(Blend(bg, accent, 0.04), 1),
            ],
            new Point(0, 0), new Point(1, 1));
        _glow.Background = new RadialGradientBrush(Color.FromArgb(0x26, accent.R, accent.G, accent.B), Color.FromArgb(0, accent.R, accent.G, accent.B))
        {
            Center = new Point(0.92, 0.04), GradientOrigin = new Point(0.92, 0.04), RadiusX = 0.34, RadiusY = 0.42,
        };
    }
}
