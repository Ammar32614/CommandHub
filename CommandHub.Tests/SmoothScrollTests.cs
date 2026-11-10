using CommandHub.Home;

namespace CommandHub.Tests;

public class SmoothScrollTests
{
    [Fact]
    public void Step_MovesPartWayTowardTarget() =>
        Assert.InRange(SmoothScroll.Step(0, 100, 0.016), 10, 40);

    [Fact]
    public void Step_ArrivesWhenClose() => Assert.Equal(100, SmoothScroll.Step(99.7, 100, 0.016));

    [Fact]
    public void Step_WorksUpward() => Assert.InRange(SmoothScroll.Step(100, 0, 0.016), 60, 90);

    [Fact]
    public void Step_LongFrameDoesNotOvershoot() => Assert.Equal(100, SmoothScroll.Step(0, 100, 5));

    [Theory]
    [InlineData(0, 800, 1.0)]
    [InlineData(400, 800, 0.65)]
    [InlineData(800, 800, 0.3)]
    [InlineData(2000, 800, 0.3)]
    public void HeaderOpacity_FadesAsYouScroll(double offset, double header, double expected) =>
        Assert.Equal(expected, SmoothScroll.HeaderOpacity(offset, header), 2);

    [Theory]
    [InlineData(0, 800, false)]
    [InlineData(300, 800, false)]
    [InlineData(500, 800, true)]
    public void InTiles_AfterHalfTheHeader(double offset, double header, bool expected) =>
        Assert.Equal(expected, SmoothScroll.InTiles(offset, header));
}
