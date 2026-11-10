using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace CommandHub.Tiles.Links;

public partial class LinksTileView : UserControl
{
    private readonly TileContext _ctx;
    private List<QuickLink> _links;
    private readonly DispatcherTimer _errorTimer = new() { Interval = TimeSpan.FromSeconds(4) };

    public LinksTileView(TileContext ctx)
    {
        InitializeComponent();
        _ctx = ctx;
        _links = LinksContent.Read(ctx.Content);
        _errorTimer.Tick += (_, _) => { _errorTimer.Stop(); ErrorText.Visibility = Visibility.Collapsed; };
        // The board rebuilds tiles (edit mode, add, remove, page switch) and the app can close:
        // either way, keep half-finished edits.
        Loaded += (_, _) => App.CommittingEdits += CommitIfEditing;
        Unloaded += (_, _) =>
        {
            _errorTimer.Stop();
            App.CommittingEdits -= CommitIfEditing;
            CommitIfEditing();
        };
        ShowLinks();
    }

    private static string GlyphFor(LinkKind kind) => kind switch
    {
        LinkKind.Web => "\uE774",
        LinkKind.Folder => "\uE8B7",
        LinkKind.File => "\uE8A5",
        _ => "\uE783",
    };

    private void ShowLinks()
    {
        EditPanel.Visibility = Visibility.Collapsed;
        ShowPanel.Visibility = Visibility.Visible;
        EditBtn.Visibility = Visibility.Visible;
        EmptyPanel.Visibility = _links.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        LinkButtons.Children.Clear();
        foreach (var link in _links)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock
            {
                Text = GlyphFor(LinkTarget.Guess(link.Target)), FontFamily = (System.Windows.Media.FontFamily)FindResource("IconFont"),
                FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0),
            });
            content.Children.Add(new TextBlock { Text = link.Name.Length > 0 ? link.Name : link.Target, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 180, TextTrimming = TextTrimming.CharacterEllipsis });

            var button = new Button
            {
                Style = (Style)FindResource("ChipButton"), Content = content, ToolTip = link.Target,
                Margin = new Thickness(0, 0, 6, 6), Foreground = (System.Windows.Media.Brush)FindResource("Text"),
            };
            System.Windows.Automation.AutomationProperties.SetName(button, link.Name.Length > 0 ? link.Name : link.Target);
            button.Click += (_, _) =>
            {
                if (!LinkTarget.TryOpen(link.Target, out var error)) ShowError(error);
            };
            LinkButtons.Children.Add(button);
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
        _errorTimer.Stop();
        _errorTimer.Start();
    }

    // ---------------- Editing ----------------

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        Rows.Children.Clear();
        foreach (var link in _links) AddRow(link.Name, link.Target);
        if (_links.Count == 0) AddRow("", "");
        ShowPanel.Visibility = Visibility.Collapsed;
        EditBtn.Visibility = Visibility.Collapsed;
        EditPanel.Visibility = Visibility.Visible;
    }

    private void AddRow_Click(object sender, RoutedEventArgs e) => AddRow("", "");

    private void AddRow(string name, string target)
    {
        var fieldStyle = (Style)FindResource("FieldBox");
        var nameBox = new TextBox { Style = fieldStyle, Tag = "Name", Text = name, Width = 110, Padding = new Thickness(8, 4, 8, 4), FontSize = 12 };
        var targetBox = new TextBox { Style = fieldStyle, Tag = "https://… or C:\\folder", Text = target, Padding = new Thickness(8, 4, 8, 4), FontSize = 12, Margin = new Thickness(6, 0, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetName(nameBox, "Link name");
        System.Windows.Automation.AutomationProperties.SetName(targetBox, "Link target");
        var remove = new Button
        {
            Style = (Style)FindResource("IconButton"), Content = "\uE711", FontSize = 9, Width = 24, Height = 24,
            ToolTip = "Remove link", Margin = new Thickness(4, 0, 0, 0),
        };
        System.Windows.Automation.AutomationProperties.SetName(remove, "Remove link");

        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(nameBox, Dock.Left);
        DockPanel.SetDock(remove, Dock.Right);
        row.Children.Add(nameBox);
        row.Children.Add(remove);
        row.Children.Add(targetBox);
        remove.Click += (_, _) => Rows.Children.Remove(row);
        Rows.Children.Add(row);
    }

    private void Done_Click(object sender, RoutedEventArgs e)
    {
        CommitEdits();
        ShowLinks();
    }

    private void CommitIfEditing()
    {
        // Save and close: an editor left open would later write its (now stale) rows over newer content.
        if (EditPanel.Visibility != Visibility.Visible) return;
        CommitEdits();
        ShowLinks();
    }

    private void CommitEdits()
    {
        _links = Rows.Children.OfType<DockPanel>()
            .Select(r => r.Children.OfType<TextBox>().ToArray())
            .Select(b => new QuickLink(b[0].Text.Trim(), b[1].Text.Trim()))
            .Where(l => l.Target.Length > 0)
            .ToList();
        LinksContent.Write(_ctx.Content, _links);
        _ctx.SaveNow();
    }
}
