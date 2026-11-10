using System.IO;
using System.Text.Json;

namespace CommandHub.Tiles.Focus;

/// <summary>Today's study numbers from Stint's log. <see cref="Week"/> is 7 days of seconds, oldest first, today last.</summary>
public record StintToday(int TodaySeconds, int GoalMinutes, int Streak, IReadOnlyList<int> Week);

/// <summary>Reads Stint's data.json (read-only; Stint owns the file). Never throws.</summary>
public static class StintReader
{
    public static string DefaultPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Stint", "data.json");

    public static StintToday? ReadFile(string path, DateOnly today)
    {
        try
        {
            if (!File.Exists(path)) return null;
            // Stint may be writing at the same moment: share the file rather than lock it.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return Read(reader.ReadToEnd(), today);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static StintToday? Read(string json, DateOnly today)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var perDay = new Dictionary<DateOnly, int>();
            if (root.TryGetProperty("sessions", out var sessions) && sessions.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in sessions.EnumerateArray())
                {
                    if (s.ValueKind != JsonValueKind.Object) continue;
                    if (!s.TryGetProperty("start", out var start) || start.ValueKind != JsonValueKind.String) continue;
                    if (!DateTimeOffset.TryParse(start.GetString(), out var when)) continue;
                    if (!s.TryGetProperty("seconds", out var secs) || !secs.TryGetInt32(out int seconds) || seconds <= 0) continue;
                    var day = DateOnly.FromDateTime(when.LocalDateTime);
                    perDay[day] = perDay.GetValueOrDefault(day) + seconds;
                }
            }

            int goal = 120;
            if (root.TryGetProperty("settings", out var settings) && settings.ValueKind == JsonValueKind.Object
                && settings.TryGetProperty("dailyGoalMinutes", out var g) && g.TryGetInt32(out int gm) && gm > 0)
                goal = gm;

            var cursor = perDay.ContainsKey(today) ? today : today.AddDays(-1);
            int streak = 0;
            while (perDay.ContainsKey(cursor)) { streak++; cursor = cursor.AddDays(-1); }

            var week = Enumerable.Range(0, 7).Select(i => perDay.GetValueOrDefault(today.AddDays(i - 6))).ToList();
            return new StintToday(perDay.GetValueOrDefault(today), goal, streak, week);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
