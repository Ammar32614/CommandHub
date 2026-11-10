namespace CommandHub.Tests;

public class ModelTests
{
    [Theory]
    [InlineData(TileSize.Small, 1, 1)]
    [InlineData(TileSize.Wide, 2, 1)]
    [InlineData(TileSize.Tall, 1, 2)]
    [InlineData(TileSize.Large, 2, 2)]
    public void Span_MatchesSpec(TileSize s, int cols, int rows) =>
        Assert.Equal((cols, rows), TileSizes.Span(s));

    [Fact]
    public void CreateDefault_HomeHasOneOfEachStarterTile()
    {
        var d = HubData.CreateDefault();
        var home = Assert.Single(d.Pages);
        Assert.Equal("Home", home.Name);
        Assert.Equal(new[] { "clock", "note", "todo", "links" }, home.Tiles.Select(t => t.TypeId));
        Assert.Equal(1, d.Version);
        Assert.Equal("Midnight", d.Settings.Theme);
    }
}
