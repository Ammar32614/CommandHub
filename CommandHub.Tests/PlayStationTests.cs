using System.Text.Json;
using CommandHub.AddOn.PlayStation;

namespace CommandHub.Tests;

public class PlayStationStoreTests
{
    [Theory]
    [InlineData("$59.99", 59.99)]
    [InlineData("$1,059.99", 1059.99)]
    [InlineData("1.724,50 TL", 1724.50)]
    [InlineData("215,00 TL", 215.00)]
    [InlineData("UAH 1 699,00", 1699.00)]
    [InlineData("UAH 1 699,00", 1699.00)]
    [InlineData("2 599 грн", 2599)]
    [InlineData("Free", 0)]
    public void ShownPrices_InEachStoresFormat(string text, double expected) =>
        Assert.Equal((decimal)expected, Store.ParseShownPrice(text));

    [Theory]
    [InlineData("")]
    [InlineData("Announced")]
    public void ShownPrices_Unreadable_IsNull(string text) => Assert.Null(Store.ParseShownPrice(text));

    const string ConceptJson = """
        { "id": "10010195", "name": "Tales of the Shire",
          "media": [ { "type": "IMAGE", "role": "SCREENSHOT", "url": "https://x/shot.jpg" }, { "type": "IMAGE", "role": "MASTER", "url": "https://x/master.png" } ],
          "products": [
            { "id": "UP4581-PPSA21218_00-TOTSGAME0000SIEA", "name": "Tales of the Shire", "platforms": ["PS5"], "storeDisplayClassification": "FULL_GAME",
              "edition": { "name": "Standard Edition" },
              "webctas": [
                { "type": "ADD_TO_CART", "price": { "basePriceValue": 3499, "discountedValue": 2099, "currencyCode": "USD", "endTime": "1795247940000",
                  "savingTag": "Save 40%", "isTiedToSubscription": false, "serviceBranding": ["NONE"] } },
                { "type": "UPSELL_PS_PLUS_DISCOUNT", "price": { "basePriceValue": 3499, "discountedValue": 1749, "currencyCode": "USD",
                  "isTiedToSubscription": true, "serviceBranding": ["PS_PLUS"] } } ] },
            { "id": "UP4581-PPSA21218_00-TOTSDELUXE000000", "name": "Tales of the Shire Deluxe", "webctas": [] } ] }
        """;

    [Fact]
    public void Concept_ReadsEditionsSaleAndPlusPrice()
    {
        using var doc = JsonDocument.Parse(ConceptJson);
        var c = Store.ParseConcept(doc.RootElement, "USD")!;
        Assert.Equal("10010195", c.Id);
        Assert.Equal("https://x/master.png", c.Image);
        Assert.Equal(2, c.Editions.Count);
        var e = c.Editions[0];
        Assert.Equal("TOTSGAME0000SIEA", e.Label);
        Assert.Equal(3499, e.Offer!.BaseMinor);
        Assert.Equal(2099, e.Offer.SaleMinor);
        Assert.Equal(1749, e.Offer.PlusMinor);
        Assert.Equal(40, e.Offer.PercentOff);
        Assert.Equal(new DateTime(2026, 11, 21, 7, 59, 0, DateTimeKind.Utc), e.Offer.EndsUtc);
        Assert.Null(c.Editions[1].Offer); // not buyable on its own
    }

    [Fact]
    public void Hits_ReadSearchResults()
    {
        using var doc = JsonDocument.Parse("""
            {"data":{"universalSearch":{"results":[
              {"__typename":"Product","id":"EP0700-PPSA04609_00-ELDENRING0000000","name":"ELDEN RING","platforms":["PS4","PS5"],"localizedStoreDisplayClassification":"Full Game",
               "price":{"basePrice":"1.499,00 TL","discountedPrice":"1.499,00 TL","discountText":null,"isFree":false},
               "media":[{"type":"IMAGE","role":"MASTER","url":"https://x/m.png"}]},
              {"__typename":"Concept","id":"123"}]}}}
            """);
        var hit = Assert.Single(Store.ParseHits(doc.RootElement, "universalSearch", "results"));
        Assert.Equal("1.499,00 TL", hit.SalePrice);
        Assert.Equal("https://x/m.png", hit.Image);
    }

