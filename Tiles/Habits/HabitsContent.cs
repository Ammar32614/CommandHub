using System.Globalization;
using System.Text.Json.Nodes;

namespace CommandHub.Tiles.Habits;

public record Habit(string Name, List<DateOnly> Done);

/// <summary>Habit tile content: <c>{ "habits": [ { "name", "done": ["yyyy-MM-dd", …] } ] }</c>.</summary>
public static class HabitsContent
{
    /// <summary>A brand-new tile (no "habits" key yet) starts with three gentle habits.</summary>
    public static List<Habit> Read(JsonObject c)
    {
        if (!c.ContainsKey("habits")) return [new("Drink water", []), new("Exercise", []), new("Read 10 pages", [])];
        var list = new List<Habit>();
        foreach (var o in Json.Objects(c["habits"]))
        {
            var name = Json.Str(o["name"]);
            if (name.Length == 0) continue;
            var done = (o["done"] as JsonArray ?? [])
                .Select(n => DateOnly.TryParseExact(Json.Str(n), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : (DateOnly?)null)
                .OfType<DateOnly>().Distinct().ToList();
            list.Add(new Habit(name, done));
        }
        return list;
    }

    /// <summary>Writes habits, keeping only the last year of ticks so the file stays small.</summary>
    public static void Write(JsonObject c, IEnumerable<Habit> habits, DateOnly? today = null)
    {
        var cutoff = (today ?? DateOnly.FromDateTime(DateTime.Today)).AddDays(-366);
        c["habits"] = new JsonArray(habits.Select(h => (JsonNode)new JsonObject
        {
            ["name"] = h.Name,
            ["done"] = new JsonArray(h.Done.Where(d => d >= cutoff).OrderBy(d => d)
                .Select(d => (JsonNode)JsonValue.Create(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))).ToArray()),
        }).ToArray());
    }

    public static void Toggle(Habit h, DateOnly day)
    {
        if (!h.Done.Remove(day)) h.Done.Add(day);
    }

    /// <summary>Days in a row ending today (or yesterday, if today isn't ticked yet).</summary>
    public static int Streak(Habit h, DateOnly today)
    {
        var days = h.Done.ToHashSet();
        var day = days.Contains(today) ? today : today.AddDays(-1);
        int n = 0;
        while (days.Contains(day)) { n++; day = day.AddDays(-1); }
        return n;
    }
}
