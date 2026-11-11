using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace CommandHub.AddOns;

/// <summary>
/// What an add-on is. An add-on is a DLL with one public class implementing this, plus a manifest.json.
/// It can use anything public in Command hub (PageUi, Backdrop, SmoothScroll, theme brushes) so it looks native.
/// </summary>
public interface IAddOn
{
    /// <summary>Called once, on the UI thread, right after the add-on is loaded (at startup or on install).</summary>
    void Start(IAddOnHost host);

    /// <summary>Builds the add-on's tab. Called each time the tab is opened.</summary>
    UIElement CreatePage();
}

/// <summary>What Command hub gives an add-on.</summary>
public interface IAddOnHost
{
    /// <summary>The add-on's own folder for data. Kept when the add-on is updated or removed.</summary>
    string DataFolder { get; }

    /// <summary>Command hub's HTTP client (IPv4 first, sensible timeouts).</summary>
    HttpClient Http { get; }

    /// <summary>Reads a JSON file from the data folder; a fresh T if it's missing. A damaged file is kept as name.corrupt-time.json.</summary>
    T Load<T>(string name) where T : new();

    /// <summary>Writes a JSON file to the data folder atomically.</summary>
    void Save<T>(string name, T value);

    /// <summary>Shows a message in the banner at the top of the window.</summary>
    void Notify(string text);
}

/// <summary>The contract version. A catalog entry with a different "api" isn't offered.</summary>
public static class AddOnApi
{
    public const int Version = 1;
}

public sealed class AddOnException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>The <see cref="IAddOnHost"/> each loaded add-on gets.</summary>
public sealed class AddOnHost(string dataFolder) : IAddOnHost
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string DataFolder { get; } = dataFolder;
    public HttpClient Http => Services.Net.Http;

    /// <summary>Files that exist but couldn't be read (or kept aside) this run: never overwritten until the next start.</summary>
    private readonly HashSet<string> _protected = new(StringComparer.OrdinalIgnoreCase);

    public T Load<T>(string name) where T : new()
    {
        var path = PathOf(name);
        if (!File.Exists(path)) return new T();
        string text;
        try { text = File.ReadAllText(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Locked by a backup tool or antivirus, say: run on an empty copy, but don't write over the real one.
            _protected.Add(path);
            Notify("An add-on's saved data couldn't be read right now, so its changes won't be saved until you restart Command hub.");
            return new T();
        }
        try
        {
            return JsonSerializer.Deserialize<T>(text, Json) ?? new T();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            try { File.Copy(path, Path.Combine(DataFolder, $"{Path.GetFileNameWithoutExtension(path)}.corrupt-{DateTime.Now:yyyyMMddHHmmss}.json"), overwrite: true); }
            catch (Exception copyEx) when (copyEx is IOException or UnauthorizedAccessException) { _protected.Add(path); }
            return new T();
        }
    }

    public void Save<T>(string name, T value)
    {
        var path = PathOf(name);
        if (_protected.Contains(path)) return;
        Directory.CreateDirectory(DataFolder);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Json));
        // Keep the previous version too, one step back, in case something goes wrong.
        if (File.Exists(path)) File.Replace(tmp, path, path + ".previous");
        else File.Move(tmp, path);
    }

    public void Notify(string text) => (Application.Current?.MainWindow as MainWindow)?.ShowNotice(text);

    private string PathOf(string name)
    {
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains("..")) throw new ArgumentException("Bad file name", nameof(name));
        return Path.Combine(DataFolder, name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? name : name + ".json");
    }
}
