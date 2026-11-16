using CommandHub.AddOn.Backlog;

namespace CommandHub.Tests;

public class BacklogTests
{
    static readonly DateOnly Today = new(2026, 11, 16);

    [Fact]
    public void Hours_StartTheGame_AndLogTheDay()
    {
        var g = new Game { Title = "Astro Bot" };
        BacklogLogic.AddHours(g, 1.5m, Today);
        BacklogLogic.AddHours(g, 1m, Today);
        Assert.Equal(GameStatus.Playing, g.Status);
        Assert.Equal(2.5m, g.Hours);
        Assert.Equal(2.5m, Assert.Single(g.Sessions).Hours);
        BacklogLogic.AddHours(g, -10m, Today); // undoing can't go below zero
        Assert.Equal(0m, g.Hours);
    }

    [Fact]
    public void Stats_CountBacklogHoursFinishesAndTime()
    {
        var a = new Game { Status = GameStatus.Backlog, EstimateHours = 12 };
        var b = new Game { Status = GameStatus.Backlog, EstimateHours = 30 };
        var c = new Game { Status = GameStatus.Platinum, Finished = Today.AddDays(-10), Hours = 60 };
        var d = new Game { Status = GameStatus.Playing };
        BacklogLogic.AddHours(d, 3, Today.AddDays(-1));
        var s = BacklogLogic.Stats([a, b, c, d], Today);
        Assert.Equal(2, s.Backlog);
        Assert.Equal(42, s.BacklogHours);
        Assert.Equal(1, s.FinishedThisYear);
        Assert.Equal(1, s.Platinums);
        Assert.Equal(3, s.HoursThisWeek);
        Assert.Equal(63, s.HoursTotal);
    }

    [Fact]
    public void Pick_ShortestOrRandom_OnlyFromTheBacklog()
    {
        var shortOne = new Game { Title = "short", EstimateHours = 6 };
        var longOne = new Game { Title = "long", EstimateHours = 80 };
        var done = new Game { Title = "done", Status = GameStatus.Finished };
        Assert.Same(shortOne, BacklogLogic.Pick([longOne, shortOne, done], new Random(1), shortest: true));
        Assert.NotSame(done, BacklogLogic.Pick([longOne, shortOne, done], new Random(7), shortest: false));
        Assert.Null(BacklogLogic.Pick([done], new Random(1), false));
    }

    [Fact]
    public void PlayStationWishlist_SkipsWhatYouAlreadyHave()
    {
        var json = """{"wishlist":[{"conceptId":"10000333","name":"ELDEN RING","image":"https://x/e.png"},{"conceptId":"10010195","name":"Tales of the Shire"},{"conceptId":"10000333","name":"dupe edition"}]}""";
        var have = new[] { new Game { ConceptId = "10010195" } };
        var w = Assert.Single(BacklogLogic.ReadPlayStationWishlist(json, have));
        Assert.Equal("ELDEN RING", w.Name);
        Assert.Empty(BacklogLogic.ReadPlayStationWishlist("broken", have));
        Assert.Equal("45 min", BacklogLogic.Hours(0.75m));
    }
}
