using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CommandHub.Services;
using Microsoft.Win32;

namespace CommandHub.Views;

public partial class SettingsPage : UserControl
{
    private readonly StartupManager _startup = StartupManager.ForCurrentApp();
    private bool _loading;

    public SettingsPage()
    {
        InitializeComponent();
        _loading = true;
        try { StartWithWindows.IsChecked = _startup.IsEnabled; }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException) { StartWithWindows.IsEnabled = false; }
        _loading = false;
        BuildThemes();
        RefreshUpdates();
    }

    // ---------------- Updates ----------------

    public void RefreshUpdates()
    {
        var u = App.Updater;
        VersionText.Text = $"Command hub {u.CurrentVersion}";
        UpdateStatus.Text = u.State switch
        {
            UpdateState.NotInstalled => "Running from source. Updates work in the installed app.",
            UpdateState.Checking => "Checking for updates…",
            UpdateState.Downloading => "Downloading an update…",
            UpdateState.UpToDate => "You're on the latest version.",
            UpdateState.Ready => $"Version {u.ReadyVersion ?? "new"} is ready. Restart to finish updating.",
            UpdateState.Failed => u.Error ?? "Couldn't check for updates.",
            _ => "Checks for updates automatically every few hours.",
        };
        CheckBtn.IsEnabled = u.State is UpdateState.Idle or UpdateState.UpToDate or UpdateState.Failed;
        RestartBtn.Visibility = u.State == UpdateState.Ready ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e) => await App.Updater.CheckAsync();

    private void RestartUpdate_Click(object sender, RoutedEventArgs e) => App.Updater.ApplyAndRestart();

    private void StartWithWindows_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        try
        {
            _startup.SetEnabled(StartWithWindows.IsChecked == true);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            (Application.Current.MainWindow as MainWindow)?.ShowNotice("Windows didn't allow changing the startup setting.");
            _loading = true;
            StartWithWindows.IsChecked = _startup.IsEnabled;
            _loading = false;
        }
    }

    /// <summary>One card per theme: a little picture of its background and accents, then its name.</summary>
    private void BuildThemes()
    {
        Themes.Children.Clear();
        foreach (var theme in ThemeCatalog.All)
        {
            Brush B(string key) => new SolidColorBrush(App.ParseColor(theme[key]));
            bool selected = theme.Name == App.Theme.Name;

            var preview = new Grid { Height = 54 };
            preview.Children.Add(new Border { Background = B("BgColor"), CornerRadius = new CornerRadius(8, 8, 0, 0) });
            var tiles = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            tiles.Children.Add(new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(5), Background = B("SurfaceColor") });
            tiles.Children.Add(new Border { Width = 18, Height = 26, CornerRadius = new CornerRadius(5), Background = B("RaisedColor"), Margin = new Thickness(4, 0, 0, 0) });
            preview.Children.Add(tiles);
            var dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            foreach (var k in new[] { "AccentColor", "Accent2Color", "Accent3Color", "Accent4Color" })
                dots.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = B(k), Margin = new Thickness(2, 0, 2, 0) });
            preview.Children.Add(dots);

            var label = new Border
            {
                Background = B("SurfaceColor"), CornerRadius = new CornerRadius(0, 0, 8, 8), Padding = new Thickness(10, 6, 10, 7),
                Child = new TextBlock { Text = theme.Name, Foreground = B("TextColor"), FontSize = 12, FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal },
            };
            var stack = new StackPanel();
            stack.Children.Add(preview);
            stack.Children.Add(label);

            var card = new Border
            {
                Child = stack, CornerRadius = new CornerRadius(10), Padding = new Thickness(2),
                BorderThickness = new Thickness(selected ? 2 : 1),
            };
            card.SetResourceReference(Border.BorderBrushProperty, selected ? "AccentBrush" : "Hairline");

            var button = new Button
            {
                Content = card, Margin = new Thickness(4), Cursor = Cursors.Hand, ToolTip = theme.Blurb,
                Template = BareTemplate, FocusVisualStyle = (Style)FindResource("FocusRing"),
            };
            System.Windows.Automation.AutomationProperties.SetName(button, $"{theme.Name} theme");
            button.Click += (_, _) =>
            {
                if (App.Data.Settings.Theme == theme.Name) return;
                App.Data.Settings.Theme = theme.Name;
                App.ApplyTheme(theme.Name);
                App.SaveNow();
                BuildThemes();
            };
            Themes.Children.Add(button);
        }
    }

    /// <summary>A button that is just its content (the card draws its own border).</summary>
    private static readonly ControlTemplate BareTemplate = CreateBareTemplate();

    private static ControlTemplate CreateBareTemplate()
    {
        var t = new ControlTemplate(typeof(Button));
        t.VisualTree = new FrameworkElementFactory(typeof(ContentPresenter));
        return t;
    }
}
