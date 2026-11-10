using System.Windows;
using System.Windows.Controls;
using CommandHub.Models;
using CommandHub.Services;
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
        }
        if (PageNav.Children.Count > 0) ((RadioButton)PageNav.Children[0]).IsChecked = true;
    }

    private static StackPanel NavContent(string icon, string name)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new TextBlock { Text = icon, FontFamily = (System.Windows.Media.FontFamily)Application.Current.FindResource("IconFont"), FontSize = 14, Width = 20, VerticalAlignment = VerticalAlignment.Center });
        sp.Children.Add(new TextBlock { Text = name, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        return sp;
    }

    public void ShowPage(Page page) => PageHost.Content = new BoardView(page);

    public void ShowSettings() => SettingsNav.IsChecked = true;

    private void Settings_Checked(object sender, RoutedEventArgs e) => PageHost.Content = new SettingsPage();

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
