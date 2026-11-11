using CommandHub.Services;

namespace CommandHub.Tests;

public class DataStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hub-tests-" + Guid.NewGuid().ToString("N"));
    private string PathIn => Path.Combine(_dir, "data.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private void WriteRaw(string json)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathIn, json);
    }

    [Fact]
    public void DefaultPath_IsUnderAppDataCommandHub() =>
        Assert.EndsWith(Path.Combine("CommandHub", "data.json"), DataStore.DefaultPath);

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var d = new DataStore(PathIn).Load();
        Assert.Equal("Home", Assert.Single(d.Pages).Name);
        Assert.Equal(13, d.Pages[0].Tiles.Count);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsTileContentAndUnknownTypes()
    {
        var s = new DataStore(PathIn);
        var d = HubData.CreateDefault();
        d.Pages[0].Tiles.Add(new PlacedTile
        {
            Id = Guid.NewGuid(), TypeId = "stocks", Column = 0, Row = 9, Size = TileSize.Small,
            Content = new JsonObject { ["city"] = "Lahore" },
        });
        d.Pages[0].Tiles.Single(x => x.TypeId == "note").Content["text"] = "hello";
        d.Settings.Theme = "Ocean";

        Assert.True(s.Save(d));
        var loaded = s.Load();
        var t = loaded.Pages[0].Tiles;

        Assert.Equal("hello", (string?)t.Single(x => x.TypeId == "note").Content["text"]);
        Assert.Equal("Lahore", (string?)t.Single(x => x.TypeId == "stocks").Content["city"]);
        Assert.Equal(TileSize.Large, t.Single(x => x.TypeId == "clock").Size);
        Assert.Equal("Ocean", loaded.Settings.Theme);
    }

    [Fact]
    public void Save_WritesEnumsAsStrings()
    {
        new DataStore(PathIn).Save(HubData.CreateDefault());
        Assert.Contains("\"Large\"", File.ReadAllText(PathIn));
    }

    [Fact]
    public void Load_NewerVersion_RefusesToOverwrite()
    {
        WriteRaw("""{"version":99,"pages":[]}""");
        var s = new DataStore(PathIn);
        s.Load();
        Assert.True(s.ReadFailed);
        Assert.False(s.Save(HubData.CreateDefault()));
        Assert.Contains("99", File.ReadAllText(PathIn));
    }

    [Fact]
    public void Load_NewerVersionWithUnknownEnum_RefusesToOverwriteAndIsNotCorrupt()
    {
        var json = """{"version":99,"pages":[{"name":"Home","tiles":[{"typeId":"note","column":0,"row":0,"size":"Huge"}]}]}""";
        WriteRaw(json);
        var s = new DataStore(PathIn);
        s.Load();
        Assert.True(s.ReadFailed);
        Assert.False(s.Save(HubData.CreateDefault()));
        Assert.Equal(json, File.ReadAllText(PathIn));
        Assert.Empty(Directory.GetFiles(_dir, "data.corrupt-*.json"));
    }

    [Fact]
    public void Load_CorruptFile_ReportsWhereTheBackupIs()
    {
        WriteRaw("{ not json");
        var s = new DataStore(PathIn);
        s.Load();
        Assert.NotNull(s.RecoveredBackupPath);
        Assert.True(File.Exists(s.RecoveredBackupPath));
    }

    [Fact]
    public void Load_GoodFile_HasNoBackupPath()
    {
        var s = new DataStore(PathIn);
        s.Save(HubData.CreateDefault());
        s.Load();
        Assert.Null(s.RecoveredBackupPath);
    }

    [Fact]
    public void Load_OverlappingTiles_AreRepaired()
    {
        WriteRaw("""
            {"version":1,"pages":[{"name":"Home","tiles":[
              {"typeId":"note","column":0,"row":0,"size":"Large"},
              {"typeId":"todo","column":0,"row":0,"size":"Large"}]}]}
            """);
        var tiles = new DataStore(PathIn).Load().Pages[0].Tiles;
        Assert.NotEqual((tiles[0].Column, tiles[0].Row), (tiles[1].Column, tiles[1].Row));
    }

    [Fact]
    public void Load_EmptyPages_GetsHome()
    {
        WriteRaw("""{"version":1,"pages":[]}""");
        Assert.Equal("Home", Assert.Single(new DataStore(PathIn).Load().Pages).Name);
    }

    [Fact]
    public void Load_NullEntries_DoNotThrow()
    {
        WriteRaw("""{"version":4,"pages":[null,{"name":null,"tiles":[null,{"typeId":"note","content":null}]}],"settings":null}""");
        var d = new DataStore(PathIn).Load();
        var tile = Assert.Single(Assert.Single(d.Pages).Tiles);
        Assert.NotNull(tile.Content);
        Assert.Equal("Midnight", d.Settings.Theme);
        Assert.False(string.IsNullOrEmpty(d.Pages[0].Name));
    }

    [Fact]
    public void Load_CorruptFile_BacksUpAndReturnsDefaults()
    {
        WriteRaw("{ not json");
        var s = new DataStore(PathIn);
        var d = s.Load();
        Assert.Single(d.Pages);
        Assert.False(s.ReadFailed);
        Assert.Single(Directory.GetFiles(_dir, "data.corrupt-*.json"));
    }

    [Fact]
    public void Load_LockedFile_ReturnsDefaultsAndRefusesToOverwrite()
    {
        var store = new DataStore(PathIn);
        var data = HubData.CreateDefault();
        data.Settings.Theme = "Dusk";
        store.Save(data);
        var original = File.ReadAllText(PathIn);

        HubData loaded;
        using (new FileStream(PathIn, FileMode.Open, FileAccess.Read, FileShare.None))
            loaded = store.Load();

        Assert.Equal("Midnight", loaded.Settings.Theme);
        Assert.True(store.ReadFailed);
        Assert.False(store.Save(loaded));
        Assert.Equal(original, File.ReadAllText(PathIn));
    }
}
