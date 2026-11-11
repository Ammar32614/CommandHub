using CommandHub.Journal;
using CommandHub.Money;

namespace CommandHub.Tests;

public class MoneyLogicTests
{
    static readonly DateOnly Today = new(2026, 11, 11);

    static Transaction Spend(decimal amount, string category, int daysAgo = 0, string note = "") =>
        new() { Amount = amount, Category = category, Date = Today.AddDays(-daysAgo), Kind = MoneyKind.Expense, Note = note };

    static Transaction Earn(decimal amount, int daysAgo = 0) =>
        new() { Amount = amount, Category = "Allowance", Date = Today.AddDays(-daysAgo), Kind = MoneyKind.Income };

    [Theory]
    [InlineData("1200", 1200)]
    [InlineData("1,200", 1200)]
    [InlineData("1.5k", 1500)]
    [InlineData("Rs 450", 450)]
    [InlineData("  99.50 ", 99.50)]
    [InlineData("2K", 2000)]
    public void ParseAmount_Forgiving(string text, decimal expected) => Assert.Equal(expected, MoneyLogic.ParseAmount(text));

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-50")]
    [InlineData("0")]
    public void ParseAmount_Invalid_IsNull(string text) => Assert.Null(MoneyLogic.ParseAmount(text));

    [Fact]
    public void Format_UsesSymbolAndGrouping() => Assert.Equal("Rs 12,340", MoneyLogic.Format(12340m, "Rs"));

    [Fact]
    public void Format_KeepsCentsOnlyWhenPresent() => Assert.Equal("$ 4.50", MoneyLogic.Format(4.5m, "$"));

    [Fact]
    public void Month_TotalsSpentAndIncome_OnlyThatMonth()
    {
        var t = new[] { Spend(500, "Food"), Spend(300, "Transport", 5), Earn(20000, 3), Spend(999, "Food", 40) };
        var m = MoneyLogic.Month(t, 2026, 11);
        Assert.Equal(800m, m.Spent);
        Assert.Equal(20000m, m.Income);
        Assert.Equal(2, m.Expenses.Count);
    }

    [Fact]
    public void ByCategory_SortedWithShares()
    {
        var cats = MoneyLogic.ByCategory([Spend(600, "Food"), Spend(200, "Transport"), Spend(200, "Food")]);
        Assert.Equal("Food", cats[0].Category);
        Assert.Equal(800m, cats[0].Amount);
        Assert.Equal(0.8, cats[0].Share, 3);
    }

    [Fact]
    public void Budget_LeftAndPerDay_CountsToday()
    {
        // Nov 11 → 20 days left including today; spent 800 of 20,000.
        var b = MoneyLogic.Budget(20000m, 800m, Today);
        Assert.Equal(19200m, b.Left);
        Assert.Equal(20, b.DaysLeft);
        Assert.Equal(960m, b.PerDay);
        Assert.False(b.Over);
    }

    [Fact]
    public void Budget_Overspent_PerDayIsZero()
    {
        var b = MoneyLogic.Budget(500m, 800m, Today);
        Assert.True(b.Over);
        Assert.Equal(0m, b.PerDay);
        Assert.Equal(-300m, b.Left);
    }

    [Fact]
    public void DailySpend_HasEveryDayOfTheMonth()
    {
        var days = MoneyLogic.DailySpend([Spend(100, "Food"), Spend(50, "Food"), Spend(70, "Fun", 1)], 2026, 11);
        Assert.Equal(30, days.Count);
        Assert.Equal(150m, days[10]);
        Assert.Equal(70m, days[9]);
    }

    [Fact]
    public void ChangeVsLastMonth_Percent()
    {
        var t = new[] { Spend(1500, "Food"), Spend(1000, "Food", 30) }; // Oct 12
        Assert.Equal(50, MoneyLogic.ChangeVsLastMonth(t, 2026, 11));
    }

    [Fact]
    public void ChangeVsLastMonth_NoHistory_IsNull() => Assert.Null(MoneyLogic.ChangeVsLastMonth([Spend(5, "Food")], 2026, 11));

    [Fact]
    public void Insights_NameBiggestCategory()
    {
        var lines = MoneyLogic.Insights([Spend(600, "Food"), Spend(200, "Transport")], 2026, 11, "Rs", Today);
        Assert.Contains(lines, l => l.Contains("Food") && l.Contains("75%"));
    }

    // Today is Wednesday 11 Nov 2026. Payments are logged after the fact, so dates read backwards.
    [Theory]
    [InlineData("", "2026-11-11")]
    [InlineData("today", "2026-11-11")]
    [InlineData("Yesterday", "2026-11-10")]
    [InlineData("wed", "2026-11-11")]
    [InlineData("mon", "2026-11-09")]
    [InlineData("thursday", "2026-11-05")]
    [InlineData("5 nov", "2026-11-05")]
    [InlineData("nov 20", "2025-11-20")]
    [InlineData("2026-10-31", "2026-10-31")]
    public void ParsePastDate_ReadsBackwards(string text, string expected) =>
        Assert.Equal(DateOnly.Parse(expected), MoneyLogic.ParsePastDate(text, Today));

