using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommandHub.Layout;
using CommandHub.Models;

namespace CommandHub.Services;

/// <summary>Loads and saves <see cref="HubData"/> as JSON. Never throws on a bad file (same rules as Stint's DataStore).</summary>
public class DataStore(string path)
{
    public static string DefaultPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CommandHub", "data.json");

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string FilePath => path;

    /// <summary>
    /// True when the last Load couldn't read (or back up) the file, or the file came from a newer
    /// version of the app. Save then refuses to overwrite it.
    /// </summary>
    public bool ReadFailed { get; private set; }

    public HubData Load()
    {
        ReadFailed = false;
        if (!File.Exists(path)) return HubData.CreateDefault();

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Locked by a sync tool/antivirus etc. Run on defaults but never overwrite the real file.
            ReadFailed = true;
            return HubData.CreateDefault();
        }

        HubData data;
        try
        {
            data = JsonSerializer.Deserialize<HubData>(json, Json) ?? HubData.CreateDefault();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            // Keep the unreadable file so nothing is silently lost, then start fresh.
            try
            {
                var backup = Path.Combine(Path.GetDirectoryName(path)!, $"data.corrupt-{DateTime.Now:yyyyMMddHHmmss}.json");
                File.Copy(path, backup, overwrite: true);
            }
            catch (Exception copyEx) when (copyEx is IOException or UnauthorizedAccessException)
            {
                ReadFailed = true; // couldn't back it up: don't replace it either
            }
            return HubData.CreateDefault();
        }

        Sanitize(data);
        if (data.Version > HubData.CurrentVersion)
        {
            // Written by a newer Command hub: show what we can, but never write over it.
            ReadFailed = true;
            return data;
        }

        foreach (var page in data.Pages) BoardGrid.Normalize(page.Tiles);
        return data;
    }

    /// <summary>Writes atomically. Returns false (and writes nothing) if the existing file must not be replaced.</summary>
    public bool Save(HubData data)
    {
        if (ReadFailed) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data, Json));
        File.Move(tmp, path, overwrite: true);
        return true;
    }

    /// <summary>Replaces nulls a hand-edited or damaged file can contain, so the rest of the app never sees them.</summary>
    private static void Sanitize(HubData data)
    {
        data.Settings ??= new AppSettings();
        if (string.IsNullOrWhiteSpace(data.Settings.Theme)) data.Settings.Theme = "Midnight";

        data.Pages = (data.Pages ?? []).Where(p => p is not null).ToList();
        if (data.Pages.Count == 0) data.Pages = [HubData.CreateHome()];

        foreach (var page in data.Pages)
        {
            if (string.IsNullOrWhiteSpace(page.Name)) page.Name = "Page";
            page.Icon ??= "";
            page.Tiles = (page.Tiles ?? []).Where(t => t is not null).ToList();
            foreach (var t in page.Tiles)
            {
                t.TypeId ??= "";
                t.Content ??= new();
                if (t.Id == Guid.Empty) t.Id = Guid.NewGuid();
            }
        }
    }
}
