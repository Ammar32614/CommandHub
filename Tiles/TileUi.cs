using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CommandHub.Tiles;

/// <summary>Small helpers so code-built tile views use theme resources (and follow theme changes).</summary>
internal static class TileUi
{
    public static TextBlock Text(string text, double size = 13, string brush = "Text", bool semibold = false)
    {
        var t = new TextBlock { Text = text, FontSize = size, TextTrimming = TextTrimming.CharacterEllipsis };
        if (semibold) t.FontWeight = FontWeights.SemiBold;
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    public static TextBlock Display(string text, double size, string brush = "Text", FontWeight? weight = null)
    {
        var t = Text(text, size, brush);
        t.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        t.FontWeight = weight ?? FontWeights.Light;
        return t;
    }

    public static TextBlock Icon(string glyph, double size, string brush)
    {
        var t = Text(glyph, size, brush);
        t.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        return t;
    }

    public static Border Bar(double fraction, string brush, double height = 5)
    {
        var fill = new Border { CornerRadius = new CornerRadius(height / 2), HorizontalAlignment = HorizontalAlignment.Left };
        fill.SetResourceReference(Border.BackgroundProperty, brush);
        var track = new Border { CornerRadius = new CornerRadius(height / 2), Height = height, Child = fill };
        track.SetResourceReference(Border.BackgroundProperty, "Raised");
        track.SizeChanged += (_, _) => fill.Width = Math.Max(0, track.ActualWidth * Math.Clamp(fraction, 0, 1));
        return track;
    }

    public static Button IconButton(string glyph, string name, double size = 26)
    {
        var b = new Button { Content = glyph, Width = size, Height = size, FontSize = 11, ToolTip = name };
        b.SetResourceReference(FrameworkElement.StyleProperty, "IconButton");
        System.Windows.Automation.AutomationProperties.SetName(b, name);
        return b;
    }

    /// <summary>Pencil button placed in the tile's title row (top-right), like the quick links tile.</summary>
    public static Button EditButton(string name)
    {
        var b = IconButton("", name);
        b.HorizontalAlignment = HorizontalAlignment.Right;
        b.VerticalAlignment = VerticalAlignment.Top;
        b.Margin = new Thickness(0, -32, -6, 0);
        return b;
    }

    public static TextBox Field(string placeholder, string text = "", string? name = null)
    {
        var f = new TextBox { Tag = placeholder, Text = text, Padding = new Thickness(8, 4, 8, 4), FontSize = 12 };
        f.SetResourceReference(FrameworkElement.StyleProperty, "FieldBox");
        System.Windows.Automation.AutomationProperties.SetName(f, name ?? placeholder);
        return f;
    }

    public static Button Ghost(string text, RoutedEventHandler click, bool accent = false)
    {
        var b = new Button { Content = text, Padding = new Thickness(12, 5, 12, 5) };
        b.SetResourceReference(FrameworkElement.StyleProperty, accent ? "AccentButton" : "GhostButton");
        b.Click += click;
        return b;
    }

    /// <summary>"1h 12m", "25m", "0m".</summary>
    public static string Duration(int seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1 ? (t.Minutes == 0 ? $"{(int)t.TotalHours}h" : $"{(int)t.TotalHours}h {t.Minutes}m") : $"{t.Minutes}m";
    }
}
