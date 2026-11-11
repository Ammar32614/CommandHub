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
        SourceInitialized += (_, _) =>
        {
            WindowEffects.ApplyRoundedCorners(this, dark: !App.Theme.IsLight);
            WindowEffects.KeepMaximizedInWorkArea(this);
        };
        StateChanged += (_, _) =>
        {
            UpdateMaximized();
            if (WindowState != WindowState.Minimized) _wasMaximized = WindowState == WindowState.Maximized;
        };
        Closing += (_, _) => { RememberPlacement(); App.FlushSaves(); };
        RestorePlacement();
        ApplyTextScale();
        BuildNav();
    }

    // ---------------- Text size and window position ----------------

    /// <summary>Scales the whole window (Settings, Text size).</summary>
    public void ApplyTextScale()
    {
        double s = Settings.SettingsLogic.SnapScale(App.Data.Settings.TextScale);
        Root.LayoutTransform = s == 1.0 ? null : new System.Windows.Media.ScaleTransform(s, s);
    }

    private void RestorePlacement()
    {
        if (!App.Data.Settings.RememberWindow) return;
        var screen = (SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (Settings.SettingsLogic.FitOnScreen(App.Data.Settings.Window, screen) is not { } p) return;
        if (!WindowEffects.TitleBarIsOnAMonitor(p.Left, p.Top, p.Width)) return; // that monitor is gone
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = p.Left; Top = p.Top; Width = p.Width; Height = p.Height;
        if (p.Maximized) { WindowState = WindowState.Maximized; _wasMaximized = true; }
    }

    private bool _wasMaximized;

    private void RememberPlacement()
    {
        if (!App.Data.Settings.RememberWindow || WindowState == WindowState.Minimized && RestoreBounds.IsEmpty) return;
        var r = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (r.IsEmpty) return;
        // Closed while minimised: remember whether it was maximised before that.
        bool maximized = WindowState == WindowState.Minimized ? _wasMaximized : WindowState == WindowState.Maximized;
        App.Data.Settings.Window = new Models.WindowPlacement { Left = r.Left, Top = r.Top, Width = r.Width, Height = r.Height, Maximized = maximized };
        App.SaveSoon(); // flushed right after, as the window closes
    }

    /// <summary>A sidebar tab other than a board page: the built-in ones, and later ones add-ons bring.</summary>
    public sealed record HubTab(string Name, string Icon, Func<UIElement> Create, bool CanHide = true);

    /// <summary>The built-in tabs, in sidebar order (right below Home).</summary>
    public static readonly List<HubTab> BuiltInTabs =
    [
        new("Study", "", () => new Study.StudyPage()),
        new("Planner", "", () => new Planner.PlannerPage()),
        new("Money", "", () => new Money.MoneyPage()),
        new("Journal", "", () => new Journal.JournalPage()),
    ];

    /// <summary>Tabs added by installed add-ons (shown after the built-in ones).</summary>
    public static readonly List<HubTab> AddOnTabs = [];

    private readonly Dictionary<string, RadioButton> _tabNav = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Rebuilds the sidebar (after Settings hides or shows a tab). Keeps Settings selected.</summary>
    public void RebuildNav() => BuildNav(selectStart: false);

    private void BuildNav(bool selectStart = true)
    {
        PageNav.Children.Clear();
        _tabNav.Clear();
        _plannerNav = null;
        var hidden = App.Data.Settings.HiddenTabs;
        foreach (var page in App.Data.Pages)
        {
            var item = NavItem(page.Icon, page.Name);
            item.Checked += (_, _) => ShowPage(page);
            PageNav.Children.Add(item);
            _tabNav.TryAdd(page.Name, item);

            // The tabs sit directly below Home.
            if (page != App.Data.Pages[0]) continue;
            foreach (var tab in BuiltInTabs.Concat(AddOnTabs))
            {
                if (tab.CanHide && hidden.Contains(tab.Name, StringComparer.OrdinalIgnoreCase)) continue;
                var nav = NavItem(tab.Icon, tab.Name);
                var captured = tab;
                nav.Checked += (_, _) => OpenTab(captured);
                PageNav.Children.Add(nav);
                _tabNav[tab.Name] = nav;
                if (tab.Name == "Planner") _plannerNav = nav;
            }
        }
        if (!selectStart) return;
        var start = _tabNav.GetValueOrDefault(App.Data.Settings.StartPage) ?? PageNav.Children.OfType<RadioButton>().FirstOrDefault();
        if (start != null) start.IsChecked = true;
    }

    private RadioButton NavItem(string icon, string name)
    {
        var item = new RadioButton
        {
            GroupName = "Nav",
            Style = (Style)FindResource("NavItem"),
            Content = NavContent(icon, name),
            Margin = new Thickness(0, 0, 0, 2),
        };
        System.Windows.Automation.AutomationProperties.SetName(item, name);
        return item;
    }

    private void OpenTab(HubTab tab)
    {
        HideSheet();
        App.CommitOpenEdits(); // leaving Home: let any open tile editor save first
        try
        {
            PageHost.Content = tab.Create();
        }
        catch (Exception ex)
        {
            // An add-on's page failing to build mustn't take the whole app down.
            PageHost.Content = null;
            ShowNotice($"{tab.Name} couldn't open: {ex.Message}");
        }
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

    // ---------------- Updates ----------------

    /// <summary>Shows the sidebar card when an update has been downloaded; Settings shows the rest.</summary>
    public void ShowUpdateState()
    {
        var u = App.Updater;
        bool ready = u.State == UpdateState.Ready;
        UpdateCard.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
        UpdateText.Text = ready ? $"Version {u.ReadyVersion ?? "new"} is downloaded. Your data stays as it is." : "";
        (PageHost.Content as SettingsPage)?.RefreshUpdates();
    }

    private void RestartToUpdate_Click(object sender, RoutedEventArgs e) => App.Updater.ApplyAndRestart();

    private RadioButton? _plannerNav;

    /// <summary>Selects Planner in the sidebar (used by the "Up next" tile). Opens it even when the tab is hidden.</summary>
    public void ShowPlanner()
    {
        var planner = BuiltInTabs.First(t => t.Name == "Planner");
        if (_plannerNav is null || _plannerNav.IsChecked == true) OpenTab(planner);
        else _plannerNav.IsChecked = true;
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

    private void Settings_Checked(object sender, RoutedEventArgs e) => ShowSettingsPage();

    public void ShowSettingsPage()
    {
        HideSheet();
        App.CommitOpenEdits(); // leaving Home: let any open tile editor save first
        PageHost.Content = new SettingsPage { Margin = new Thickness(0, 40, 0, 0) };
    }

    /// <summary>Opens About (from Settings); Settings stays selected in the sidebar.</summary>
    public void ShowAbout()
    {
        HideSheet();
        PageHost.Content = new AboutPage();
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
        Root.Margin = new Thickness(0); // maximising already stops at the taskbar (see KeepMaximizedInWorkArea)
        MaxBtn.Content = max ? "" : "";
        MaxBtn.ToolTip = max ? "Restore" : "Maximize";
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
