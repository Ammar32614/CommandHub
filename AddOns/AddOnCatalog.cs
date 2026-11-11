using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CommandHub.AddOns;

/// <summary>One add-on offered in the catalogue.</summary>
public sealed record CatalogEntry(
    string Id, string Name, string Version, int Api, string MinHost,
    string Blurb, string Description, string Icon, string Url, string Sha256, long Size);

/// <summary>An add-on's manifest.json, shipped inside its zip.</summary>
public sealed record AddOnManifest(string Id, string Name, string Version, int Api, string Entry, string Blurb, string Icon);

/// <summary>Reading the catalogue and deciding what can be installed. Never throws on bad input.</summary>
public static partial class AddOnCatalog
{
    /// <summary>Where the catalogue lives: a fixed "addons" release in the project's GitHub repo.</summary>
    public const string DefaultUrl = Services.Updates.RepoUrl + "/releases/download/addons/catalog.json";

    /// <summary>COMMANDHUB_ADDON_SOURCE can point at a folder (or another URL) with a catalog.json, for testing.</summary>
    public static string Source =>
        Environment.GetEnvironmentVariable("COMMANDHUB_ADDON_SOURCE") is { Length: > 0 } custom
            ? (custom.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? custom : Path.Combine(custom, "catalog.json"))
            : DefaultUrl;

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,31}$")]
    private static partial Regex IdPattern();

    public static bool IsValidId(string id) => IdPattern().IsMatch(id ?? "");

    public static List<CatalogEntry> Parse(string json)
    {
        var list = new List<CatalogEntry>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("addons", out var addons) || addons.ValueKind != JsonValueKind.Array) return list;
            foreach (var a in addons.EnumerateArray())
            {
                if (a.ValueKind != JsonValueKind.Object) continue;
                string S(string name) => a.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                int api = a.TryGetProperty("api", out var ap) && ap.TryGetInt32(out var n) ? n : 0;
                long size = a.TryGetProperty("size", out var sz) && sz.TryGetInt64(out var l) ? l : 0;
                var e = new CatalogEntry(S("id"), S("name"), S("version"), api, S("minHost") is { Length: > 0 } m ? m : "0.0.0",
                    S("blurb"), S("description"), S("icon"), S("url"), S("sha256"), size);
                if (!IsValidId(e.Id) || e.Name.Length == 0 || !System.Version.TryParse(e.Version, out _) || e.Url.Length == 0 || e.Sha256.Length == 0) continue;
                list.Add(e);
            }
        }
        catch (JsonException) { }
        return list;
    }

    public static AddOnManifest? ParseManifest(string json)
    {
        try
        {
            var m = JsonSerializer.Deserialize<AddOnManifest>(json, AddOnHost.Json);
            return m is not null && IsValidId(m.Id) && System.Version.TryParse(m.Version, out _) && !string.IsNullOrWhiteSpace(m.Entry)
                   && m.Entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && Path.GetFileName(m.Entry) == m.Entry
                ? m with { Name = m.Name ?? m.Id, Blurb = m.Blurb ?? "", Icon = m.Icon ?? "" }
                : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Offered only to the contract version this app speaks, and only if this app is new enough.</summary>
    public static bool IsCompatible(CatalogEntry e, string hostVersion) =>
        e.Api == AddOnApi.Version
        && System.Version.TryParse(Normalize(hostVersion), out var host)
        && System.Version.TryParse(Normalize(e.MinHost), out var min)
        && host >= min;

    public static bool IsNewer(string candidate, string current) =>
        System.Version.TryParse(Normalize(candidate), out var a) && System.Version.TryParse(Normalize(current), out var b) && a > b;

    private static string Normalize(string v)
    {
        var core = (v ?? "").Split('-', '+')[0].Trim();
        return core.Count(c => c == '.') == 0 && core.Length > 0 ? core + ".0" : core;
    }

    /// <summary>A zip named relative to the catalogue lives next to it (same folder or same release).</summary>
    public static string ResolveUrl(string catalogLocation, string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var abs) && (abs.Scheme == Uri.UriSchemeHttps || abs.Scheme == Uri.UriSchemeHttp)) return url;
        if (catalogLocation.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return new Uri(new Uri(catalogLocation), url).ToString();
        return Path.Combine(Path.GetDirectoryName(catalogLocation) ?? "", url);
    }
}
