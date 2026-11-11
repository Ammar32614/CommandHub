using System.IO;
using Path = System.IO.Path;
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
        _ = new Home.SmoothScroll(Scroll);
        _loading = true;
        try { StartWithWindows.IsChecked = _startup.IsEnabled; }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException) { StartWithWindows.IsEnabled = false; }
        RememberWindow.IsChecked = S.RememberWindow;
        AutoUpdate.IsChecked = S.AutoUpdate;
        BuildThemes();
        BuildTextSize();
        BuildName();
        BuildStartPage();
        BuildTabSwitches();
        _loading = false;
        AddOns.AddOnManager.BuildSettingsSection(AddOnsSection);
        DataText.Text = $"{App.Store.FilePath}. Backups go in the backups folder next to it.";
        RefreshUpdates();
    }

    private static Models.AppSettings S => App.Data.Settings;
    private static MainWindow? Main => Application.Current.MainWindow as MainWindow;

    // ---------------- Appearance and you ----------------

    private static RadioButton Segment(string text, bool on, string group, string name, Action pick, double minWidth = 64)
    {
        var rb = new RadioButton { Content = text, GroupName = group, IsChecked = on, MinWidth = minWidth, Padding = new Thickness(10, 0, 10, 0) };
        rb.SetResourceReference(StyleProperty, "Segment");
        System.Windows.Automation.AutomationProperties.SetName(rb, name);
        rb.Checked += (_, _) => pick();
        return rb;
    }

    private void BuildTextSize()
    {
        TextSize.Children.Clear();
        double current = Settings.SettingsLogic.SnapScale(S.TextScale);
        foreach (var s in Settings.SettingsLogic.Scales)
        {
            var scale = s;
            TextSize.Children.Add(Segment($"{s * 100:0}%", s == current, "TextSize", $"Text size {s * 100:0} percent", () =>
            {
                if (_loading) return;
                S.TextScale = scale;
                App.SaveNow();
                Main?.ApplyTextScale();
            }, 0));
        }
        var box = new Border { Padding = new Thickness(3), CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
        box.SetResourceReference(Border.BackgroundProperty, "Bg");
        box.SetResourceReference(Border.BorderBrushProperty, "Hairline");
        var parent = (DockPanel)TextSize.Parent;
        parent.Children.Remove(TextSize);
        box.Child = TextSize;
        DockPanel.SetDock(box, Dock.Right);
        parent.Children.Insert(0, box);
    }

    private void BuildName()
    {
        var field = Home.PageUi.Field(Tiles.Greeting.GreetingText.FriendlyName(Environment.UserName), S.Name, "Your name");
        void Hint() => NameHint.Text = string.IsNullOrWhiteSpace(field.Text)
            ? $"Greetings use your Windows name, {Settings.SettingsLogic.DisplayName("", Environment.UserName)}. Type another if you like."
            : "Used by the greeting on Home.";
        field.TextChanged += (_, _) =>
        {
            S.Name = field.Text.Trim();
            App.SaveSoon();
            Hint();
        };
        NameHost.Content = field;
        Hint();
    }

    private void BuildStartPage()
    {
        StartPage.Children.Clear();
        var home = App.Data.Pages.FirstOrDefault()?.Name ?? "Home";
        var names = new[] { home }.Concat(MainWindow.BuiltInTabs.Concat(MainWindow.AddOnTabs)
            .Where(t => !S.HiddenTabs.Contains(t.Name, StringComparer.OrdinalIgnoreCase)).Select(t => t.Name)).ToList();
        var current = names.FirstOrDefault(n => n.Equals(S.StartPage, StringComparison.OrdinalIgnoreCase)) ?? home;
        foreach (var n in names)
        {
            var name = n;
            StartPage.Children.Add(Segment(n, n == current, "StartPage", $"Open on {n}", () =>
            {
                if (_loading) return;
                S.StartPage = name;
                App.SaveNow();
            }, 72));
        }
    }

    private void BuildTabSwitches()
    {
        TabSwitches.Children.Clear();
        var tabs = MainWindow.BuiltInTabs.Concat(MainWindow.AddOnTabs).Where(t => t.CanHide).ToList();
        for (int i = 0; i < tabs.Count; i++)
        {
            var tab = tabs[i];
            if (i > 0) TabSwitches.Children.Add(Hairline());
            var text = new StackPanel { Margin = new Thickness(0, 0, 20, 0) };
            text.Children.Add(new TextBlock { Text = tab.Name });
            var caption = new TextBlock { Text = TabBlurb(tab.Name), TextWrapping = TextWrapping.Wrap };
            caption.SetResourceReference(StyleProperty, "Caption");
            caption.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(caption);
            var sw = new CheckBox { Content = text, IsChecked = !S.HiddenTabs.Contains(tab.Name, StringComparer.OrdinalIgnoreCase) };
            sw.SetResourceReference(StyleProperty, "Switch");
            System.Windows.Automation.AutomationProperties.SetName(sw, $"Show {tab.Name}");
            sw.Checked += (_, _) => SetTabVisible(tab.Name, true);
            sw.Unchecked += (_, _) => SetTabVisible(tab.Name, false);
            TabSwitches.Children.Add(sw);
        }
        var note = new TextBlock { Text = "Hiding a tab keeps everything in it. Switch it back on any time.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
        note.SetResourceReference(StyleProperty, "Caption");
        TabSwitches.Children.Add(note);
    }

    private static string TabBlurb(string name) => name switch
    {
        "Study" => "Study time from Stint, by course and task.",
        "Planner" => "Classes, deadlines, exams and events.",
        "Money" => "Spending, budgets and where it goes.",
        "Journal" => "A line or a page a day, with your mood.",
        _ => AddOns.AddOnManager.BlurbFor(name) ?? "",
    };

    private void SetTabVisible(string name, bool visible)
    {
        if (_loading) return;
        S.HiddenTabs.RemoveAll(t => t.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (!visible) S.HiddenTabs.Add(name);
        App.SaveNow();
        Main?.RebuildNav();
        _loading = true;
        BuildStartPage();
        _loading = false;
    }

    private static Border Hairline()
    {
        var b = new Border { Height = 1, Margin = new Thickness(0, 14, 0, 14) };
        b.SetResourceReference(Border.BackgroundProperty, "Hairline");
        return b;
    }

    // ---------------- Window, data, about ----------------

    private void RememberWindow_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        S.RememberWindow = RememberWindow.IsChecked == true;
        if (!S.RememberWindow) S.Window = null;
        App.SaveNow();
    }

    private void AutoUpdate_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        S.AutoUpdate = AutoUpdate.IsChecked == true;
        App.SaveNow();
    }

    private void BackUp_Click(object sender, RoutedEventArgs e)
    {
        App.SaveNow();
        try
        {
            var dir = Path.Combine(Path.GetDirectoryName(App.Store.FilePath)!, "backups");
            Directory.CreateDirectory(dir);
            var target = Path.Combine(dir, $"data-manual-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Copy(App.Store.FilePath, target, overwrite: false);
            Main?.ShowNotice($"Backed up to {target}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Main?.ShowNotice("Couldn't make a backup: " + ex.Message);
        }
    }

    private void OpenData_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{Path.GetDirectoryName(App.Store.FilePath)}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Main?.ShowNotice("Couldn't open the folder: " + ex.Message);
        }
    }

    private void About_Click(object sender, RoutedEventArgs e) => Main?.ShowAbout();

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
