using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CommandHub.Home;

/// <summary>Shared building blocks for the code-built After Hours pages (Planner; Study has its own copies).</summary>
public static class PageUi
{
    public static TextBlock T(string text, double size, string brush = "Text", FontWeight? weight = null, bool wrap = false)
    {
        var t = new TextBlock { Text = text, FontSize = size, TextTrimming = TextTrimming.CharacterEllipsis };
        if (weight is { } w) t.FontWeight = w;
        if (wrap) { t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; }
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    public static TextBlock Label(string text, string brush = "AccentBrush") => T(text, 9, brush, FontWeights.SemiBold);

    public static TextBlock Big(string text, double size)
    {
        var t = T(text, size, "Text", FontWeights.Normal);
        t.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        return t;
    }

    public static TextBlock Glyph(string glyph, double size, string brush)
    {
        var t = T(glyph, size, brush);
        t.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        return t;
    }

    public static Brush ColorBrush(string hex)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch (FormatException) { return Brushes.Gray; }
    }

    public static Border Card(UIElement body, Thickness? padding = null)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(10), Padding = padding ?? new Thickness(18, 16, 18, 18), BorderThickness = new Thickness(1),
            BorderBrush = Backdrop.Wash("TextColor", 0x20), Child = body,
        };
        card.SetResourceReference(Border.BackgroundProperty, "Surface");
        return card;
    }

    public static Button Bare(UIElement content, string name, string? tooltip = null)
    {
        var b = new Button { Content = content, Template = BareTemplate, Cursor = System.Windows.Input.Cursors.Hand, ToolTip = tooltip, Background = Brushes.Transparent };
        b.SetResourceReference(FrameworkElement.FocusVisualStyleProperty, "FocusRing");
        System.Windows.Automation.AutomationProperties.SetName(b, name);
        return b;
    }

    public static Button Styled(string text, string style, RoutedEventHandler click, Thickness? padding = null)
    {
        var b = new Button { Content = text, Padding = padding ?? new Thickness(12, 6, 12, 6) };
        b.SetResourceReference(FrameworkElement.StyleProperty, style);
        System.Windows.Automation.AutomationProperties.SetName(b, text);
        b.Click += click;
        return b;
    }

    public static TextBox Field(string placeholder, string text, string name)
    {
        var f = new TextBox { Tag = placeholder, Text = text, Padding = new Thickness(10, 6, 10, 6), FontSize = 13 };
        f.SetResourceReference(FrameworkElement.StyleProperty, "FieldBox");
        System.Windows.Automation.AutomationProperties.SetName(f, name);
        return f;
    }

    private static readonly ControlTemplate BareTemplate = CreateBare();

    private static ControlTemplate CreateBare()
    {
        var t = new ControlTemplate(typeof(Button));
        t.VisualTree = new FrameworkElementFactory(typeof(ContentPresenter));
        return t;
    }
}
