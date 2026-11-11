using System.Text.Json.Nodes;
using CommandHub.Tiles.Links;

namespace CommandHub.Tests;

public class LinkGroupTests
{
    [Fact]
    public void Group_RoundTrips_AndOldLinksHaveNone()
    {
        var c = new JsonObject();
        LinksContent.Write(c, [new QuickLink("Mail", "https://mail.example.com", "Uni"), new QuickLink("Stint", @"C:\Stint.exe")]);
        Assert.Equal([new QuickLink("Mail", "https://mail.example.com", "Uni"), new QuickLink("Stint", @"C:\Stint.exe")], LinksContent.Read(c));
        Assert.False(((JsonObject)c["links"]![1]!).ContainsKey("group")); // ungrouped links stay as they were

        var old = JsonNode.Parse("""{"links":[{"name":"A","target":"a.com"}]}""")!.AsObject();
        Assert.Equal("", LinksContent.Read(old)[0].Group);
    }

    [Fact]
    public void Arrange_GroupsSitWhereTheirFirstLinkWas()
    {
        var entries = LinksContent.Arrange(
        [
            new("Stint", "s.exe"),
            new("Portal", "p.com", "Uni"),
            new("Docs", "d.com"),
            new("Mail", "m.com", "uni"), // same group, any case
            new("Steam", "st.exe", "Games"),
        ]);
        Assert.Equal(["Stint", "Uni", "Docs", "Games"], entries.Select(e => e.Group ?? e.Links[0].Name));
        Assert.Equal(["Portal", "Mail"], entries[1].Links.Select(l => l.Name));
        Assert.Null(entries[0].Group);
    }

    [Fact]
    public void Groups_ListsDistinctNames() =>
        Assert.Equal(["Uni", "Games"], LinksContent.Groups([new("a", "a", "Uni"), new("b", "b", " uni "), new("c", "c"), new("d", "d", "Games")]));
}
