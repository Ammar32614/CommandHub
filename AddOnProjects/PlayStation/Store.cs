using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CommandHub.AddOn.PlayStation;

/// <summary>A PlayStation Store region this add-on compares.</summary>
public sealed record Region(string Code, string Name, string Locale, string Country, string Language, string Currency, string StoreLocale)
{
    public static readonly Region US = new("US", "United States", "en-US", "US", "en", "USD", "en-us");
    public static readonly Region TR = new("TR", "Turkey", "en-TR", "TR", "en", "TRY", "en-tr");
    // The Ukrainian store answers in Russian or Ukrainian; ru-UA keeps edition names in English.
    public static readonly Region UA = new("UA", "Ukraine", "ru-UA", "UA", "ru", "UAH", "ru-ua");
    public static readonly Region[] All = [US, TR, UA];
    public static Region? Find(string code) => All.FirstOrDefault(r => r.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

    public string ProductUrl(string productId) => $"https://store.playstation.com/{StoreLocale}/product/{productId}";
}

/// <summary>A price as the store shows it: in whole minor units (cents, kuruş, kopiyky).</summary>
public sealed record Offer(long BaseMinor, long SaleMinor, string Currency, DateTime? EndsUtc, string SavingTag, long? PlusMinor, bool IsFree)
{
    public bool OnSale => SaleMinor < BaseMinor;
    public decimal Base => BaseMinor / 100m;
    public decimal Sale => SaleMinor / 100m;
    public decimal? Plus => PlusMinor / 100m;
    public int PercentOff => BaseMinor <= 0 ? 0 : (int)Math.Round(100m * (BaseMinor - SaleMinor) / BaseMinor);
}

/// <summary>One buyable edition of a game in one region.</summary>
public sealed record Edition(string ProductId, string Name, string EditionName, string Classification, IReadOnlyList<string> Platforms, Offer? Offer)
{
    /// <summary>The part of the product ID that's the same in every region ("ELDENRING0000000").</summary>
    public string Label => Store.LabelOf(ProductId);
}

/// <summary>A game ("concept") in one region, with its editions.</summary>
public sealed record Concept(string Id, string Name, string Image, IReadOnlyList<Edition> Editions);

/// <summary>A search or deals result: just what the store's lists show.</summary>
public sealed record StoreHit(string ProductId, string Name, string Classification, IReadOnlyList<string> Platforms, string Image, string BasePrice, string SalePrice, string DiscountText, bool IsFree);

/// <summary>
/// Reads the PlayStation Store's own web API (the one store.playstation.com uses), anonymously.
/// These are persisted queries: if Sony changes them, calls fail with <see cref="StoreChangedException"/>
/// and the add-on needs an update.
/// </summary>
public sealed partial class Store(HttpClient http)
{
    private const string Endpoint = "https://web.np.playstation.com/api/graphql/v1/op";
    public const string DealsCategory = "3f772501-f6f8-49b7-abac-874a88ca4897";

    private static readonly Dictionary<string, string> Hashes = new()
    {
        ["getSearchResults"] = "4df6284f982e57bec70f23c77e2c219dc792eb19af7fb3d3a81767aa3f1958aa",
        ["productRetrieveForUpsellWithCtas"] = "9bd6ae6de5060fbe63fac5f394e7e5c75532743f06241b3ac835985267fba2b5",
        ["conceptRetrieveForUpsellWithCtas"] = "afa38e2e1ad7f940532edf20e5e6229c5637e79f2360c4c26b41615d0645694f",
        ["categoryStrandRetrieve"] = "7d944fd25a1d2218ca1de4d48f59dc6077a3e08c74533d3980a70c184b048712",
    };

    private async Task<JsonDocument> Query(string operation, object variables, Region region, CancellationToken ct)
    {
        var url = Endpoint
            + "?operationName=" + operation
            + "&variables=" + Uri.EscapeDataString(JsonSerializer.Serialize(variables))
            + "&extensions=" + Uri.EscapeDataString("{\"persistedQuery\":{\"version\":1,\"sha256Hash\":\"" + Hashes[operation] + "\"}}");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("x-psn-store-locale-override", region.Locale);
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        JsonDocument doc;
        try { doc = JsonDocument.Parse(text); }
        catch (JsonException) { throw new HttpRequestException($"The PlayStation Store answered with {(int)response.StatusCode}."); }
        if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0
            && (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null))
        {
            var message = errors[0].TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
            doc.Dispose();
            if (message.Contains("PersistedQuery", StringComparison.OrdinalIgnoreCase)) throw new StoreChangedException();
            throw new HttpRequestException("The PlayStation Store said: " + message);
        }
        return doc;
    }

