using System.IO;
using System.Text.Json;

namespace CommandHub.Study;

/// <summary>One logged block of study from Stint. <see cref="Task"/> is what was typed in "What are you working on?".</summary>
public record StudySession(DateTime Start, int Seconds, string Subject, string Color, string Task, string Mode, int Rating)
{
    public DateOnly Day => DateOnly.FromDateTime(Start);
    public DateTime End => Start.AddSeconds(Seconds);
}

/// <summary>Stint's study log, read-only (Stint owns the file). Never throws on a bad file.</summary>
public sealed record StudyLog(IReadOnlyList<StudySession> Sessions, int GoalMinutes)
{
    public static string DefaultPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Stint", "data.json");

    private static readonly Guid GeneralId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public static StudyLog? ReadFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            // Stint may be writing at the same moment: share the file rather than lock it.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd(), TimeZoneInfo.Local);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static StudyLog? Parse(string json, TimeZoneInfo zone)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var subjects = new Dictionary<Guid, (string Name, string Color)>();
            if (root.TryGetProperty("subjects", out var subs) && subs.ValueKind == JsonValueKind.Array)
                foreach (var s in subs.EnumerateArray())
                    if (s.ValueKind == JsonValueKind.Object && Str(s, "id") is { } id && Guid.TryParse(id, out var gid))
                        subjects[gid] = (Str(s, "name") is { Length: > 0 } n ? n : "Untitled", Str(s, "color") is { Length: > 0 } c ? c : "#C8CDD8");
            (string Name, string Color) general = subjects.TryGetValue(GeneralId, out var g) ? g : ("General", "#C8CDD8");

            var sessions = new List<StudySession>();
            if (root.TryGetProperty("sessions", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in list.EnumerateArray())
                {
                    if (s.ValueKind != JsonValueKind.Object) continue;
                    if (Str(s, "start") is not { } startText || !DateTimeOffset.TryParse(startText, out var start)) continue;
                    if (!s.TryGetProperty("seconds", out var secs) || !secs.TryGetInt32(out int seconds) || seconds <= 0) continue;

                    var subject = Str(s, "subjectId") is { } sid && Guid.TryParse(sid, out var sg) && subjects.TryGetValue(sg, out var found) ? found : general;
                    var task = (Str(s, "description") ?? "").Trim();
                    int rating = s.TryGetProperty("rating", out var r) && r.TryGetInt32(out int rv) ? Math.Clamp(rv, 0, 5) : 0;
                    sessions.Add(new StudySession(
                        TimeZoneInfo.ConvertTime(start, zone).DateTime, seconds, subject.Name, subject.Color,
                        task.Length > 0 ? task : "Untitled", Str(s, "mode") ?? "", rating));
                }
            }

            int goal = 120;
            if (root.TryGetProperty("settings", out var st) && st.ValueKind == JsonValueKind.Object
                && st.TryGetProperty("dailyGoalMinutes", out var gm) && gm.TryGetInt32(out int gmv) && gmv > 0)
                goal = gmv;

            return new StudyLog(sessions.OrderBy(x => x.Start).ToList(), goal);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
