namespace CommandHub.Models;

public class AppSettings
{
    public string Theme { get; set; } = "Midnight";
}

/// <summary>Everything Command hub saves, as one versioned JSON document.</summary>
public class HubData
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public List<Page> Pages { get; set; } = [];
    public AppSettings Settings { get; set; } = new();

    public static HubData CreateDefault() => new() { Pages = [CreateHome()] };

    public static Page CreateHome() => new()
    {
        Name = "Home",
        Icon = "",
        Tiles =
        [
            new() { TypeId = "clock", Column = 0, Row = 0, Size = TileSize.Large },
            new() { TypeId = "note", Column = 2, Row = 0, Size = TileSize.Wide },
            new() { TypeId = "todo", Column = 4, Row = 0, Size = TileSize.Tall },
            new() { TypeId = "links", Column = 2, Row = 1, Size = TileSize.Wide },
        ],
    };
}
