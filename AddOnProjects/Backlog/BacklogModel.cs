using System.Text.Json;

namespace CommandHub.AddOn.Backlog;

public enum GameStatus { Backlog, Playing, Finished, Platinum, Shelved }

public sealed class BacklogData
{
    public List<Game> Games { get; set; } = [];
}

public sealed class Game
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string Platform { get; set; } = "PS5";
    public GameStatus Status { get; set; } = GameStatus.Backlog;
    public decimal Hours { get; set; }
    /// <summary>Roughly how long it takes to finish (your guess or a "how long to beat" figure); helps pick what's next.</summary>
    public decimal? EstimateHours { get; set; }
    /// <summary>Out of 10, 0 = not rated.</summary>
    public int Rating { get; set; }
    public string Image { get; set; } = "";
    /// <summary>The PlayStation Store concept, when it came from there (to avoid adding it twice).</summary>
    public string ConceptId { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTime AddedUtc { get; set; } = DateTime.UtcNow;
    public DateOnly? Started { get; set; }
    public DateOnly? Finished { get; set; }
    public List<Session> Sessions { get; set; } = [];
}

public sealed class Session
{
    public DateOnly Date { get; set; }
    public decimal Hours { get; set; }
}

/// <summary>A game on the PlayStation add-on's wishlist, offered here once you've bought it.</summary>
public sealed record Wished(string ConceptId, string Name, string Image);

public sealed record BacklogStats(int Owned, int Backlog, decimal? BacklogHours, int Playing, int FinishedThisYear, int Platinums, decimal HoursThisWeek, decimal HoursThisMonth, decimal HoursTotal);

public static class BacklogLogic
{
    public static string StatusName(GameStatus s) => s switch
    {
        GameStatus.Backlog => "Backlog",
        GameStatus.Playing => "Playing",
        GameStatus.Finished => "Finished",
        GameStatus.Platinum => "Platinum",
        _ => "Shelved",
    };

    public static void SetStatus(Game g, GameStatus status, DateOnly today)
    {
        g.Status = status;
        if (status == GameStatus.Playing) g.Started ??= today;
        if (status is GameStatus.Finished or GameStatus.Platinum)
        {
            g.Started ??= today;
            g.Finished ??= today;
        }
        else g.Finished = null;
    }

    /// <summary>Logs time played today; playing something on your backlog starts it.</summary>
    public static void AddHours(Game g, decimal hours, DateOnly today)
    {
        if (hours == 0) return;
        g.Hours = Math.Max(0, g.Hours + hours);
        var day = g.Sessions.FirstOrDefault(s => s.Date == today);
        if (day is null) g.Sessions.Add(day = new Session { Date = today });
        day.Hours = Math.Max(0, day.Hours + hours);
        if (hours > 0 && g.Status == GameStatus.Backlog) SetStatus(g, GameStatus.Playing, today);
    }

    public static BacklogStats Stats(IReadOnlyList<Game> games, DateOnly today)
    {
        var week = today.AddDays(-6);
        var month = new DateOnly(today.Year, today.Month, 1);
        var backlog = games.Where(g => g.Status == GameStatus.Backlog).ToList();
        decimal? backlogHours = backlog.Count > 0 && backlog.All(g => g.EstimateHours is not null) ? backlog.Sum(g => g.EstimateHours!.Value)
            : backlog.Any(g => g.EstimateHours is not null) ? backlog.Sum(g => g.EstimateHours ?? 0) : null;
        return new BacklogStats(
            games.Count, backlog.Count, backlogHours, games.Count(g => g.Status == GameStatus.Playing),
            games.Count(g => g.Status is GameStatus.Finished or GameStatus.Platinum && g.Finished?.Year == today.Year),
            games.Count(g => g.Status == GameStatus.Platinum),
            games.Sum(g => g.Sessions.Where(s => s.Date >= week && s.Date <= today).Sum(s => s.Hours)),
            games.Sum(g => g.Sessions.Where(s => s.Date >= month && s.Date <= today).Sum(s => s.Hours)),
            games.Sum(g => g.Hours));
    }

    /// <summary>What to play next from the backlog: random, or the shortest you've estimated.</summary>
    public static Game? Pick(IReadOnlyList<Game> games, Random random, bool shortest)
    {
        var pool = games.Where(g => g.Status == GameStatus.Backlog).ToList();
        if (pool.Count == 0) return null;
        if (shortest && pool.Any(g => g.EstimateHours is not null))
            return pool.Where(g => g.EstimateHours is not null).OrderBy(g => g.EstimateHours).First();
        return pool[random.Next(pool.Count)];
    }

    /// <summary>Reads the PlayStation add-on's wishlist file (if it's installed), skipping games already here.</summary>
    public static List<Wished> ReadPlayStationWishlist(string json, IEnumerable<Game> have)
    {
        var owned = have.Select(g => g.ConceptId).Where(c => c.Length > 0).ToHashSet();
        var list = new List<Wished>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("wishlist", out var w) || w.ValueKind != JsonValueKind.Array) return list;
            foreach (var item in w.EnumerateArray())
            {
                string S(string n) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                var id = S("conceptId");
                if (id.Length == 0 || owned.Contains(id) || list.Any(x => x.ConceptId == id)) continue;
                list.Add(new Wished(id, S("name"), S("image")));
            }
        }
        catch (JsonException) { }
        return list;
    }

    /// <summary>"12.5 h", "45 min".</summary>
    public static string Hours(decimal h) => h < 1 && h > 0 ? $"{Math.Round(h * 60)} min" : $"{h:0.#} h";
}
