using System.Windows;
using System.Windows.Controls;
using CommandHub.Models;
using CommandHub.Services;
using CommandHub.Home;
using CommandHub.Views;
using Page = CommandHub.Models.Page;

namespace CommandHub;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowEffects.ApplyRoundedCorners(this, dark: !App.Theme.IsLight);
        StateChanged += (_, _) => UpdateMaximized();
        Closing += (_, _) => App.FlushSaves();
        BuildNav();
    }

    private void BuildNav()
    {
        PageNav.Children.Clear();
        foreach (var page in App.Data.Pages)
        {
            var item = new RadioButton
            {
                GroupName = "Nav",
                Style = (Style)FindResource("NavItem"),
                Content = NavContent(page.Icon, page.Name),
                Margin = new Thickness(0, 0, 0, 2),
            };
            System.Windows.Automation.AutomationProperties.SetName(item, page.Name);
            item.Checked += (_, _) => ShowPage(page);
            PageNav.Children.Add(item);

            // Study sits directly below Home.
            if (page == App.Data.Pages[0])
            {
                var study = new RadioButton
                {
                    GroupName = "Nav",
                    Style = (Style)FindResource("NavItem"),
                    Content = NavContent("", "Study"),
                    Margin = new Thickness(0, 0, 0, 2),
                };
                System.Windows.Automation.AutomationProperties.SetName(study, "Study");
                study.Checked += (_, _) => ShowStudy();
                PageNav.Children.Add(study);
            }
        }
        if (PageNav.Children.Count > 0) ((RadioButton)PageNav.Children[0]).IsChecked = true;
    }

    private static StackPanel NavContent(string icon, string name)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var glyph = new TextBlock { Text = icon, FontFamily = (System.Windows.Media.FontFamily)Application.Current.FindResource("IconFont"), FontSize = 13, Width = 22, VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        sp.Children.Add(glyph);
        sp.Children.Add(new TextBlock { Text = name, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        return sp;
    }

    /// <summary>The first page is Home: a welcome header with the tiles below it. Other pages are just their board.</summary>
    public void ShowPage(Page page)
    {
        HideSheet();
        PageHost.Content = page == App.Data.Pages.FirstOrDefault()
            ? new HomeView(page)
            : new BoardView(page) { Margin = new Thickness(0, 40, 0, 0) };
    }

    public void ShowSettings() => SettingsNav.IsChecked = true;

    private void ShowStudy()
    {
        HideSheet();
        App.CommitOpenEdits(); // leaving Home: let any open tile editor save first
        PageHost.Content = new Study.StudyPage();
    }

    // ---------------- Sheet and toast ----------------

    public void ShowSheet(UIElement content)
    {
        Sheet.Content = content;
        Sheet.Visibility = Visibility.Visible;
    }

    public void HideSheet()
    {
        Sheet.Content = null;
        Sheet.Visibility = Visibility.Collapsed;
    }

    private readonly System.Windows.Threading.DispatcherTimer _toastTimer = new();
    private Action? _toastAction;

    /// <summary>A message with one action button (e.g. "Tile removed · Undo") that hides itself after a while.</summary>
    public void ShowToast(string text, string actionLabel, Action action, TimeSpan duration)
    {
        ToastText.Text = text;
        ToastAction.Content = actionLabel;
        System.Windows.Automation.AutomationProperties.SetName(ToastAction, actionLabel);
        _toastAction = action;
        Toast.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Interval = duration;
        _toastTimer.Tick -= ToastTimeout;
        _toastTimer.Tick += ToastTimeout;
        _toastTimer.Start();
    }

    public void HideToast()
    {
        _toastTimer.Stop();
        _toastAction = null;
        Toast.Visibility = Visibility.Collapsed;
    }

    private void ToastTimeout(object? sender, EventArgs e) => HideToast();

    private void ToastAction_Click(object sender, RoutedEventArgs e)
    {
        var action = _toastAction;
        HideToast();
        action?.Invoke();
    }

    private void Settings_Checked(object sender, RoutedEventArgs e)
    {
        HideSheet();
        PageHost.Content = new SettingsPage { Margin = new Thickness(0, 40, 0, 0) };
    }

    // ---------------- Notice banner ----------------

    public void ShowNotice(string text)
    {
        NoticeText.Text = text;
        Notice.Visibility = Visibility.Visible;
    }

    private void DismissNotice_Click(object sender, RoutedEventArgs e) => Notice.Visibility = Visibility.Collapsed;

    // ---------------- Window chrome ----------------

    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
    }

    /// <summary>A borderless maximized window overhangs the screen edges; pad it back in.</summary>
    private void UpdateMaximized()
    {
        bool max = WindowState == WindowState.Maximized;
        Root.Margin = max ? new Thickness(7) : new Thickness(0);
        MaxBtn.Content = max ? "" : "";
        MaxBtn.ToolTip = max ? "Restore" : "Maximize";
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
