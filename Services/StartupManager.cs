using Microsoft.Win32;

namespace CommandHub.Services;

/// <summary>Per-user "start when I sign in" via the HKCU Run key. No admin rights needed.</summary>
public class StartupManager(string runKeyPath, string exePath)
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "CommandHub";
    public const string MinimizedFlag = "--minimized";

    public static StartupManager ForCurrentApp() => new(RunKey, Environment.ProcessPath ?? "");

    private string Command => $"\"{exePath}\" {MinimizedFlag}";

    /// <summary>True only when the entry points at this exe (an old install location doesn't count).</summary>
    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(runKeyPath);
            return key?.GetValue(ValueName) is string v && string.Equals(v, Command, StringComparison.OrdinalIgnoreCase);
        }
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(runKeyPath);
        if (enabled) key.SetValue(ValueName, Command);
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
