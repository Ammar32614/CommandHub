using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Xml.Linq;

namespace CommandHub.AddOn.PlayStation;

/// <summary>How many of each currency one US dollar buys, and where each figure came from.</summary>
public sealed class RateSet
{
    public DateTime FetchedUtc { get; set; }
    public decimal PkrPerUsd { get; set; }
    public string PkrSource { get; set; } = "";
    public decimal TryPerUsd { get; set; }
    public string TrySource { get; set; } = "";
    public decimal UahPerUsd { get; set; }
    public string UahSource { get; set; } = "";

    public bool IsUsable => PkrPerUsd > 0 && TryPerUsd > 0 && UahPerUsd > 0;
}

/// <summary>
/// Exchange rates. Lira and hryvnia use the official rates of the Central Bank of the Republic of Türkiye and the
/// National Bank of Ukraine. The State Bank of Pakistan doesn't publish its rate in a form apps may read, so
/// dollars to rupees uses a free market rate (open.er-api.com) unless you type SBP's rate in yourself.
/// </summary>
public static class Rates
{
    public const string TcmbUrl = "https://www.tcmb.gov.tr/kurlar/today.xml";
    public const string NbuUrl = "https://bank.gov.ua/NBUStatService/v1/statdirectory/exchange?valcode=USD&json";
    public const string MarketUrl = "https://open.er-api.com/v6/latest/USD";

    /// <summary>Rupees for an amount in USD, TRY or UAH. <paramref name="manualPkrPerUsd"/> (SBP's rate, typed in) wins when set.</summary>
    public static decimal? ToPkr(decimal amount, string currency, RateSet? rates, decimal? manualPkrPerUsd = null)
    {
        if (rates is null) return null;
        var pkrPerUsd = manualPkrPerUsd is > 0 ? manualPkrPerUsd.Value : rates.PkrPerUsd;
        if (pkrPerUsd <= 0) return null;
        decimal? usd = currency.ToUpperInvariant() switch
        {
            "USD" => amount,
            "TRY" => rates.TryPerUsd > 0 ? amount / rates.TryPerUsd : null,
            "UAH" => rates.UahPerUsd > 0 ? amount / rates.UahPerUsd : null,
            _ => null,
        };
        return usd is { } u ? Math.Round(u * pkrPerUsd, 0) : null;
    }

    public static decimal? ParseTcmb(string xml)
    {
        try
        {
            var usd = XDocument.Parse(xml).Descendants("Currency").FirstOrDefault(c => (string?)c.Attribute("CurrencyCode") == "USD");
            var selling = usd?.Element("ForexSelling")?.Value;
            return decimal.TryParse(selling, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : null;
        }
        catch (System.Xml.XmlException) { return null; }
    }

    public static decimal? ParseNbu(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var e in doc.RootElement.EnumerateArray())
                if (e.TryGetProperty("cc", out var cc) && cc.GetString() == "USD" && e.TryGetProperty("rate", out var r) && r.TryGetDecimal(out var v) && v > 0)
                    return v;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { }
        return null;
    }

    public static (decimal? Pkr, decimal? Try, decimal? Uah) ParseMarket(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("rates", out var r)) return (null, null, null);
            decimal? Get(string code) => r.TryGetProperty(code, out var v) && v.TryGetDecimal(out var d) && d > 0 ? d : null;
            return (Get("PKR"), Get("TRY"), Get("UAH"));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return (null, null, null); }
    }

    /// <summary>Fetches all three. Each falls back to the market rate, then to <paramref name="previous"/>; null only if nothing is known.</summary>
    public static async Task<RateSet?> FetchAsync(HttpClient http, RateSet? previous, CancellationToken ct = default)
    {
        async Task<string?> Get(string url)
        {
            try { return await http.GetStringAsync(url, ct); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return null; }
        }
        var tcmb = Get(TcmbUrl);
        var nbu = Get(NbuUrl);
        var market = Get(MarketUrl);
        await Task.WhenAll(tcmb, nbu, market);

        var (mPkr, mTry, mUah) = market.Result is { } m ? ParseMarket(m) : (null, null, null);
        var tryOfficial = tcmb.Result is { } t ? ParseTcmb(t) : null;
        var uahOfficial = nbu.Result is { } n ? ParseNbu(n) : null;

        var set = new RateSet { FetchedUtc = DateTime.UtcNow };
        (set.PkrPerUsd, set.PkrSource) = mPkr is { } p ? (p, "Market rate (open.er-api.com)") : (previous?.PkrPerUsd ?? 0, previous?.PkrSource ?? "");
        (set.TryPerUsd, set.TrySource) = tryOfficial is { } to ? (to, "Central Bank of Türkiye (official)")
            : mTry is { } tm ? (tm, "Market rate (open.er-api.com)") : (previous?.TryPerUsd ?? 0, previous?.TrySource ?? "");
        (set.UahPerUsd, set.UahSource) = uahOfficial is { } uo ? (uo, "National Bank of Ukraine (official)")
            : mUah is { } um ? (um, "Market rate (open.er-api.com)") : (previous?.UahPerUsd ?? 0, previous?.UahSource ?? "");
        if (mPkr is null && tryOfficial is null && uahOfficial is null && mTry is null && previous is not null) set.FetchedUtc = previous.FetchedUtc;
        return set.IsUsable ? set : previous;
    }
}
