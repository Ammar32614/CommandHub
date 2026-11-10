using CommandHub.Tiles.Countdown;
using CommandHub.Tiles.Focus;
using CommandHub.Tiles.Greeting;
using CommandHub.Tiles.Habits;
using CommandHub.Tiles.Progress;
using CommandHub.Tiles.SystemPulse;
using CommandHub.Tiles.Weather;

namespace CommandHub.Tests;

public class GreetingTests
{
    [Theory]
    [InlineData(5, "Good morning")]
    [InlineData(11, "Good morning")]
    [InlineData(12, "Good afternoon")]
    [InlineData(16, "Good afternoon")]
    [InlineData(17, "Good evening")]
    [InlineData(21, "Good evening")]
    [InlineData(22, "Good night")]
    [InlineData(3, "Good night")]
    public void Salutation_ByHour(int hour, string expected) => Assert.Equal(expected, GreetingText.Salutation(hour));

    [Theory]
    [InlineData("ammar", "Ammar")]
    [InlineData("john.smith", "John")]
    [InlineData("", "")]
    public void FriendlyName_CapitalisesFirstPart(string user, string expected) => Assert.Equal(expected, GreetingText.FriendlyName(user));

    [Fact]
    public void LineFor_SameDaySameLine_NextDayDifferent()
    {
        var d = new DateOnly(2026, 11, 10);
        Assert.Equal(GreetingText.LineFor(d), GreetingText.LineFor(d));
        Assert.NotEqual(GreetingText.LineFor(d), GreetingText.LineFor(d.AddDays(1)));
        Assert.False(string.IsNullOrWhiteSpace(GreetingText.LineFor(d)));
    }
}

public class TimeProgressTests
{
    [Fact]
    public void Day_Noon_IsHalf() => Assert.Equal(0.5, TimeProgress.Day(new DateTime(2026, 11, 10, 12, 0, 0)), 3);

    [Fact]
    public void Week_StartsMonday() =>
        Assert.Equal(0.0, TimeProgress.Week(new DateTime(2026, 11, 9, 0, 0, 0)), 3); // Monday

    [Fact]
    public void Week_WednesdayNoon() =>
        Assert.Equal(2.5 / 7, TimeProgress.Week(new DateTime(2026, 11, 11, 12, 0, 0)), 3);

    [Fact]
    public void Week_SundayLate_NearlyOne() =>
        Assert.True(TimeProgress.Week(new DateTime(2026, 11, 15, 23, 59, 0)) > 0.99);

    [Fact]
    public void Month_FirstDayMidnight_IsZero() => Assert.Equal(0.0, TimeProgress.Month(new DateTime(2026, 11, 1)), 3);

    [Fact]
    public void Month_Middle() => Assert.Equal(15.0 / 30, TimeProgress.Month(new DateTime(2026, 11, 16)), 3);

    [Fact]
    public void Year_LeapYearMiddle() => Assert.Equal(183.0 / 366, TimeProgress.Year(new DateTime(2028, 7, 2)), 3);
}

public class CountdownTests
{
    [Fact]
    public void Read_MissingKey_SeedsNextNewYear()
    {
        var events = CountdownContent.Read(new JsonObject(), new DateOnly(2026, 11, 10));
        var e = Assert.Single(events);
        Assert.Equal("New Year", e.Name);
        Assert.Equal(new DateOnly(2027, 1, 1), e.Date);
    }

    [Fact]
    public void Read_EmptyList_StaysEmpty()
    {
        var c = new JsonObject();
        CountdownContent.Write(c, []);
        Assert.Empty(CountdownContent.Read(c, new DateOnly(2026, 11, 10)));
    }

    [Fact]
    public void RoundTrip()
    {
        var c = new JsonObject();
        CountdownContent.Write(c, [new("Exam", new DateOnly(2026, 12, 1))]);
        Assert.Equal([new CountdownEntry("Exam", new DateOnly(2026, 12, 1))], CountdownContent.Read(c, new DateOnly(2026, 11, 10)));
    }

    [Fact]
    public void Read_BadDate_IsSkipped()
    {
        var c = JsonNode.Parse("""{"events":[{"name":"x","date":"soon"},{"name":"ok","date":"2026-12-01"}]}""")!.AsObject();
        Assert.Equal("ok", Assert.Single(CountdownContent.Read(c, new DateOnly(2026, 11, 10))).Name);
    }

