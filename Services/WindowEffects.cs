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
}
