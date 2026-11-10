using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CommandHub.Tiles.Todo;

public partial class TodoTileView : UserControl
{
    private readonly TileContext _ctx;
    private readonly List<TodoItem> _items;

    public TodoTileView(TileContext ctx)
    {
        InitializeComponent();
        _ctx = ctx;
        _items = TodoContent.Read(ctx.Content);
        AddBox.KeyDown += AddBox_KeyDown;
        Rebuild();
    }

    private void AddBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (!TodoContent.TryAdd(_items, AddBox.Text)) return;
        AddBox.Clear();
        Commit();
    }

    private void Commit()
    {
        TodoContent.Write(_ctx.Content, _items);
        _ctx.SaveNow();
        Rebuild();
    }

    private void Rebuild()
    {
        List.Children.Clear();
        EmptyText.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        for (int i = 0; i < _items.Count; i++)
        {
            int index = i;
            var item = _items[i];

            var check = new CheckBox
            {
                IsChecked = item.Done,
                Style = (Style)FindResource("TodoCheck"),
                Content = new TextBlock
                {
                    Text = item.Text, TextWrapping = TextWrapping.Wrap, FontSize = 13,
                    TextDecorations = item.Done ? TextDecorations.Strikethrough : null,
                },
            };
            System.Windows.Automation.AutomationProperties.SetName(check, item.Text);
            // Checked/Unchecked (not Click) so keyboard and accessibility toggles are saved too.
            RoutedEventHandler toggled = (_, _) => { _items[index] = item with { Done = check.IsChecked == true }; Commit(); };
            check.Checked += toggled;
            check.Unchecked += toggled;

            var delete = new Button
            {
                Style = (Style)FindResource("IconButton"), Content = "", FontSize = 9,
                Width = 22, Height = 22, ToolTip = "Delete", Opacity = 0,
                VerticalAlignment = VerticalAlignment.Top,
            };
            System.Windows.Automation.AutomationProperties.SetName(delete, $"Delete {item.Text}");
            delete.Click += (_, _) => { _items.RemoveAt(index); Commit(); };

            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4), Background = System.Windows.Media.Brushes.Transparent };
            DockPanel.SetDock(delete, Dock.Right);
            row.Children.Add(delete);
            row.Children.Add(check);
            // Shown on hover or keyboard focus; never hidden outright, so keyboard and screen readers can reach it.
            void UpdateDelete() => delete.Opacity = row.IsMouseOver || row.IsKeyboardFocusWithin ? 1 : 0;
            row.MouseEnter += (_, _) => UpdateDelete();
            row.MouseLeave += (_, _) => UpdateDelete();
            row.IsKeyboardFocusWithinChanged += (_, _) => UpdateDelete();
            List.Children.Add(row);
        }
    }
}
