using CommandHub.Layout;
using CommandHub.Services;

namespace CommandHub.Tests;

public class HomeUpgradeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hub-upgrade-" + Guid.NewGuid().ToString("N"));
    private string PathIn => Path.Combine(_dir, "data.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    static readonly string[] NewHomeTypes =
        ["greeting", "weather", "progress", "clock", "focus", "system", "todo", "habits", "note", "links", "countdown", "upnext", "news"];

    [Fact]
    public void CreateDefault_IsCurrentVersionWithVibrantHome()
    {
        var d = HubData.CreateDefault();
        Assert.Equal(HubData.CurrentVersion, d.Version);
        var tiles = d.Pages[0].Tiles;
        Assert.Equal(NewHomeTypes.OrderBy(x => x), tiles.Select(t => t.TypeId).OrderBy(x => x));
        foreach (var t in tiles) Assert.True(BoardGrid.Fits(tiles, t.Column, t.Row, t.Size, t.Id), $"{t.TypeId} overlaps");
        Assert.True(tiles.All(t => t.Row + TileSizes.Span(t.Size).Rows <= 5), "home should fit in 5 rows");
    }

    private static string V1(string tilesJson) => "{\"version\":1,\"pages\":[{\"name\":\"Home\",\"tiles\":[" + tilesJson + "]}]}";

    private const string UntouchedV1Tiles = """
        {"id":"11111111-1111-1111-1111-111111111111","typeId":"clock","column":0,"row":0,"size":"Large"},
        {"id":"22222222-2222-2222-2222-222222222222","typeId":"note","column":2,"row":0,"size":"Wide","content":{"text":"keep me"}},
        {"id":"33333333-3333-3333-3333-333333333333","typeId":"todo","column":4,"row":0,"size":"Tall","content":{"items":[{"text":"Milk","done":true}]}},
        {"id":"44444444-4444-4444-4444-444444444444","typeId":"links","column":2,"row":1,"size":"Wide","content":{"links":[{"name":"News","target":"https://example.com"}]}}
        """;

    [Fact]
    public void Upgrade_UntouchedV1Home_GetsNewLayoutAndKeepsContent()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathIn, V1(UntouchedV1Tiles));
        var store = new DataStore(PathIn);
        var d = store.Load();

        Assert.Equal(HubData.CurrentVersion, d.Version);
        Assert.False(store.ReadFailed);
        var tiles = d.Pages[0].Tiles;
        Assert.Equal(NewHomeTypes.OrderBy(x => x), tiles.Select(t => t.TypeId).OrderBy(x => x));
        var note = tiles.Single(t => t.TypeId == "note");
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), note.Id);
        Assert.Equal("keep me", (string?)note.Content["text"]);
        Assert.Equal((0, 3), (note.Column, note.Row));
        Assert.Equal("Milk", (string?)tiles.Single(t => t.TypeId == "todo").Content["items"]![0]!["text"]);
        Assert.Equal("News", (string?)tiles.Single(t => t.TypeId == "links").Content["links"]![0]!["name"]);
    }

    [Fact]
    public void Upgrade_RearrangedV1Home_KeepsPositionsAndAddsNewTilesInFreeSpots()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathIn, V1("""
            {"typeId":"note","column":0,"row":0,"size":"Wide","content":{"text":"mine"}},
            {"typeId":"note","column":2,"row":0,"size":"Wide"},
            {"typeId":"clock","column":4,"row":0,"size":"Small"}
            """));
        var tiles = new DataStore(PathIn).Load().Pages[0].Tiles;

        Assert.Equal((0, 0), (tiles[0].Column, tiles[0].Row));
        Assert.Equal((4, 0), (tiles[2].Column, tiles[2].Row));
        Assert.Equal(TileSize.Small, tiles[2].Size);
        foreach (var type in new[] { "greeting", "weather", "progress", "focus", "system", "countdown", "habits" })
            Assert.Single(tiles, t => t.TypeId == type);
        Assert.DoesNotContain(tiles, t => t.TypeId == "todo"); // the user removed it; don't bring it back
        foreach (var t in tiles) Assert.True(BoardGrid.Fits(tiles, t.Column, t.Row, t.Size, t.Id), $"{t.TypeId} overlaps");
    }

    [Fact]
    public void Upgrade_IsSavedAsCurrentVersion()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathIn, V1(UntouchedV1Tiles));
        var store = new DataStore(PathIn);
        store.Save(store.Load());
        Assert.Contains("\"version\": 4", File.ReadAllText(PathIn));
        Assert.Equal(13, new DataStore(PathIn).Load().Pages[0].Tiles.Count);
    }

    [Fact]
    public void Load_Version2_IsNotUpgradedAgain()
    {
        var store = new DataStore(PathIn);
        var d = HubData.CreateDefault();
        d.Pages[0].Tiles.RemoveAll(t => t.TypeId == "weather");
        store.Save(d);
        Assert.DoesNotContain(store.Load().Pages[0].Tiles, t => t.TypeId == "weather");
    }
}

