using System.Windows.Controls;
using System.Windows.Threading;

namespace CommandHub.Tiles.Clock;

public partial class ClockTileView : UserControl
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    public ClockTileView()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => Update();
        Loaded += (_, _) => { Update(); _timer.Start(); };
        Unloaded += (_, _) => _timer.Stop();
        Update();
    }

    private void Update()
    {
        var now = DateTime.Now;
        TimeText.Text = now.ToString("t");
        DateText.Text = now.ToString("dddd, MMMM d");
    }
}
