using CommandHub.Services;

namespace CommandHub.Tests;

public class DebouncerTests
{
    [Fact]
    public void Flush_RunsPendingActionOnce()
    {
        int n = 0;
        var d = new Debouncer(TimeSpan.FromSeconds(10), () => n++);
        d.Request();
        d.Request();
        Assert.True(d.IsPending);
        d.Flush();
        d.Flush();
        Assert.Equal(1, n);
        Assert.False(d.IsPending);
    }

    [Fact]
    public void Flush_WithNothingPending_DoesNothing()
    {
        int n = 0;
        new Debouncer(TimeSpan.FromSeconds(10), () => n++).Flush();
        Assert.Equal(0, n);
    }

    [Fact]
    public void Request_DoesNotRunImmediately()
    {
        int n = 0;
        var d = new Debouncer(TimeSpan.FromSeconds(10), () => n++);
        d.Request();
        Assert.Equal(0, n);
    }
}
