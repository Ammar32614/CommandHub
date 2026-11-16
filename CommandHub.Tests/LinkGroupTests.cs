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

    static readonly List<QuickLink> Row =
    [
        new("Stint", "s.exe"), new("Portal", "p.com", "Uni"), new("Docs", "d.com"), new("Mail", "m.com", "Uni"), new("Steam", "st.exe"),
    ];
    // As shown on Home: Stint, [Uni: Portal, Mail], Docs, Steam

    static List<string> Shown(List<QuickLink> links) =>
        LinksContent.Arrange(links).Select(e => e.Group is { } g ? $"[{g}: {string.Join(", ", e.Links.Select(l => l.Name))}]" : e.Links[0].Name).ToList();

    [Fact]
    public void MoveEntry_MovesGroupsAsOne()
    {
        Assert.Equal(["[Uni: Portal, Mail]", "Docs", "Stint", "Steam"], Shown(LinksContent.MoveEntry(Row, 0, 2)));
        Assert.Equal(["Stint", "Docs", "Steam", "[Uni: Portal, Mail]"], Shown(LinksContent.MoveEntry(Row, 1, 99)));
        Assert.Equal(Shown(Row), Shown(LinksContent.MoveEntry(Row, 2, 2)));
    }

    [Fact]
    public void MoveIntoGroup_AddsToTheEndOfThatGroup()
    {
        var after = LinksContent.MoveIntoGroup(Row, entry: 2, group: "uni");
        Assert.Equal(["Stint", "[Uni: Portal, Mail, Docs]", "Steam"], Shown(after));
        Assert.Equal("Uni", after.Single(l => l.Name == "Docs").Group); // takes the group's spelling
        Assert.Equal(Shown(Row), Shown(LinksContent.MoveIntoGroup(Row, entry: 1, group: "Uni"))); // a group can't go in a group
    }

    [Fact]
    public void WithinGroup_ReorderAndTakeOut()
    {
        Assert.Equal(["Stint", "[Uni: Mail, Portal]", "Docs", "Steam"], Shown(LinksContent.MoveWithinGroup(Row, "Uni", 0, 1)));
        var out1 = LinksContent.TakeOutOfGroup(Row, "Uni", 1);
        Assert.Equal(["Stint", "[Uni: Portal]", "Mail", "Docs", "Steam"], Shown(out1));
        Assert.Equal("", out1.Single(l => l.Name == "Mail").Group);
        // Taking the last one out leaves no empty group behind.
        Assert.Equal(["Stint", "Portal", "Docs", "Steam"], Shown(LinksContent.TakeOutOfGroup(out1, "Uni", 0)).Where(x => x != "Mail").ToList());
    }
}
