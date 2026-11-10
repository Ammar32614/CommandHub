namespace CommandHub.Study;

public enum StudyRange { Week, Month, AllTime }

public record StudySummary(int TotalSeconds, int Sessions, int ActiveDays, int DailyAverageSeconds, int GoalDays, int Days, StudySession? Longest);
public record SubjectPart(string Subject, string Color, int Seconds);
public record DayTotal(DateOnly Day, IReadOnlyList<SubjectPart> Parts, int TotalSeconds);
public record SubjectTotal(string Subject, string Color, int Seconds, double Share, int Sessions);
public record TaskTotal(string Subject, string Color, string Task, int Seconds, int Sessions, DateOnly LastStudied);

/// <summary>All the numbers behind the Study tab. Pure functions over the session list.</summary>
public static class StudyStats
{
    public static (DateOnly From, DateOnly To) Bounds(StudyRange range, DateOnly today, IEnumerable<StudySession> all) => range switch
    {
        StudyRange.Week => (today.AddDays(-6), today),
        StudyRange.Month => (today.AddDays(-29), today),
        _ => (all.Select(s => s.Day).DefaultIfEmpty(today).Min() is var first && first < today ? first : today, today),
    };

    public static List<StudySession> InRange(IEnumerable<StudySession> sessions, DateOnly from, DateOnly to) =>
        sessions.Where(s => s.Day >= from && s.Day <= to).ToList();

    public static StudySummary Summarise(IEnumerable<StudySession> all, DateOnly from, DateOnly to, int goalMinutes)
    {
        var s = InRange(all, from, to);
        int days = to.DayNumber - from.DayNumber + 1;
        int total = s.Sum(x => x.Seconds);
        var perDay = s.GroupBy(x => x.Day).Select(g => g.Sum(x => x.Seconds)).ToList();
        return new StudySummary(total, s.Count, perDay.Count, days > 0 ? total / days : 0,
            perDay.Count(d => d >= goalMinutes * 60), days, s.MaxBy(x => x.Seconds));
    }

    /// <summary>Current streak (ending today, or yesterday if today is still empty) and the best streak ever.</summary>
    public static (int Current, int Best) Streaks(IEnumerable<StudySession> all, DateOnly today)
    {
        var days = all.Select(s => s.Day).ToHashSet();
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

    /// <summary>Percent change against the same-length period just before; null when that period is empty.</summary>
    public static int? ChangePercent(IEnumerable<StudySession> all, DateOnly from, DateOnly to)
    {
        int days = to.DayNumber - from.DayNumber + 1;
        var list = all.ToList();
        int now = InRange(list, from, to).Sum(s => s.Seconds);
        int before = InRange(list, from.AddDays(-days), from.AddDays(-1)).Sum(s => s.Seconds);
        return before == 0 ? null : (int)Math.Round((now - before) * 100.0 / before);
    }

    public static List<DayTotal> PerDay(IEnumerable<StudySession> all, DateOnly from, DateOnly to)
    {
        var byDay = InRange(all, from, to).GroupBy(s => s.Day).ToDictionary(g => g.Key, g => g.ToList());
        var result = new List<DayTotal>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var parts = byDay.TryGetValue(d, out var list)
                ? list.GroupBy(s => s.Subject).Select(g => new SubjectPart(g.Key, g.First().Color, g.Sum(s => s.Seconds)))
                      .OrderByDescending(p => p.Seconds).ToList()
                : [];
            result.Add(new DayTotal(d, parts, parts.Sum(p => p.Seconds)));
        }
        return result;
    }

    public static List<SubjectTotal> BySubject(IEnumerable<StudySession> sessions)
    {
        var list = sessions.ToList();
        double total = Math.Max(1, list.Sum(s => s.Seconds));
        return list.GroupBy(s => s.Subject)
            .Select(g => new SubjectTotal(g.Key, g.First().Color, g.Sum(s => s.Seconds), g.Sum(s => s.Seconds) / total, g.Count()))
            .OrderByDescending(s => s.Seconds).ToList();
    }