    [Fact]
    public void Match_FindsTheSameEditionInAnotherRegion()
    {
        var tr = new[]
        {
            new Edition("EP0700-PPSA04609_00-ELDENRINGSOTEE00", "ELDEN RING Shadow of the Erdtree Edition", "", "", [], null),
            new Edition("EP0700-PPSA04609_00-ELDENRING0000000", "ELDEN RING PS4 & PS5", "", "", [], null),
        };
        Assert.Equal("EP0700-PPSA04609_00-ELDENRING0000000", WishLogic.Match(tr, "ELDENRING0000000", "whatever")!.ProductId);
        Assert.Equal("EP0700-PPSA04609_00-ELDENRINGSOTEE00", WishLogic.Match(tr, "NOPE", "Elden Ring: Shadow of the Erdtree Edition")!.ProductId);
        Assert.Null(WishLogic.Match(tr, "NOPE", "Something else"));
    }
}

public class PlayStationRatesTests
{
    static RateSet R => new() { PkrPerUsd = 280, TryPerUsd = 40, UahPerUsd = 40 };

    [Fact]
    public void ToPkr_ConvertsThroughDollars()
    {
        Assert.Equal(16797, Rates.ToPkr(59.99m, "USD", R));
        Assert.Equal(10493, Rates.ToPkr(1499m, "TRY", R));   // 1499 / 40 * 280
        Assert.Equal(12593, Rates.ToPkr(1799m, "UAH", R));
        Assert.Equal(16500, Rates.ToPkr(59.99m, "USD", R, manualPkrPerUsd: 275.05m));
        Assert.Null(Rates.ToPkr(10, "EUR", R));
        Assert.Null(Rates.ToPkr(10, "USD", null));
    }

    [Fact]
    public void Parse_CentralBankAndMarketFormats()
    {
        Assert.Equal(50.1981m, Rates.ParseTcmb("""<Tarih_Date><Currency CurrencyCode="USD"><ForexBuying>50.1078</ForexBuying><ForexSelling>50.1981</ForexSelling></Currency></Tarih_Date>"""));
        Assert.Equal(45.5013m, Rates.ParseNbu("""[{"r030":840,"rate":45.5013,"cc":"USD"}]"""));
        Assert.Equal((276.95m, 50.24m, 45.58m), Rates.ParseMarket("""{"result":"success","rates":{"PKR":276.95,"TRY":50.24,"UAH":45.58}}"""));
        Assert.Null(Rates.ParseTcmb("<html>"));
        Assert.Null(Rates.ParseNbu("[]"));
    }
}

public class PlayStationWishlistTests
{
    static readonly DateOnly Day = new(2026, 11, 11);
    static RateSet R => new() { PkrPerUsd = 280, TryPerUsd = 40, UahPerUsd = 40 };
    static RegionOffer O(long sale, long @base, string cur) => new() { Available = true, SaleMinor = sale, BaseMinor = @base, Currency = cur, ProductId = "x" };

    [Fact]
    public void Best_IsTheCheapestInRupees()
    {
        var item = new WishItem();
        WishLogic.Record(item, Region.US, O(5999, 5999, "USD"), R, null, Day);
        WishLogic.Record(item, Region.TR, O(149900, 149900, "TRY"), R, null, Day);
        WishLogic.Record(item, Region.UA, new RegionOffer { Available = false }, R, null, Day);
        var prices = WishLogic.Prices(item, R, null);
        Assert.Equal(["TR", "US", "UA"], prices.Select(p => p.Region.Code));
        Assert.Equal("TR", WishLogic.Best(item, R, null)!.Region.Code);
        Assert.Equal("TR", item.Lowest!.Region);
    }

    [Fact]
    public void Record_OnePointPerRegionPerDay_AndAlerts()
    {
        var item = new WishItem { TargetPkr = 9000 };
        Assert.Empty(WishLogic.Record(item, Region.TR, O(149900, 149900, "TRY"), R, null, Day)); // first sighting: no alerts
        WishLogic.Record(item, Region.TR, O(149900, 149900, "TRY"), R, null, Day);
        Assert.Single(item.History);

        var events = WishLogic.Record(item, Region.TR, O(99900, 149900, "TRY"), R, null, Day.AddDays(1));
        Assert.Contains(PriceEvent.SaleStarted, events);
        Assert.Contains(PriceEvent.NewLow, events);
        Assert.Contains(PriceEvent.HitTarget, events); // 999 / 40 * 280 = 6993 ≤ 9000
        Assert.Equal(2, item.History.Count);

        // Same sale the next day: nothing new to say.
        Assert.Empty(WishLogic.Record(item, Region.TR, O(99900, 149900, "TRY"), R, null, Day.AddDays(2)));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(-21.0, true)]
    [InlineData(-5.0, false)]
    public void Check_IsDailyish(double? hoursAgo, bool due) =>
        Assert.Equal(due, WishLogic.IsCheckDue(hoursAgo is { } h ? DateTime.UtcNow.AddHours(h) : null, DateTime.UtcNow));
}
