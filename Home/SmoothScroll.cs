using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CommandHub.Home;

/// <summary>
/// Eased vertical scrolling for a ScrollViewer: wheel steps and "scroll to" glide instead of jumping.
/// Runs only while moving (hooks the render loop, then unhooks).
/// </summary>
public sealed class SmoothScroll
{
    private const double Speed = 14; // higher = snappier
    private readonly ScrollViewer _sv;
    private readonly Stopwatch _clock = new();
    private double _target;
    private bool _running;

    public SmoothScroll(ScrollViewer sv)
    {
        _sv = sv;
        _target = sv.VerticalOffset;
        sv.PreviewMouseWheel += (_, e) =>
        {
            // A list or note under the pointer that can still scroll this way keeps the wheel.
            if (e.Handled || InnerCanScroll(e.OriginalSource as System.Windows.DependencyObject, sv, e.Delta)) return;
            e.Handled = true;
            ScrollTo((_running ? _target : sv.VerticalOffset) - e.Delta * 1.1);
        };
        // Dragging the scrollbar or keyboard scrolling moves the offset directly: follow it.
        sv.ScrollChanged += (_, _) => { if (!_running) _target = sv.VerticalOffset; };
        sv.Unloaded += (_, _) => Stop();
    }

    /// <summary>True if a scroll area between <paramref name="source"/> and <paramref name="outer"/> can still move in the wheel's direction.</summary>
    public static bool InnerCanScroll(System.Windows.DependencyObject? source, ScrollViewer outer, int delta)
    {
        for (var node = source; node != null && node != outer;
             node = node is Visual ? VisualTreeHelper.GetParent(node) : System.Windows.LogicalTreeHelper.GetParent(node))
        {
            if (node is ScrollViewer sv && sv.ScrollableHeight > 0)
            {
                bool canUp = sv.VerticalOffset > 0, canDown = sv.VerticalOffset < sv.ScrollableHeight;
                if (delta > 0 ? canUp : canDown) return true;
            }
        }
        return false;
    }

    /// <summary>One animation frame: move part of the way toward the target (frame-rate independent).</summary>
    public static double Step(double current, double target, double seconds)
    {
        if (Math.Abs(target - current) < 0.5) return target;
        double t = 1 - Math.Exp(-Speed * Math.Max(0, seconds));
        double next = current + (target - current) * t;
        return Math.Abs(target - next) < 0.5 ? target : next;
    }

    /// <summary>The welcome header fades to 30% as it scrolls away, so the tiles feel like the next layer.</summary>
    public static double HeaderOpacity(double offset, double headerHeight) =>
        headerHeight <= 0 ? 1 : 1 - 0.7 * Math.Clamp(offset / headerHeight, 0, 1);

    /// <summary>Past half the header, the user is "in the tiles".</summary>
    public static bool InTiles(double offset, double headerHeight) => offset > headerHeight * 0.5;

    public void ScrollTo(double offset)
    {
        _target = Math.Clamp(offset, 0, _sv.ScrollableHeight);
        if (_running) return;
        _running = true;
        _clock.Restart();
        CompositionTarget.Rendering += OnFrame;
    }

    /// <summary>
    /// One frame of a glide. Stops when the target is reached, when the page has become shorter than the
    /// target (it clamps to the end), or when something else moved the offset (the user dragged the scrollbar).
    /// </summary>
    public static (double Offset, bool Done) Frame(double current, double target, double scrollable, double? lastSet, double seconds)
    {
        if (lastSet is { } set && Math.Abs(current - set) > 0.5) return (current, true);
        target = Math.Clamp(target, 0, Math.Max(0, scrollable));
        double next = Step(current, target, seconds);
        return (next, next == target);
    }

    private double? _lastSet;

    private void OnFrame(object? sender, EventArgs e)
    {
        double dt = _clock.Elapsed.TotalSeconds;
        _clock.Restart();
        var (next, done) = Frame(_sv.VerticalOffset, _target, _sv.ScrollableHeight, _lastSet, dt);
        if (next != _sv.VerticalOffset) _sv.ScrollToVerticalOffset(next);
        _lastSet = next;
        if (done) Stop();
    }

    private void Stop()
    {
        if (!_running) return;
        _running = false;
        _lastSet = null;
        _target = _sv.VerticalOffset;
        CompositionTarget.Rendering -= OnFrame;
    }
}
