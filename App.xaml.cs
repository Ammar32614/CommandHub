using System.IO;
using System.Windows;
using System.Windows.Media;
using CommandHub.Models;
using CommandHub.Services;
using Velopack;

namespace CommandHub;

public partial class App : Application
{
    public static DataStore Store { get; private set; } = null!;
    public static HubData Data { get; private set; } = null!;
    public static Updates Updater { get; private set; } = null!;

    /// <summary>
    /// Entry point. Velopack must run first: during install, update and uninstall it starts the app
    /// with special arguments, handles them and exits before any window is created.
    /// </summary>
    [STAThread]
    private static void Main(string[] args)
    {
        VelopackApp.Build().Run();
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    private Mutex? _instanceMutex;
    private EventWaitHandle? _activateSignal;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // COMMANDHUB_DATA points the app at a different file (testing, demo data).
        var customData = Environment.GetEnvironmentVariable("COMMANDHUB_DATA") is { Length: > 0 } p ? p : null;

        // One copy per data file, so two windows never fight over the same data.json. A second launch brings the first forward.
        // A copy using a different file (COMMANDHUB_DATA) is a separate instance.
        var instance = customData is null ? "CommandHub" : "CommandHub." + StableHash(Path.GetFullPath(customData).ToUpperInvariant());
        _instanceMutex = new Mutex(true, instance + ".SingleInstance", out bool first);
        _activateSignal = new EventWaitHandle(false, EventResetMode.AutoReset, instance + ".Activate");
        if (!first)
        {
            _activateSignal.Set();
            Shutdown();
            return;
        }
        var signal = _activateSignal;
        new Thread(() =>
        {
            while (signal.WaitOne())
                Dispatcher.BeginInvoke(() => (MainWindow as MainWindow)?.BringToFront());
        }) { IsBackground = true, Name = "Command hub activation listener" }.Start();

        DispatcherUnhandledException += OnUnhandledException;
        SessionEnding += (_, _) => FlushSaves();

        Store = new DataStore(customData ?? DataStore.DefaultPath);
        Data = Store.Load();
        // Write the new layout once, so it isn't rebuilt on every launch. Errors are left for the next save
        // (a locked file must not crash the launch that upgrades the user).
        if (Store.Upgraded)
        {
            try { Store.Save(Data); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        ApplyTheme(Data.Settings.Theme);
        Updater = new Updates();

        AddOns.AddOnManager.Startup(); // before the window builds its sidebar
        MainWindow = new MainWindow();
        // Launched by "Start with Windows": stay out of the way until needed.
        if (e.Args.Contains(StartupManager.MinimizedFlag)) MainWindow.WindowState = WindowState.Minimized;
        MainWindow.Show();
        if (Store.ReadFailed)
            ((MainWindow)MainWindow).ShowNotice("Your saved data couldn't be opened, so changes won't be saved until you restart Command hub.");
        else if (Store.RecoveredBackupPath is { } backup)
            ((MainWindow)MainWindow).ShowNotice($"Your saved data was damaged, so Command hub started fresh. A copy was kept at {backup}");

        Updater.Changed += () => ((MainWindow)MainWindow).ShowUpdateState();
        ((MainWindow)MainWindow).ShowUpdateState();
        Updater.StartBackgroundChecks();
    }

    /// <summary>A short name-safe hash that is the same on every run (string.GetHashCode is not).</summary>
    private static string StableHash(string s) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s)))[..12];

    protected override void OnExit(ExitEventArgs e)
    {
        FlushSaves();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static Debouncer? _saveSoon;

    /// <summary>Saves after typing pauses for 600 ms.</summary>
    public static void SaveSoon() => (_saveSoon ??= new Debouncer(TimeSpan.FromMilliseconds(600), SaveNow)).Request();

    /// <summary>
    /// Raised just before the final save (window closing, sign-out, exit) so views holding
    /// half-finished edits can write them into their tile content first.
    /// </summary>
    public static event Action? CommittingEdits;

    /// <summary>Asks every open tile editor to write its half-finished edits into tile content now.</summary>
    public static void CommitOpenEdits() => CommittingEdits?.Invoke();

    /// <summary>Writes anything still waiting to be saved.</summary>
    /// <summary>Saves, then starts a fresh copy and closes this one (to finish updating or removing add-ons).</summary>
    public static void Restart()
    {
        FlushSaves();
        var app = (App)Current;
        // Let go of "one copy at a time" first, or the new copy would just bring this one forward and quit.
        try { app._instanceMutex?.ReleaseMutex(); } catch (ApplicationException) { }
        app._instanceMutex?.Dispose();
        app._instanceMutex = null;
        if (Environment.ProcessPath is { } exe)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false }); }
            catch (System.ComponentModel.Win32Exception) { }
        }
        Current.Shutdown();
    }

    public static void FlushSaves()
    {
        CommitOpenEdits();
        _saveSoon?.Flush();
    }

    private static bool _warnedReadOnly;

    /// <summary>Raised after every save attempt, so views that summarise other tiles (the Home header) can refresh.</summary>
    public static event Action? Saved;

    /// <summary>True once a save has been refused or has failed this session.</summary>
    public static bool SaveTrouble { get; private set; }

    public static void SaveNow()
    {
        try { SaveNowCore(); }
        finally { Saved?.Invoke(); }
    }

    private static void SaveNowCore()
    {
        try
        {
            bool saved = Store.Save(Data);
            SaveTrouble = !saved;
            if (!saved && !_warnedReadOnly)
            {
                _warnedReadOnly = true;
                (Current.MainWindow as MainWindow)?.ShowNotice(
                    "Your saved data couldn't be opened, so changes won't be saved until you restart Command hub.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SaveTrouble = true;
            (Current.MainWindow as MainWindow)?.ShowNotice("Couldn't save your changes. " + ex.Message);
        }
    }

    /// <summary>Last-resort handler: save what we can, write a crash file, tell the user where it is.</summary>
    private void OnUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        string dir = Path.GetDirectoryName(Store?.FilePath ?? DataStore.DefaultPath)!;
        string file = Path.Combine(dir, "crash.log");
        try
        {
            Directory.CreateDirectory(dir);
            File.AppendAllText(file, $"[{DateTime.Now:u}] {e.Exception}\n\n");
            if (Data != null) Store!.Save(Data);
        }
        catch (Exception) { /* nothing more we can do */ }
        MessageBox.Show($"Command hub hit an unexpected problem and needs to close. Your tiles are saved.\n\nDetails were written to:\n{file}",
            "Command hub", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
        Shutdown(1);
    }

    // ---------------- Themes ----------------

    public static ThemeDef Theme { get; private set; } = ThemeCatalog.All[0];

    /// <summary>Raised after a theme is applied, for anything that baked colours into code.</summary>
    public static event Action? ThemeChanged;

    private static readonly Dictionary<string, string> BrushFor = new()
    {
        ["BgColor"] = "Bg", ["SurfaceColor"] = "Surface", ["RaisedColor"] = "Raised", ["TextColor"] = "Text",
        ["MutedColor"] = "Muted", ["HairlineColor"] = "Hairline", ["StrongColor"] = "Strong",
        ["ScrimColor"] = "Scrim", ["OnAccentColor"] = "OnAccent", ["AccentColor"] = "AccentBrush",
        ["Accent2Color"] = "Accent2Brush", ["Accent3Color"] = "Accent3Brush", ["Accent4Color"] = "Accent4Brush",
    };

    /// <summary>
    /// Swaps every theme colour and brush. Everything references them via DynamicResource,
    /// so open screens repaint immediately.
    /// </summary>
    public static void ApplyTheme(string? name)
    {
        Theme = ThemeCatalog.Get(name);
        var res = Current.Resources;
        foreach (var key in ThemeCatalog.Keys)
        {
            var c = ParseColor(Theme[key]);
            res[key] = c;
            if (BrushFor.TryGetValue(key, out var brushKey)) res[brushKey] = new SolidColorBrush(c);
        }
        if (Current.MainWindow != null) WindowEffects.ApplyRoundedCorners(Current.MainWindow, dark: !Theme.IsLight);
        ThemeChanged?.Invoke();
    }

    public static Color ParseColor(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return Colors.Gray;
        try { return ColorConverter.ConvertFromString(hex) is Color c ? c : Colors.Gray; }
        catch (Exception ex) when (ex is FormatException or NotSupportedException) { return Colors.Gray; }
    }
}
