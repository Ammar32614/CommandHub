using System.Windows;
using System.Windows.Threading;
using CommandHub.AddOns;

namespace CommandHub.AddOn.Media;

/// <summary>The Media add-on: lists of what you watch, refreshed once a day for new episodes.</summary>
public sealed class MediaAddOn : IAddOn
{
    private Library? _library;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(30) };

    public void Start(IAddOnHost host)
    {
        _library = new Library(host);
        _library.Load();
        _timer.Tick += async (_, _) => await RefreshIfDue();
        _timer.Start();
        var first = new DispatcherTimer { Interval = TimeSpan.FromSeconds(90) };
        first.Tick += async (_, _) => { first.Stop(); await RefreshIfDue(); };
        first.Start();
    }

    private async Task RefreshIfDue()
    {
        if (_library is null || _library.Data.Items.Count == 0) return;
        if (_library.Data.LastCheckUtc is { } last && DateTime.UtcNow - last < TimeSpan.FromHours(20)) return;
        try { await _library.Refresh(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { } // never let a background refresh take Command hub down
    }

    public UIElement CreatePage() => new MediaPage(_library!);
}
