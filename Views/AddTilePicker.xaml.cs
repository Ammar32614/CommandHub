using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommandHub.Tiles;

namespace CommandHub.Views;

/// <summary>A sheet listing every tile type. Raises <see cref="Picked"/> or <see cref="Closed"/>.</summary>
public partial class AddTilePicker : UserControl
{
    public event Action<ITileType>? Picked;
    public event Action? Closed;

    public AddTilePicker()
    {
        InitializeComponent();
        foreach (var type in TileRegistry.All)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock
            {
                Text = type.Icon, FontFamily = (System.Windows.Media.FontFamily)FindResource("IconFont"), FontSize = 15,
                Width = 26, VerticalAlignment = VerticalAlignment.Center, Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush"),
            });
            row.Children.Add(new TextBlock { Text = type.DisplayName, VerticalAlignment = VerticalAlignment.Center });

            var button = new Button { Style = (Style)FindResource("MenuRowButton"), Content = row };
            System.Windows.Automation.AutomationProperties.SetName(button, $"Add {type.DisplayName}");
            button.Click += (_, _) => Picked?.Invoke(type);
            Types.Children.Add(button);
        }
        Loaded += (_, _) => (Types.Children.Count > 0 ? Types.Children[0] : this).Focus();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Closed?.Invoke(); } };
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Closed?.Invoke();

    private void Scrim_MouseDown(object sender, MouseButtonEventArgs e) => Closed?.Invoke();

    private void Card_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;
}
