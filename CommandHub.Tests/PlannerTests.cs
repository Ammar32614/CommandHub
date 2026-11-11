using CommandHub.Planner;

namespace CommandHub.Tests;

public class PlannerLogicTests
{
    static readonly DateOnly Today = new(2026, 11, 10); // Tuesday

    static PlanItem Item(string title, int daysFromToday, PlanKind kind = PlanKind.Deadline, string? start = null, bool weekly = false, int? untilDays = null, bool done = false) => new()
    {
        Title = title, Kind = kind, Date = Today.AddDays(daysFromToday),
        Start = start is null ? null : TimeOnly.Parse(start), Weekly = weekly,
        Until = untilDays is { } u ? Today.AddDays(u) : null, Done = done,
    };

    [Fact]
    public void WeekStart_IsMonday() => Assert.Equal(new DateOnly(2026, 11, 9), PlannerLogic.WeekStart(Today));

    [Fact]
    public void WeekStart_OnSunday_GoesBackSixDays() => Assert.Equal(new DateOnly(2026, 11, 9), PlannerLogic.WeekStart(new DateOnly(2026, 11, 15)));

    [Fact]
    public void MonthGrid_Is42DaysStartingMonday()
    {
        var days = PlannerLogic.MonthGrid(2026, 11);
        Assert.Equal(42, days.Count);
        Assert.Equal(DayOfWeek.Monday, days[0].DayOfWeek);
        Assert.Contains(new DateOnly(2026, 11, 1), days);
        Assert.Contains(new DateOnly(2026, 11, 30), days);
    }

    [Fact]
    public void Occurrences_SingleItemsInRange_SortedByDateThenTime()
    {
        var items = new[] { Item("B", 1, start: "14:00"), Item("A", 1, start: "09:00"), Item("Out", 20), Item("C", 0) };
        var occ = PlannerLogic.Occurrences(items, Today, Today.AddDays(6));
        Assert.Equal(new[] { "C", "A", "B" }, occ.Select(o => o.Item.Title));
    }

    [Fact]
    public void Occurrences_AllDayBeforeTimed_OnTheSameDay()
    {
        var occ = PlannerLogic.Occurrences([Item("Timed", 0, start: "08:00"), Item("AllDay", 0)], Today, Today);
        Assert.Equal(new[] { "AllDay", "Timed" }, occ.Select(o => o.Item.Title));
    }

    [Fact]
    public void Occurrences_WeeklyRepeats_UntilTheEndDate()
    {
        var cls = Item("ML lecture", -7, PlanKind.Class, "10:00", weekly: true, untilDays: 14);
        var occ = PlannerLogic.Occurrences([cls], Today.AddDays(-7), Today.AddDays(30));
        Assert.Equal(new[] { Today.AddDays(-7), Today, Today.AddDays(7), Today.AddDays(14) }, occ.Select(o => o.Date));
    }

    [Fact]
    public void Occurrences_WeeklyWithoutEnd_StartsOnFirstDateNotBefore()
    {
        var cls = Item("Lab", 3, PlanKind.Class, weekly: true);
        var occ = PlannerLogic.Occurrences([cls], Today, Today.AddDays(13));
        Assert.Equal(new[] { Today.AddDays(3), Today.AddDays(10) }, occ.Select(o => o.Date));
    }

    [Fact]
    public void UpNext_OverdueFirst_ThenUpcoming_SkippingDone()
    {
        var items = new[]
        {
            Item("Late report", -2), Item("Done thing", -1, done: true), Item("Exam", 5, PlanKind.Exam),
            Item("Quiz", 1, PlanKind.Exam), Item("Far", 40), Item("Old event", -3, PlanKind.Event),
        };
        var next = PlannerLogic.UpNext(items, Today, 14);
        Assert.Equal(new[] { "Late report", "Quiz", "Exam" }, next.Select(o => o.Item.Title));
        Assert.True(PlannerLogic.IsOverdue(next[0], Today));
        Assert.False(PlannerLogic.IsOverdue(next[1], Today));
    }