    /// <summary>Course + task pairs ("ML · Assignment" vs "ML · Lecture 8"), merged ignoring case and spacing.</summary>
    public static List<TaskTotal> ByTask(IEnumerable<StudySession> sessions) =>
        sessions.GroupBy(s => (s.Subject, Key: s.Task.Trim().ToLowerInvariant()))
            .Select(g =>
            {
                var latest = g.MaxBy(s => s.Start)!;
                return new TaskTotal(g.Key.Subject, latest.Color, latest.Task.Trim(), g.Sum(s => s.Seconds), g.Count(), latest.Day);
            })
            .OrderByDescending(t => t.Seconds).ToList();

    /// <summary>Seconds studied per weekday (row 0 = Monday) and hour of day, splitting sessions across hour boundaries.</summary>
    public static int[,] HourGrid(IEnumerable<StudySession> sessions)
    {
        var grid = new int[7, 24];
        foreach (var s in sessions)
        {
            var t = s.Start;
            var end = s.End;
            while (t < end)
            {
                var nextHour = new DateTime(t.Year, t.Month, t.Day, t.Hour, 0, 0).AddHours(1);
                var chunkEnd = nextHour < end ? nextHour : end;
                grid[((int)t.DayOfWeek + 6) % 7, t.Hour] += (int)(chunkEnd - t).TotalSeconds;
                t = chunkEnd;
            }
        }
        return grid;
    }

    /// <summary>The two-hour window of the day with the most study, e.g. "9 pm – 11 pm"; null with no data.</summary>
    public static string? PeakWindow(int[,] grid)
    {
        int best = -1, bestTotal = 0;
        for (int h = 0; h < 24; h++)
        {
            int total = 0;
            for (int d = 0; d < 7; d++) total += grid[d, h] + grid[d, (h + 1) % 24];
            if (total > bestTotal) { bestTotal = total; best = h; }
        }
        return best < 0 ? null : $"{Hour(best)} – {Hour((best + 2) % 24)}";
    }

    public static string Hour(int h) => h switch { 0 => "12 am", 12 => "12 pm", < 12 => $"{h} am", _ => $"{h - 12} pm" };

    /// <summary>A few plain-language findings for the range.</summary>
    public static List<string> Insights(IEnumerable<StudySession> all, DateOnly from, DateOnly to, int goalMinutes)
    {
        var list = all.ToList();
        var s = InRange(list, from, to);
        var lines = new List<string>();
        if (s.Count == 0) return lines;

        var top = BySubject(s)[0];
        lines.Add($"Most of your time went to {top.Subject} ({Math.Round(top.Share * 100)}%).");
        var task = ByTask(s)[0];
        lines.Add($"Your biggest task was {task.Task} in {task.Subject}, at {Format(task.Seconds)}.");
        if (PeakWindow(HourGrid(s)) is { } peak) lines.Add($"You study most between {peak}.");
        var sum = Summarise(list, from, to, goalMinutes);
        lines.Add($"You hit your {Format(goalMinutes * 60)} goal on {sum.GoalDays} of {sum.Days} days.");
        if (ChangePercent(list, from, to) is { } change)
            lines.Add(change >= 0 ? $"That's {change}% more than the {sum.Days} days before." : $"That's {-change}% less than the {sum.Days} days before.");
        if (sum.Longest is { } longest)
            lines.Add($"Longest sitting: {Format(longest.Seconds)} on {longest.Task} ({longest.Start:ddd d MMM}).");
        return lines;
    }

    /// <summary>Label every Nth bar so a chart shows about ten labels whatever its length.</summary>
    public static int LabelEvery(int bars) => bars <= 10 ? 1 : (int)Math.Ceiling(bars / 10.0);

    /// <summary>"3h 26m", "3h", "25m", "0m".</summary>
    public static string Format(int seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        int h = (int)t.TotalHours;
        return h > 0 ? (t.Minutes > 0 ? $"{h}h {t.Minutes}m" : $"{h}h") : $"{t.Minutes}m";
    }
}
