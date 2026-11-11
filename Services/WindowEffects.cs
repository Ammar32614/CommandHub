using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CommandHub.Services;

public static class WindowEffects
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Win11 rounded corners + dark frame. Silently does nothing on older Windows.</summary>
    public static void ApplyRoundedCorners(Window window, bool dark = true)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        int round = DWMWCP_ROUND, darkFlag = dark ? 1 : 0;
        try
        {
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkFlag, sizeof(int));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    // ---------------- Maximise within the work area ----------------

    private const int WM_GETMINMAXINFO = 0x0024;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MINMAXINFO { public POINT Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
    [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int Size; public RECT Monitor, Work; public uint Flags; }

    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    /// <summary>
    /// A window without the standard frame maximises over the whole screen, so the taskbar covers its bottom edge.
    /// This makes it maximise into the monitor's work area instead (the screen minus the taskbar).
    /// </summary>
    public static void KeepMaximizedInWorkArea(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        HwndSource.FromHwnd(hwnd)?.AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
        {
            if (msg != WM_GETMINMAXINFO) return IntPtr.Zero;
            var monitor = MonitorFromWindow(h, MONITOR_DEFAULTTONEAREST);
            var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return IntPtr.Zero;
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(l);
            mmi.MaxPosition.X = info.Work.Left - info.Monitor.Left;
            mmi.MaxPosition.Y = info.Work.Top - info.Monitor.Top;
            mmi.MaxSize.X = info.Work.Right - info.Work.Left;
            mmi.MaxSize.Y = info.Work.Bottom - info.Work.Top;
            // An auto-hidden taskbar needs a one-pixel gap to be able to pop up.
            if (info.Work.Bottom == info.Monitor.Bottom && info.Work.Top == info.Monitor.Top) mmi.MaxSize.Y -= 1;
            // Handling the message stops WPF's own handler, which is what applies MinWidth/MinHeight: do that here.
            var toDevice = PresentationSource.FromVisual(window)?.CompositionTarget?.TransformToDevice;
            double sx = toDevice?.M11 ?? 1, sy = toDevice?.M22 ?? 1;
            if (window.MinWidth > 0) mmi.MinTrackSize.X = (int)Math.Ceiling(window.MinWidth * sx);
            if (window.MinHeight > 0) mmi.MinTrackSize.Y = (int)Math.Ceiling(window.MinHeight * sy);
            Marshal.StructureToPtr(mmi, l, true);
            handled = true;
            return IntPtr.Zero;
        });
    }

    [DllImport("user32.dll")] private static extern IntPtr MonitorFromRect(ref RECT rect, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForSystem();

    /// <summary>
    /// True when the top strip of a window at this position (in device-independent pixels) is on some monitor,
    /// so a window restored there can be grabbed and moved. Monitors of different sizes leave gaps inside the
    /// overall screen area; this catches those too.
    /// </summary>
    public static bool TitleBarIsOnAMonitor(double left, double top, double width)
    {
        try
        {
            double scale = GetDpiForSystem() / 96.0;
            var strip = new RECT
            {
                Left = (int)((left + 40) * scale), Top = (int)(top * scale),
                Right = (int)((left + Math.Max(80, width - 140)) * scale), Bottom = (int)((top + 32) * scale),
            };
            return MonitorFromRect(ref strip, 0 /* MONITOR_DEFAULTTONULL */) != IntPtr.Zero;
        }
        catch (EntryPointNotFoundException) { return true; } // very old Windows: trust the simpler check
    }
}