    [Fact]
    public void Upcoming_SortsAndDropsPast()
    {
        var today = new DateOnly(2026, 11, 10);
        var list = CountdownContent.Upcoming(
            [new("Later", new(2027, 3, 1)), new("Past", new(2026, 11, 1)), new("Today", today), new("Soon", new(2026, 11, 20))], today);
        Assert.Equal(new[] { "Today", "Soon", "Later" }, list.Select(e => e.Name));
    }

    [Theory]
    [InlineData(0, "Today")]
    [InlineData(1, "Tomorrow")]
    [InlineData(10, "10 days")]
    public void Label(int days, string expected) =>
        Assert.Equal(expected, CountdownContent.Label(new DateOnly(2026, 11, 10).AddDays(days), new DateOnly(2026, 11, 10)));
}

public class HabitsTests
{
    static readonly DateOnly Today = new(2026, 11, 10);

    [Fact]
    public void Read_MissingKey_SeedsStarterHabits() =>
        Assert.Equal(3, HabitsContent.Read(new JsonObject()).Count);

    [Fact]
    public void RoundTrip()
    {
        var c = new JsonObject();
        HabitsContent.Write(c, [new Habit("Read", [Today, Today.AddDays(-1)])]);
        var h = Assert.Single(HabitsContent.Read(c));
        Assert.Equal("Read", h.Name);
        Assert.Equal(2, h.Done.Count);
    }

    [Fact]
    public void Toggle_AddsThenRemoves()
    {
        var h = new Habit("Read", []);
        HabitsContent.Toggle(h, Today);
        Assert.Contains(Today, h.Done);
        HabitsContent.Toggle(h, Today);
        Assert.DoesNotContain(Today, h.Done);
    }

    [Fact]
    public void Streak_CountsBackFromToday() =>
        Assert.Equal(3, HabitsContent.Streak(new Habit("x", [Today, Today.AddDays(-1), Today.AddDays(-2), Today.AddDays(-4)]), Today));

    [Fact]
    public void Streak_TodayNotYetDone_CountsFromYesterday() =>
        Assert.Equal(2, HabitsContent.Streak(new Habit("x", [Today.AddDays(-1), Today.AddDays(-2)]), Today));

    [Fact]
    public void Streak_None() => Assert.Equal(0, HabitsContent.Streak(new Habit("x", [Today.AddDays(-3)]), Today));

    [Fact]
    public void Write_DropsDaysOlderThanAYear()
    {
        var c = new JsonObject();
        HabitsContent.Write(c, [new Habit("x", [Today, Today.AddDays(-400)])], Today);
        Assert.Single(Assert.Single(HabitsContent.Read(c)).Done);
    }
}

public class StintReaderTests
{
    static readonly DateOnly Today = new(2026, 11, 10);

    [Fact]
    public void Read_SumsTodayAndStreakAndGoal()
    {
        var json = """
            {"version":1,"sessions":[
              {"start":"2026-11-10T11:34:32+05:00","seconds":1800},
              {"start":"2026-11-10T13:00:00+05:00","seconds":600},
              {"start":"2026-11-09T09:00:00+05:00","seconds":1200},
              {"start":"2026-11-07T09:00:00+05:00","seconds":900}],
             "settings":{"dailyGoalMinutes":90}}
            """;
        var s = StintReader.Read(json, Today)!;
        Assert.Equal(2400, s.TodaySeconds);
        Assert.Equal(90, s.GoalMinutes);
        Assert.Equal(2, s.Streak);
        Assert.Equal(7, s.Week.Count);
        Assert.Equal(2400, s.Week[^1]);
        Assert.Equal(1200, s.Week[^2]);
        Assert.Equal(900, s.Week[^4]);
    }

    [Fact]
    public void Read_Garbage_IsNull() => Assert.Null(StintReader.Read("{ nope", Today));

    [Fact]
    public void Read_MissingParts_UsesDefaults()
    {
        var s = StintReader.Read("""{"sessions":[null,{"seconds":5}]}""", Today)!;
        Assert.Equal(0, s.TodaySeconds);
        Assert.Equal(120, s.GoalMinutes);
    }

