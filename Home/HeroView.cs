using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CommandHub.Tiles.Greeting;
using CommandHub.Tiles.Links;
using CommandHub.Tiles.Weather;
using Page = CommandHub.Models.Page;

namespace CommandHub.Home;

/// <summary>
/// The "After Hours" welcome header at the top of Home: date, big headline, quick links,
/// a daily reminder and open to-dos. Everything comes from the page's own tiles.
/// </summary>
public sealed class HeroView : UserControl
{
    private readonly Page _page;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(20) };
    private bool _pinning;

    /// <summary>Raised when the header changed tile content (ticked a to-do, pinned a link).</summary>
    public event Action? ContentChanged;
    public event Action? ScrollToTilesRequested;
    public event Action? PersonalizeRequested;

    public HeroView(Page page)
    {
        _page = page;
        _clock.Tick += (_, _) => Render();
        Loaded += (_, _) =>
        {
            App.Saved += OnSaved;
            App.ThemeChanged += Render;
            _clock.Start();
            Render();
        };
        Unloaded += (_, _) =>
        {
            App.Saved -= OnSaved;
            App.ThemeChanged -= Render;
            _clock.Stop();
        };
        Render();
    }

    private void OnSaved()
    {
        if (!_pinning) Render();
    }

    // ---------------- Colours ----------------

    private static Color ColorOf(string key) =>
        Application.Current.TryFindResource(key) is Color c ? c : Colors.Gray;

    /// <summary>A theme colour at a given opacity (0–255), e.g. a faint wash of the text colour.</summary>
    private static SolidColorBrush Wash(string key, byte alpha)
    {
        var c = ColorOf(key);
        return new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
    }

    private static TextBlock T(string text, double size, string brush, FontWeight? weight = null)
    {
        var t = new TextBlock { Text = text, FontSize = size, TextTrimming = TextTrimming.CharacterEllipsis };
        if (weight is { } w) t.FontWeight = w;
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    private static TextBlock Glyph(string glyph, double size, string brush)
    {
        var t = T(glyph, size, brush);
        t.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        return t;
    }

    // ---------------- Layout ----------------

    private void Render()
    {
        var now = DateTime.Now;
        var weather = HeroData.Weather(_page);

        var root = new Grid { Margin = new Thickness(33, 0, 33, 19) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Top bar: breadcrumb left, weather right (leaving room for the window buttons).
        var top = new DockPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        if (weather is not null)
        {
            var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 118, 0) };
            right.Children.Add(Glyph(SkyGlyph(weather), 11, "AccentBrush"));
            right.Children.Add(Spaced(T($"{weather.Temperature}°", 11, "Muted"), 6));
            right.Children.Add(Spaced(T(weather.City, 11, "Muted"), 16));
            DockPanel.SetDock(right, Dock.Right);
            top.Children.Add(right);
        }
        top.Children.Add(T(HeroText.Breadcrumb(_page.Name, now), 11, "Muted"));
        root.Children.Add(top);

        // Welcome block.
        var welcome = new StackPanel { Margin = new Thickness(0, 34, 0, 0) };
        welcome.Children.Add(T(HeroText.Eyebrow(now.Hour), 10, "AccentBrush", FontWeights.SemiBold));
        var headline = T("Your space.\nYour pace.", 52, "Text", FontWeights.Normal);
        headline.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        headline.TextTrimming = TextTrimming.None;
        headline.LineHeight = 54;
        headline.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        headline.Margin = new Thickness(-2, 14, 0, 12);
        welcome.Children.Add(headline);
        welcome.Children.Add(T(HeroText.SubLine(now.ToString("t").ToLowerInvariant(), now, weather), 13, "Muted"));
        welcome.Children.Add(_pinning ? PinEditor() : QuickRow());
        welcome.Children.Add(LowerCards());
        Grid.SetRow(welcome, 1);
        root.Children.Add(welcome);

        // Footer.
        var footer = new DockPanel();
        var personalize = TextButton("Personalize this space", "\uE70F", () => PersonalizeRequested?.Invoke());
        DockPanel.SetDock(personalize, Dock.Right);
        footer.Children.Add(personalize);
        bool trouble = App.SaveTrouble || App.Store.ReadFailed;
        var status = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        status.Children.Add(T("●", 10, trouble ? "Danger" : "AccentBrush"));
        status.Children.Add(Spaced(T(trouble ? "Changes aren't being saved" : "All systems ready", 10, "Muted"), 7));
        DockPanel.SetDock(status, Dock.Left);
        footer.Children.Add(status);
        var tiles = TextButton("Your tiles", "\uE74B", () => ScrollToTilesRequested?.Invoke());
        tiles.HorizontalAlignment = HorizontalAlignment.Center;
        footer.Children.Add(tiles);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        Content = root;
    }

    private static FrameworkElement Spaced(FrameworkElement e, double left)
    {
        e.Margin = new Thickness(left, 0, 0, 0);
        return e;
    }

    private static string SkyGlyph(HeroWeather w) => w.Sky == Sky.Clear ? (w.IsDay ? "\uE706" : "\uE708") : "\uE753";

    /// <summary>A quiet text link like "Personalize this space ✎".</summary>
    private static Button TextButton(string text, string glyph, Action click)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(T(text, 10, "Muted"));
        content.Children.Add(Spaced(Glyph(glyph, 10, "Muted"), 7));
        var b = new Button { Content = content, Cursor = Cursors.Hand, Template = Bare, Background = Brushes.Transparent };
        b.SetResourceReference(FrameworkElement.FocusVisualStyleProperty, "FocusRing");
        System.Windows.Automation.AutomationProperties.SetName(b, text);
        b.Click += (_, _) => click();
        b.MouseEnter += (_, _) => { foreach (var t in content.Children.OfType<TextBlock>()) t.SetResourceReference(TextBlock.ForegroundProperty, "Text"); };
        b.MouseLeave += (_, _) => { foreach (var t in content.Children.OfType<TextBlock>()) t.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); };
        return b;
    }

    // ---------------- Quick links ----------------

    private FrameworkElement QuickRow()
    {
        var row = new WrapPanel { Margin = new Thickness(0, 31, 0, 0) };
        foreach (var link in HeroData.Links(_page, 5))
        {
            var kind = LinkTarget.Guess(link.Target);
            var glyph = kind switch { LinkKind.Web => "\uE774", LinkKind.Folder => "\uE8B7", LinkKind.File => "\uE8A5", _ => "\uE71B" };
            var square = Square(glyph, link.Name.Length > 0 ? link.Name : link.Target, link.Target);
            square.Click += (_, _) =>
            {
                if (!LinkTarget.TryOpen(link.Target, out var error))
                    (Application.Current.MainWindow as MainWindow)?.ShowNotice($"{link.Name}: {error}");
            };
            row.Children.Add(square);
        }
        var pin = Square("\uE710", "Pin a link", "Add a website, folder or app here");
        pin.Click += (_, _) => { _pinning = true; Render(); };
        row.Children.Add(pin);
        return row;
    }

    private static Button Square(string glyph, string label, string tooltip)
    {
        var icon = Glyph(glyph, 20, "AccentBrush");
        icon.Margin = new Thickness(0, 0, 0, 8);
        var text = T(label, 11, "Text");
        var stack = new StackPanel();
        stack.Children.Add(icon);
        stack.Children.Add(text);

        var card = new Border
        {
            Width = 104, Height = 88, CornerRadius = new CornerRadius(11), Padding = new Thickness(13),
            BorderThickness = new Thickness(1), Background = Wash("TextColor", 0x0A), BorderBrush = Wash("TextColor", 0x22),
            Child = stack, RenderTransform = new TranslateTransform(),
        };
        var b = new Button { Content = card, Cursor = Cursors.Hand, Template = Bare, ToolTip = tooltip, Margin = new Thickness(0, 0, 11, 11) };
        b.SetResourceReference(FrameworkElement.FocusVisualStyleProperty, "FocusRing");
        System.Windows.Automation.AutomationProperties.SetName(b, label);
        b.MouseEnter += (_, _) =>
        {
            card.Background = Wash("TextColor", 0x14);
            card.BorderBrush = Wash("AccentColor", 0x88);
            ((TranslateTransform)card.RenderTransform).Y = -2;
        };
        b.MouseLeave += (_, _) =>
        {
            card.Background = Wash("TextColor", 0x0A);
            card.BorderBrush = Wash("TextColor", 0x22);
            ((TranslateTransform)card.RenderTransform).Y = 0;
        };
        return b;
    }

    private FrameworkElement PinEditor()
    {
        TextBox Field(string placeholder, string name, double width)
        {
            var f = new TextBox { Tag = placeholder, Width = width, Padding = new Thickness(8, 5, 8, 5), FontSize = 12, Margin = new Thickness(0, 0, 8, 0) };
            f.SetResourceReference(FrameworkElement.StyleProperty, "FieldBox");
            System.Windows.Automation.AutomationProperties.SetName(f, name);
            return f;
        }
        var name = Field("Name (optional)", "Pin name", 150);
        var target = Field("https://… or C:\\folder", "Pin target", 260);
        var error = T("", 11, "Danger");
        error.Margin = new Thickness(0, 6, 0, 0);

        void Pin()
        {
            if (!HeroData.PinLink(_page, name.Text, target.Text)) { error.Text = "Type a website, folder or app first."; target.Focus(); return; }
            _pinning = false;
            App.SaveNow();
            ContentChanged?.Invoke();
            Render();
        }
        void Cancel() { _pinning = false; Render(); }

        var pinBtn = new Button { Content = "Pin", Padding = new Thickness(14, 5, 14, 5) };
        pinBtn.SetResourceReference(FrameworkElement.StyleProperty, "AccentButton");
        pinBtn.Click += (_, _) => Pin();
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(8, 0, 0, 0) };
        cancel.SetResourceReference(FrameworkElement.StyleProperty, "GhostButton");
        cancel.Click += (_, _) => Cancel();
        target.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Pin(); } };
        name.KeyDown += (_, e) => { if (e.Key == Key.Escape) Cancel(); };
        target.KeyDown += (_, e) => { if (e.Key == Key.Escape) Cancel(); };

        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(name);
        line.Children.Add(target);
        line.Children.Add(pinBtn);
        line.Children.Add(cancel);
        var panel = new StackPanel { Margin = new Thickness(0, 31, 0, 11), MinHeight = 88 };
        panel.Children.Add(T("PIN A LINK", 9, "AccentBrush", FontWeights.SemiBold));
        line.Margin = new Thickness(0, 10, 0, 0);
        panel.Children.Add(line);
        panel.Children.Add(error);
        Dispatcher.BeginInvoke(() => target.Focus(), DispatcherPriority.Input);
        return panel;
    }

    // ---------------- Reminder and to-dos ----------------

    private FrameworkElement LowerCards()
    {
        var grid = new Grid { Margin = new Thickness(0, 16, 0, 0), MaxWidth = 740, HorizontalAlignment = HorizontalAlignment.Left };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.85, GridUnitType.Star) });

        // A little reminder.
        var quote = T(GreetingText.LineFor(DateOnly.FromDateTime(DateTime.Today)), 16, "Text");
        quote.TextWrapping = TextWrapping.Wrap;
        quote.TextTrimming = TextTrimming.None;
        quote.LineHeight = 24;
        quote.Margin = new Thickness(0, 14, 0, 0);
        var reminder = Card("A LITTLE REMINDER", Glyph("\uE734", 10, "AccentBrush"), quote);
        grid.Children.Add(reminder);

        // On your mind.
        var todos = HeroData.OpenTodos(_page);
        var list = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (var (index, text) in todos.Take(3))
        {
            var check = new Button { Content = T("○", 12, "AccentBrush"), Template = Bare, Cursor = Cursors.Hand, ToolTip = "Mark done", Margin = new Thickness(0, 0, 8, 0) };
            check.SetResourceReference(FrameworkElement.FocusVisualStyleProperty, "FocusRing");
            System.Windows.Automation.AutomationProperties.SetName(check, $"Mark {text} done");
            check.Click += (_, _) =>
            {
                if (!HeroData.TickTodo(_page, index)) return;
                App.SaveNow();
                ContentChanged?.Invoke();
                Render();
            };
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 0) };
            DockPanel.SetDock(check, Dock.Left);
            row.Children.Add(check);
            row.Children.Add(T(text, 11, "Text"));
            var line = new Border { Padding = new Thickness(0, 8, 0, 8), BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = Wash("TextColor", 0x12), Child = row };
            list.Children.Add(line);
        }
        if (list.Children.Count > 0) ((Border)list.Children[^1]).BorderThickness = new Thickness(0);
        if (todos.Count == 0)
        {
            var none = T("Nothing on your mind. Add to-dos in your tiles below.", 11, "Muted");
            none.TextWrapping = TextWrapping.Wrap;
            none.Margin = new Thickness(0, 10, 0, 0);
            list.Children.Add(none);
        }
        var count = TextButton(todos.Count == 1 ? "1 ITEM" : $"{todos.Count} ITEMS", "\uE8AD", () => ScrollToTilesRequested?.Invoke());
        var mind = Card("ON YOUR MIND", count, list);
        Grid.SetColumn(mind, 2);
        grid.Children.Add(mind);
        return grid;
    }

    private static Border Card(string title, FrameworkElement corner, FrameworkElement body)
    {
        var head = new DockPanel();
        DockPanel.SetDock(corner, Dock.Right);
        head.Children.Add(corner);
        head.Children.Add(T(title, 9, "AccentBrush", FontWeights.SemiBold));
        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(body);
        var card = new Border
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(16), MinHeight = 115,
            BorderThickness = new Thickness(1), BorderBrush = Wash("TextColor", 0x20), Child = stack,
        };
        card.SetResourceReference(Border.BackgroundProperty, "Surface");
        return card;
    }

    private static readonly ControlTemplate Bare = CreateBare();

    private static ControlTemplate CreateBare()
    {
        var t = new ControlTemplate(typeof(Button));
        t.VisualTree = new FrameworkElementFactory(typeof(ContentPresenter));
        return t;
    }
}
