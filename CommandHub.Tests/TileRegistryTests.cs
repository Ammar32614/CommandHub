using CommandHub.Tiles;

namespace CommandHub.Tests;

public class TileRegistryTests
{
    [Fact]
    public void Registry_HasFourStarterTypes() =>
        Assert.Equal(new[] { "clock", "note", "todo", "links" }, TileRegistry.All.Select(t => t.TypeId));

    [Fact]
    public void Registry_UnknownType_IsNull() => Assert.Null(TileRegistry.Find("weather"));

    [Fact]
    public void Registry_Find_ReturnsType() => Assert.Equal("note", TileRegistry.Find("note")?.TypeId);

    [Fact]
    public void Registry_DefaultSizeIsAllowed() =>
        Assert.All(TileRegistry.All, t => Assert.Contains(t.DefaultSize, t.AllowedSizes));

    [Fact]
    public void Clock_IsNeverTall() =>
        Assert.DoesNotContain(TileSize.Tall, TileRegistry.Find("clock")!.AllowedSizes);

    [Theory]
    [InlineData("clock", TileSize.Large)]
    [InlineData("note", TileSize.Wide)]
    [InlineData("todo", TileSize.Tall)]
    [InlineData("links", TileSize.Wide)]
    public void DefaultSizes_MatchPlan(string id, TileSize size) =>
        Assert.Equal(size, TileRegistry.Find(id)!.DefaultSize);
}
