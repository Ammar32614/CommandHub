using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Loader;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CommandHub.Home;
using static CommandHub.Home.PageUi;

namespace CommandHub.AddOns;

/// <summary>
/// Loads installed add-ons at startup, and installs, updates and removes them from Settings.
/// Add-ons come from the catalogue on GitHub (see <see cref="AddOnCatalog"/>); each is checked against
/// the SHA-256 the catalogue lists before it's unpacked.
/// </summary>
public static class AddOnManager
{
    private sealed record Running(IAddOn AddOn, AddOnManifest Manifest, MainWindow.HubTab Tab);

    private static readonly Dictionary<string, Running> Loaded = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> LoadErrors = new(StringComparer.OrdinalIgnoreCase);
    private static List<CatalogEntry>? _catalog;
    private static string _catalogError = "";
    private static bool _fetching;
    private static DateTime _fetchedUtc;
    private static readonly HashSet<string> Busy = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> NeedsRestart = new(StringComparer.OrdinalIgnoreCase);
    private static Panel? _section;

    /// <summary>Set when loading add-ons failed outright at startup; shown in Settings.</summary>
    public static string StartupError { get; set; } = "";

    private static AddOnFiles Files => new(AddOnFiles.DefaultRoot(App.Store.FilePath));
    private static string DataFolderFor(string id) => Path.Combine(Path.GetDirectoryName(App.Store.FilePath)!, "addon-data", id);
    private static MainWindow? Main => Application.Current.MainWindow as MainWindow;

