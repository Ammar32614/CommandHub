using System.Windows.Threading;

namespace CommandHub.Services;

/// <summary>
/// Runs an action once things go quiet: each <see cref="Request"/> restarts the wait.
/// <see cref="Flush"/> runs a pending action right away (used when the app closes).
/// </summary>
public sealed class Debouncer
{
    private readonly DispatcherTimer _timer;
    private readonly Action _action;

    public Debouncer(TimeSpan delay, Action action)
    {
        _action = action;
        _timer = new DispatcherTimer { Interval = delay };
        _timer.Tick += (_, _) => Flush();
    }

    public bool IsPending => _timer.IsEnabled;

    public void Request()
    {
        _timer.Stop();
        _timer.Start();
    }

    public void Flush()
    {
        if (!_timer.IsEnabled) return;
        _timer.Stop();
        _action();
    }
}
