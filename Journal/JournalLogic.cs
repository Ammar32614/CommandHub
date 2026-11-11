using System.Text.RegularExpressions;

namespace CommandHub.Journal;

/// <summary>One day's entry. Mood: 0 = not set, 1 Awful … 5 Great.</summary>
public class JournalEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly Date { get; set; }
    public int Mood { get; set; }
    public string Text { get; set; } = "";
    public DateTime Updated { get; set; } = DateTime.Now;
}

public record Memory(string Label, JournalEntry Entry);

public static partial class JournalLogic
{
    public static readonly string[] MoodNames = ["", "Awful", "Bad", "Okay", "Good", "Great"];
    /// <summary>Colours for moods 1–5, red to green, fixed so they read the same in every theme.</summary>
    public static readonly string[] MoodColors = ["#00000000", "#F0605D", "#F2A14A", "#9AA3B2", "#4FC3D1", "#6BD68A"];

    public static bool IsEmpty(JournalEntry e) => e.Mood == 0 && string.IsNullOrWhiteSpace(e.Text);

    /// <summary>Days in a row with an entry, ending today (or yesterday if today is still blank), and the best run ever.</summary>
    public static (int Current, int Best) Streaks(IEnumerable<JournalEntry> entries, DateOnly today)
    {
        var days = entries.Where(e => !IsEmpty(e)).Select(e => e.Date).ToHashSet();
        var cursor = days.Contains(today) ? today : today.AddDays(-1);
        int current = 0;
        while (days.Contains(cursor)) { current++; cursor = cursor.AddDays(-1); }
        int best = 0, run = 0;
        DateOnly? prev = null;
        foreach (var d in days.OrderBy(d => d))
        {
            run = prev is { } p && d.DayNumber == p.DayNumber + 1 ? run + 1 : 1;
            best = Math.Max(best, run);
            prev = d;
        }
        return (current, best);
    }

    [GeneratedRegex(@"#(\w+)")]
    private static partial Regex TagPattern();

    public static List<string> Tags(string text) =>
        TagPattern().Matches(text ?? "").Select(m => m.Groups[1].Value.ToLowerInvariant()).Distinct().ToList();

    public static int WordCount(string text) =>
        (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>Entries whose text or tags contain the query, newest first.</summary>
    public static List<JournalEntry> Search(IEnumerable<JournalEntry> entries, string query)
    {
        var q = (query ?? "").Trim().TrimStart('#');
        if (q.Length == 0) return [];
        return entries.Where(e => !IsEmpty(e) && (e.Text.Contains(q, StringComparison.CurrentCultureIgnoreCase) || Tags(e.Text).Contains(q.ToLowerInvariant())))
            .OrderByDescending(e => e.Date).ToList();
    }

    /// <summary>What you wrote a week, a month and a year ago today (where you wrote something).</summary>
    public static List<Memory> OnThisDay(IEnumerable<JournalEntry> entries, DateOnly today)
    {
        var byDay = entries.Where(e => !IsEmpty(e)).GroupBy(e => e.Date).ToDictionary(g => g.Key, g => g.First());
        var list = new List<Memory>();
        foreach (var (label, day) in new[] { ("A week ago", today.AddDays(-7)), ("A month ago", today.AddMonths(-1)), ("A year ago", today.AddYears(-1)) })
            if (byDay.TryGetValue(day, out var e)) list.Add(new Memory(label, e));
        return list;
    }

    public static double? MoodAverage(IEnumerable<JournalEntry> entries)
    {
        var moods = entries.Where(e => e.Mood is >= 1 and <= 5).Select(e => e.Mood).ToList();
        return moods.Count == 0 ? null : moods.Average();
    }

    private static readonly string[] Prompts =
    [
        "What went well today?",
        "What's one thing you learned?",
        "What drained your energy, and what gave you some?",
        "What are you grateful for right now?",
        "What would make tomorrow great?",
        "What did you avoid today, and why?",
        "Who made your day better?",
        "What's on your mind that you haven't said out loud?",
        "What progress did you make, however small?",
        "If today had a title, what would it be?",
        "What surprised you today?",
        "What's one thing you'd do differently?",
        "How did you look after yourself today?",
        "What are you looking forward to?",
    ];

    public static string PromptFor(DateOnly day) => Prompts[day.DayNumber % Prompts.Length];
}
