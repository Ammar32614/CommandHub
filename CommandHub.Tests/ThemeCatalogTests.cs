using CommandHub.Services;

namespace CommandHub.Tests;

public class ThemeCatalogTests
{
    [Fact]
    public void Catalog_HasStintThemesInOrder() =>
        Assert.Equal(new[] { "Midnight", "Daylight", "Paper", "Forest", "Ocean", "Dusk", "After Hours" }, ThemeCatalog.All.Select(t => t.Name));

    [Fact]
    public void Midnight_AccentMatchesStintFocus() =>
        Assert.Equal("#F5B14C", ThemeCatalog.Get("Midnight")["AccentColor"]);

    [Fact]
    public void Get_UnknownName_FallsBackToMidnight() =>
        Assert.Equal("Midnight", ThemeCatalog.Get("nope").Name);

    [Fact]
    public void Get_IsCaseInsensitive() =>
        Assert.Equal("Ocean", ThemeCatalog.Get("ocean").Name);

    [Fact]
    public void EveryTheme_DefinesEveryKey() =>
        Assert.All(ThemeCatalog.All, t => Assert.All(ThemeCatalog.Keys, k => Assert.False(string.IsNullOrEmpty(t[k]))));

    [Fact]
    public void Keys_HaveNoTimerSpecificNames() =>
        Assert.DoesNotContain(ThemeCatalog.Keys, k => k is "FocusColor" or "BreakColor" or "TimerColor" or "StopwatchColor");
}

public class AfterHoursThemeTests
{
    [Fact]
    public void AfterHours_IsDarkWithLimeAccent()
    {
        var t = ThemeCatalog.Get("After Hours");
        Assert.Equal("After Hours", t.Name);
        Assert.False(t.IsLight);
        Assert.Equal("#C7D68E", t["AccentColor"]);
        Assert.Equal("#13191D", t["BgColor"]);
    }
}
