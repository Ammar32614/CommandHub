using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using CommandHub.Models;

namespace CommandHub.Tiles.Greeting;

public sealed class GreetingTile : ITileType
{
    public string TypeId => "greeting";
    public string DisplayName => "Greeting";
    public string Icon => "";
    public IReadOnlyList<TileSize> AllowedSizes { get; } = [TileSize.Wide, TileSize.Large];
    public TileSize DefaultSize => TileSize.Wide;
    public FrameworkElement CreateView(TileContext ctx) => new GreetingView();
}

/// <summary>"Good evening, Ammar" plus a line for the day. Updates every minute.</summary>
public sealed class GreetingView : UserControl
{
    private readonly TextBlock _hello = TileUi.Display("", 26, weight: FontWeights.SemiBold);
    private readonly TextBlock _line = TileUi.Text("", 13, "Muted");
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly string _name = GreetingText.FriendlyName(Environment.UserName);

    public GreetingView()
    {
        _hello.TextWrapping = TextWrapping.Wrap;
        _line.TextWrapping = TextWrapping.Wrap;
        _line.Margin = new Thickness(0, 8, 0, 0);
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
        stack.Children.Add(_hello);
        stack.Children.Add(_line);
        Content = stack;

        _timer.Tick += (_, _) => Update();
        Loaded += (_, _) => { Update(); _timer.Start(); };
        Unloaded += (_, _) => _timer.Stop();
        Update();
    }

    private void Update()
    {
        var now = DateTime.Now;
        _hello.Inlines.Clear();
        _hello.Inlines.Add(new Run(GreetingText.Salutation(now.Hour) + (_name.Length > 0 ? ", " : "")));
        if (_name.Length > 0)
        {
            var name = new Run(_name);
            name.SetResourceReference(TextElement.ForegroundProperty, "AccentBrush");
            _hello.Inlines.Add(name);
        }
        _line.Text = GreetingText.LineFor(DateOnly.FromDateTime(now));
    }
}
