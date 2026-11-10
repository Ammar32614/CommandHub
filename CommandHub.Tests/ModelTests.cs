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

}