    [Fact]
    public void UpNext_IncludesNextClassOccurrence()
    {
        var next = PlannerLogic.UpNext([Item("Lecture", -6, PlanKind.Class, "09:00", weekly: true)], Today, 14);
        Assert.Equal(Today.AddDays(1), next.First().Date);
    }

    [Theory]
    [InlineData(0, "Today")]
    [InlineData(1, "Tomorrow")]
    [InlineData(3, "In 3 days")]
    [InlineData(-1, "Yesterday")]
    [InlineData(-4, "4 days ago")]
    public void When_Labels(int days, string expected) => Assert.Equal(expected, PlannerLogic.When(Today.AddDays(days), Today));

    [Theory]
    [InlineData("2026-11-20", 2026, 11, 20)]
    [InlineData("20 Nov", 2026, 11, 20)]
    [InlineData("nov 20", 2026, 11, 20)]
    [InlineData("today", 2026, 11, 10)]
    [InlineData("Tomorrow", 2026, 11, 11)]
    [InlineData("fri", 2026, 11, 13)]
    [InlineData("tue", 2026, 11, 10)]
    [InlineData("mon", 2026, 11, 16)]
    [InlineData("5 jan", 2027, 1, 5)]
    public void ParseDate_Natural(string text, int y, int m, int d) =>
        Assert.Equal(new DateOnly(y, m, d), PlannerLogic.ParseDate(text, Today));

    [Theory]
    [InlineData("")]
    [InlineData("someday")]
    [InlineData("31 feb")]
    public void ParseDate_Invalid_IsNull(string text) => Assert.Null(PlannerLogic.ParseDate(text, Today));

    [Theory]
    [InlineData("14:00", 14, 0)]
    [InlineData("2pm", 14, 0)]
    [InlineData("2:30 pm", 14, 30)]
    [InlineData("9", 9, 0)]
    [InlineData("12am", 0, 0)]
    [InlineData("12 pm", 12, 0)]
    [InlineData("0930", 9, 30)]
    public void ParseTime_Flexible(string text, int h, int m) => Assert.Equal(new TimeOnly(h, m), PlannerLogic.ParseTime(text));

    [Theory]
    [InlineData("25:00")]
    [InlineData("13pm")]
    [InlineData("noonish")]
    public void ParseTime_Invalid_IsNull(string text) => Assert.Null(PlannerLogic.ParseTime(text));

    [Fact]
    public void ParseTime_Blank_IsNull_AndValid() => Assert.True(PlannerLogic.TryParseOptionalTime("  ", out var t) && t is null);

    [Fact]
    public void Validate_NeedsTitle_AndEndAfterStart()
    {
        Assert.Equal("Give it a name.", PlannerLogic.Validate("", Today, null, null));
        Assert.Equal("The end time should be after the start.", PlannerLogic.Validate("x", Today, new TimeOnly(14, 0), new TimeOnly(13, 0)));
        Assert.Null(PlannerLogic.Validate("x", Today, new TimeOnly(14, 0), new TimeOnly(15, 0)));
    }

    [Fact]
    public void Counts_ForTheHeadline()
    {
        var occ = PlannerLogic.Occurrences([Item("a", 0), Item("b", 1, PlanKind.Exam), Item("c", 2, PlanKind.Class, weekly: true), Item("d", 3)], Today, Today.AddDays(6));
        Assert.Equal("2 deadlines   ·   1 exam   ·   1 class", PlannerLogic.Summary(occ));
    }
}