    /// <summary>Called once at startup, before the window builds its sidebar.</summary>
    public static void Startup()
    {
        var files = Files;
        files.CleanUp();
        foreach (var addOn in files.Installed()) TryLoad(addOn);

        // With automatic updates on, look for add-on updates shortly after starting and mention them.
        if (Loaded.Count == 0) return;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            if (!App.Data.Settings.AutoUpdate) return;
            await FetchCatalog();
            var updates = (_catalog ?? []).Where(e => Loaded.TryGetValue(e.Id, out var r) && AddOnCatalog.IsNewer(e.Version, r.Manifest.Version)).Select(e => e.Name).ToList();
            if (updates.Count > 0)
                Main?.ShowNotice($"An update for {string.Join(" and ", updates)} is ready. Install it from Settings › Add-ons.");
        };
        timer.Start();
    }

    private static bool TryLoad(InstalledAddOn installed)
    {
        var m = installed.Manifest;
        try
        {
            if (m.Api != AddOnApi.Version)
                throw new AddOnException("it was made for a different version of Command hub. Update it below.");
            var context = new AddOnLoadContext(installed.EntryPath);
            var assembly = context.LoadFromAssemblyPath(installed.EntryPath);
            var type = assembly.GetExportedTypes().FirstOrDefault(t => typeof(IAddOn).IsAssignableFrom(t) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null)
                ?? throw new AddOnException("it doesn't contain an add-on");
            var addOn = (IAddOn)Activator.CreateInstance(type)!;
            addOn.Start(new AddOnHost(DataFolderFor(m.Id)));
            var tab = new MainWindow.HubTab(m.Name, m.Icon.Length > 0 ? m.Icon : "", addOn.CreatePage);
            MainWindow.AddOnTabs.RemoveAll(t => t.Name.Equals(m.Name, StringComparison.OrdinalIgnoreCase));
            MainWindow.AddOnTabs.Add(tab);
            Loaded[m.Id] = new Running(addOn, m, tab);
            LoadErrors.Remove(m.Id);
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A broken or out-of-date add-on must never stop Command hub starting.
            LoadErrors[m.Id] = ex is AddOnException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    /// <summary>The add-on whose code threw this exception (anywhere in its stack, or its inner exceptions), or null.</summary>
    public static string? AddOnNameFor(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            var frames = new System.Diagnostics.StackTrace(e, false).GetFrames();
            foreach (var f in frames)
            {
                var asm = f.GetMethod()?.DeclaringType?.Assembly;
                if (asm is null || AssemblyLoadContext.GetLoadContext(asm) is not AddOnLoadContext) continue;
                var entry = Loaded.Values.FirstOrDefault(r => r.AddOn.GetType().Assembly == asm);
                return entry?.Manifest.Name ?? asm.GetName().Name ?? "An add-on";
            }
        }
        return null;
    }

    public static string? BlurbFor(string tabName) =>
        Loaded.Values.FirstOrDefault(r => r.Manifest.Name.Equals(tabName, StringComparison.OrdinalIgnoreCase))?.Manifest.Blurb;

    // ---------------- Catalogue, install, remove ----------------

    private static async Task<byte[]> ReadBytes(string location) =>
        location.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? await Services.Net.Http.GetByteArrayAsync(location)
            : await File.ReadAllBytesAsync(location);

    private static async Task FetchCatalog()
    {
        if (_fetching) return;
        _fetching = true;
        _catalogError = "";
        Refresh();
        try
        {
            var source = AddOnCatalog.Source;
            var json = System.Text.Encoding.UTF8.GetString(await ReadBytes(source));
            _catalog = AddOnCatalog.Parse(json)
                .Where(e => AddOnCatalog.IsCompatible(e, App.Updater.CurrentVersion))
                .Select(e => e with { Url = AddOnCatalog.ResolveUrl(source, e.Url) })
                .ToList();
            _fetchedUtc = DateTime.UtcNow;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            _catalogError = "Couldn't reach the add-on catalogue. Check your connection and try again.";
        }
        finally
        {
            _fetching = false;
            Refresh();
        }
    }

    private static async Task Install(CatalogEntry entry)
    {
        if (!Busy.Add(entry.Id)) return;
        Refresh();
        try
        {
            var zip = await ReadBytes(entry.Url);
            var installed = await Task.Run(() => Files.Install(entry, zip));
            if (Loaded.ContainsKey(entry.Id))
            {
                NeedsRestart.Add(entry.Id); // the old version is running; the new one loads next start
            }
            else if (TryLoad(installed))
            {
                Main?.RebuildNav();
                Main?.ShowNotice($"{installed.Manifest.Name} is installed. It's in the sidebar.");
            }
            else
            {
                Main?.ShowNotice($"{installed.Manifest.Name} was installed but couldn't start: {LoadErrors.GetValueOrDefault(entry.Id)}");
            }
        }
        catch (AddOnException ex) { Main?.ShowNotice(ex.Message); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            Main?.ShowNotice("Couldn't install the add-on: " + ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            Main?.ShowNotice("Couldn't download the add-on. Check your connection and try again.");
        }
        finally
        {
            Busy.Remove(entry.Id);
            Refresh();
        }
    }

    private static void Remove(string id, string name)
    {
        Files.MarkRemoved(id);
        if (Loaded.Remove(id, out var running))
        {
            MainWindow.AddOnTabs.Remove(running.Tab);
            NeedsRestart.Add(id);
        }
        Main?.RebuildNav();
        Main?.ShowNotice($"{name} is removed. Its saved data is kept, in case you install it again.");
        Refresh();
    }

    // ---------------- Settings section ----------------

    /// <summary>Fills the Add-ons section of Settings (and keeps it up to date while it's shown).</summary>
    public static void BuildSettingsSection(Panel host)
    {
        _section = host;
        host.Unloaded += (_, _) => { if (_section == host) _section = null; };
        Refresh();
        // Look again each time Settings opens (at most every few minutes), so new add-ons and updates show up.
        if (!_fetching && (_catalog is null || DateTime.UtcNow - _fetchedUtc > TimeSpan.FromMinutes(5))) _ = FetchCatalog();
    }

    private static void Refresh()
    {
        var host = _section;
        if (host is null) return;
        host.Children.Clear();

        var title = new TextBlock { Text = "Add-ons", Margin = new Thickness(0, 24, 0, 4) };
        title.SetResourceReference(FrameworkElement.StyleProperty, "SectionTitle");
        host.Children.Add(title);
        var intro = new TextBlock { Text = "Optional tabs for specific interests. Install only what you want; each adds a tab to the sidebar.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        intro.SetResourceReference(FrameworkElement.StyleProperty, "Caption");
        host.Children.Add(intro);
        if (StartupError.Length > 0) host.Children.Add(T("Add-ons couldn't be loaded at startup: " + StartupError, 12, "Danger", wrap: true));

        var list = new StackPanel();
        if (NeedsRestart.Count > 0)
        {
            var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            var restart = Styled("Restart now", "AccentButton", (_, _) => App.Restart(), new Thickness(14, 5, 14, 5));
            DockPanel.SetDock(restart, Dock.Right);
            bar.Children.Add(restart);
            bar.Children.Add(T("Restart Command hub to finish updating or removing add-ons.", 12, "Text", wrap: true));
            list.Children.Add(bar);
        }

        var installed = Files.Installed().ToDictionary(i => i.Manifest.Id, StringComparer.OrdinalIgnoreCase);
        var ids = (_catalog ?? []).Select(e => e.Id).Concat(installed.Keys).Concat(Loaded.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        bool first = true;
        foreach (var id in ids)
        {
            if (!first) list.Children.Add(Rule());
            first = false;
            var entry = _catalog?.FirstOrDefault(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            installed.TryGetValue(id, out var local);
            Loaded.TryGetValue(id, out var running);
            list.Children.Add(Row(id, entry, local, running));
        }
        if (ids.Count == 0)
            list.Children.Add(T(_fetching ? "Looking for add-ons…" : _catalogError.Length > 0 ? _catalogError : "No add-ons are available yet.", 12, "Muted", wrap: true));
        else if (_catalogError.Length > 0)
        {
            list.Children.Add(Rule());
            list.Children.Add(T(_catalogError, 12, "Muted", wrap: true));
        }
        if (_catalogError.Length > 0 && !_fetching)
        {
            var retry = Styled("Try again", "GhostButton", async (_, _) => await FetchCatalog(), new Thickness(12, 4, 12, 4));
            retry.HorizontalAlignment = HorizontalAlignment.Left;
            retry.Margin = new Thickness(0, 10, 0, 0);
            list.Children.Add(retry);
        }

        var card = new Border { Child = list };
        card.SetResourceReference(FrameworkElement.StyleProperty, "Card");
        host.Children.Add(card);
    }

    private static FrameworkElement Row(string id, CatalogEntry? entry, InstalledAddOn? local, Running? running)
    {
        var name = running?.Manifest.Name ?? local?.Manifest.Name ?? entry?.Name ?? id;
        var icon = (entry?.Icon is { Length: > 0 } i ? i : null) ?? local?.Manifest.Icon ?? "";
        var installedVersion = local?.Manifest.Version ?? running?.Manifest.Version;
        bool update = entry != null && installedVersion != null && AddOnCatalog.IsNewer(entry.Version, installedVersion);
        bool busy = Busy.Contains(id);

        var row = new DockPanel();
        var badge = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Top };
        badge.Background = Backdrop.Wash("AccentColor", 0x22);
        var glyph = Glyph(icon, 18, "AccentBrush");
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        glyph.VerticalAlignment = VerticalAlignment.Center;
        badge.Child = glyph;
        DockPanel.SetDock(badge, Dock.Left);
        row.Children.Add(badge);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        if (busy)
            buttons.Children.Add(T(installedVersion is null ? "Installing…" : "Updating…", 12, "Muted"));
        else if (NeedsRestart.Contains(id))
            buttons.Children.Add(T("Restart to finish", 12, "Muted"));
        else if (installedVersion is null)
        {
            if (entry != null)
            {
                var install = Styled("Install", "AccentButton", async (_, _) => await Install(entry), new Thickness(16, 5, 16, 5));
                System.Windows.Automation.AutomationProperties.SetName(install, $"Install {name}");
                buttons.Children.Add(install);
            }
        }
        else
        {
            if (update)
            {
                var up = Styled($"Update to {entry!.Version}", "AccentButton", async (_, _) => await Install(entry), new Thickness(14, 5, 14, 5));
                System.Windows.Automation.AutomationProperties.SetName(up, $"Update {name}");
                up.Margin = new Thickness(0, 0, 8, 0);
                buttons.Children.Add(up);
            }
            var remove = Styled("Remove", "GhostButton", (_, _) => Remove(id, name), new Thickness(12, 5, 12, 5));
            System.Windows.Automation.AutomationProperties.SetName(remove, $"Remove {name}");
            buttons.Children.Add(remove);
        }
        DockPanel.SetDock(buttons, Dock.Right);
        row.Children.Add(buttons);

        var text = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(T(name, 14, "Text", FontWeights.SemiBold));
        var version = installedVersion != null ? $"{installedVersion} installed" : entry != null ? $"{entry.Version}  ·  {Math.Max(1, entry.Size / 1024)} KB" : "";
        var v = T(version, 11, "Muted");
        v.Margin = new Thickness(10, 2, 0, 0);
        head.Children.Add(v);
        text.Children.Add(head);
        var blurb = entry?.Blurb ?? local?.Manifest.Blurb ?? "";
        if (blurb.Length > 0) text.Children.Add(Margin(T(blurb, 12, "Text", wrap: true), 3));
        if (entry?.Description is { Length: > 0 } d) text.Children.Add(Margin(T(d, 11, "Muted", wrap: true), 3));
        if (LoadErrors.TryGetValue(id, out var error)) text.Children.Add(Margin(T("Couldn't start: " + error, 11, "Danger", wrap: true), 4));
        row.Children.Add(text);
        return row;
    }

    private static FrameworkElement Margin(FrameworkElement e, double top)
    {
        e.Margin = new Thickness(0, top, 0, 0);
        return e;
    }

    private static Border Rule()
    {
        var b = new Border { Height = 1, Margin = new Thickness(0, 14, 0, 14) };
        b.SetResourceReference(Border.BackgroundProperty, "Hairline");
        return b;
    }
}

/// <summary>
/// Each add-on loads in its own context, so its private libraries can't clash with Command hub's or another add-on's.
/// Anything Command hub already has loaded (itself, WPF, .NET) is shared, so the add-on talks to the real app.
/// </summary>
internal sealed class AddOnLoadContext(string mainPath) : AssemblyLoadContext(Path.GetFileNameWithoutExtension(mainPath), isCollectible: false)
{
    private readonly string _folder = Path.GetDirectoryName(mainPath)!;

    protected override Assembly? Load(AssemblyName name)
    {
        if (Default.Assemblies.Any(a => string.Equals(a.GetName().Name, name.Name, StringComparison.OrdinalIgnoreCase))) return null;
        // Shared framework assemblies that just haven't been loaded yet also come from the default context.
        try { var shared = Default.LoadFromAssemblyName(name); if (shared != null) return null; }
        catch (FileNotFoundException) { }
        catch (FileLoadException) { }   // a version Command hub doesn't have: use the add-on's own copy if it brought one
        var local = Path.Combine(_folder, name.Name + ".dll");
        return File.Exists(local) ? LoadFromAssemblyPath(local) : null;
    }
}
