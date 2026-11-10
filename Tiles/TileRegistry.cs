using CommandHub.Tiles.Clock;
using CommandHub.Tiles.Links;
using CommandHub.Tiles.Note;
using CommandHub.Tiles.Todo;

namespace CommandHub.Tiles;

/// <summary>Every tile type the app knows. Adding a tile type = one new folder under Tiles/ plus one line here.</summary>
public static class TileRegistry
{
    public static IReadOnlyList<ITileType> All { get; } =
    [
        new ClockTile(),
        new NoteTile(),
        new TodoTile(),
        new LinksTile(),
        new Greeting.GreetingTile(),
        new Weather.WeatherTile(),
        new Progress.ProgressTile(),
        new Focus.FocusTile(),
        new SystemPulse.SystemTile(),
        new Countdown.CountdownTile(),
        new Habits.HabitsTile(),
        new UpNext.UpNextTile(),
    ];

    /// <summary>Null for a type this version doesn't know (e.g. from a newer save file); the board shows a placeholder.</summary>
    public static ITileType? Find(string typeId) => All.FirstOrDefault(t => t.TypeId == typeId);
}
