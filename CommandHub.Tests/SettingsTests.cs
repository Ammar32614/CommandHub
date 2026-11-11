using CommandHub.Models;
using CommandHub.Settings;

namespace CommandHub.Tests;

public class SettingsTests
{
    [Theory]
    [InlineData("", "john.smith", "John")]
    [InlineData("  ", "ammar", "Ammar")]
    [InlineData("Ammar", "john.smith", "Ammar")]
    [InlineData(" Sam ", "x", "Sam")]
    public void DisplayName_PrefersTheTypedName(string typed, string windowsUser, string expected) =>
        Assert.Equal(expected, SettingsLogic.DisplayName(typed, windowsUser));

    static readonly (double L, double T, double W, double H) Screen = (0, 0, 1920, 1080);

    [Fact]
    public void Placement_OnScreen_IsKept()
    {
        var p = new WindowPlacement { Left = 100, Top = 80, Width = 1200, Height = 800 };
        Assert.Same(p, SettingsLogic.FitOnScreen(p, Screen));
    }

    [Fact]
    public void Placement_OffScreen_IsDropped()
    {
        // Saved on a second monitor that's gone now.
        Assert.Null(SettingsLogic.FitOnScreen(new WindowPlacement { Left = 2500, Top = 100, Width = 1200, Height = 800 }, Screen));
    }

    [Fact]
    public void Placement_TooSmallOrBroken_IsDropped()
    {
        Assert.Null(SettingsLogic.FitOnScreen(new WindowPlacement { Left = 10, Top = 10, Width = 50, Height = 40 }, Screen));
        Assert.Null(SettingsLogic.FitOnScreen(new WindowPlacement { Left = double.NaN, Top = 10, Width = 900, Height = 600 }, Screen));
        Assert.Null(SettingsLogic.FitOnScreen(null, Screen));
    }

    [Fact]
    public void Placement_TitleBarAboveScreen_IsDropped() =>
        Assert.Null(SettingsLogic.FitOnScreen(new WindowPlacement { Left = 100, Top = -300, Width = 1200, Height = 800 }, Screen));

    [Theory]
    [InlineData(0.2, 0.9)]
    [InlineData(1.0, 1.0)]
    [InlineData(1.12, 1.1)]
    [InlineData(9, 1.25)]
    [InlineData(double.NaN, 1.0)]
    public void TextScale_SnapsToAStep(double saved, double expected) => Assert.Equal(expected, SettingsLogic.SnapScale(saved));

    [Fact]
    public void OldSettingsFile_GetsDefaults()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hubset-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "data.json");
        File.WriteAllText(path, """{"version":3,"pages":[],"settings":{"theme":"Dusk","hiddenTabs":null,"textScale":7,"startPage":null,"name":null}}""");
        var s = new CommandHub.Services.DataStore(path).Load().Settings;
        Assert.Equal("Dusk", s.Theme);
        Assert.Empty(s.HiddenTabs);
        Assert.Equal(1.25, s.TextScale);
        Assert.Equal("Home", s.StartPage);
        Assert.Equal("", s.Name);
        Assert.True(s.AutoUpdate);
        Directory.Delete(dir, true);
    }
}
