using System.Globalization;
using System.Text.RegularExpressions;

namespace CommandHub.Planner;

/// <summary>Pure planner rules: repeating classes, what's next, and forgiving date/time parsing.</summary>
public static partial class PlannerLogic
{
    public static DateOnly WeekStart(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    /// <summary>Six Monday-first weeks covering the month, for a month grid.</summary>
    public static List<DateOnly> MonthGrid(int year, int month)
    {
        var start = WeekStart(new DateOnly(year, month, 1));
        return Enumerable.Range(0, 42).Select(start.AddDays).ToList();
    }

    /// <summary>Everything happening between two days (inclusive), weekly classes expanded, sorted by day then time.</summary>
    public static List<PlanOccurrence> Occurrences(IEnumerable<PlanItem> items, DateOnly from, DateOnly to)
    {
        var list = new List<PlanOccurrence>();
        foreach (var item in items)
        {
            if (!item.Weekly)
            {
                if (item.Date >= from && item.Date <= to) list.Add(new PlanOccurrence(item, item.Date));
                continue;
            }
            var last = item.Until is { } until && until < to ? until : to;
            var day = item.Date;
            if (day < from) day = day.AddDays((from.DayNumber - day.DayNumber + 6) / 7 * 7);
            for (; day <= last; day = day.AddDays(7)) list.Add(new PlanOccurrence(item, day));
        }
        return list
            .OrderBy(o => o.Date)
            .ThenBy(o => o.Item.Start.HasValue)          // all-day first
            .ThenBy(o => o.Item.Start ?? TimeOnly.MinValue)
            .ThenBy(o => o.Item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static bool IsOverdue(PlanOccurrence o, DateOnly today) =>
        o.Item.Kind == PlanKind.Deadline && !o.Item.Done && o.Date < today;

    /// <summary>Overdue deadlines first, then everything not done in the next <paramref name="days"/> days.</summary>
    /// <param name="now">Time of day; anything today that has already ended (End, or Start if no End) is skipped.</param>
    public static List<PlanOccurrence> UpNext(IEnumerable<PlanItem> items, DateOnly today, int days, TimeOnly? now = null)
    {
        var all = items.ToList();
        var overdue = all.Where(i => !i.Weekly && i.Kind == PlanKind.Deadline && !i.Done && i.Date < today)
            .OrderBy(i => i.Date).Select(i => new PlanOccurrence(i, i.Date));
        // A weekly class shows once (its next lecture), not every week in the window.
        var upcoming = Occurrences(all.Where(i => !i.Done), today, today.AddDays(days))
            .Where(o => !(now is { } t && o.Date == today && (o.Item.End ?? o.Item.Start) is { } ends && ends < t))
            .GroupBy(o => o.Item.Weekly ? o.Item.Id : Guid.NewGuid())
            .Select(g => g.First())
            .OrderBy(o => o.Date).ThenBy(o => o.Item.Start.HasValue).ThenBy(o => o.Item.Start ?? TimeOnly.MinValue);
        return overdue.Concat(upcoming).ToList();
    }

    public static string When(DateOnly day, DateOnly today) => (day.DayNumber - today.DayNumber) switch
    {
        0 => "Today",
        1 => "Tomorrow",
        -1 => "Yesterday",
        > 1 and var n => $"In {n} days",
        var n => $"{-n} days ago",
    };

    public static string KindName(PlanKind kind, int count) => (kind, count == 1) switch
    {
        (PlanKind.Class, true) => "class",
        (PlanKind.Class, false) => "classes",
        (PlanKind.Deadline, true) => "deadline",
        (PlanKind.Deadline, false) => "deadlines",
        (PlanKind.Exam, true) => "exam",
        (PlanKind.Exam, false) => "exams",
        (_, true) => "event",
        _ => "events",
    };

    /// <summary>"2 deadlines · 1 exam · 3 classes", or "Nothing planned".</summary>
    public static string Summary(IEnumerable<PlanOccurrence> occurrences)
    {
        var counts = occurrences.GroupBy(o => o.Item.Kind).ToDictionary(g => g.Key, g => g.Count());
        var parts = new[] { PlanKind.Deadline, PlanKind.Exam, PlanKind.Class, PlanKind.Event }
            .Where(counts.ContainsKey).Select(k => $"{counts[k]} {KindName(k, counts[k])}").ToList();
        return parts.Count == 0 ? "Nothing planned" : string.Join("   ·   ", parts);
    }

    // ---------------- Parsing ----------------

    private static readonly string[] Weekdays = ["mon", "tue", "wed", "thu", "fri", "sat", "sun"];

    /// <summary>
    /// "2026-11-20", "20 Nov", "nov 20", "today", "tomorrow", "fri" (the next Friday, today included).
    /// Day-and-month without a year means the next time that date comes round.
    /// </summary>
    public static DateOnly? ParseDate(string text, DateOnly today)
    {
        var t = (text ?? "").Trim().ToLowerInvariant();
        if (t.Length == 0) return null;
        if (t == "today") return today;
        if (t == "tomorrow") return today.AddDays(1);
        if (t.Length >= 3 && Array.IndexOf(Weekdays, t[..3]) is var wd and >= 0 && "monday tuesday wednesday thursday friday saturday sunday".Contains(t))
        {
            int current = ((int)today.DayOfWeek + 6) % 7;
            return today.AddDays((wd - current + 7) % 7);
        }

        var inv = CultureInfo.InvariantCulture;
        if (DateOnly.TryParseExact(t, ["yyyy-MM-dd", "yyyy-M-d", "d MMM yyyy", "d MMMM yyyy", "MMM d yyyy", "MMMM d yyyy"], inv, DateTimeStyles.None, out var full))
            return full;
        foreach (var year in new[] { today.Year, today.Year + 1 })
        {
            if (DateOnly.TryParseExact($"{t} {year}", ["d MMM yyyy", "d MMMM yyyy", "MMM d yyyy", "MMMM d yyyy"], inv, DateTimeStyles.None, out var d)
                && d >= today)
                return d;
        }
        return null;
    }

    [GeneratedRegex(@"^(\d{1,2})(?::?(\d{2}))?\s*(am|pm)?$", RegexOptions.IgnoreCase)]
    private static partial Regex TimePattern();

    /// <summary>"14:00", "2pm", "2:30 pm", "9", "0930". Null if it isn't a time.</summary>
    public static TimeOnly? ParseTime(string text)
    {
        var m = TimePattern().Match((text ?? "").Trim());
        if (!m.Success) return null;
        int h = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        int min = m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
        if (min > 59) return null;
        if (m.Groups[3].Success)
        {
            if (h is < 1 or > 12) return null;
            bool pm = m.Groups[3].Value.Equals("pm", StringComparison.OrdinalIgnoreCase);
            h = h % 12 + (pm ? 12 : 0);
        }
        else if (h > 23) return null;
        return new TimeOnly(h, min);
    }

    /// <summary>Blank is fine (no time); anything else must be a valid time.</summary>
    public static bool TryParseOptionalTime(string text, out TimeOnly? time)
    {
        time = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        time = ParseTime(text);
        return time is not null;
    }

    /// <summary>
    /// What to show under a time box: how the text was read, a nudge when a bare small hour
    /// ("2") probably meant the afternoon, or a problem when it isn't a time.
    /// </summary>
    public static (string Text, bool Problem) TimeHint(string text)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0) return ("", false);
        if (ParseTime(t) is not { } time) return ("Try \"2pm\" or 14:00", true);
        var read = $"Read as {FormatTime(time)}";
        if (t.All(char.IsDigit) && time.Hour is >= 1 and <= 7 && time.Minute == 0)
            return ($"{read}. Type {time.Hour}pm for the {(time.Hour <= 5 ? "afternoon" : "evening")}.", false);
        return (read, false);
    }

    public static string FormatTime(TimeOnly t) => t.ToString(t.Minute == 0 ? "h tt" : "h:mm tt", CultureInfo.InvariantCulture).ToLowerInvariant();

    /// <summary>The message to show, or null when the item can be saved.</summary>
    public static string? Validate(string title, DateOnly? date, TimeOnly? start, TimeOnly? end)
    {
        if (string.IsNullOrWhiteSpace(title)) return "Give it a name.";
        if (date is null) return "Pick a date.";
        if (start is { } s && end is { } e && e <= s) return "The end time should be after the start.";
        return null;
    }
}