public class PlannerDataTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hub-plan-" + Guid.NewGuid().ToString("N"));
    private string PathIn => Path.Combine(_dir, "data.json");
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [Fact]
    public void PlanItems_RoundTrip()
    {
        var store = new CommandHub.Services.DataStore(PathIn);
        var d = HubData.CreateDefault();
        d.Planner.Add(new PlanItem
        {
            Title = "ML final", Kind = PlanKind.Exam, Date = new DateOnly(2026, 12, 15), Start = new TimeOnly(9, 30),
            End = new TimeOnly(12, 0), Course = "ML", Notes = "Chapters 1-6", Weekly = false,
        });
        store.Save(d);
        var item = Assert.Single(store.Load().Planner);
        Assert.Equal("ML final", item.Title);
        Assert.Equal(PlanKind.Exam, item.Kind);
        Assert.Equal(new TimeOnly(9, 30), item.Start);
        Assert.Equal("Chapters 1-6", item.Notes);
    }

    [Fact]
    public void Version2File_UpgradesTo3_KeepingTiles_AndAddsUpNextInAFreeSpot()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathIn, """{"version":2,"pages":[{"name":"Home","tiles":[{"typeId":"note","column":0,"row":0,"size":"Wide","content":{"text":"keep"}}]}]}""");
        var store = new CommandHub.Services.DataStore(PathIn);
        var d = store.Load();
        Assert.Equal(HubData.CurrentVersion, d.Version);
        Assert.True(store.Upgraded);
        Assert.Empty(d.Planner);
        Assert.Equal("keep", (string?)d.Pages[0].Tiles.Single(t => t.TypeId == "note").Content["text"]);
        var upNext = Assert.Single(d.Pages[0].Tiles, t => t.TypeId == "upnext");
        Assert.True(CommandHub.Layout.BoardGrid.Fits(d.Pages[0].Tiles, upNext.Column, upNext.Row, upNext.Size, upNext.Id));
    }

    [Fact]
    public void NullPlannerEntries_AreDropped()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathIn, """{"version":3,"pages":[],"planner":[null,{"title":null,"kind":"Exam","date":"2026-12-01"}]}""");
        var item = Assert.Single(new CommandHub.Services.DataStore(PathIn).Load().Planner);
        Assert.Equal("Untitled", item.Title);
    }
}

public class PlannerUpNextRepeatTests
{
    [Fact]
    public void UpNext_ShowsOnlyTheNextOccurrenceOfAWeeklyClass()
    {
        var today = new DateOnly(2026, 11, 10);
        var cls = new PlanItem { Title = "Lecture", Kind = PlanKind.Class, Date = today.AddDays(-1), Weekly = true };
        var next = PlannerLogic.UpNext([cls], today, 21);
        Assert.Equal(today.AddDays(6), Assert.Single(next).Date);
    }
}

public class PlannerUpNextNowTests
{
    static readonly DateOnly Today = new(2026, 11, 10);

    [Fact]
    public void UpNext_SkipsWhatAlreadyEndedToday_AndShowsNextWeeksClass()
    {
        var cls = new PlanItem { Title = "Lecture", Kind = PlanKind.Class, Date = Today, Start = new TimeOnly(9, 0), End = new TimeOnly(10, 30), Weekly = true };
        var next = PlannerLogic.UpNext([cls], Today, 21, now: new TimeOnly(18, 0));
        Assert.Equal(Today.AddDays(7), Assert.Single(next).Date);
    }

    [Fact]
    public void UpNext_KeepsLaterToday_AndAllDayItems()
    {
        var later = new PlanItem { Title = "Evening exam", Kind = PlanKind.Exam, Date = Today, Start = new TimeOnly(19, 0) };
        var allDay = new PlanItem { Title = "Due today", Kind = PlanKind.Deadline, Date = Today };
        var started = new PlanItem { Title = "Ends later", Kind = PlanKind.Event, Date = Today, Start = new TimeOnly(17, 0), End = new TimeOnly(20, 0) };
        var next = PlannerLogic.UpNext([later, allDay, started], Today, 21, now: new TimeOnly(18, 0));
        Assert.Equal(3, next.Count);
    }
}

public class PlannerTimeHintTests
{
    [Theory]
    [InlineData("", "", false)]
    [InlineData("2pm", "Read as 2 pm", false)]
    [InlineData("14:30", "Read as 2:30 pm", false)]
    [InlineData("2", "Read as 2 am. Type 2pm for the afternoon.", false)]
    [InlineData("7", "Read as 7 am. Type 7pm for the evening.", false)]
    [InlineData("9", "Read as 9 am", false)]
    [InlineData("25:00", "Try \"2pm\" or 14:00", true)]
    public void TimeHint_ExplainsHowATimeWasRead(string text, string hint, bool problem) =>
        Assert.Equal((hint, problem), PlannerLogic.TimeHint(text));
}
