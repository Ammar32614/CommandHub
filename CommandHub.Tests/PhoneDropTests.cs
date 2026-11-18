using System.Net.Http;
using System.Text;
using CommandHub.AddOn.PhoneDrop;

namespace CommandHub.Tests;

public class QrTests
{
    [Theory]
    [InlineData("hi", 21)]                                                           // version 1
    [InlineData("http://192.168.100.123:48123/?k=Q2xhdWRlUGhvbmVEcm9w", 33)]           // version 4
    [InlineData("https://example.com/a-somewhat-longer-address/that/needs/a-bigger-code?with=query&and=more-stuff-here", 41)] // version 6
    public void Size_FollowsTheVersion(string text, int size) => Assert.Equal(size, Qr.Encode(text).GetLength(0));

    [Fact]
    public void FinderPatterns_AndTimingAreInPlace()
    {
        var m = Qr.Encode("http://192.168.1.5:48123/?k=abc");
        int n = m.GetLength(0);
        foreach (var (r, c) in new[] { (0, 0), (0, n - 7), (n - 7, 0) })
        {
            for (int i = 0; i < 7; i++)
            {
                Assert.True(m[r, c + i]); Assert.True(m[r + 6, c + i]); // top and bottom edges
                Assert.True(m[r + i, c]); Assert.True(m[r + i, c + 6]); // left and right edges
            }
            Assert.False(m[r + 1, c + 1]);
            Assert.True(m[r + 3, c + 3]);                               // centre
        }
        for (int i = 8; i < n - 8; i++) Assert.Equal(i % 2 == 0, m[6, i]); // timing
        Assert.True(m[n - 8, 8]);                                          // the dark module
    }

    [Fact]
    public void TooLong_Throws() => Assert.Throws<ArgumentException>(() => Qr.Encode(new string('x', 300)));

    /// <summary>Writes SVGs to decode in a browser by hand (set QR_SVG_DIR). Not a check on its own.</summary>
    [Fact]
    public void ExportForDecoding()
    {
        var dir = Environment.GetEnvironmentVariable("QR_SVG_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        string[] samples =
        [
            "hi",
            "http://192.168.100.123:48123/?k=Q2xhdWRlUGhvbmVEcm9w",
            "https://example.com/a-somewhat-longer-address/that/needs/a-bigger-code?with=query&and=more-stuff-here",
            "Phone drop: " + new string('z', 150),
        ];
        int[] masks = [0, 1, 2, 3, 4, 5, 6, 7];
        var all = samples.SelectMany(t => masks.Select(mk => (Text: t, Mask: mk))).ToArray();
        for (int s = 0; s < all.Length; s++)
        {
            var m = Qr.Encode(all[s].Text, all[s].Mask);
            int n = m.GetLength(0), q = 4;
            var svg = new StringBuilder($"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 {n + 2 * q} {n + 2 * q}' width='{(n + 2 * q) * 8}' height='{(n + 2 * q) * 8}' shape-rendering='crispEdges'><rect width='100%' height='100%' fill='#fff'/>");
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) if (m[y, x]) svg.Append($"<rect x='{x + q}' y='{y + q}' width='1' height='1'/>");
            svg.Append("</svg>");
            File.WriteAllText(Path.Combine(dir, $"qr{s}.svg"), svg.ToString());
            File.WriteAllText(Path.Combine(dir, $"qr{s}.txt"), all[s].Text);
        }
    }
}

public class DropLogicTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "drop-" + Guid.NewGuid().ToString("N"));
    public DropLogicTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    [Theory]
    [InlineData("IMG_2041.HEIC", "IMG_2041.HEIC")]
    [InlineData("../../Windows/evil.exe", "evil.exe")]
    [InlineData(@"C:\Users\x\photo.jpg", "photo.jpg")]
    [InlineData("a:b*c?.txt", "a_b_c_.txt")]
    [InlineData("CON.txt", "_CON.txt")]
    [InlineData("...", "file-")]
    [InlineData("", "file-")]
    public void SafeName_KeepsFilesInTheFolder(string input, string expectedStart) =>
        Assert.StartsWith(expectedStart, DropLogic.SafeName(input));

    [Fact]
    public void UniquePath_NeverOverwrites()
    {
        File.WriteAllText(Path.Combine(_dir, "photo.jpg"), "x");
        File.WriteAllText(Path.Combine(_dir, "photo (2).jpg.part"), "x"); // one still arriving
        Assert.Equal(Path.Combine(_dir, "photo (3).jpg"), DropLogic.UniquePath(_dir, "photo.jpg"));
        Assert.Equal(Path.Combine(_dir, "new.mp4"), DropLogic.UniquePath(_dir, "new.mp4"));
    }

    [Theory]
    [InlineData("192.168.1.20", true)]
    [InlineData("10.0.0.7", true)]
    [InlineData("172.20.1.1", true)]
    [InlineData("172.32.1.1", false)]
    [InlineData("100.101.5.6", true)]   // Tailscale
    [InlineData("127.0.0.1", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("::ffff:192.168.0.4", true)]
    public void OnlyLocalDevices(string ip, bool local) => Assert.Equal(local, DropLogic.IsLocal(System.Net.IPAddress.Parse(ip)));

    [Fact]
    public void Parse_ReadsPathQueryAndHeaders()
    {
        var r = DropLogic.Parse("POST /api/upload?k=abc&name=My%20Photo.jpg HTTP/1.1\r\nHost: 192.168.1.5:48123\r\nContent-Length: 1234\r\nX-Thing:  y ")!;
        Assert.Equal("POST", r.Method);
        Assert.Equal("/api/upload", r.Path);
        Assert.Equal("My Photo.jpg", r.Query["name"]);
        Assert.Equal("abc", r.Query["k"]);
        Assert.Equal(1234, r.ContentLength);
        Assert.Equal("y", r.Headers["x-thing"]);
        Assert.Null(DropLogic.Parse("garbage"));
        Assert.Equal(-1, DropLogic.Parse("GET / HTTP/1.1")!.ContentLength);
    }

    [Fact]
    public void Secrets()
    {
        var k = DropLogic.NewKey();
        Assert.Equal(22, k.Length);
        Assert.DoesNotContain('+', k);
        Assert.True(DropLogic.SameSecret(k, k));
        Assert.False(DropLogic.SameSecret(k, k + "x"));
        Assert.False(DropLogic.SameSecret(null, k));
        Assert.Matches("^[0-9]{6}$", DropLogic.NewPin());
        Assert.Equal("3.4 MB", DropLogic.Size(3_565_158));
    }
}

