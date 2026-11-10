using CommandHub.Study;

namespace CommandHub.Tests;

public class StudyLogTests
{
    const string Json = """
        {"version":1,
         "subjects":[{"id":"00000000-0000-0000-0000-000000000001","name":"DIP","color":"#C8CDD8"},
                     {"id":"b69cecda-3f38-4f50-b662-8a1492689c89","name":"ML","color":"#4FD1C5"}],
         "sessions":[
           {"start":"2026-11-10T09:00:00+05:00","seconds":7200,"mode":"Stopwatch","subjectId":"b69cecda-3f38-4f50-b662-8a1492689c89","description":"Assignment","rating":4},
           {"start":"2026-11-10T14:00:00+05:00","seconds":3600,"mode":"Focus","subjectId":"b69cecda-3f38-4f50-b662-8a1492689c89","description":" lecture 8 "},
           {"start":"2026-11-09T20:30:00+05:00","seconds":1800,"mode":"Timer","subjectId":"ffffffff-0000-0000-0000-000000000000","description":""},
           null, {"seconds":5}, {"start":"bad","seconds":60}],
         "settings":{"dailyGoalMinutes":90}}
        """;

    [Fact]
    public void Parse_ReadsSessionsSubjectsAndGoal()
    {
        var log = StudyLog.Parse(Json, TimeZoneInfo.FindSystemTimeZoneById("Pakistan Standard Time"))!;
        Assert.Equal(3, log.Sessions.Count);
        Assert.Equal(90, log.GoalMinutes);
        var a = log.Sessions.Single(s => s.Task == "Assignment");
        Assert.Equal("ML", a.Subject);
        Assert.Equal("#4FD1C5", a.Color);
        Assert.Equal(new DateTime(2026, 11, 10, 9, 0, 0), a.Start);
        Assert.Equal(4, a.Rating);
        Assert.Equal("Stopwatch", a.Mode);
    }

    [Fact]
    public void Parse_TrimsTask_AndNamesEmptyOnes()
    {
        var log = StudyLog.Parse(Json, TimeZoneInfo.FindSystemTimeZoneById("Pakistan Standard Time"))!;
        Assert.Contains(log.Sessions, s => s.Task == "lecture 8");
        Assert.Contains(log.Sessions, s => s.Task == "Untitled");
    }

    [Fact]
    public void Parse_UnknownSubject_FallsBackToGeneral() =>
        Assert.Equal("DIP", StudyLog.Parse(Json, TimeZoneInfo.FindSystemTimeZoneById("Pakistan Standard Time"))!
            .Sessions.Single(s => s.Task == "Untitled").Subject);

    [Fact]
    public void Parse_Garbage_IsNull() => Assert.Null(StudyLog.Parse("nope", TimeZoneInfo.Local));

    [Fact]
    public void ReadFile_Missing_IsNull() => Assert.Null(StudyLog.ReadFile(@"C:\nope\data.json"));
}

public class StudyStatsTests
{
    static readonly DateOnly Today = new(2026, 11, 10); // a Tuesday

    static StudySession S(int daysAgo, int hour, int minutes, string subject = "ML", string task = "Assignment", string color = "#4FD1C5") =>
        new(Today.AddDays(-daysAgo).ToDateTime(new TimeOnly(hour, 0)), minutes * 60, subject, color, task, "Focus", 0);

    [Theory]
    [InlineData(StudyRange.Week, 6)]
    [InlineData(StudyRange.Month, 29)]
    public void Bounds_EndToday(StudyRange r, int daysBack)
    {
        var (from, to) = StudyStats.Bounds(r, Today, []);
        Assert.Equal(Today, to);
        Assert.Equal(Today.AddDays(-daysBack), from);
    }

    [Fact]
    public void Bounds_AllTime_StartsAtFirstSession() =>
        Assert.Equal(Today.AddDays(-40), StudyStats.Bounds(StudyRange.AllTime, Today, [S(3, 9, 10), S(40, 9, 10)]).From);

    [Fact]
    public void Summary_TotalsAveragesAndGoalDays()
    {
        var sessions = new[] { S(0, 9, 120), S(0, 14, 60), S(1, 20, 30), S(10, 9, 600) };
        var sum = StudyStats.Summarise(sessions, Today.AddDays(-6), Today, goalMinutes: 90);
        Assert.Equal(210 * 60, sum.TotalSeconds);
        Assert.Equal(3, sum.Sessions);
        Assert.Equal(2, sum.ActiveDays);
        Assert.Equal(210 * 60 / 7, sum.DailyAverageSeconds);
        Assert.Equal(1, sum.GoalDays);
        Assert.Equal(120 * 60, sum.Longest!.Seconds);
    }

    [Fact]
    public void Streaks_CurrentAndBest()
    {
        var sessions = new[] { S(0, 9, 10), S(1, 9, 10), S(5, 9, 10), S(6, 9, 10), S(7, 9, 10) };
        Assert.Equal((2, 3), StudyStats.Streaks(sessions, Today));
    }

    [Fact]
    public void Streak_TodayEmpty_CountsFromYesterday() =>
        Assert.Equal(2, StudyStats.Streaks([S(1, 9, 10), S(2, 9, 10)], Today).Current);

    [Fact]
    public void Change_VsPreviousPeriod()
    {
        var sessions = new[] { S(0, 9, 90), S(8, 9, 60) };
        Assert.Equal(50, StudyStats.ChangePercent(sessions, Today.AddDays(-6), Today));
    }

