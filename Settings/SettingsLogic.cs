using CommandHub.Models;
using CommandHub.Tiles.Greeting;

namespace CommandHub.Settings;

/// <summary>Pure rules behind the Settings page.</summary>
public static class SettingsLogic
{
    /// <summary>Text size steps offered in Settings.</summary>
    public static readonly double[] Scales = [0.9, 1.0, 1.1, 1.25];

    /// <summary>The name greetings use: what you typed in Settings, else the first part of your Windows user name.</summary>
    public static string DisplayName(string? typed, string windowsUser) =>
        string.IsNullOrWhiteSpace(typed) ? GreetingText.FriendlyName(windowsUser) : typed.Trim();

    /// <summary>The nearest offered text size (1.0 for nonsense).</summary>
    public static double SnapScale(double saved) =>
        double.IsFinite(saved) ? Scales.MinBy(s => Math.Abs(s - saved)) : 1.0;

    /// <summary>
    /// The saved window position if it's still usable on this screen: a sensible size, and its title bar
    /// reachable (so a monitor that's been unplugged can't leave the window somewhere you can't see). Else null.
    /// </summary>
    public static WindowPlacement? FitOnScreen(WindowPlacement? p, (double L, double T, double W, double H) screen)
    {
        if (p is null) return null;
        if (!double.IsFinite(p.Left) || !double.IsFinite(p.Top) || !double.IsFinite(p.Width) || !double.IsFinite(p.Height)) return null;
        if (p.Width < 400 || p.Height < 300) return null;
        // At least 120 px of the title bar must be on screen, and its top edge can't be above the screen.
        double visibleWidth = Math.Min(p.Left + p.Width, screen.L + screen.W) - Math.Max(p.Left, screen.L);
        if (visibleWidth < 120) return null;
        if (p.Top < screen.T || p.Top > screen.T + screen.H - 40) return null;
        return p;
    }
}
