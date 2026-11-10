using CommandHub.Tiles.Links;
using CommandHub.Tiles.Note;
using CommandHub.Tiles.Todo;

namespace CommandHub.Tests;

public class TileContentTests
{
    [Fact]
    public void Note_ReadMissing_IsEmpty() => Assert.Equal("", NoteContent.Read(new JsonObject()));

    [Fact]
    public void Note_ReadWrongType_IsEmpty() => Assert.Equal("", NoteContent.Read(new JsonObject { ["text"] = 5 }));

    [Fact]
    public void Note_RoundTrip()
    {
        var c = new JsonObject();
        NoteContent.Write(c, "line 1\nline 2");
        Assert.Equal("line 1\nline 2", NoteContent.Read(c));
    }

    [Fact]
    public void Todo_RoundTrip()
    {
        var c = new JsonObject();
        TodoContent.Write(c, [new("Milk", false), new("Gym", true)]);
        Assert.Equal([new TodoItem("Milk", false), new TodoItem("Gym", true)], TodoContent.Read(c));
    }

    [Fact]
    public void Todo_TryAdd_TrimsAndRejectsBlank()
    {
        var l = new List<TodoItem>();
        Assert.False(TodoContent.TryAdd(l, "   "));
        Assert.True(TodoContent.TryAdd(l, "  Call bank "));
        Assert.Equal("Call bank", l.Single().Text);
        Assert.False(l.Single().Done);
    }

    [Fact]
    public void Todo_ReadGarbage_IsEmpty() => Assert.Empty(TodoContent.Read(new JsonObject { ["items"] = "x" }));

    [Fact]
    public void Todo_ReadSkipsBadEntries()
    {
        var c = JsonNode.Parse("""{"items":[null, 3, {"text":"ok","done":true}, {"done":false}]}""")!.AsObject();
        Assert.Equal([new TodoItem("ok", true)], TodoContent.Read(c));
    }

    [Fact]
    public void Links_RoundTrip()
    {
        var c = new JsonObject();
        LinksContent.Write(c, [new("Stint", @"C:\Apps\Stint.exe"), new("News", "https://example.com")]);
        Assert.Equal([new QuickLink("Stint", @"C:\Apps\Stint.exe"), new QuickLink("News", "https://example.com")], LinksContent.Read(c));
    }

    [Fact]
    public void Links_ReadGarbage_IsEmpty() => Assert.Empty(LinksContent.Read(new JsonObject { ["links"] = 1 }));

    [Theory]
    [InlineData("https://example.com", LinkKind.Web)]
    [InlineData("http://example.com/x?y=1", LinkKind.Web)]
    [InlineData("", LinkKind.Invalid)]
    [InlineData("   ", LinkKind.Invalid)]
    [InlineData("C:\\definitely\\missing\\folder", LinkKind.Invalid)]
    [InlineData("ftp://x", LinkKind.Invalid)]
    [InlineData("not a link", LinkKind.Invalid)]
    public void LinkTarget_Classify(string t, LinkKind k) => Assert.Equal(k, LinkTarget.Classify(t));

    [Theory]
    [InlineData("www.example.com", LinkKind.Web)]
    [InlineData("  WWW.Example.com/path ", LinkKind.Web)]
    public void LinkTarget_BareWww_IsWeb(string t, LinkKind k) => Assert.Equal(k, LinkTarget.Classify(t));

    [Theory]
    [InlineData("www.example.com", "https://www.example.com")]
    [InlineData(" https://a.com ", "https://a.com")]
    [InlineData("C:\\Apps", "C:\\Apps")]
    public void LinkTarget_Normalize(string t, string expected) => Assert.Equal(expected, LinkTarget.Normalize(t));

    // Guess never touches the disk, so a link to an offline network share can't freeze the UI while drawing icons.
    [Theory]
    [InlineData("https://example.com", LinkKind.Web)]
    [InlineData("www.example.com", LinkKind.Web)]
    [InlineData(@"\\offline-nas\share\movies", LinkKind.Folder)]
    [InlineData(@"\\offline-nas\share\report.pdf", LinkKind.File)]
    [InlineData(@"C:\Apps\Stint.exe", LinkKind.File)]
    [InlineData(@"C:\Users\me\Documents", LinkKind.Folder)]
    [InlineData("", LinkKind.Invalid)]
    [InlineData("not a link", LinkKind.Invalid)]
    public void LinkTarget_Guess_UsesTextOnly(string t, LinkKind k) => Assert.Equal(k, LinkTarget.Guess(t));

    [Fact]
    public void LinkTarget_ExistingFolder_IsFolder() => Assert.Equal(LinkKind.Folder, LinkTarget.Classify(Path.GetTempPath()));

    [Fact]
    public void LinkTarget_ExistingFile_IsFile()
    {
        var f = Path.GetTempFileName();
        try { Assert.Equal(LinkKind.File, LinkTarget.Classify(f)); }
        finally { File.Delete(f); }
    }

    [Fact]
    public void LinkTarget_TryOpenInvalid_ReturnsErrorWithoutThrowing()
    {
        Assert.False(LinkTarget.TryOpen("C:\\missing\\x.exe", out var err));
        Assert.False(string.IsNullOrWhiteSpace(err));
    }
}
