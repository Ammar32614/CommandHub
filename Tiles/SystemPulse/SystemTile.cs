using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using CommandHub.Models;

namespace CommandHub.Tiles.SystemPulse;

public sealed class SystemTile : ITileType
{
    public string TypeId => "system";
    public string DisplayName => "System pulse";
    public string Icon => "";
    public IReadOnlyList<TileSize> AllowedSizes { get; } = [TileSize.Small, TileSize.Tall, TileSize.Wide, TileSize.Large];
    public TileSize DefaultSize => TileSize.Tall;
    public string AccentKey => "Accent4Brush";
    public FrameworkElement CreateView(TileContext ctx) => new SystemView();
}

/// <summary>Live CPU with a sparkline, plus memory, disk, battery and uptime. Samples every 1.5 s while visible.</summary>
public sealed class SystemView : UserControl
{
    private const int History = 40;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private readonly Queue<double> _cpu = new();
    private CpuTimes _last = SystemStats.ReadCpuTimes();

    private readonly TextBlock _cpuText = TileUi.Display("0%", 30, weight: FontWeights.Normal);
    private readonly Polyline _line = new() { StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
    private readonly Polygon _area = new() { Opacity = 0.18 };
    private readonly Canvas _chart = new() { Height = 38, ClipToBounds = true, Margin = new Thickness(0, 4, 0, 12) };
    private readonly StackPanel _rows = new();

    public SystemView()
    {
        _line.SetResourceReference(Shape.StrokeProperty, "Accent4Brush");
        _area.SetResourceReference(Shape.FillProperty, "Accent4Brush");
        _chart.Children.Add(_area);
        _chart.Children.Add(_line);
        _chart.SizeChanged += (_, _) => DrawChart();

        var head = new DockPanel();
        var label = TileUi.Text("CPU", 11, "Muted");
        label.VerticalAlignment = VerticalAlignment.Bottom;
        label.Margin = new Thickness(6, 0, 0, 6);
        head.Children.Add(_cpuText);
        head.Children.Add(label);

        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(_chart);
        stack.Children.Add(_rows);
        Content = stack;

        _timer.Tick += (_, _) => Tick();
        Loaded += (_, _) => { Tick(); _timer.Start(); };
        Unloaded += (_, _) => _timer.Stop();
    }

    private void Tick()
    {
        var now = SystemStats.ReadCpuTimes();
        double cpu = SystemStats.CpuPercent(_last, now);
        _last = now;
        _cpu.Enqueue(cpu);
        while (_cpu.Count > History) _cpu.Dequeue();
        _cpuText.Text = $"{Math.Round(cpu)}%";
        DrawChart();

        var s = SystemStats.Sample();
        _rows.Children.Clear();
        AddRow("Memory", $"{s.MemoryUsedGb:0.0} / {s.MemoryTotalGb:0} GB", s.MemoryPercent / 100, "Accent2Brush");
        AddRow("Disk", $"{s.DiskFreeGb:0} GB free", s.DiskPercent / 100, "Accent3Brush");
        if (s.BatteryPercent is { } b)
            AddRow(s.Charging ? "Battery · charging" : "Battery", $"{b}%", b / 100.0, b <= 20 && !s.Charging ? "Danger" : "AccentBrush");
        var up = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };
        var upValue = TileUi.Text(SystemStats.FormatUptime(s.Uptime), 12, "Text");
        DockPanel.SetDock(upValue, Dock.Right);
        up.Children.Add(upValue);
        up.Children.Add(TileUi.Text("Up for", 12, "Muted"));
        _rows.Children.Add(up);
    }

    private void AddRow(string name, string value, double fraction, string brush)
    {
        var top = new DockPanel();
        var v = TileUi.Text(value, 12, "Text");
        DockPanel.SetDock(v, Dock.Right);
        top.Children.Add(v);
        top.Children.Add(TileUi.Text(name, 12, "Muted"));
        var row = new StackPanel { Margin = new Thickness(0, 0, 0, 9) };
        row.Children.Add(top);
        var bar = TileUi.Bar(fraction, brush);
        bar.Margin = new Thickness(0, 4, 0, 0);
        row.Children.Add(bar);
        _rows.Children.Add(row);
    }

    private void DrawChart()
    {
        double w = _chart.ActualWidth, h = _chart.ActualHeight;
        if (w <= 0 || _cpu.Count < 2) return;
        var values = _cpu.ToArray();
        double step = w / (History - 1);
        double x0 = w - step * (values.Length - 1);
        var pts = new PointCollection();
        for (int i = 0; i < values.Length; i++)
            pts.Add(new Point(x0 + i * step, h - 2 - (h - 4) * values[i] / 100));
        _line.Points = pts;
        var area = new PointCollection(pts) { new Point(w, h), new Point(x0, h) };
        _area.Points = area;
    }
}