public class DropServerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dropsrv-" + Guid.NewGuid().ToString("N"));
    private readonly DropService _svc;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private string Base => $"http://127.0.0.1:{_svc.Port}";

    public DropServerTests()
    {
        _svc = new DropService(new CommandHub.AddOns.AddOnHost(Path.Combine(_root, "data")));
        _svc.Load();
        _svc.Settings.Folder = Path.Combine(_root, "inbox");
        _svc.Settings.Port = 48500 + Random.Shared.Next(400);
        Assert.True(_svc.Start());
    }

    public void Dispose()
    {
        _svc.Stop();
        _http.Dispose();
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public async Task Page_Pairing_Upload_AndDownload()
    {
        // The page itself is open; the API isn't without the key.
        var page = await _http.GetStringAsync(Base + "/");
        Assert.Contains("PHONE DROP", page);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await _http.GetAsync(Base + "/api/state")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await _http.GetAsync(Base + "/api/state?k=wrong")).StatusCode);

        // Wrong PIN, then the right one gives the key.
        var wrong = _svc.Pin == "000000" ? "111111" : "000000";
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await _http.PostAsync(Base + "/api/pair", new StringContent("pin=" + wrong))).StatusCode);
        var pair = await _http.PostAsync(Base + "/api/pair", new StringContent("pin=" + _svc.Pin));
        Assert.True(pair.IsSuccessStatusCode);
        var key = System.Text.Json.JsonDocument.Parse(await pair.Content.ReadAsStringAsync()).RootElement.GetProperty("key").GetString()!;
        Assert.Equal(_svc.Settings.Key, key);

        // A 30 MB "video" arrives intact under its own name, and a second one doesn't overwrite it.
        var data = new byte[30 * 1024 * 1024];
        Random.Shared.NextBytes(data);
        for (int i = 0; i < 2; i++)
        {
            var up = await _http.PostAsync($"{Base}/api/upload?k={key}&name={Uri.EscapeDataString("Holiday clip.mp4")}", new ByteArrayContent(data));
            Assert.True(up.IsSuccessStatusCode, await up.Content.ReadAsStringAsync());
        }
        var first = Path.Combine(_svc.Settings.Folder, "Holiday clip.mp4");
        Assert.Equal(System.Security.Cryptography.SHA256.HashData(data), System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(first)));
        Assert.True(File.Exists(Path.Combine(_svc.Settings.Folder, "Holiday clip (2).mp4")));
        Assert.Empty(Directory.GetFiles(_svc.Settings.Folder, "*.part"));
        Assert.Equal(2, _svc.Snapshot().History.Count);

        // A name that tries to escape the folder lands inside it.
        var evil = await _http.PostAsync($"{Base}/api/upload?k={key}&name={Uri.EscapeDataString("../../evil.txt")}", new StringContent("x"));
        Assert.True(evil.IsSuccessStatusCode);
        Assert.True(File.Exists(Path.Combine(_svc.Settings.Folder, "evil.txt")));
        Assert.False(File.Exists(Path.Combine(_root, "evil.txt")));

        // A file shared from the PC downloads with its name.
        var shared = Path.Combine(_root, "notes ü.pdf");
        File.WriteAllBytes(shared, [1, 2, 3, 4, 5]);
        _svc.AddOutgoing([shared]);
        var state = System.Text.Json.JsonDocument.Parse(await _http.GetStringAsync($"{Base}/api/state?k={key}")).RootElement;
        var id = state.GetProperty("files")[0].GetProperty("id").GetString();
        var dl = await _http.GetAsync($"{Base}/api/file/{id}?k={key}");
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, await dl.Content.ReadAsByteArrayAsync());
        Assert.Equal("notes ü.pdf", dl.Content.Headers.ContentDisposition!.FileNameStar);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await _http.GetAsync($"{Base}/api/file/{id}")).StatusCode);
    }

    [Fact]
    public async Task NewPairing_LocksOutOldPhones()
    {
        var old = _svc.Settings.Key;
        _svc.NewPairing();
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await _http.GetAsync($"{Base}/api/state?k={old}")).StatusCode);
        Assert.True((await _http.GetAsync($"{Base}/api/state?k={_svc.Settings.Key}")).IsSuccessStatusCode);
    }
}
