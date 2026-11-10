using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CommandHub.Home;

/// <summary>
/// The After Hours page background, from the current theme: a diagonal wash leaning toward the cool
/// accent plus a faint warm glow top-right. Used by Home and Study.
/// </summary>
public static class Backdrop
{
    public static Color ColorOf(string key) =>
        Application.Current.TryFindResource(key) is Color c ? c : Colors.Gray;

    public static Color Blend(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    /// <summary>A theme colour at a given opacity (0–255).</summary>
    public static SolidColorBrush Wash(string colorKey, byte alpha)
    {
        var c = ColorOf(colorKey);
        return new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
    }

    public static void Paint(Border @base, Border glow)
    {
        var bg = ColorOf("BgColor");
        var cool = ColorOf("Accent2Color");
        var accent = ColorOf("AccentColor");
        @base.Background = new LinearGradientBrush(
            [
                new GradientStop(Blend(bg, cool, 0.05), 0),
                new GradientStop(Blend(bg, cool, 0.10), 0.53),
                new GradientStop(Blend(bg, accent, 0.04), 1),
            ],
            new Point(0, 0), new Point(1, 1));
        glow.Background = new RadialGradientBrush(Color.FromArgb(0x26, accent.R, accent.G, accent.B), Color.FromArgb(0, accent.R, accent.G, accent.B))
        {
            Center = new Point(0.92, 0.04), GradientOrigin = new Point(0.92, 0.04), RadiusX = 0.34, RadiusY = 0.42,
        };
    }
}
