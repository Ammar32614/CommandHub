using System.Globalization;
using System.Text.RegularExpressions;

namespace CommandHub.Money;

public record MonthSummary(decimal Spent, decimal Income, IReadOnlyList<Transaction> Expenses, IReadOnlyList<Transaction> Incomes);
public record CategoryTotal(string Category, decimal Amount, double Share, int Count);
public record BudgetState(decimal Budget, decimal Spent, decimal Left, int DaysLeft, decimal PerDay, bool Over);

/// <summary>Pure money rules: parsing typed amounts, month totals, budgets and insights.</summary>
public static partial class MoneyLogic
{
    // A bare ".5" is a fraction, but after a currency symbol ("Rs.450") the dot belongs to the symbol.
    [GeneratedRegex(@"^(?:\.(\d+)|[^\d\-]*?(\d[\d,]*(?:\.\d+)?))\s*(k)?$", RegexOptions.IgnoreCase)]
    private static partial Regex AmountPattern();

    /// <summary>The largest single amount accepted (a trillion), so totals can never overflow.</summary>
    public const decimal MaxAmount = 1_000_000_000_000m;

    /// <summary>"1200", "1,200", "1.5k", "Rs 450", "Rs.450". Null when it isn't a positive amount.</summary>
    public static decimal? ParseAmount(string text)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0 || t.Contains('-')) return null;
        var m = AmountPattern().Match(t);
        if (!m.Success) return null;
        var digits = m.Groups[1].Success ? "0." + m.Groups[1].Value : m.Groups[2].Value.Replace(",", "");
        if (digits.Length > 20 || !decimal.TryParse(digits, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)) return null;
        if (m.Groups[3].Success) { if (value > MaxAmount) return null; value *= 1000; }
        return value > 0 && value <= MaxAmount ? Math.Round(value, 2) : null;
    }

    private static readonly string[] Weekdays = ["mon", "tue", "wed", "thu", "fri", "sat", "sun"];

    /// <summary>
    /// When a payment happened. Blank or "today", "yesterday", a weekday (the most recent one, today included),
    /// "5 nov" (the most recent 5 Nov) or 2026-10-31. Null for future dates and anything unreadable.
    /// </summary>
    public static DateOnly? ParsePastDate(string text, DateOnly today)
    {
        var t = (text ?? "").Trim().ToLowerInvariant();
        if (t.Length == 0 || t == "today") return today;
        if (t == "yesterday") return today.AddDays(-1);
        if (t.Length >= 3 && Array.IndexOf(Weekdays, t[..3]) is var wd and >= 0 && "monday tuesday wednesday thursday friday saturday sunday".Contains(t))
        {
            int current = ((int)today.DayOfWeek + 6) % 7;
            return today.AddDays(-((current - wd + 7) % 7));
        }
        var inv = CultureInfo.InvariantCulture;
        if (DateOnly.TryParseExact(t, ["yyyy-MM-dd", "yyyy-M-d", "d MMM yyyy", "d MMMM yyyy", "MMM d yyyy", "MMMM d yyyy"], inv, DateTimeStyles.None, out var full))
            return full <= today && full.Year >= 1900 ? full : null;
        foreach (var year in new[] { today.Year, today.Year - 1 })
            if (DateOnly.TryParseExact($"{t} {year}", ["d MMM yyyy", "d MMMM yyyy", "MMM d yyyy", "MMMM d yyyy"], inv, DateTimeStyles.None, out var d) && d <= today)
                return d;
        return null;
    }

    /// <summary>"Rs 12,340" or "$ 4.50" (cents only when there are some).</summary>
    public static string Format(decimal amount, string symbol)
    {
        var n = amount.ToString(amount % 1 == 0 ? "N0" : "N2", CultureInfo.InvariantCulture);
        return $"{symbol} {n}";
    }

    public static MonthSummary Month(IEnumerable<Transaction> all, int year, int month)
    {
        var inMonth = all.Where(t => t.Date.Year == year && t.Date.Month == month).ToList();
        var expenses = inMonth.Where(t => t.Kind == MoneyKind.Expense).OrderByDescending(t => t.Date).ToList();
        var incomes = inMonth.Where(t => t.Kind == MoneyKind.Income).OrderByDescending(t => t.Date).ToList();
        return new MonthSummary(expenses.Sum(t => t.Amount), incomes.Sum(t => t.Amount), expenses, incomes);
    }

    public static List<CategoryTotal> ByCategory(IEnumerable<Transaction> expenses)
    {
        var list = expenses.ToList();
        var total = list.Sum(t => t.Amount);
        return list.GroupBy(t => t.Category, StringComparer.OrdinalIgnoreCase)
            .Select(g => new CategoryTotal(g.First().Category, g.Sum(t => t.Amount), total == 0 ? 0 : (double)(g.Sum(t => t.Amount) / total), g.Count()))
            .OrderByDescending(c => c.Amount).ToList();
    }

    /// <summary>What's left of a monthly budget and how much that allows per remaining day (today included).</summary>
    public static BudgetState Budget(decimal budget, decimal spent, DateOnly today)
    {
        int daysLeft = DateTime.DaysInMonth(today.Year, today.Month) - today.Day + 1;
        var left = budget - spent;
        var perDay = left > 0 ? Math.Round(left / daysLeft, 2) : 0;
        return new BudgetState(budget, spent, left, daysLeft, perDay, left < 0);
    }

    /// <summary>Spending on each day of the month (index 0 = the 1st).</summary>
    public static List<decimal> DailySpend(IEnumerable<Transaction> all, int year, int month)
    {
        var days = Enumerable.Repeat(0m, DateTime.DaysInMonth(year, month)).ToList();
        foreach (var t in all.Where(t => t.Kind == MoneyKind.Expense && t.Date.Year == year && t.Date.Month == month))
            days[t.Date.Day - 1] += t.Amount;
        return days;
    }

    /// <summary>
    /// Percent change in spending against the month before; null when that month had none.
    /// While <paramref name="today"/> is inside the month, it compares like for like: the same days of last month so far.
    /// </summary>
    public static int? ChangeVsLastMonth(IEnumerable<Transaction> all, int year, int month, DateOnly? today = null)
    {
        if (year <= 1 && month <= 1) return null;
        var list = all.ToList();
        var prev = new DateOnly(year, month, 1).AddMonths(-1);
        var now = Month(list, year, month).Spent;
        int upTo = today is { } t && t.Year == year && t.Month == month
            ? Math.Min(t.Day, DateTime.DaysInMonth(prev.Year, prev.Month))
            : 31;
        var before = Month(list, prev.Year, prev.Month).Expenses.Where(x => x.Date.Day <= upTo).Sum(x => x.Amount);
        return before == 0 ? null : (int)Math.Round((now - before) * 100 / before, MidpointRounding.AwayFromZero);
    }

    public static List<string> Insights(IEnumerable<Transaction> all, int year, int month, string symbol, DateOnly today)
    {
        var list = all.ToList();
        var m = Month(list, year, month);
        var lines = new List<string>();
        if (m.Expenses.Count == 0) return lines;
        var top = ByCategory(m.Expenses)[0];
        lines.Add($"{top.Category} is your biggest spend this month ({Math.Round(top.Share * 100, MidpointRounding.AwayFromZero)}%).");
        bool current = today.Year == year && today.Month == month;
        int days = current ? today.Day : DateTime.DaysInMonth(year, month);
        lines.Add($"You've averaged {Format(Math.Round(m.Spent / days, 0), symbol)} a day.");
        if (ChangeVsLastMonth(list, year, month, today) is { } change)
        {
            var than = current ? $"the same {today.Day} {(today.Day == 1 ? "day" : "days")} last month" : "last month";
            lines.Add(change >= 0 ? $"That's {change}% more than {than}." : $"That's {-change}% less than {than}.");
        }
        var biggest = m.Expenses.MaxBy(t => t.Amount)!;
        lines.Add($"Biggest single spend: {Format(biggest.Amount, symbol)} on {(biggest.Note.Length > 0 ? biggest.Note : biggest.Category)} ({biggest.Date:ddd d MMM}).");
        return lines;
    }
}