    public async Task<List<StoreHit>> Search(string term, Region region, CancellationToken ct = default)
    {
        using var doc = await Query("getSearchResults", new
        {
            countryCode = region.Country, languageCode = region.Language, nextCursor = "", pageOffset = 0, pageSize = 24, searchTerm = term,
        }, region, ct);
        return ParseHits(doc.RootElement, "universalSearch", "results");
    }

    public async Task<List<StoreHit>> Deals(Region region, int size = 60, CancellationToken ct = default)
    {
        using var doc = await Query("categoryStrandRetrieve", new
        {
            id = DealsCategory, pageArgs = new { size, offset = 0 }, maxResults = size,
        }, region, ct);
        return ParseHits(doc.RootElement, "categoryStrandRetrieve", "products");
    }

    /// <summary>The game a product belongs to, with every edition and its price in that region. Null if it isn't sold there.</summary>
    public async Task<Concept?> ConceptOfProduct(string productId, Region region, CancellationToken ct = default)
    {
        using var doc = await Query("productRetrieveForUpsellWithCtas", new { productId }, region, ct);
        return doc.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("productRetrieve", out var p) && p.ValueKind == JsonValueKind.Object
               && p.TryGetProperty("concept", out var c) ? ParseConcept(c, region.Currency) : null;
    }

    /// <summary>A game by its concept ID (the same in every region). Null if it isn't sold there.</summary>
    public async Task<Concept?> ConceptById(string conceptId, Region region, CancellationToken ct = default)
    {
        using var doc = await Query("conceptRetrieveForUpsellWithCtas", new { conceptId }, region, ct);
        return doc.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("conceptRetrieve", out var c) && c.ValueKind == JsonValueKind.Object
            ? ParseConcept(c, region.Currency) : null;
    }

    // ---------------- Parsing (public for tests) ----------------

    public static string LabelOf(string productId)
    {
        var parts = (productId ?? "").Split('-');
        return parts.Length >= 3 ? parts[2] : productId ?? "";
    }

    private static readonly string[] CoverRoles = ["MASTER", "GAMEHUB_COVER_ART", "EDITION_KEY_ART", "PORTRAIT_BANNER", "FOUR_BY_THREE_BANNER"];

    /// <summary>The best square-ish cover from a media list.</summary>
    public static string Cover(JsonElement owner)
    {
        var media = new List<(string Role, string Url)>();
        void Collect(JsonElement list)
        {
            if (list.ValueKind != JsonValueKind.Array) return;
            foreach (var m in list.EnumerateArray())
                if (Str(m, "type") == "IMAGE") media.Add((Str(m, "role"), Str(m, "url")));
        }
        if (owner.TryGetProperty("media", out var direct)) Collect(direct);
        if (owner.TryGetProperty("personalizedMeta", out var pm) && pm.ValueKind == JsonValueKind.Object && pm.TryGetProperty("media", out var pmm)) Collect(pmm);
        foreach (var role in CoverRoles)
            if (media.FirstOrDefault(m => m.Role == role && m.Url.Length > 0) is { Url.Length: > 0 } hit) return hit.Url;
        return media.FirstOrDefault(m => m.Url.Length > 0).Url ?? "";
    }

    public static List<StoreHit> ParseHits(JsonElement root, string container, string listName)
    {
        var list = new List<StoreHit>();
        if (!root.TryGetProperty("data", out var data) || !data.TryGetProperty(container, out var c) || c.ValueKind != JsonValueKind.Object
            || !c.TryGetProperty(listName, out var items) || items.ValueKind != JsonValueKind.Array) return list;
        foreach (var p in items.EnumerateArray())
        {
            if (p.ValueKind != JsonValueKind.Object || Str(p, "__typename") is { Length: > 0 } t && t != "Product") continue;
            var id = Str(p, "id");
            if (id.Length == 0) continue;
            var price = p.TryGetProperty("price", out var pr) && pr.ValueKind == JsonValueKind.Object ? pr : default;
            list.Add(new StoreHit(id, Str(p, "name"), Str(p, "localizedStoreDisplayClassification"), Platforms(p), Cover(p),
                price.ValueKind == JsonValueKind.Object ? Str(price, "basePrice") : "",
                price.ValueKind == JsonValueKind.Object ? Str(price, "discountedPrice") : "",
                price.ValueKind == JsonValueKind.Object ? Str(price, "discountText") : "",
                price.ValueKind == JsonValueKind.Object && price.TryGetProperty("isFree", out var f) && f.ValueKind == JsonValueKind.True));
        }
        return list;
    }

