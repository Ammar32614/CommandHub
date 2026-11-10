using CommandHub.Home;
using CommandHub.Layout;
using CommandHub.Tiles.Links;
using CommandHub.Tiles.Todo;
using CommandHub.Tiles.Weather;

namespace CommandHub.Tests;

public class HeroTextTests
{
    [Theory]
    [InlineData(8, "YOUR MORNING, AT YOUR PACE")]
    [InlineData(14, "YOUR AFTERNOON, AT YOUR PACE")]
    [InlineData(21, "YOUR EVENING, AT YOUR PACE")]
    [InlineData(2, "YOUR NIGHT, AT YOUR PACE")]
    public void Eyebrow_FollowsTimeOfDay(int hour, string expected) => Assert.Equal(expected, HeroText.Eyebrow(hour));

    [Fact]
    public void Breadcrumb_IsPageAndDateInCaps() =>
        Assert.Equal("HOME   /   TUESDAY, NOVEMBER 10", HeroText.Breadcrumb("Home", new DateTime(2026, 11, 10)));

    [Fact]
    public void SubLine_WithWeather() =>
        Assert.Equal("9:29 pm   ·   Karachi   ·   24° and clear",
            HeroText.SubLine("9:29 pm", new DateTime(2026, 11, 10), new HeroWeather("Karachi", 24, "Clear", Sky.Clear, false)));

    [Fact]
    public void SubLine_WithoutWeather_ShowsDate() =>
        Assert.Equal("9:29 pm   ·   Tuesday, November 10", HeroText.SubLine("9:29 pm", new DateTime(2026, 11, 10), null));
}

public class HeroDataTests
{
    static Page PageWith(params PlacedTile[] tiles) => new() { Name = "Home", Tiles = tiles.ToList() };

    const string Report = """
        {"current":{"temperature_2m":23.6,"apparent_temperature":25,"weather_code":0,"wind_speed_10m":3,"is_day":0},
         "daily":{"time":["2026-11-10"],"weather_code":[0],"temperature_2m_max":[30],"temperature_2m_min":[20]}}
        """;

    [Fact]
    public void Weather_FromFirstWeatherTileWithAReport()
    {
        var tile = new PlacedTile { TypeId = "weather", Content = new JsonObject { ["place"] = "Karachi, Pakistan", ["lat"] = 24.9, ["lon"] = 67.0, ["report"] = Report } };
        var w = HeroData.Weather(PageWith(tile))!;
        Assert.Equal("Karachi", w.City);
        Assert.Equal(24, w.Temperature);
        Assert.Equal("Clear", w.Description);
        Assert.False(w.IsDay);
    }

    [Fact]
    public void Weather_NoCitySet_IsNull() =>
        Assert.Null(HeroData.Weather(PageWith(new PlacedTile { TypeId = "weather" })));

    [Fact]
    public void Links_FromFirstLinksTile_SkipsEmptyTargets_Capped()
    {
        var c = new JsonObject();
        LinksContent.Write(c, [new("A", "https://a.com"), new("B", ""), new("C", @"C:\"), new("D", "https://d.com"), new("E", "https://e.com"), new("F", "https://f.com")]);
        var links = HeroData.Links(PageWith(new PlacedTile { TypeId = "links", Content = c }), 4);
        Assert.Equal(new[] { "A", "C", "D", "E" }, links.Select(l => l.Name));
    }

    [Fact]
    public void OpenTodos_ListsOnlyUnfinished_WithTheirIndex()
    {
        var c = new JsonObject();
        TodoContent.Write(c, [new("Done one", true), new("Milk", false), new("Gym", false)]);
        var todos = HeroData.OpenTodos(PageWith(new PlacedTile { TypeId = "todo", Content = c }));
        Assert.Equal([(1, "Milk"), (2, "Gym")], todos);
    }

    [Fact]
    public void TickTodo_MarksItDone()
    {
        var c = new JsonObject();
        TodoContent.Write(c, [new("Milk", false), new("Gym", false)]);
        var page = PageWith(new PlacedTile { TypeId = "todo", Content = c });
        Assert.True(HeroData.TickTodo(page, 1));
        Assert.Equal([new TodoItem("Milk", false), new TodoItem("Gym", true)], TodoContent.Read(c));
    }

    [Fact]
    public void TickTodo_BadIndex_DoesNothing()
    {
        var page = PageWith(new PlacedTile { TypeId = "todo" });
        Assert.False(HeroData.TickTodo(page, 5));
    }

    [Fact]
    public void PinLink_AddsToFirstLinksTile()
    {
        var page = PageWith(new PlacedTile { TypeId = "links" }, new PlacedTile { TypeId = "links", Row = 1 });
        Assert.True(HeroData.PinLink(page, " Mail ", " www.example.com "));
        Assert.Equal([new QuickLink("Mail", "www.example.com")], LinksContent.Read(page.Tiles[0].Content));
        Assert.Empty(LinksContent.Read(page.Tiles[1].Content));
    }

    [Fact]
    public void PinLink_NoLinksTile_AddsOneInAFreeSpot()
    {
        var page = PageWith(new PlacedTile { TypeId = "clock", Size = TileSize.Large });
        Assert.True(HeroData.PinLink(page, "", "https://a.com"));
        var links = Assert.Single(page.Tiles, t => t.TypeId == "links");
        Assert.True(BoardGrid.Fits(page.Tiles, links.Column, links.Row, links.Size, links.Id));
        Assert.Equal("a.com", LinksContent.Read(links.Content).Single().Name);
    }

    [Fact]
    public void PinLink_EmptyTarget_IsRejected() =>
        Assert.False(HeroData.PinLink(PageWith(), "x", "  "));
}
