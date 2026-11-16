using CommandHub.AddOn.Flashcards;

namespace CommandHub.Tests;

public class FlashcardsTests
{
    static readonly DateOnly Today = new(2026, 11, 16);

    [Fact]
    public void Good_GrowsTheGap_1_3_ThenTimesEase()
    {
        var c = new Card { Added = Today };
        Cards.Answer(c, Answer.Good, Today);
        Assert.Equal(Today.AddDays(1), c.Due);
        Cards.Answer(c, Answer.Good, Today.AddDays(1));
        Assert.Equal(3, c.IntervalDays);
        Cards.Answer(c, Answer.Good, Today.AddDays(4));
        Assert.Equal(8, c.IntervalDays); // 3 × 2.5 = 7.5 → 8
        Assert.Equal(3, c.Reps);
    }

    [Fact]
    public void Again_ComesBackToday_AndCountsALapse()
    {
        var c = new Card { Reps = 3, IntervalDays = 10, Due = Today };
        Cards.Answer(c, Answer.Again, Today);
        Assert.Equal(Today, c.Due);
        Assert.Equal(0, c.Reps);
        Assert.Equal(1, c.Lapses);
        Assert.Equal(2.3, c.Ease, 3);
    }

    [Fact]
    public void Easy_BeatsGood_HardIsShorter_EaseNeverBelowFloor()
    {
        var c = new Card { Reps = 2, IntervalDays = 10, Ease = 2.5 };
        Assert.True(Cards.Preview(c, Answer.Easy) > Cards.Preview(c, Answer.Good));
        Assert.True(Cards.Preview(c, Answer.Hard) < Cards.Preview(c, Answer.Good));
        Assert.True(Cards.Preview(c, Answer.Hard) > 10);
        var hardOne = new Card { Ease = 1.3 };
        Cards.Answer(hardOne, Answer.Again, Today);
        Assert.Equal(1.3, hardOne.Ease, 3);
    }

    [Fact]
    public void Queue_DueFirst_ThenNewUpToTheDailyLimit()
    {
        var deck = new Deck();
        deck.Cards.Add(new Card { Front = "due", Due = Today.AddDays(-1), Reps = 1, IntervalDays = 1 });
        deck.Cards.Add(new Card { Front = "later", Due = Today.AddDays(3), Reps = 1 });
        for (int i = 0; i < 5; i++) deck.Cards.Add(new Card { Front = $"new{i}", Added = Today.AddDays(-5 + i) });
        Assert.Equal(["due", "new0", "new1"], Cards.Queue(deck, Today, 2).Select(c => c.Front));
        deck.Cards.First(c => c.Front == "new0").FirstSeen = Today; // one new card already started today
        deck.Cards.First(c => c.Front == "new0").Due = Today.AddDays(1);
        Assert.Equal(["due", "new1"], Cards.Queue(deck, Today, 2).Select(c => c.Front));
    }

    [Fact]
    public void Parse_TakesSemicolonsTabsAndDashes()
    {
        var cards = Cards.Parse("猫 ; cat ; neko ; 猫が好き\n犬\tdog\nwater - mizu\n\njust one side\n ; empty front", Today);
        Assert.Equal(["猫", "犬", "water"], cards.Select(c => c.Front));
        Assert.Equal("neko", cards[0].Reading);
        Assert.Equal("猫が好き", cards[0].Example);
        Assert.Equal("dog", cards[1].Back);
    }

    [Fact]
    public void Streak_AndStarterDeck()
    {
        var data = new FlashData();
        Cards.LogAnswer(data, Today.AddDays(-2), true);
        Cards.LogAnswer(data, Today.AddDays(-1), false);
        Assert.Equal(2, Cards.Streak(data.Log, Today));
        Cards.LogAnswer(data, Today, true);
        Assert.Equal(3, Cards.Streak(data.Log, Today));
        Assert.Equal(46, Cards.Hiragana(Today).Cards.Count);
        Assert.Equal("2.5 mo", Cards.Spell(75));
    }
}
