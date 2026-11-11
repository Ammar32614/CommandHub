namespace CommandHub.AddOn.PlayStation;

/// <summary>Everything the add-on saves (playstation.json in its data folder).</summary>
public sealed class PsData
{
    public List<WishItem> Wishlist { get; set; } = [];
    public RateSet? Rates { get; set; }
    /// <summary>SBP's USD→PKR rate, typed in by you. Used instead of the market rate when set.</summary>
    public decimal? ManualPkrPerUsd { get; set; }
    public DateTime? LastCheckUtc { get; set; }
    public string LastError { get; set; } = "";
    public string DealsRegion { get; set; } = "TR";
    public string SearchRegion { get; set; } = "US";
}

/// <summary>One game edition you're watching, with its price in each region.</summary>
public sealed class WishItem
{
    public string ConceptId { get; set; } = "";
    /// <summary>The edition: the region-independent part of its product ID.</summary>
    public string Label { get; set; } = "";
    public string Name { get; set; } = "";
    public string EditionName { get; set; } = "";
    public string Image { get; set; } = "";
    public DateTime AddedUtc { get; set; } = DateTime.UtcNow;
    /// <summary>Alert when the best price drops to this many rupees or less.</summary>
    public decimal? TargetPkr { get; set; }
    public Dictionary<string, RegionOffer> Offers { get; set; } = [];
    public List<PricePoint> History { get; set; } = [];
    public LowPoint? Lowest { get; set; }
    /// <summary>The lowest price seen in each region, in that region's own money (so exchange rates can't fake a drop).</summary>
    public Dictionary<string, long> LowestMinor { get; set; } = [];
    public List<string> Platforms { get; set; } = [];
}

public sealed class RegionOffer
{
    public string ProductId { get; set; } = "";
    public bool Available { get; set; }
    public long BaseMinor { get; set; }
    public long SaleMinor { get; set; }
    public long? PlusMinor { get; set; }
    public string Currency { get; set; } = "";
    public DateTime? EndsUtc { get; set; }
    public string SavingTag { get; set; } = "";
    public DateTime CheckedUtc { get; set; }

    public bool OnSale => Available && SaleMinor < BaseMinor;
    public int PercentOff => BaseMinor <= 0 ? 0 : (int)Math.Round(100m * (BaseMinor - SaleMinor) / BaseMinor);
}

/// <summary>A day's price in one region, in rupees (at that day's rate).</summary>
public sealed class PricePoint
{
    public DateOnly Date { get; set; }
    public string Region { get; set; } = "";
    public decimal Pkr { get; set; }
    public long Minor { get; set; }
}

public sealed class LowPoint
{
    public decimal Pkr { get; set; }
    public string Region { get; set; } = "";
    public DateOnly Date { get; set; }
    public long Minor { get; set; }
    public string Currency { get; set; } = "";
}

public enum PriceEvent { NewLow, SaleStarted, HitTarget }

public sealed record RegionPrice(Region Region, RegionOffer Offer, decimal? Pkr);

/// <summary>The wishlist rules: which region is cheapest, history, lowest-ever and alerts. Pure, so it's tested.</summary>
public static class WishLogic
{
    /// <summary>Each region's current price in rupees, cheapest first; regions that don't sell it last.</summary>
    public static List<RegionPrice> Prices(WishItem item, RateSet? rates, decimal? manual) =>
        Region.All
            .Select(r => item.Offers.TryGetValue(r.Code, out var o) ? new RegionPrice(r, o, o.Available ? Rates.ToPkr(o.SaleMinor / 100m, o.Currency, rates, manual) : null) : null)
            .OfType<RegionPrice>()
            .OrderBy(p => p.Pkr is null ? 1 : 0).ThenBy(p => p.Pkr ?? decimal.MaxValue)
            .ToList();

    public static RegionPrice? Best(WishItem item, RateSet? rates, decimal? manual) =>
        Prices(item, rates, manual).FirstOrDefault(p => p.Pkr is not null);

