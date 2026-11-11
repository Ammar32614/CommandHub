using CommandHub.Tiles.News;

namespace CommandHub.Tests;

public class NewsTests
{
    const string Rss = """
        <?xml version="1.0"?>
        <rss version="2.0"><channel><title>Dawn - Home</title>
          <item><title><![CDATA[Budget talks <b>resume</b> &amp; more]]></title><link>https://www.dawn.com/news/1</link><pubDate>Wed, 11 Nov 2026 11:45:01 +0500</pubDate></item>
          <item><title>Second</title><link> https://www.dawn.com/news/2 </link><pubDate>Wed, 11 Nov 26 05:20:02 +0500</pubDate></item>
          <item><title>No link</title></item>
          <item><title>   </title><link>https://x.com/blank-title</link></item>
        </channel></rss>
        """;

    const string Atom = """
        <?xml version="1.0" encoding="UTF-8"?>
        <feed xmlns="http://www.w3.org/2005/Atom"><title>The Verge</title>
          <entry><title type="html">Upscaling, explained</title>
            <link rel="alternate" type="text/html" href="https://www.theverge.com/tech/1"/>
            <updated>2026-11-11T00:34:34+00:00</updated></entry>
        </feed>
        """;

    [Fact]
    public void Rss_ReadsTitlesLinksAndDates()
    {
        var feed = NewsFeed.Parse(Rss, "fallback")!;
        Assert.Equal("Dawn - Home", feed.Title);
        Assert.Equal(2, feed.Items.Count); // the one without a link and the one without a title are skipped
        Assert.Equal("Budget talks resume & more", feed.Items[0].Title);
        Assert.Equal("https://www.dawn.com/news/2", feed.Items[1].Link);
        Assert.Equal(new DateTimeOffset(2026, 11, 11, 6, 45, 1, TimeSpan.Zero), feed.Items[0].Published!.Value.ToUniversalTime());
        Assert.Equal(new DateTimeOffset(2026, 11, 11, 5, 20, 2, TimeSpan.FromHours(5)), feed.Items[1].Published);
    }

    [Fact]
    public void Atom_ReadsAlternateLinkAndUpdated()
    {
        var feed = NewsFeed.Parse(Atom, "fallback")!;
        Assert.Equal("The Verge", feed.Title);
        var item = Assert.Single(feed.Items);
        Assert.Equal("https://www.theverge.com/tech/1", item.Link);
        Assert.Equal(new DateTimeOffset(2026, 11, 11, 0, 34, 34, TimeSpan.Zero), item.Published);
    }

    [Theory]
    [InlineData("<html><body>not a feed</body></html>")]
    [InlineData("")]
    [InlineData("<rss><channel><item>")]
    public void Junk_IsNull(string text) => Assert.Null(NewsFeed.Parse(text, "x"));

    [Theory]
    [InlineData("Tue, 10 Nov 2026 23:00:00 GMT", 2026, 11, 10, 23)]
    [InlineData("10 Nov 2026 23:00 +0000", 2026, 11, 10, 23)]
    [InlineData("Tue, 10 Nov 2026 18:00:00 EST", 2026, 11, 10, 23)]
    [InlineData("2026-11-10T23:00:00Z", 2026, 11, 10, 23)]
    public void Dates_InTheUsualShapes(string text, int y, int mo, int d, int h) =>
        Assert.Equal(new DateTimeOffset(y, mo, d, h, 0, 0, TimeSpan.Zero), NewsFeed.ParseDate(text)!.Value.ToUniversalTime());

    [Fact]
    public void Merge_NewestFirst_NoDuplicates_Capped()
    {
        var t = new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
        var a = new List<NewsItem> { new("A1", "https://a/1", "A", t.AddHours(-1)), new("A2", "https://a/2", "A", t.AddHours(-5)) };
        var b = new List<NewsItem> { new("B1", "https://b/1", "B", t.AddHours(-2)), new("A1 again", "https://a/1", "B", t), new("Undated", "https://b/u", "B", null) };
        var merged = NewsFeed.Merge([a, b], 3);
        Assert.Equal(["A1", "B1", "A2"], merged.Select(i => i.Title));
    }

    [Theory]
    [InlineData(0.5, "just now")]
    [InlineData(5, "5 min")]
    [InlineData(90, "1 h")]
    [InlineData(60 * 30, "1 d")]
    public void Ago_IsShort(double minutes, string expected) =>
        Assert.Equal(expected, NewsFeed.Ago(DateTimeOffset.UtcNow.AddMinutes(-minutes), DateTimeOffset.UtcNow));
}