    [Theory]
    [InlineData("tomorrow")]
    [InlineData("2026-12-01")]
    [InlineData("soon")]
    public void ParsePastDate_RejectsFutureAndNonsense(string text) =>
        Assert.Null(MoneyLogic.ParsePastDate(text, Today));
}

public class JournalLogicTests
{
    static readonly DateOnly Today = new(2026, 11, 11);

    static JournalEntry E(int daysAgo, string text = "x", int mood = 3) => new() { Date = Today.AddDays(-daysAgo), Text = text, Mood = mood };

    [Fact]
    public void Streaks_CurrentAndBest()
    {
        var entries = new[] { E(0), E(1), E(2), E(5), E(6), E(7), E(8) };
        Assert.Equal((3, 4), JournalLogic.Streaks(entries, Today));
    }

    [Fact]
    public void Streak_EmptyEntriesDoNotCount() =>
        Assert.Equal(1, JournalLogic.Streaks([E(0), E(1, text: "  ", mood: 0)], Today).Current);

    [Fact]
    public void Tags_FromHashtags_Distinct_Lowercase() =>
        Assert.Equal(new[] { "exams", "gym" }, JournalLogic.Tags("Long day #Exams then #gym. Again #exams"));

    [Theory]
    [InlineData("", 0)]
    [InlineData("one two  three\nfour", 4)]
    public void WordCount(string text, int n) => Assert.Equal(n, JournalLogic.WordCount(text));

    [Fact]
    public void Search_MatchesTextAndTags_NewestFirst()
    {
        var entries = new[] { E(3, "Studied ML all day"), E(1, "Gym then #ml revision"), E(2, "Nothing much") };
        var found = JournalLogic.Search(entries, "ml");
        Assert.Equal(new[] { Today.AddDays(-1), Today.AddDays(-3) }, found.Select(e => e.Date));
    }

    [Fact]
    public void Search_Blank_ReturnsNothing() => Assert.Empty(JournalLogic.Search([E(1)], "  "));

    [Fact]
    public void OnThisDay_WeekMonthYearAgo()
    {
        var entries = new[] { E(7, "week"), new JournalEntry { Date = Today.AddMonths(-1), Text = "month" }, new JournalEntry { Date = Today.AddYears(-1), Text = "year" }, E(3, "no") };
        var memories = JournalLogic.OnThisDay(entries, Today);
        Assert.Equal(new[] { "A week ago", "A month ago", "A year ago" }, memories.Select(m => m.Label));
    }

    [Fact]
    public void MoodAverage_IgnoresUnset() =>
        Assert.Equal(4.0, JournalLogic.MoodAverage([E(0, mood: 5), E(1, mood: 3), E(2, mood: 0)])!.Value, 3);

    [Fact]
    public void PromptFor_SameDaySamePrompt_Changes()
    {
        Assert.Equal(JournalLogic.PromptFor(Today), JournalLogic.PromptFor(Today));
        Assert.NotEqual(JournalLogic.PromptFor(Today), JournalLogic.PromptFor(Today.AddDays(1)));
    }

    [Fact]
    public void IsEmpty_NoMoodNoText() => Assert.True(JournalLogic.IsEmpty(new JournalEntry { Date = Today, Text = " \n " }));
}

public class MoneyJournalDataTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hub-mj-" + Guid.NewGuid().ToString("N"));
    private string PathIn => Path.Combine(_dir, "data.json");
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [Fact]
    public void MoneyAndJournal_RoundTrip()
    {
        var store = new CommandHub.Services.DataStore(PathIn);
        var d = HubData.CreateDefault();
        d.Money.Currency = "Rs";
        d.Money.MonthlyBudget = 20000m;
        d.Money.CategoryBudgets["Food"] = 6000m;
        d.Money.Transactions.Add(new Transaction { Amount = 450.5m, Category = "Food", Date = new DateOnly(2026, 11, 11), Note = "Lunch" });
        d.Journal.Add(new JournalEntry { Date = new DateOnly(2026, 11, 11), Mood = 4, Text = "Good day #ml" });
        store.Save(d);
        var back = store.Load();
        Assert.Equal(20000m, back.Money.MonthlyBudget);
        Assert.Equal(6000m, back.Money.CategoryBudgets["Food"]);
        Assert.Equal(450.5m, Assert.Single(back.Money.Transactions).Amount);
        Assert.Equal("Good day #ml", Assert.Single(back.Journal).Text);
    }

    [Fact]
    public void NullMoneyAndJournal_AreRepaired()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathIn, """{"version":3,"pages":[],"money":{"transactions":[null,{"amount":5,"category":null}],"categoryBudgets":null},"journal":[null,{"date":"2026-11-11","text":null}]}""");
        var d = new CommandHub.Services.DataStore(PathIn).Load();
        Assert.Equal("Other", Assert.Single(d.Money.Transactions).Category);
        Assert.NotNull(d.Money.CategoryBudgets);
        Assert.Equal("", Assert.Single(d.Journal).Text);
    }

    [Fact]
    public void Journal_KeepsOneEntryPerDay_TheLongest()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathIn, """{"version":3,"pages":[],"journal":[{"date":"2026-11-11","text":"short"},{"date":"2026-11-11","text":"the longer one"}]}""");
        Assert.Equal("the longer one", Assert.Single(new CommandHub.Services.DataStore(PathIn).Load().Journal).Text);
    }
}