    /// <summary>
    /// Stores a region's fresh price: one history point per region per day, the lowest-ever price,
    /// and the alerts worth telling you about.
    /// </summary>
    public static List<PriceEvent> Record(WishItem item, Region region, RegionOffer offer, RateSet? rates, decimal? manual, DateOnly today)
    {
        var events = new List<PriceEvent>();
        item.Offers.TryGetValue(region.Code, out var before);
        item.Offers[region.Code] = offer;
        if (!offer.Available) return events;
        if (offer.OnSale && before is { OnSale: false } or { Available: false }) events.Add(PriceEvent.SaleStarted);

        var pkr = Rates.ToPkr(offer.SaleMinor / 100m, offer.Currency, rates, manual);
        if (pkr is not { } value) return events;
        item.History.RemoveAll(h => h.Date == today && h.Region == region.Code);
        item.History.Add(new PricePoint { Date = today, Region = region.Code, Pkr = value, Minor = offer.SaleMinor });
        // Keep about a year per region.
        item.History.RemoveAll(h => h.Date < today.AddDays(-400));

        // A new low means the store's own price fell below anything seen there before (rates can't fake one).
        item.LowestMinor ??= [];
        bool seen = item.LowestMinor.TryGetValue(region.Code, out var regionLow);
        if (!seen || offer.SaleMinor < regionLow) item.LowestMinor[region.Code] = offer.SaleMinor;
        if (seen && offer.SaleMinor < regionLow) events.Add(PriceEvent.NewLow);
        // The lowest in rupees across regions is just for display.
        if (item.Lowest is null || value < item.Lowest.Pkr)
            item.Lowest = new LowPoint { Pkr = value, Region = region.Code, Date = today, Minor = offer.SaleMinor, Currency = offer.Currency };
        if (item.TargetPkr is { } target && value <= target && (before is null || !before.Available
            || (Rates.ToPkr(before.SaleMinor / 100m, before.Currency, rates, manual) ?? decimal.MaxValue) > target))
            events.Add(PriceEvent.HitTarget);
        return events;
    }

    /// <summary>Converts an edition's store offer into what's saved.</summary>
    public static RegionOffer FromOffer(string productId, Offer? offer, DateTime nowUtc) => offer is null
        ? new RegionOffer { ProductId = productId, Available = false, CheckedUtc = nowUtc }
        : new RegionOffer
        {
            ProductId = productId, Available = true, BaseMinor = offer.BaseMinor, SaleMinor = offer.SaleMinor, PlusMinor = offer.PlusMinor,
            Currency = offer.Currency, EndsUtc = offer.EndsUtc, SavingTag = offer.SavingTag, CheckedUtc = nowUtc,
        };

    /// <summary>
    /// The same edition in another region's list: the same label (ignoring Sony's regional "SIEA"/"SIEE" ending),
    /// else the same name on the same platforms.
    /// </summary>
    public static Edition? Match(IEnumerable<Edition> editions, string label, string name, IReadOnlyCollection<string>? platforms = null)
    {
        var list = editions.ToList();
        var key = LabelKey(label);
        return list.FirstOrDefault(e => LabelKey(e.Label) == key)
               ?? list.FirstOrDefault(e => Normalize(e.Name) == Normalize(name) && SamePlatforms(e.Platforms, platforms));
    }

    /// <summary>A label with the regional publisher ending removed ("TOTSGAME0000SIEA" and "...SIEE" are the same game).</summary>
    public static string LabelKey(string label)
    {
        var l = (label ?? "").ToUpperInvariant();
        return l.Length > 8 && System.Text.RegularExpressions.Regex.IsMatch(l, "SIE[A-Z]$") ? l[..^4] : l;
    }

    private static bool SamePlatforms(IReadOnlyList<string> a, IReadOnlyCollection<string>? b) =>
        b is null || b.Count == 0 || a.Count == 0 || a.OrderBy(x => x).SequenceEqual(b.OrderBy(x => x), StringComparer.OrdinalIgnoreCase);

    private static string Normalize(string s) => new string((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>Daily checks: due when never checked, or 20 hours have passed (so it drifts no later than a day).</summary>
    public static bool IsCheckDue(DateTime? lastUtc, DateTime nowUtc) => lastUtc is not { } last || nowUtc - last >= TimeSpan.FromHours(20);

    public static string Money(decimal amount, string currency) => currency.ToUpperInvariant() switch
    {
        "USD" => "$" + amount.ToString(amount % 1 == 0 ? "N0" : "N2", System.Globalization.CultureInfo.InvariantCulture),
        "TRY" => amount.ToString("N2", System.Globalization.CultureInfo.InvariantCulture) + " TL",
        "UAH" => amount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " UAH",
        "PKR" => "Rs " + amount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture),
        _ => amount.ToString("N2", System.Globalization.CultureInfo.InvariantCulture) + " " + currency,
    };
}
