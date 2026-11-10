using System.IO;
using System.Runtime.InteropServices;

namespace CommandHub.Tiles.SystemPulse;

/// <summary>Cumulative CPU times from GetSystemTimes. Kernel time includes idle time.</summary>
public readonly record struct CpuTimes(long Idle, long Kernel, long User);

public record SystemSnapshot(double MemoryPercent, double MemoryUsedGb, double MemoryTotalGb,
    double DiskPercent, double DiskFreeGb, int? BatteryPercent, bool Charging, TimeSpan Uptime);

/// <summary>Local machine stats via Win32. Nothing leaves the computer.</summary>
public static class SystemStats
{
    public static double CpuPercent(CpuTimes before, CpuTimes after)
    {
        long idle = after.Idle - before.Idle;
        long total = (after.Kernel - before.Kernel) + (after.User - before.User);
        return total <= 0 ? 0 : Math.Clamp((total - idle) * 100.0 / total, 0, 100);
    }

    public static string FormatUptime(TimeSpan t) =>
        t.TotalDays >= 1 ? $"{(int)t.TotalDays}d {t.Hours}h" :
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" : $"{t.Minutes}m";

    public static CpuTimes ReadCpuTimes()
    {
        if (!GetSystemTimes(out long idle, out long kernel, out long user)) return default;
        return new CpuTimes(idle, kernel, user);
    }

    public static SystemSnapshot Sample()
    {
        var mem = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        double memTotal = 0, memUsed = 0, memPct = 0;
        if (GlobalMemoryStatusEx(ref mem))
        {
            memTotal = mem.TotalPhys / 1073741824.0;
            memUsed = (mem.TotalPhys - mem.AvailPhys) / 1073741824.0;
            memPct = mem.MemoryLoad;
        }

        double diskPct = 0, diskFree = 0;
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
            if (drive.IsReady && drive.TotalSize > 0)
            {
                diskFree = drive.AvailableFreeSpace / 1073741824.0;
                diskPct = 100.0 * (drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }

        int? battery = null;
        bool charging = false;
        if (GetSystemPowerStatus(out var power) && power.BatteryFlag != 128 && power.BatteryLifePercent <= 100)
        {
            battery = power.BatteryLifePercent;
            charging = power.ACLineStatus == 1;
        }

        return new SystemSnapshot(memPct, memUsed, memTotal, diskPct, diskFree, battery, charging,
            TimeSpan.FromMilliseconds(Environment.TickCount64));
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerStatus
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out PowerStatus status);
}
