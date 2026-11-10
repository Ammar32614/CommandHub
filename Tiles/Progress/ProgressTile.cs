using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CommandHub.Controls;
using CommandHub.Models;

namespace CommandHub.Tiles.Progress;

public sealed class ProgressTile : ITileType
{
    public string TypeId => "progress";
    public string DisplayName => "Time progress";
    public string Icon => "";
    public IReadOnlyList<TileSize> AllowedSizes { get; } = [TileSize.Wide, TileSize.Large];
    public TileSize DefaultSize => TileSize.Wide;
    public string AccentKey => "Accent3Brush";
    public FrameworkElement CreateView(TileContext ctx) => new ProgressView();
}

/// <summary>Four rings: how much of the day, week, month and year has gone.</summary>
public sealed class ProgressView : UserControl
{
    private static readonly (string Label, string Brush, Func<DateTime, double> Of)[] Rings =
    [
        ("Day", "AccentBrush", TimeProgress.Day),
        ("Week", "Accent2Brush", TimeProgress.Week),
        ("Month", "Accent3Brush", TimeProgress.Month),
        ("Year", "Accent4Brush", TimeProgress.Year),
    ];

    private readonly List<(ProgressRing Ring, TextBlock Percent)> _parts = [];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(1) };

    public ProgressView()
    {
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (label, brush, _) in Rings)
        {
            var ring = new ProgressRing { Width = 58, Height = 58, Thickness = 6 };
            ring.SetResourceReference(ProgressRing.ArcBrushProperty, brush);
            ring.SetResourceReference(ProgressRing.TrackBrushProperty, "Raised");
            var percent = TileUi.Text("", 12, "Text", semibold: true);
            percent.HorizontalAlignment = HorizontalAlignment.Center;
            percent.VerticalAlignment = VerticalAlignment.Center;
            var holder = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
            holder.Children.Add(ring);
            holder.Children.Add(percent);

            var caption = TileUi.Text(label, 11, "Muted");
            caption.HorizontalAlignment = HorizontalAlignment.Center;
            caption.Margin = new Thickness(0, 6, 0, 0);

            var column = new StackPanel { Margin = new Thickness(8, 0, 8, 0) };
            column.Children.Add(holder);
            column.Children.Add(caption);
            grid.Children.Add(column);
            _parts.Add((ring, percent));
        }
        Content = new Viewbox { Child = grid, StretchDirection = StretchDirection.DownOnly };

        _timer.Tick += (_, _) => Update();
        Loaded += (_, _) => { Update(); _timer.Start(); };
        Unloaded += (_, _) => _timer.Stop();
        Update();
    }

    private void Update()
    {
        var now = DateTime.Now;
        for (int i = 0; i < Rings.Length; i++)
        {
            double p = Rings[i].Of(now);
            _parts[i].Ring.Progress = p;
            _parts[i].Percent.Text = $"{(int)Math.Floor(p * 100)}%";
        }
    }
}