    [Fact]
    public void Change_NoPreviousData_IsNull() =>
        Assert.Null(StudyStats.ChangePercent([S(0, 9, 90)], Today.AddDays(-6), Today));

    [Fact]
    public void PerDay_StacksBySubject_IncludingEmptyDays()
    {
        var days = StudyStats.PerDay([S(0, 9, 60), S(0, 10, 30, "DIP", "Lab", "#C8CDD8"), S(2, 9, 20)], Today.AddDays(-2), Today);
        Assert.Equal(3, days.Count);
        Assert.Empty(days[1].Parts);
        Assert.Equal(90 * 60, days[2].TotalSeconds);
        Assert.Equal(new[] { "ML", "DIP" }, days[2].Parts.Select(p => p.Subject));
    }

    [Fact]
    public void BySubject_SharesAddUp()
    {
        var subjects = StudyStats.BySubject([S(0, 9, 90), S(0, 12, 30, "DIP", "Lab", "#C8CDD8")]);
        Assert.Equal("ML", subjects[0].Subject);
        Assert.Equal(0.75, subjects[0].Share, 3);
        Assert.Equal(1.0, subjects.Sum(s => s.Share), 3);
    }

    [Fact]
    public void ByTask_SeparatesTasksWithinACourse_AndMergesCaseInsensitively()
    {
        var tasks = StudyStats.ByTask([S(0, 9, 120, task: "Assignment"), S(1, 9, 60, task: "Lecture 8"), S(2, 9, 30, task: "assignment ")]);
        Assert.Equal(2, tasks.Count);
        Assert.Equal("Assignment", tasks[0].Task);
        Assert.Equal(150 * 60, tasks[0].Seconds);
        Assert.Equal(2, tasks[0].Sessions);
        Assert.Equal(Today, tasks[0].LastStudied);
        Assert.Equal("Lecture 8", tasks[1].Task);
    }

    [Fact]
    public void ByTask_SameTaskNameInTwoCoursesStaysSeparate() =>
        Assert.Equal(2, StudyStats.ByTask([S(0, 9, 30, "ML", "Quiz"), S(0, 10, 30, "DIP", "Quiz", "#C8CDD8")]).Count);

    [Fact]
    public void HourGrid_SplitsAcrossHours_MondayFirst()
    {
        // Today is Tuesday: 9:00 for 90 minutes → 60 min at 9, 30 min at 10 on row 1 (Tue).
        var grid = StudyStats.HourGrid([S(0, 9, 90)]);
        Assert.Equal(3600, grid[1, 9]);
        Assert.Equal(1800, grid[1, 10]);
        Assert.Equal(0, grid[0, 9]);
    }

    [Fact]
    public void PeakWindow_FindsBestTwoHours()
    {
        var grid = StudyStats.HourGrid([S(0, 21, 120), S(1, 21, 60), S(2, 9, 30)]);
        Assert.Equal("9 pm – 11 pm", StudyStats.PeakWindow(grid));
    }

    [Fact]
    public void PeakWindow_Empty_IsNull() => Assert.Null(StudyStats.PeakWindow(new int[7, 24]));

    [Fact]
    public void Insights_MentionTopCourseAndTask()
    {
        var sessions = new[] { S(0, 9, 120, task: "Assignment"), S(0, 14, 60, task: "Lecture 8"), S(1, 21, 30, "DIP", "Lab", "#C8CDD8") };
        var lines = StudyStats.Insights(sessions, Today.AddDays(-6), Today, 90);
        Assert.Contains(lines, l => l.Contains("ML") && l.Contains("86%"));
        Assert.Contains(lines, l => l.Contains("Assignment"));
    }

    [Theory]
    [InlineData(0, "0m")]
    [InlineData(59, "0m")]
    [InlineData(25 * 60, "25m")]
    [InlineData(3 * 3600, "3h")]
    [InlineData(3 * 3600 + 26 * 60, "3h 26m")]
    public void Format(int seconds, string expected) => Assert.Equal(expected, StudyStats.Format(seconds));
}

public class StudyHardeningTests
{
    [Fact]
    public void Parse_HugeSeconds_IsCappedAtADay_AndTotalsDoNotOverflow()
    {
        var log = StudyLog.Parse("""{"sessions":[{"start":"2026-11-10T09:00:00+05:00","seconds":2147483647},{"start":"2026-11-10T10:00:00+05:00","seconds":2147483647}]}""", TimeZoneInfo.Utc)!;
        Assert.All(log.Sessions, s => Assert.Equal(86400, s.Seconds));
        var day = new DateOnly(2026, 11, 10);
        Assert.Equal(2 * 86400, StudyStats.Summarise(log.Sessions, day, day, 60).TotalSeconds);
    }

    [Fact]
    public void Parse_ExtremeStart_IsSkipped_NotThrown()
    {
        var log = StudyLog.Parse("""{"sessions":[{"start":"0001-01-01T00:00:00+05:00","seconds":60},{"start":"2026-11-10T09:00:00+05:00","seconds":60}]}""", TimeZoneInfo.Utc)!;
        Assert.Single(log.Sessions);
    }

    [Theory]
    [InlineData(7, 1)]
    [InlineData(30, 3)]
    [InlineData(52, 6)]
    [InlineData(150, 15)]
    public void LabelEvery_KeepsAboutTenLabels(int bars, int every) => Assert.Equal(every, StudyStats.LabelEvery(bars));
}
