using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;
using CommandHub.Home;
using CommandHub.Services;
using static CommandHub.Home.PageUi;

namespace CommandHub.Views;

/// <summary>About Command hub: version, what's new, links and credits. Opened from Settings.</summary>
public sealed class AboutPage : UserControl
{
    /// <summary>What changed in each version, newest first. Shown on this page.</summary>
    public static readonly (string Version, string[] Changes)[] History =
    [
        ("1.2.0",
        [
            "Add-ons: optional tabs you install from Settings. The first two are a PlayStation Store price tracker and a media tracker.",
            "A news tile for the headlines from sources you pick.",
            "Quick links can be grouped.",
            "Many more settings: your name, the tab to open on, which tabs show, text size, remembering the window, automatic updates, backups.",
            "This About page.",
            "Fixed: a maximized window no longer slips under the taskbar.",
        ]),
        ("1.1.0",
        [
            "Planner, Money and Journal tabs.",
            "The \"Up next\" tile on Home.",
            "A new icon that pairs with Stint.",
        ]),
        ("1.0.0",
        [
            "The first release: the Home board with eleven tiles, the Study tab and in-app updates.",
        ]),
    ];

    public AboutPage()
    {
        var content = new StackPanel { Margin = new Thickness(33, 0, 33, 40), MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };
        WindowChrome.SetIsHitTestVisibleInChrome(content, true);
        var scroll = new ScrollViewer { Content = content, Focusable = false, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        _ = new SmoothScroll(scroll);
        var b = new Border();
        var glow = new Border { IsHitTestVisible = false };
        var layers = new Grid();
        layers.Children.Add(b);
        layers.Children.Add(glow);
        layers.Children.Add(scroll);
        Content = layers;
        Loaded += (_, _) => Backdrop.Paint(b, glow);

        var top = new DockPanel { Height = 40 };
        var back = Styled("Back to settings", "GhostButton", (_, _) => (Application.Current.MainWindow as MainWindow)?.ShowSettingsPage(), new Thickness(12, 4, 12, 4));
        back.VerticalAlignment = VerticalAlignment.Bottom;
        back.HorizontalAlignment = HorizontalAlignment.Left;
        top.Children.Add(back);
        content.Children.Add(top);

        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 30, 0, 0) };
        head.Children.Add(new Image
        {
            Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/commandhub.png")),
            Width = 64, Height = 64, Margin = new Thickness(0, 0, 18, 0),
        });
        RenderOptions.SetBitmapScalingMode(head.Children[0], BitmapScalingMode.HighQuality);
        var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(Big("Command hub", 40));
        title.Children.Add(T($"Version {App.Updater.CurrentVersion}   ·   A personal command centre, made as a companion to Stint", 13, "Muted"));
        head.Children.Add(title);
        content.Children.Add(head);

        // Links
        var links = new WrapPanel { Margin = new Thickness(0, 24, 0, 0) };
        foreach (var (text, url) in new[]
        {
            ("Project on GitHub", Updates.RepoUrl),
            ("All versions", Updates.RepoUrl + "/releases"),
            ("Report a problem", Updates.RepoUrl + "/issues/new"),
        })
        {
            var link = Styled(text, "GhostButton", (_, _) => Open(url), new Thickness(14, 6, 14, 6));
            link.Margin = new Thickness(0, 0, 8, 8);
            links.Children.Add(link);
        }
        content.Children.Add(links);

        // What's new
        var news = new StackPanel();
        news.Children.Add(Spaced(Label("WHAT'S NEW"), 0, 6));
        foreach (var (version, changes) in History)
        {
            news.Children.Add(Spaced(T(version == App.Updater.CurrentVersion ? $"{version}  (this version)" : version, 14, "Text", FontWeights.SemiBold), 12, 6));
            foreach (var c in changes)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                var dot = T("●", 7, "AccentBrush");
                dot.Margin = new Thickness(2, 5, 10, 0);
                dot.VerticalAlignment = VerticalAlignment.Top;
                DockPanel.SetDock(dot, Dock.Left);
                row.Children.Add(dot);
                row.Children.Add(T(c, 13, wrap: true));
                news.Children.Add(row);
            }
        }
        var newsCard = Card(news);
        newsCard.Margin = new Thickness(0, 16, 0, 0);
        content.Children.Add(newsCard);

        // Credits
        var credits = new StackPanel();
        credits.Children.Add(Spaced(Label("MADE WITH"), 0, 10));
        foreach (var (name, what, url) in new[]
        {
            ("Open-Meteo", "Weather, free and without an account", "https://open-meteo.com"),
            ("Velopack", "In-app updates", "https://velopack.io"),
            (".NET and WPF", "The app itself", "https://dotnet.microsoft.com"),
            ("Segoe Fluent Icons", "Icons", "https://learn.microsoft.com/windows/apps/design/style/segoe-fluent-icons-font"),
        })
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var open = Styled("Visit", "GhostButton", (_, _) => Open(url), new Thickness(10, 3, 10, 3));
            System.Windows.Automation.AutomationProperties.SetName(open, $"Visit {name}");
            DockPanel.SetDock(open, Dock.Right);
            row.Children.Add(open);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(T(name, 13));
            text.Children.Add(T(what, 11, "Muted"));
            row.Children.Add(text);
            credits.Children.Add(row);
        }
        var creditsCard = Card(credits);
        creditsCard.Margin = new Thickness(0, 12, 0, 0);
        content.Children.Add(creditsCard);

        var privacy = new StackPanel();
        privacy.Children.Add(Spaced(Label("PRIVACY"), 0, 10));
        privacy.Children.Add(T("Everything you add stays on this PC, in " + App.Store.FilePath + ". Command hub has no account and sends nothing about you anywhere. " +
            "It only goes online to fetch what you ask for: weather, news feeds you add, updates from GitHub, and whatever an add-on you install says it needs.", 13, wrap: true));
        var privacyCard = Card(privacy);
        privacyCard.Margin = new Thickness(0, 12, 0, 0);
        content.Children.Add(privacyCard);
    }

    private static FrameworkElement Spaced(FrameworkElement e, double top, double bottom)
    {
        e.Margin = new Thickness(0, top, 0, bottom);
        return e;
    }

    /// <summary>Opens a web link in the default browser.</summary>
    public static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            (Application.Current.MainWindow as MainWindow)?.ShowNotice("Couldn't open the link: " + ex.Message);
        }
    }
}
