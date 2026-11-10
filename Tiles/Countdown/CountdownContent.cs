using System.Globalization;
using System.Text.Json.Nodes;

namespace CommandHub.Tiles.Countdown;

public record CountdownEntry(string Name, DateOnly Date);

/// <summary>Countdown tile content: <c>{ "events": [ { "name", "date": "yyyy-MM-dd" } ] }</c>.</summary>
public static class CountdownContent
{
    /// <summary>A brand-new tile (no "events" key yet) starts with a countdown to next New Year.</summary>
    public static List<CountdownEntry> Read(JsonObject c, DateOnly today)
    {
        if (!c.ContainsKey("events")) return [new("New Year", new DateOnly(today.Year + 1, 1, 1))];
        var list = new List<CountdownEntry>();
        foreach (var o in Json.Objects(c["events"]))
            if (DateOnly.TryParseExact(Json.Str(o["date"]), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                list.Add(new CountdownEntry(Json.Str(o["name"]), d));
        return list;
    }

    public static void Write(JsonObject c, IEnumerable<CountdownEntry> events) =>
        c["events"] = new JsonArray(events.Select(e => (JsonNode)new JsonObject
        {
            ["name"] = e.Name,
            ["date"] = e.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        }).ToArray());

    /// <summary>Today and later, soonest first. Past events are kept in the file but not shown.</summary>
    public static List<CountdownEntry> Upcoming(IEnumerable<CountdownEntry> events, DateOnly today) =>
        events.Where(e => e.Date >= today).OrderBy(e => e.Date).ToList();

    public static string Label(DateOnly date, DateOnly today) => (date.DayNumber - today.DayNumber) switch
    {
        0 => "Today",
        1 => "Tomorrow",
        var n => $"{n} days",
    };
}
