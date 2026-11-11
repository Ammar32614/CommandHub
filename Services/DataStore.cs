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

    /// <summary>Set when the last Load found a damaged file and kept a copy of it here before starting fresh.</summary>
    public string? RecoveredBackupPath { get; private set; }

    /// <summary>
    /// True when the last Load couldn't read (or back up) the file, or the file came from a newer
    /// version of the app. Save then refuses to overwrite it.
    /// </summary>
    public bool ReadFailed { get; private set; }

    /// <summary>True when the last Load brought an older file up to the current version (the caller should save it).</summary>
    public bool Upgraded { get; private set; }

    private int? _upgradedFrom;

    public HubData Load()
    {
        ReadFailed = false;
        RecoveredBackupPath = null;
        Upgraded = false;
        _upgradedFrom = null;
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
            // A newer Command hub may use values this version can't read (a new tile size, say).
            // That file isn't damaged: run on defaults but never write over it.
            if (VersionOf(json) > HubData.CurrentVersion)
            {
                ReadFailed = true;
                return HubData.CreateDefault();
            }

            // Keep the unreadable file so nothing is silently lost, then start fresh.
            try
            {
                var backup = Path.Combine(Path.GetDirectoryName(path)!, $"data.corrupt-{DateTime.Now:yyyyMMddHHmmss}.json");
                File.Copy(path, backup, overwrite: true);
                RecoveredBackupPath = backup;
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
        Upgraded = data.Version < HubData.CurrentVersion;
        _upgradedFrom = Upgraded ? data.Version : null;
        data.Upgrade();
        return data;
    }

    /// <summary>Writes atomically. Returns false (and writes nothing) if the existing file must not be replaced.</summary>
    public bool Save(HubData data)
    {
        if (ReadFailed) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (_upgradedFrom is int oldVersion && File.Exists(path))
        {
            // Upgrades are one-way: keep the old file once, before it is first overwritten.
            var backup = Path.Combine(Path.GetDirectoryName(path)!, $"data.v{oldVersion}.json");
            if (!File.Exists(backup)) File.Copy(path, backup);
            _upgradedFrom = null;
        }
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data, Json));
        File.Move(tmp, path, overwrite: true);
        return true;
    }

    /// <summary>The top-level "version" number, or 0 if the text isn't JSON or has none.</summary>
    private static int VersionOf(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("version", out var v)
                && v.TryGetInt32(out int n) ? n : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    /// <summary>Replaces nulls a hand-edited or damaged file can contain, so the rest of the app never sees them.</summary>
    private static void Sanitize(HubData data)
    {
        data.Settings ??= new AppSettings();
        if (string.IsNullOrWhiteSpace(data.Settings.Theme)) data.Settings.Theme = "Midnight";

        data.Pages = (data.Pages ?? []).Where(p => p is not null).ToList();
        if (data.Pages.Count == 0) data.Pages = [HubData.CreateHome()];

        data.Money ??= new();
        if (string.IsNullOrWhiteSpace(data.Money.Currency)) data.Money.Currency = Money.MoneyData.DefaultCurrency();
        data.Money.CategoryBudgets = (data.Money.CategoryBudgets ?? []).ToDictionary(kv => kv.Key, kv => Math.Clamp(kv.Value, 0, Money.MoneyLogic.MaxAmount));
        data.Money.MonthlyBudget = Math.Clamp(data.Money.MonthlyBudget, 0, Money.MoneyLogic.MaxAmount);
        data.Money.Transactions = (data.Money.Transactions ?? []).Where(t => t is not null).ToList();
        foreach (var t in data.Money.Transactions)
        {
            if (string.IsNullOrWhiteSpace(t.Category)) t.Category = "Other";
            t.Note ??= "";
            t.Amount = Math.Min(Math.Abs(t.Amount), Money.MoneyLogic.MaxAmount);
            if (t.Id == Guid.Empty) t.Id = Guid.NewGuid();
        }

        // One journal entry per day: if a damaged file has duplicates, keep the one with the most writing.
        data.Journal = (data.Journal ?? []).Where(j => j is not null)
            .Select(j => { j.Text ??= ""; j.Mood = Math.Clamp(j.Mood, 0, 5); return j; })
            .GroupBy(j => j.Date).Select(g => g.OrderByDescending(j => j.Text.Length).First())
            .OrderBy(j => j.Date).ToList();

        data.Planner = (data.Planner ?? []).Where(p => p is not null).ToList();
        foreach (var item in data.Planner)
        {
            if (string.IsNullOrWhiteSpace(item.Title)) item.Title = "Untitled";
            item.Course ??= "";
            item.Notes ??= "";
            if (item.Id == Guid.Empty) item.Id = Guid.NewGuid();
        }

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
