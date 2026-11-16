namespace CommandHub.AddOn.Flashcards;

public sealed class FlashData
{
    public List<Deck> Decks { get; set; } = [];
    /// <summary>New cards introduced per deck per day.</summary>
    public int NewPerDay { get; set; } = 10;
    public List<DayLog> Log { get; set; } = [];
}

public sealed class Deck
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public List<Card> Cards { get; set; } = [];
}

public sealed class Card
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Front { get; set; } = "";
    public string Back { get; set; } = "";
    /// <summary>Pronunciation or reading (romaji, furigana…), shown with the answer.</summary>
    public string Reading { get; set; } = "";
    public string Example { get; set; } = "";
    public DateOnly Added { get; set; }
    /// <summary>Null until first studied.</summary>
    public DateOnly? Due { get; set; }
    /// <summary>The day it was first studied (for the daily new-card limit).</summary>
    public DateOnly? FirstSeen { get; set; }
    public int Reps { get; set; }
    public int IntervalDays { get; set; }
    public double Ease { get; set; } = 2.5;
    public int Lapses { get; set; }
}

public sealed class DayLog
{
    public DateOnly Date { get; set; }
    public int Reviewed { get; set; }
    public int Remembered { get; set; }
}

public enum Answer { Again, Hard, Good, Easy }

/// <summary>Spaced repetition (an SM-2 variant, like Anki), importing word lists, and stats. Pure, so it's tested.</summary>
public static class Cards
{
    /// <summary>The interval (days) an answer would give, without changing the card: shown on the answer buttons.</summary>
    public static int Preview(Card c, Answer a) => Next(c, a).Interval;

    private static (int Interval, double Ease) Next(Card c, Answer a)
    {
        double ease = c.Ease;
        int interval;
        switch (a)
        {
            case Flashcards.Answer.Again:
                return (0, Math.Max(1.3, ease - 0.2));
            case Flashcards.Answer.Hard:
                interval = c.Reps == 0 ? 1 : Math.Max(c.IntervalDays + 1, (int)Math.Round(c.IntervalDays * 1.2));
                return (interval, Math.Max(1.3, ease - 0.15));
            case Flashcards.Answer.Good:
                interval = c.Reps switch { 0 => 1, 1 => 3, _ => Math.Max(c.IntervalDays + 1, (int)Math.Round(c.IntervalDays * ease)) };
                return (interval, ease);
            default:
                interval = c.Reps == 0 ? 4 : Math.Max(c.IntervalDays + 2, (int)Math.Round(c.IntervalDays * ease * 1.3));
                return (interval, ease + 0.15);
        }
    }

    /// <summary>Records an answer: when the card comes back, and how easy it is. "Again" brings it back this session.</summary>
    public static void Answer(Card c, Answer a, DateOnly today)
    {
        c.FirstSeen ??= today;
        var (interval, ease) = Next(c, a);
        c.Ease = ease;
        if (a == Flashcards.Answer.Again)
        {
            if (c.Reps > 0) c.Lapses++;
            c.Reps = 0;
            c.IntervalDays = 0;
            c.Due = today;
            return;
        }
        c.Reps++;
        c.IntervalDays = interval;
        c.Due = today.AddDays(interval);
    }

    /// <summary>Today's cards for a deck: everything due, then new cards up to the daily limit (minus new ones already started today).</summary>
    public static List<Card> Queue(Deck deck, DateOnly today, int newPerDay)
    {
        var due = deck.Cards.Where(c => c.Due is { } d && d <= today).OrderBy(c => c.Due).ThenBy(c => c.IntervalDays).ToList();
        int newToday = deck.Cards.Count(c => c.FirstSeen == today);
        var fresh = deck.Cards.Where(c => c.Due is null).OrderBy(c => c.Added).Take(Math.Max(0, newPerDay - newToday));
        return due.Concat(fresh).ToList();
    }

    public static int Learned(Deck d) => d.Cards.Count(c => c.Reps > 0);
    public static int Mature(Deck d) => d.Cards.Count(c => c.IntervalDays >= 21);

    public static void LogAnswer(FlashData data, DateOnly today, bool remembered)
    {
        var day = data.Log.FirstOrDefault(l => l.Date == today);
        if (day is null) data.Log.Add(day = new DayLog { Date = today });
        day.Reviewed++;
        if (remembered) day.Remembered++;
    }

    /// <summary>Days in a row with reviews, ending today (or yesterday if today hasn't started).</summary>
    public static int Streak(IEnumerable<DayLog> log, DateOnly today)
    {
        var days = log.Where(l => l.Reviewed > 0).Select(l => l.Date).ToHashSet();
        var d = days.Contains(today) ? today : today.AddDays(-1);
        int n = 0;
        while (days.Contains(d)) { n++; d = d.AddDays(-1); }
        return n;
    }

    /// <summary>
    /// Cards from pasted lines: "front ; back ; reading ; example", or tab-separated (from a spreadsheet),
    /// or "front - back". Blank lines and lines without an answer are skipped.
    /// </summary>
    public static List<Card> Parse(string text, DateOnly today)
    {
        var list = new List<Card>();
        foreach (var raw in (text ?? "").Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r');
            if (line.Length == 0) continue;
            string[] parts = line.Contains('\t') ? line.Split('\t')
                : line.Contains(';') ? line.Split(';')
                : line.Contains(" - ") ? line.Split(" - ", 2)
                : line.Contains('=') ? line.Split('=', 2)
                : [line];
            parts = parts.Select(p => p.Trim()).ToArray();
            if (parts.Length < 2 || parts[0].Length == 0 || parts[1].Length == 0) continue;
            list.Add(new Card
            {
                Front = parts[0], Back = parts[1], Reading = parts.Length > 2 ? parts[2] : "", Example = parts.Length > 3 ? parts[3] : "", Added = today,
            });
        }
        return list;
    }

    /// <summary>"in 3 d", "in 2 mo" for the answer buttons.</summary>
    public static string Spell(int days) => days switch
    {
        <= 0 => "now",
        < 30 => $"{days} d",
        < 365 => $"{Math.Round(days / 30.0, 1):0.#} mo",
        _ => $"{Math.Round(days / 365.0, 1):0.#} y",
    };

    /// <summary>A starter deck: the 46 basic hiragana with their romaji.</summary>
    public static Deck Hiragana(DateOnly today)
    {
        const string pairs = "あ a,い i,う u,え e,お o,か ka,き ki,く ku,け ke,こ ko,さ sa,し shi,す su,せ se,そ so,た ta,ち chi,つ tsu,て te,と to,な na,に ni,ぬ nu,ね ne,の no,は ha,ひ hi,ふ fu,へ he,ほ ho,ま ma,み mi,む mu,め me,も mo,や ya,ゆ yu,よ yo,ら ra,り ri,る ru,れ re,ろ ro,わ wa,を wo,ん n";
        var deck = new Deck { Name = "Japanese: Hiragana" };
        foreach (var p in pairs.Split(','))
        {
            var bits = p.Split(' ');
            deck.Cards.Add(new Card { Front = bits[0], Back = bits[1], Added = today });
        }
        return deck;
    }
}
