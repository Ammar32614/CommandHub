using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using CommandHub.AddOns;

namespace CommandHub.Tests;

public class AddOnCatalogTests
{
    const string Catalog = """
        { "addons": [
          { "id": "playstation", "name": "PlayStation", "version": "1.0.0", "api": 1, "minHost": "1.2.0",
            "blurb": "Prices", "icon": "\uE7FC", "url": "playstation-1.0.0.zip", "sha256": "ab", "size": 1000 },
          { "id": "Bad Id!", "name": "x", "version": "1.0.0", "api": 1, "url": "x.zip", "sha256": "ab" },
          { "id": "media", "name": "Media", "version": "nope", "api": 1, "url": "m.zip", "sha256": "ab" },
          { "id": "future", "name": "Future", "version": "2.0.0", "api": 2, "minHost": "1.2.0", "url": "f.zip", "sha256": "ab" },
          null
        ] }
        """;

    [Fact]
    public void Parse_KeepsOnlyWellFormedEntries()
    {
        var list = AddOnCatalog.Parse(Catalog);
        Assert.Equal(["playstation", "future"], list.Select(e => e.Id));
        Assert.Equal("1.0.0", list[0].Version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"addons": 5}""")]
    public void Parse_Junk_IsEmpty(string json) => Assert.Empty(AddOnCatalog.Parse(json));

    [Fact]
    public void Compatible_NeedsSameApiAndNewEnoughHost()
    {
        var list = AddOnCatalog.Parse(Catalog);
        Assert.True(AddOnCatalog.IsCompatible(list[0], "1.2.0"));
        Assert.True(AddOnCatalog.IsCompatible(list[0], "1.10.0"));
        Assert.False(AddOnCatalog.IsCompatible(list[0], "1.1.0"));
        Assert.False(AddOnCatalog.IsCompatible(list[1], "9.0.0")); // api 2
    }

    [Theory]
    [InlineData("1.0.1", "1.0.0", true)]
    [InlineData("1.10.0", "1.9.0", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("0.9", "1.0.0", false)]
    [InlineData("junk", "1.0.0", false)]
    public void IsNewer_ComparesVersionsNumerically(string a, string b, bool expected) =>
        Assert.Equal(expected, AddOnCatalog.IsNewer(a, b));

    [Fact]
    public void ResolveUrl_RelativeNamesSitNextToTheCatalog()
    {
        Assert.Equal("https://github.com/o/r/releases/download/addons/p.zip",
            AddOnCatalog.ResolveUrl("https://github.com/o/r/releases/download/addons/catalog.json", "p.zip"));
        Assert.Equal(@"C:\feed\p.zip", AddOnCatalog.ResolveUrl(@"C:\feed\catalog.json", "p.zip"));
        Assert.Equal("https://x.com/a.zip", AddOnCatalog.ResolveUrl("https://github.com/catalog.json", "https://x.com/a.zip"));
    }
}

public class AddOnFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hub-addons-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private static byte[] Zip(params (string Name, string Text)[] files)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, text) in files)
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open());
                w.Write(text);
            }
        return ms.ToArray();
    }

    private static string Manifest(string id, string version) =>
        $$"""{"id":"{{id}}","name":"{{id}}","version":"{{version}}","api":1,"entry":"{{id}}.dll","blurb":"b","icon":"x"}""";

    private static CatalogEntry Entry(string id, string version, byte[] zip) =>
        new(id, id, version, 1, "1.0.0", "b", "", "x", "z.zip", Convert.ToHexString(SHA256.HashData(zip)), zip.Length);

    [Fact]
    public void Install_ThenListed_AndCurrentVersionWins()
    {
        var files = new AddOnFiles(_root);
        var v1 = Zip(("manifest.json", Manifest("media", "1.0.0")), ("media.dll", "one"));
        files.Install(Entry("media", "1.0.0", v1), v1);
        var v2 = Zip(("manifest.json", Manifest("media", "1.1.0")), ("media.dll", "two"));
        files.Install(Entry("media", "1.1.0", v2), v2);

        var installed = Assert.Single(files.Installed());
        Assert.Equal("1.1.0", installed.Manifest.Version);
        Assert.Equal("two", File.ReadAllText(installed.EntryPath));

        files.CleanUp(); // the old version's folder goes at the next start
        Assert.False(Directory.Exists(Path.Combine(_root, "media", "1.0.0")));
        Assert.True(File.Exists(installed.EntryPath));
    }

    [Fact]
    public void Install_WrongChecksum_Refused_NothingWritten()
    {
        var files = new AddOnFiles(_root);
        var zip = Zip(("manifest.json", Manifest("media", "1.0.0")), ("media.dll", "x"));
        var entry = Entry("media", "1.0.0", zip) with { Sha256 = new string('0', 64) };
        Assert.Throws<AddOnException>(() => files.Install(entry, zip));
        Assert.Empty(files.Installed());
    }

    [Fact]
    public void Install_ManifestMustMatchCatalog()
    {
        var files = new AddOnFiles(_root);
        var zip = Zip(("manifest.json", Manifest("other", "1.0.0")), ("other.dll", "x"));
        Assert.Throws<AddOnException>(() => files.Install(Entry("media", "1.0.0", zip), zip));
        Assert.Empty(files.Installed());
    }

    [Fact]
    public void Install_ZipThatEscapesItsFolder_Refused()
    {
        var files = new AddOnFiles(_root);
        var zip = Zip(("manifest.json", Manifest("media", "1.0.0")), ("../../evil.txt", "x"), ("media.dll", "x"));
        Assert.Throws<AddOnException>(() => files.Install(Entry("media", "1.0.0", zip), zip));
        Assert.False(File.Exists(Path.Combine(_root, "evil.txt")));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(_root)!, "evil.txt")));
    }

    [Fact]
    public void Remove_HidesNow_DeletesAtNextStart_ReinstallUndoes()
    {
        var files = new AddOnFiles(_root);
        var zip = Zip(("manifest.json", Manifest("media", "1.0.0")), ("media.dll", "x"));
        files.Install(Entry("media", "1.0.0", zip), zip);
        files.MarkRemoved("media");
        Assert.Empty(files.Installed());
        Assert.True(files.IsPendingRemoval("media"));

        files.Install(Entry("media", "1.0.0", zip), zip); // changed their mind
        Assert.Single(files.Installed());
        files.CleanUp();
        Assert.Single(files.Installed());

        files.MarkRemoved("media");
        files.CleanUp();
        Assert.False(Directory.Exists(Path.Combine(_root, "media")));
    }

    [Fact]
    public void Installed_IgnoresBrokenFolders()
    {
        Directory.CreateDirectory(Path.Combine(_root, "junk", "1.0.0"));
        File.WriteAllText(Path.Combine(_root, "junk", "current.txt"), "1.0.0");
        Directory.CreateDirectory(Path.Combine(_root, "half"));
        Assert.Empty(new AddOnFiles(_root).Installed());
    }
}
