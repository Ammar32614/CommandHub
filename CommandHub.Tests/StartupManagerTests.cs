using CommandHub.Services;
using Microsoft.Win32;

namespace CommandHub.Tests;

public class StartupManagerTests : IDisposable
{
    // A throwaway key so tests never touch the real Run key.
    private readonly string _key = @"Software\CommandHubTests\" + Guid.NewGuid().ToString("N");

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(@"Software\CommandHubTests", throwOnMissingSubKey: false);

    [Fact]
    public void IsEnabled_NoValue_False() =>
        Assert.False(new StartupManager(_key, @"C:\Apps\CommandHub.exe").IsEnabled);

    [Fact]
    public void SetEnabled_True_WritesQuotedPathWithMinimizedFlag()
    {
        var m = new StartupManager(_key, @"C:\Program Files\CommandHub\CommandHub.exe");
        m.SetEnabled(true);

        using var k = Registry.CurrentUser.OpenSubKey(_key)!;
        Assert.Equal("\"C:\\Program Files\\CommandHub\\CommandHub.exe\" --minimized", k.GetValue("CommandHub"));
        Assert.True(m.IsEnabled);
    }

    [Fact]
    public void SetEnabled_False_RemovesValue()
    {
        var m = new StartupManager(_key, @"C:\Apps\CommandHub.exe");
        m.SetEnabled(true);
        m.SetEnabled(false);
        Assert.False(m.IsEnabled);
    }

    [Fact]
    public void IsEnabled_ValuePointsElsewhere_False()
    {
        using (var k = Registry.CurrentUser.CreateSubKey(_key))
            k.SetValue("CommandHub", "\"D:\\Old\\CommandHub.exe\" --minimized");
        Assert.False(new StartupManager(_key, @"C:\Apps\CommandHub.exe").IsEnabled);
    }
}
