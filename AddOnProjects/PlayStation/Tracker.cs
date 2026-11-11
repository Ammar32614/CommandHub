using System.IO;
using System.Net.Http;
using CommandHub.AddOns;

namespace CommandHub.AddOn.PlayStation;

/// <summary>Owns the add-on's data: the wishlist, rates and the once-a-day price check.</summary>
public sealed class Tracker(IAddOnHost host)
{
    private const string File = "playstation";
    public Store Store { get; } = new(host.Http);
    public PsData Data { get; private set; } = new();
    public bool Checking { get; private set; }
    public event Action? Changed;

    public void Load()
    {
        Data = host.Load<PsData>(File);
        Data.Wishlist = (Data.Wishlist ?? []).Where(w => w is not null && w.ConceptId.Length > 0).ToList();
        foreach (var w in Data.Wishlist)
        {
            w.Offers ??= [];
            w.History ??= [];
        }
    }

    public void Save()
    {
        try { host.Save(File, Data); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { host.Notify("PlayStation: couldn't save. " + ex.Message); }
    }

    private void Raise() => Changed?.Invoke();

    public decimal? Pkr(decimal amount, string currency) => Rates.ToPkr(amount, currency, Data.Rates, Data.ManualPkrPerUsd);

    public bool IsWished(string conceptId, string label) =>
        Data.Wishlist.Any(w => w.ConceptId == conceptId && w.Label.Equals(label, StringComparison.OrdinalIgnoreCase));

    public async Task EnsureRates()
    {
        if (Data.Rates is { } r && DateTime.UtcNow - r.FetchedUtc < TimeSpan.FromHours(12)) return;
        Data.Rates = await Rates.FetchAsync(host.Http, Data.Rates);
        Save();
        Raise();
    }

    /// <summary>The game in every region: the region you found it in first, then the others by concept ID.</summary>
    public async Task<Dictionary<string, Concept?>> LoadEverywhere(string productId, Region foundIn)
    {
        var first = await Store.ConceptOfProduct(productId, foundIn);
        var result = new Dictionary<string, Concept?> { [foundIn.Code] = first };
        if (first is null) return result;
        var others = Region.All.Where(r => r != foundIn).Select(async r =>
        {
            try { return (r.Code, Concept: await Store.ConceptById(first.Id, r)); }
            catch (HttpRequestException) { return (r.Code, Concept: (Concept?)null); }
            catch (TaskCanceledException) { return (r.Code, Concept: (Concept?)null); }
        });
        foreach (var (code, concept) in await Task.WhenAll(others)) result[code] = concept;
        return result;
    }

    public void Add(Concept concept, Edition edition, Dictionary<string, Concept?> everywhere)
    {
        if (IsWished(concept.Id, edition.Label)) return;
        var item = new WishItem
        {
            ConceptId = concept.Id, Label = edition.Label, Name = edition.Name.Length > 0 ? edition.Name : concept.Name,
            EditionName = edition.EditionName, Image = concept.Image,
        };
        var today = DateOnly.FromDateTime(DateTime.Today);
        foreach (var region in Region.All)
        {
            var match = everywhere.GetValueOrDefault(region.Code) is { } c ? WishLogic.Match(c.Editions, edition.Label, edition.Name) : null;
            WishLogic.Record(item, region, WishLogic.FromOffer(match?.ProductId ?? "", match?.Offer, DateTime.UtcNow), Data.Rates, Data.ManualPkrPerUsd, today);
        }
        Data.Wishlist.Insert(0, item);
        Save();
        Raise();
    }

    public void Remove(WishItem item)
    {
        Data.Wishlist.Remove(item);
        Save();
        Raise();
    }

    public void SetTarget(WishItem item, decimal? target)
    {
        item.TargetPkr = target is > 0 ? target : null;
        Save();
        Raise();
    }

    public void SetManualRate(decimal? rate)
    {
        Data.ManualPkrPerUsd = rate is > 0 ? rate : null;
        Save();
        Raise();
    }

    public void Touch() { Save(); Raise(); }

    /// <summary>The daily check: fresh rates, then every wishlist game in every region. Tells you about sales, new lows and targets hit.</summary>
    public async Task CheckAll(bool quiet = false)
    {
        if (Checking) return;
        Checking = true;
        Raise();
        var news = new List<string>();
        try
        {
            Data.Rates = await Rates.FetchAsync(host.Http, Data.Rates);
            var today = DateOnly.FromDateTime(DateTime.Today);
            foreach (var item in Data.Wishlist.ToList())
            {
                foreach (var region in Region.All)
                {
                    Concept? concept;
                    try { concept = await Store.ConceptById(item.ConceptId, region); }
                    catch (HttpRequestException) { continue; }   // try this region again tomorrow
                    catch (TaskCanceledException) { continue; }
                    var edition = concept is null ? null : WishLogic.Match(concept.Editions, item.Label, item.Name);
                    if (concept is not null && item.Image.Length == 0) item.Image = concept.Image;
                    var events = WishLogic.Record(item, region, WishLogic.FromOffer(edition?.ProductId ?? "", edition?.Offer, DateTime.UtcNow), Data.Rates, Data.ManualPkrPerUsd, today);
                    var price = item.Offers.TryGetValue(region.Code, out var o) && o.Available ? Pkr(o.SaleMinor / 100m, o.Currency) : null;
                    var where = $"{item.Name} in {region.Name}";
                    if (events.Contains(PriceEvent.HitTarget)) news.Add($"{where} is down to {WishLogic.Money(price ?? 0, "PKR")}, your target");
                    else if (events.Contains(PriceEvent.NewLow)) news.Add($"{where} is at its lowest yet: {WishLogic.Money(price ?? 0, "PKR")}");
                    else if (events.Contains(PriceEvent.SaleStarted)) news.Add($"{where} is {o!.PercentOff}% off");
                }
            }
            Data.LastCheckUtc = DateTime.UtcNow;
            Data.LastError = "";
        }
        catch (StoreChangedException ex) { Data.LastError = ex.Message; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { Data.LastError = "Couldn't reach the PlayStation Store. Trying again later."; }
        finally
        {
            Checking = false;
            Save();
            Raise();
        }
        if (!quiet && news.Count > 0)
            host.Notify("PlayStation: " + string.Join(". ", news.Take(3)) + (news.Count > 3 ? $", and {news.Count - 3} more." : "."));
    }
}