public class UpgradeFlagTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hub-flag-" + Guid.NewGuid().ToString("N"));
    private string PathIn => Path.Combine(_dir, "data.json");
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [Fact]
    public void Load_V1File_ReportsUpgraded()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathIn, """{"version":1,"pages":[{"name":"Home","tiles":[]}]}""");
        var store = new CommandHub.Services.DataStore(PathIn);
        store.Load();
        Assert.True(store.Upgraded);
    }

    [Fact]
    public void Load_CurrentFile_NotUpgraded()
    {
        var store = new CommandHub.Services.DataStore(PathIn);
        store.Save(HubData.CreateDefault());
        store.Load();
        Assert.False(store.Upgraded);
    }
}

public class UpgradeBackupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hub-bak-" + Guid.NewGuid().ToString("N"));
    private string PathIn => Path.Combine(_dir, "data.json");
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [Fact]
    public void FirstSaveAfterUpgrade_KeepsACopyOfTheOldFile()
    {
        Directory.CreateDirectory(_dir);
        const string v1 = """{"version":1,"pages":[{"name":"Home","tiles":[]}]}""";
        File.WriteAllText(PathIn, v1);
        var store = new CommandHub.Services.DataStore(PathIn);
        store.Save(store.Load());
        Assert.Equal(v1, File.ReadAllText(Path.Combine(_dir, "data.v1.json")));
        Assert.Contains("\"version\": 4", File.ReadAllText(PathIn));
    }

    [Fact]
    public void NormalSave_MakesNoBackup()
    {
        var store = new CommandHub.Services.DataStore(PathIn);
        store.Save(HubData.CreateDefault());
        store.Save(store.Load());
        Assert.Empty(Directory.GetFiles(_dir, "data.v*.json"));
    }

    [Fact]
    public void Upgrade_V3_AddsNews_AndBringsBackWeather_WithoutMovingAnything()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathIn, """
            {"version":3,"pages":[{"name":"Home","tiles":[
              {"id":"11111111-1111-1111-1111-111111111111","typeId":"clock","column":0,"row":0,"size":"Large"},
              {"id":"22222222-2222-2222-2222-222222222222","typeId":"note","column":2,"row":0,"size":"Wide"}]}]}
            """);
        var home = new DataStore(PathIn).Load().Pages[0];
        Assert.Contains(home.Tiles, t => t.TypeId == "news");
        Assert.Contains(home.Tiles, t => t.TypeId == "weather");
        Assert.Contains(home.Tiles, t => t.TypeId == "clock" && t.Column == 0 && t.Row == 0);
        Assert.Contains(home.Tiles, t => t.TypeId == "note" && t.Column == 2 && t.Row == 0);
        foreach (var t in home.Tiles) Assert.True(BoardGrid.Fits(home.Tiles, t.Column, t.Row, t.Size, t.Id), $"{t.TypeId} overlaps");
    }

    [Fact]
    public void Upgrade_V4_DoesNotReaddARemovedNewsTile()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathIn, """{"version":4,"pages":[{"name":"Home","tiles":[{"typeId":"clock","column":0,"row":0,"size":"Large"}]}]}""");
        Assert.DoesNotContain(new DataStore(PathIn).Load().Pages[0].Tiles, t => t.TypeId == "news");
    }
}