    [Fact]
    public void ReadFile_Missing_IsNull() => Assert.Null(StintReader.ReadFile(@"C:\nope\data.json", Today));
}

public class SystemPulseTests
{
    [Fact]
    public void CpuPercent_FromTimeDeltas()
    {
        // kernel time includes idle time
        var a = new CpuTimes(Idle: 100, Kernel: 200, User: 100);
        var b = new CpuTimes(Idle: 160, Kernel: 300, User: 140);
        // busy = (100 + 40) - 60 = 80 of 140
        Assert.Equal(80.0 / 140 * 100, SystemStats.CpuPercent(a, b), 3);
    }

    [Fact]
    public void CpuPercent_NoTimePassed_IsZero() =>
        Assert.Equal(0, SystemStats.CpuPercent(new CpuTimes(1, 2, 3), new CpuTimes(1, 2, 3)));

    [Theory]
    [InlineData(59, "0m")]
    [InlineData(3600 * 5 + 60 * 7, "5h 7m")]
    [InlineData(86400 * 2 + 3600 * 3, "2d 3h")]
    public void FormatUptime(int seconds, string expected) =>
        Assert.Equal(expected, SystemStats.FormatUptime(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Sample_ReturnsSaneValues()
    {
        var s = SystemStats.Sample();
        Assert.InRange(s.MemoryPercent, 0, 100);
        Assert.True(s.Uptime > TimeSpan.Zero);
    }
}

public class WeatherTests
{
    const string Forecast = """
        {"current":{"time":"2026-11-10T21:00","temperature_2m":18.4,"apparent_temperature":17.0,"weather_code":2,"wind_speed_10m":7.2,"is_day":0},
         "daily":{"time":["2026-11-10","2026-11-11","2026-11-12","2026-11-13"],"weather_code":[2,61,0,3],
                  "temperature_2m_max":[24.1,22.0,25.3,23.9],"temperature_2m_min":[13.2,12.9,14.0,13.5]}}
        """;

    [Fact]
    public void Parse_Forecast()
    {
        var r = WeatherApi.ParseForecast(Forecast)!;
        Assert.Equal(18.4, r.Temperature, 1);
        Assert.Equal(17.0, r.FeelsLike, 1);
        Assert.Equal(2, r.Code);
        Assert.False(r.IsDay);
        Assert.Equal(4, r.Days.Count);
        Assert.Equal(new DayForecast(new DateOnly(2026, 11, 11), 61, 22.0, 12.9), r.Days[1]);
    }

    [Fact]
    public void Parse_Garbage_IsNull() => Assert.Null(WeatherApi.ParseForecast("""{"error":true}"""));

    [Fact]
    public void Parse_Place()
    {
        var p = WeatherApi.ParsePlace("""{"results":[{"name":"Lahore","latitude":31.55,"longitude":74.34,"country":"Pakistan"}]}""")!;
        Assert.Equal("Lahore, Pakistan", p.Label);
        Assert.Equal(31.55, p.Latitude, 2);
    }

    [Fact]
    public void Parse_Place_NoResults_IsNull() => Assert.Null(WeatherApi.ParsePlace("""{"generationtime_ms":0.3}"""));

    [Theory]
    [InlineData(0, "Clear")]
    [InlineData(2, "Partly cloudy")]
    [InlineData(45, "Fog")]
    [InlineData(61, "Rain")]
    [InlineData(71, "Snow")]
    [InlineData(95, "Thunderstorm")]
    [InlineData(1234, "Unknown")]
    public void Describe(int code, string text) => Assert.Equal(text, WeatherApi.Describe(code).Text);

    [Fact]
    public void ForecastUrl_UsesFixedCoordinatesOnly()
    {
        var url = WeatherApi.ForecastUrl(31.55, 74.34);
        Assert.StartsWith("https://api.open-meteo.com/v1/forecast?latitude=31.55&longitude=74.34", url);
    }
}

public class WeatherConnectTests
{
    [Fact]
    public void ConnectOrder_PutsIPv4First_KeepsIPv6AsFallback()
    {
        var v6 = System.Net.IPAddress.Parse("2a01:4f8::1");
        var v4 = System.Net.IPAddress.Parse("192.0.2.10");
        Assert.Equal([v4, v6], WeatherApi.ConnectOrder([v6, v4]));
    }
}