    public static Concept? ParseConcept(JsonElement c, string currency)
    {
        var id = Str(c, "id");
        if (id.Length == 0) return null;
        var editions = new List<Edition>();
        if (c.TryGetProperty("products", out var products) && products.ValueKind == JsonValueKind.Array)
            foreach (var p in products.EnumerateArray())
            {
                var pid = Str(p, "id");
                if (pid.Length == 0) continue;
                var editionName = p.TryGetProperty("edition", out var ed) && ed.ValueKind == JsonValueKind.Object ? Str(ed, "name") : "";
                editions.Add(new Edition(pid, Str(p, "name"), editionName, Str(p, "storeDisplayClassification"), Platforms(p), ParseOffer(p, currency)));
            }
        return new Concept(id, Str(c, "name"), Cover(c), editions);
    }

    /// <summary>The outright price (and the PS Plus price, when there is one) from a product's buttons.</summary>
    public static Offer? ParseOffer(JsonElement product, string currency)
    {
        if (!product.TryGetProperty("webctas", out var ctas) || ctas.ValueKind != JsonValueKind.Array) return null;
        Offer? outright = null;
        long? plus = null;
        foreach (var cta in ctas.EnumerateArray())
        {
            if (!cta.TryGetProperty("price", out var p) || p.ValueKind != JsonValueKind.Object) continue;
            if (!p.TryGetProperty("basePriceValue", out var b) || !b.TryGetInt64(out var baseMinor)) continue;
            long sale = p.TryGetProperty("discountedValue", out var d) && d.TryGetInt64(out var dv) ? dv : baseMinor;
            bool tied = p.TryGetProperty("isTiedToSubscription", out var tie) && tie.ValueKind == JsonValueKind.True;
            bool plusBranded = p.TryGetProperty("serviceBranding", out var sb) && sb.ValueKind == JsonValueKind.Array
                               && sb.EnumerateArray().Any(x => (x.GetString() ?? "").Contains("PLUS", StringComparison.OrdinalIgnoreCase));
            if (tied || plusBranded)
            {
                plus = plus is { } existing ? Math.Min(existing, sale) : sale;
                continue;
            }
            DateTime? ends = long.TryParse(Str(p, "endTime"), out var ms) && ms > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime : null;
            var cur = Str(p, "currencyCode") is { Length: > 0 } cc ? cc : currency;
            bool free = p.TryGetProperty("isFree", out var fr) && fr.ValueKind == JsonValueKind.True;
            var offer = new Offer(baseMinor, sale, cur, ends, Str(p, "savingTag"), null, free);
            if (outright is null || offer.SaleMinor < outright.SaleMinor) outright = offer;
        }
        if (outright is null) return null;
        return plus is { } pl && pl < outright.SaleMinor ? outright with { PlusMinor = pl } : outright;
    }

    private static IReadOnlyList<string> Platforms(JsonElement p) =>
        p.TryGetProperty("platforms", out var pl) && pl.ValueKind == JsonValueKind.Array
            ? pl.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList()
            : [];

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    [GeneratedRegex(@"[\d][\d.,\s  ]*")]
    private static partial Regex Number();

    /// <summary>
    /// A shown price as a number: "$59.99", "1.724,50 TL", "UAH 1 699,00", "Free". Null when it can't be read.
    /// The last "," or "." followed by exactly two digits is the decimal point; other separators group thousands.
    /// </summary>
    public static decimal? ParseShownPrice(string text)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0) return null;
        if (t.Contains("free", StringComparison.OrdinalIgnoreCase) || t.Contains("бесплат", StringComparison.OrdinalIgnoreCase) || t.Contains("безкоштов", StringComparison.OrdinalIgnoreCase)) return 0;
        var m = Number().Match(t);
        if (!m.Success) return null;
        var digits = new string(m.Value.Where(ch => !char.IsWhiteSpace(ch) && ch != ' ' && ch != ' ').ToArray()).TrimEnd('.', ',');
        int lastSep = Math.Max(digits.LastIndexOf('.'), digits.LastIndexOf(','));
        string whole = digits, fraction = "";
        if (lastSep >= 0 && digits.Length - lastSep - 1 == 2)
        {
            whole = digits[..lastSep];
            fraction = digits[(lastSep + 1)..];
        }
        whole = whole.Replace(".", "").Replace(",", "");
        return decimal.TryParse(whole + (fraction.Length > 0 ? "." + fraction : ""), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}

public sealed class StoreChangedException() : Exception("The PlayStation Store changed how it works. An update to this add-on is needed.");
