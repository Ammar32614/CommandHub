namespace CommandHub.Tiles.Progress;

/// <summary>How far through the day, week (Monday start), month and year a moment is, from 0 to 1.</summary>
public static class TimeProgress
{
    private static double Fraction(DateTime now, DateTime start, DateTime end) =>
        Math.Clamp((now - start).TotalSeconds / (end - start).TotalSeconds, 0, 1);

    public static double Day(DateTime now) => Fraction(now, now.Date, now.Date.AddDays(1));

    public static double Week(DateTime now)
    {
        int sinceMonday = ((int)now.DayOfWeek + 6) % 7;
        var start = now.Date.AddDays(-sinceMonday);
        return Fraction(now, start, start.AddDays(7));
    }

    public static double Month(DateTime now)
    {
        var start = new DateTime(now.Year, now.Month, 1);
        return Fraction(now, start, start.AddMonths(1));
    }

    public static double Year(DateTime now)
    {
        var start = new DateTime(now.Year, 1, 1);
        return Fraction(now, start, start.AddYears(1));
    }
}
