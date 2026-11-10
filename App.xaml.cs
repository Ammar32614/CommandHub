using System.IO;
using System.Windows;
using System.Windows.Media;
using CommandHub.Models;
using CommandHub.Services;

namespace CommandHub;

public partial class App : Application
{
    public static DataStore Store { get; private set; } = null!;
    public static HubData Data { get; private set; } = null!;

    private Mutex? _instanceMutex;
    private EventWaitHandle? _activateSignal;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // One copy at a time, so two windows never fight over data.json. A second launch brings the first forward.
        _instanceMutex = new Mutex(true, "CommandHub.SingleInstance", out bool first);
        _activateSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "CommandHub.Activate");
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

        // COMMANDHUB_DATA points the app at a different file (testing, demo data).
        Store = new DataStore(Environment.GetEnvironmentVariable("COMMANDHUB_DATA") is { Length: > 0 } p ? p : DataStore.DefaultPath);
        Data = Store.Load();
        ApplyTheme(Data.Settings.Theme);

        MainWindow = new MainWindow();
        // Launched by "Start with Windows": stay out of the way until needed.
        if (e.Args.Contains(StartupManager.MinimizedFlag)) MainWindow.WindowState = WindowState.Minimized;
        MainWindow.Show();
        if (Store.ReadFailed)
            ((MainWindow)MainWindow).ShowNotice("Your saved data couldn't be opened, so changes won't be saved until you restart Command hub.");
        else if (Store.RecoveredBackupPath is { } backup)
            ((MainWindow)MainWindow).ShowNotice($"Your saved data was damaged, so Command hub started fresh. A copy was kept at {backup}");
    }

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

    /// <summary>Writes anything still waiting to be saved.</summary>
    public static void FlushSaves()
    {
        CommittingEdits?.Invoke();
        _saveSoon?.Flush();
    }

    private static bool _warnedReadOnly;

    public static void SaveNow()
    {
        try
        {
            if (!Store.Save(Data) && !_warnedReadOnly)
            {
                _warnedReadOnly = true;
                (Current.MainWindow as MainWindow)?.ShowNotice(
                    "Your saved data couldn't be opened, so changes won't be saved until you restart Command hub.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
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
