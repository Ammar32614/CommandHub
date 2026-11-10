using CommandHub.Layout;

namespace CommandHub.Models;

public class AppSettings
{
    public string Theme { get; set; } = "Midnight";
}

/// <summary>Everything Command hub saves, as one versioned JSON document.</summary>
public class HubData
{
    /// <summary>1 = base version (4 starter tiles). 2 = vibrant Home (11 tiles).</summary>
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;
    public List<Page> Pages { get; set; } = [];
    public AppSettings Settings { get; set; } = new();

    public static HubData CreateDefault() => new() { Pages = [CreateHome()] };

    /// <summary>The default Home: a 6 × 4 board.</summary>
    private static readonly (string Type, int Col, int Row, TileSize Size)[] HomeLayout =
    [
        ("greeting", 0, 0, TileSize.Wide), ("weather", 2, 0, TileSize.Wide), ("progress", 4, 0, TileSize.Wide),
        ("clock", 0, 1, TileSize.Large), ("focus", 2, 1, TileSize.Tall), ("system", 3, 1, TileSize.Tall),
        ("todo", 4, 1, TileSize.Tall), ("habits", 5, 1, TileSize.Tall),
        ("note", 0, 3, TileSize.Wide), ("links", 2, 3, TileSize.Wide), ("countdown", 4, 3, TileSize.Wide),
    ];

    /// <summary>The version-1 default Home, used to tell whether the user ever rearranged it.</summary>
    private static readonly (string Type, int Col, int Row, TileSize Size)[] V1HomeLayout =
    [
        ("clock", 0, 0, TileSize.Large), ("note", 2, 0, TileSize.Wide), ("todo", 4, 0, TileSize.Tall), ("links", 2, 1, TileSize.Wide),
    ];

    public static Page CreateHome() => new()
    {
        Name = "Home",
        Icon = "",
        Tiles = HomeLayout.Select(t => new PlacedTile { TypeId = t.Type, Column = t.Col, Row = t.Row, Size = t.Size }).ToList(),
    };

    /// <summary>Brings an older file up to <see cref="CurrentVersion"/>. Never moves tiles the user placed.</summary>
    public void Upgrade()
    {
        if (Version < 2 && Pages.Count > 0) UpgradeHomeTo2(Pages[0]);
        if (Version < CurrentVersion) Version = CurrentVersion;
    }

    private static void UpgradeHomeTo2(Page home)
    {
        bool untouched = home.Tiles.Count == V1HomeLayout.Length
            && V1HomeLayout.All(d => home.Tiles.Any(t => t.TypeId == d.Type && t.Column == d.Col && t.Row == d.Row && t.Size == d.Size));

        if (untouched)
        {
            // Never rearranged: switch to the new layout, carrying each starter tile's id and content across.
            var old = home.Tiles.ToDictionary(t => t.TypeId);
            home.Tiles = HomeLayout.Select(d => old.TryGetValue(d.Type, out var keep)
                    ? new PlacedTile { Id = keep.Id, TypeId = d.Type, Column = d.Col, Row = d.Row, Size = d.Size, Content = keep.Content }
                    : new PlacedTile { TypeId = d.Type, Column = d.Col, Row = d.Row, Size = d.Size })
                .ToList();
            return;
        }

        // Rearranged: leave every tile where it is and add only the tile types that are new in version 2.
        string[] added = ["greeting", "weather", "progress", "focus", "system", "countdown", "habits"];
        foreach (var type in added.Where(a => home.Tiles.All(t => t.TypeId != a)))
        {
            var size = HomeLayout.First(d => d.Type == type).Size;
            var (col, row) = BoardGrid.FindFreeSpot(home.Tiles, size);
            home.Tiles.Add(new PlacedTile { TypeId = type, Column = col, Row = row, Size = size });
        }
    }
}
