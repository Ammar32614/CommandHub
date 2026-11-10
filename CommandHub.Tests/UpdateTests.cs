using CommandHub.Services;

namespace CommandHub.Tests;

public class UpdateBackupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hub-upd-" + Guid.NewGuid().ToString("N"));
    private string DataPath => Path.Combine(_dir, "data.json");
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [Fact]
    public void BackupBeforeUpdate_CopiesTheDataFile()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(DataPath, "{\"version\":2}");
        var copy = Updates.BackupBeforeUpdate(DataPath, "1.1.0");
        Assert.NotNull(copy);
        Assert.Equal("{\"version\":2}", File.ReadAllText(copy!));
        Assert.Contains("1.1.0", Path.GetFileName(copy));
        Assert.Equal(Path.Combine(_dir, "backups"), Path.GetDirectoryName(copy));
    }

    [Fact]
    public void BackupBeforeUpdate_KeepsOnlyTheNewestFive()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(DataPath, "{}");
        for (int i = 1; i <= 7; i++)
        {
            Updates.BackupBeforeUpdate(DataPath, $"1.{i}.0");
            Thread.Sleep(20);
        }
        var left = Directory.GetFiles(Path.Combine(_dir, "backups"));
        Assert.Equal(5, left.Length);
        Assert.Contains(left, f => f.Contains("1.7.0"));
        Assert.DoesNotContain(left, f => f.Contains("1.1.0"));
    }

    [Fact]
    public void BackupBeforeUpdate_NoDataYet_IsNull_AndDoesNotThrow() =>
        Assert.Null(Updates.BackupBeforeUpdate(DataPath, "1.1.0"));

    [Theory]
    [InlineData(-1, true)]
    [InlineData(3, false)]
    [InlineData(5, true)]
    public void IsCheckDue_EveryFourHours(int hoursAgo, bool due) =>
        Assert.Equal(due, Updates.IsCheckDue(hoursAgo < 0 ? null : DateTime.UtcNow.AddHours(-hoursAgo), DateTime.UtcNow));
}
