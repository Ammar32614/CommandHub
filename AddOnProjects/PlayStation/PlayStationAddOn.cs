using System.Windows;
using System.Windows.Threading;
using CommandHub.AddOns;

namespace CommandHub.AddOn.PlayStation;

/// <summary>The PlayStation add-on: loaded by Command hub, checks wishlist prices once a day in the background.</summary>
public sealed class PlayStationAddOn : IAddOn
{
    private Tracker? _tracker;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(30) };

    public void Start(IAddOnHost host)
    {
        _tracker = new Tracker(host);
        _tracker.Load();
        _timer.Tick += async (_, _) => await CheckIfDue();
        _timer.Start();
        // First check a minute after starting, so it never slows Command hub's launch.
        var first = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        first.Tick += async (_, _) => { first.Stop(); await CheckIfDue(); };
        first.Start();
    }

    private async Task CheckIfDue()
    {
        if (_tracker is null || _tracker.Data.Wishlist.Count == 0 || !WishLogic.IsCheckDue(_tracker.Data.LastCheckUtc, DateTime.UtcNow)) return;
        try { await _tracker.CheckAll(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { } // never let a background check take Command hub down
    }

    public UIElement CreatePage() => new PlayStationPage(_tracker!);
}
