using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows.Threading;
using Velopack;
using Velopack.Sources;

namespace CommandHub.Services;

public enum UpdateState { NotInstalled, Idle, Checking, UpToDate, Downloading, Ready, Failed }

/// <summary>
/// In-app updates via Velopack from GitHub Releases. The app lives in %LocalAppData%\CommandHub and is
/// replaced on update; your data lives in %AppData%\CommandHub and is never touched by an update
/// (and is copied to backups\ just before one is applied, as a safety net).
/// </summary>
public sealed class Updates
{
    public const string RepoUrl = "https://github.com/Ammar32614/CommandHub";
    private static readonly TimeSpan CheckEvery = TimeSpan.FromHours(4);

    private readonly UpdateManager? _mgr;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(30) };
    private UpdateInfo? _pending;
    private DateTime? _lastCheck;

    public UpdateState State { get; private set; } = UpdateState.Idle;
    public string? ReadyVersion => _pending?.TargetFullRelease.Version.ToString();
    public string? Error { get; private set; }
    public event Action? Changed;

    public Updates()
    {
        try
        {
            // COMMANDHUB_UPDATE_SOURCE points at a local folder of releases (for testing an update end to end).
            _mgr = Environment.GetEnvironmentVariable("COMMANDHUB_UPDATE_SOURCE") is { Length: > 0 } folder
                ? new UpdateManager(folder)
                : new UpdateManager(new GithubSource(RepoUrl, null, false));
        }
        catch (Exception) { _mgr = null; } // never let the updater stop the app from starting
        if (_mgr is null || !_mgr.IsInstalled) State = UpdateState.NotInstalled;
        if (_mgr?.UpdatePendingRestart is { } waiting) { _pending = null; State = UpdateState.Ready; PendingAsset = waiting; }
    }

    private VelopackAsset? PendingAsset { get; set; }

    /// <summary>The running version: the installed release, or the build's own version when run from source.</summary>
    public string CurrentVersion =>
        _mgr?.CurrentVersion?.ToString()
        ?? Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "dev";

    /// <summary>Checks soon after start, then every 4 hours while the app is open.</summary>
    public void StartBackgroundChecks()
    {
        if (State == UpdateState.NotInstalled) return;
        _timer.Tick += async (_, _) => { if (IsCheckDue(_lastCheck, DateTime.UtcNow)) await CheckAsync(); };
        _timer.Start();
        var first = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        first.Tick += async (_, _) => { first.Stop(); await CheckAsync(); };
        first.Start();
    }

    public static bool IsCheckDue(DateTime? lastCheckUtc, DateTime nowUtc) =>
        lastCheckUtc is not { } last || nowUtc - last >= CheckEvery;

    /// <summary>Checks for a newer release and downloads it in the background. Never throws.</summary>
    public async Task CheckAsync()
    {
        if (_mgr is null || State is UpdateState.NotInstalled or UpdateState.Checking or UpdateState.Downloading or UpdateState.Ready) return;
        _lastCheck = DateTime.UtcNow;
        Set(UpdateState.Checking);
        try
        {
            var info = await _mgr.CheckForUpdatesAsync();
            if (info is null) { Set(UpdateState.UpToDate); return; }
            Set(UpdateState.Downloading);
            await _mgr.DownloadUpdatesAsync(info);
            _pending = info;
            Set(UpdateState.Ready);
        }
        catch (Exception ex) // offline, GitHub rate limit, bad download: report, never crash
        {
            Error = ex is HttpRequestException ? "Couldn't reach GitHub. Check your connection." : "Couldn't check for updates right now.";
            Set(UpdateState.Failed);
        }
    }

    /// <summary>Saves everything, copies the data file to backups\, then restarts into the new version.</summary>
    public void ApplyAndRestart()
    {
        if (_mgr is null || State != UpdateState.Ready) return;
        App.FlushSaves();
        App.SaveNow();
        BackupBeforeUpdate(App.Store.FilePath, ReadyVersion ?? PendingAsset?.Version.ToString() ?? "next");
        if (_pending is not null) _mgr.ApplyUpdatesAndRestart(_pending.TargetFullRelease);
        else _mgr.ApplyUpdatesAndRestart(PendingAsset);
    }

    private void Set(UpdateState state)
    {
        State = state;
        if (state != UpdateState.Failed) Error = null;
        Changed?.Invoke();
    }

    /// <summary>
    /// Copies data.json to backups\data-before-&lt;version&gt;-&lt;time&gt;.json, keeping the newest five.
    /// Returns the copy's path, or null if there was nothing to copy or copying failed.
    /// </summary>
    public static string? BackupBeforeUpdate(string dataPath, string version)
    {
        try
        {
            if (!File.Exists(dataPath)) return null;
            var dir = Path.Combine(Path.GetDirectoryName(dataPath)!, "backups");
            Directory.CreateDirectory(dir);
            var safe = string.Concat(version.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            var copy = Path.Combine(dir, $"data-before-{safe}-{DateTime.Now:yyyyMMddHHmmssfff}.json");
            File.Copy(dataPath, copy, overwrite: true);
            foreach (var old in new DirectoryInfo(dir).GetFiles("data-before-*.json").OrderByDescending(f => f.CreationTimeUtc).Skip(5))
                old.Delete();
            return copy;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}
