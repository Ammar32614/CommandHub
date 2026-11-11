using System.IO;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace CommandHub.AddOn.Media;

public enum MediaKind { Anime, Show, Movie }

public enum WatchStatus { PlanToWatch, Watching, Completed, OnHold, Dropped }

/// <summary>Everything the add-on saves (media.json in its data folder).</summary>
public sealed class MediaData
{
    public List<MediaItem> Items { get; set; } = [];
    public DateTime? LastCheckUtc { get; set; }
}

/// <summary>One anime, show or movie on your lists.</summary>
public sealed class MediaItem
{
    /// <summary>"anilist:154587" or "imdb:tt1160419": unique per title.</summary>
    public string Key { get; set; } = "";
    public MediaKind Kind { get; set; }
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public int? Year { get; set; }
    public string Image { get; set; } = "";
    public WatchStatus Status { get; set; } = WatchStatus.PlanToWatch;
    /// <summary>Your score out of 10 (0 = not rated).</summary>
    public int Rating { get; set; }
    public int Progress { get; set; }
    public int? TotalEpisodes { get; set; }
    /// <summary>Minutes per episode (or the movie's length), for time-watched stats.</summary>
    public int? Minutes { get; set; }
    public int? AniListId { get; set; }
    public int? MalId { get; set; }
    public string ImdbId { get; set; } = "";
    public int? TvMazeId { get; set; }
    public string Note { get; set; } = "";
    public DateTime AddedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedUtc { get; set; }
    public DateTime? FinishedUtc { get; set; }
    public DateTime? NextEpisodeUtc { get; set; }
    public int? NextEpisodeNumber { get; set; }
    public string AiringStatus { get; set; } = "";
    /// <summary>Days you logged episodes, for "watched this week" and streaks.</summary>
    public List<WatchDay> Log { get; set; } = [];
}

public sealed class WatchDay
{
    public DateOnly Date { get; set; }
    public int Episodes { get; set; }
}

/// <summary>What a search finds, before it's on a list.</summary>
public sealed record Found(string Key, MediaKind Kind, string Title, string Subtitle, int? Year, string Image, int? Episodes, int? Minutes,
    int? AniListId, int? MalId, string ImdbId, string Score, string AiringStatus, DateTime? NextEpisodeUtc, int? NextEpisodeNumber);

public sealed record MediaStats(int Completed, int Watching, int Planned, int EpisodesThisWeek, int EpisodesThisMonth, double HoursTotal, double? AverageRating, Dictionary<MediaKind, int> CompletedByKind);

/// <summary>The list rules: progress, status changes, stats, MyAnimeList import. Pure, so it's tested.</summary>
public static class MediaLogic
{
    public static string StatusName(WatchStatus s) => s switch
    {
        WatchStatus.PlanToWatch => "Watchlist",
        WatchStatus.Watching => "Watching",
        WatchStatus.Completed => "Completed",
        WatchStatus.OnHold => "On hold",
        _ => "Dropped",
    };

    public static string KindName(MediaKind k) => k switch { MediaKind.Anime => "Anime", MediaKind.Show => "Show", _ => "Movie" };

    public static MediaItem FromFound(Found f) => new()
    {
        Key = f.Key, Kind = f.Kind, Title = f.Title, Subtitle = f.Subtitle, Year = f.Year, Image = f.Image, TotalEpisodes = f.Kind == MediaKind.Movie ? 1 : f.Episodes,
        Minutes = f.Minutes, AniListId = f.AniListId, MalId = f.MalId, ImdbId = f.ImdbId, AiringStatus = f.AiringStatus,
        NextEpisodeUtc = f.NextEpisodeUtc, NextEpisodeNumber = f.NextEpisodeNumber,
    };

    /// <summary>
    /// Sets how many episodes you've watched, logging the difference on <paramref name="today"/>. Reaching the last
    /// episode completes it; starting one on your watchlist moves it to Watching.
    /// </summary>
    public static void SetProgress(MediaItem item, int episodes, DateOnly today, DateTime nowUtc)
    {
        var max = item.TotalEpisodes is > 0 ? item.TotalEpisodes.Value : int.MaxValue;
        var value = Math.Clamp(episodes, 0, max);
        var delta = value - item.Progress;
        item.Progress = value;
        if (delta > 0)
        {
            var day = item.Log.FirstOrDefault(d => d.Date == today);
            if (day is null) item.Log.Add(day = new WatchDay { Date = today });
            day.Episodes += delta;
        }
        if (value > 0 && item.Status == WatchStatus.PlanToWatch) SetStatus(item, WatchStatus.Watching, nowUtc);
        if (item.TotalEpisodes is > 0 && value >= item.TotalEpisodes && item.Status != WatchStatus.Completed) SetStatus(item, WatchStatus.Completed, nowUtc);
    }

    public static void SetStatus(MediaItem item, WatchStatus status, DateTime nowUtc)
    {
        item.Status = status;
        if (status == WatchStatus.Watching) item.StartedUtc ??= nowUtc;
        if (status == WatchStatus.Completed)
        {
            item.FinishedUtc ??= nowUtc;
            if (item.TotalEpisodes is > 0 && item.Progress < item.TotalEpisodes) item.Progress = item.TotalEpisodes.Value;
            if (item.Kind == MediaKind.Movie) item.Progress = 1;
        }
        else item.FinishedUtc = null;
    }

    public static MediaStats Stats(IReadOnlyList<MediaItem> items, DateOnly today)
    {
        var weekStart = today.AddDays(-6);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        int week = items.Sum(i => i.Log.Where(d => d.Date >= weekStart && d.Date <= today).Sum(d => d.Episodes));
        int month = items.Sum(i => i.Log.Where(d => d.Date >= monthStart && d.Date <= today).Sum(d => d.Episodes));
        double minutes = items.Sum(i => (double)i.Progress * (i.Minutes ?? (i.Kind == MediaKind.Movie ? 120 : i.Kind == MediaKind.Anime ? 24 : 45)));
        var rated = items.Where(i => i.Rating > 0).ToList();
        return new MediaStats(
            items.Count(i => i.Status == WatchStatus.Completed), items.Count(i => i.Status == WatchStatus.Watching), items.Count(i => i.Status == WatchStatus.PlanToWatch),
            week, month, Math.Round(minutes / 60, 1), rated.Count == 0 ? null : Math.Round(rated.Average(i => i.Rating), 1),
            Enum.GetValues<MediaKind>().ToDictionary(k => k, k => items.Count(i => i.Kind == k && i.Status == WatchStatus.Completed)));
    }

    /// <summary>Things airing soon that you're watching or planning to, soonest first.</summary>
    public static List<MediaItem> ComingUp(IEnumerable<MediaItem> items, DateTime nowUtc, int days = 7) =>
        items.Where(i => i.Status is WatchStatus.Watching or WatchStatus.PlanToWatch or WatchStatus.OnHold
                         && i.NextEpisodeUtc is { } t && t >= nowUtc.AddHours(-12) && t <= nowUtc.AddDays(days))
             .OrderBy(i => i.NextEpisodeUtc).ToList();

    /// <summary>"Today, 9:30 pm", "Tomorrow", "Sat 15 Nov".</summary>
    public static string When(DateTime utc, DateTime nowLocal)
    {
        var local = utc.ToLocalTime();
        var days = (local.Date - nowLocal.Date).Days;
        var time = local.ToString("h:mm tt").ToLowerInvariant();
        return days switch
        {
            < 0 => "Out now",
            0 => $"Today, {time}",
            1 => $"Tomorrow, {time}",
            _ => local.ToString("ddd d MMM") + ", " + time,
        };
    }

    /// <summary>A random pick from the watchlist (seeded so it's testable).</summary>
    public static MediaItem? Pick(IReadOnlyList<MediaItem> items, Random random, MediaKind? kind = null)
    {
        var pool = items.Where(i => i.Status == WatchStatus.PlanToWatch && (kind is null || i.Kind == kind)).ToList();
        return pool.Count == 0 ? null : pool[random.Next(pool.Count)];
    }

    // ---------------- MyAnimeList export ----------------

    public sealed record MalEntry(int MalId, string Title, int Episodes, int Watched, int Score, WatchStatus Status, DateOnly? Finished);

    /// <summary>Reads MyAnimeList's list export (the .xml, or the .xml.gz it downloads as). Skips anything unreadable.</summary>
    public static List<MalEntry> ReadMalExport(Stream stream)
    {
        var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        buffer.Position = 0;
        Stream source = buffer;
        if (buffer.Length > 2 && buffer.GetBuffer()[0] == 0x1F && buffer.GetBuffer()[1] == 0x8B)
            source = new GZipStream(buffer, CompressionMode.Decompress);
        XDocument doc;
        try
        {
            using var reader = XmlReader.Create(source, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
            doc = XDocument.Load(reader);
        }
        catch (Exception ex) when (ex is XmlException or InvalidDataException) { return []; }
        var list = new List<MalEntry>();
        foreach (var a in doc.Descendants("anime"))
        {
            int I(string name) => int.TryParse(a.Element(name)?.Value.Trim(), out var v) ? v : 0;
            var id = I("series_animedb_id");
            if (id <= 0) continue;
            var status = (a.Element("my_status")?.Value.Trim() ?? "") switch
            {
                "Watching" or "1" => WatchStatus.Watching,
                "Completed" or "2" => WatchStatus.Completed,
                "On-Hold" or "3" => WatchStatus.OnHold,
                "Dropped" or "4" => WatchStatus.Dropped,
                _ => WatchStatus.PlanToWatch,
            };
            DateOnly? finished = DateOnly.TryParse(a.Element("my_finish_date")?.Value.Trim(), out var f) && f.Year > 1900 ? f : null;
            list.Add(new MalEntry(id, a.Element("series_title")?.Value.Trim() ?? "", I("series_episodes"), I("my_watched_episodes"), Math.Clamp(I("my_score"), 0, 10), status, finished));
        }
        return list;
    }

    /// <summary>Adds MyAnimeList entries (with what AniList knows about them) without duplicating what's already on your lists.</summary>
    public static int Import(MediaData data, IEnumerable<MalEntry> entries, IReadOnlyDictionary<int, Found> byMalId, DateTime nowUtc)
    {
        int added = 0;
        foreach (var e in entries)
        {
            if (data.Items.Any(i => i.MalId == e.MalId)) continue;
            var item = byMalId.TryGetValue(e.MalId, out var f)
                ? FromFound(f)
                : new MediaItem { Key = $"mal:{e.MalId}", Kind = MediaKind.Anime, Title = e.Title, MalId = e.MalId, TotalEpisodes = e.Episodes > 0 ? e.Episodes : null };
            item.Status = e.Status;
            item.Rating = e.Score;
            item.Progress = item.TotalEpisodes is > 0 ? Math.Min(e.Watched, item.TotalEpisodes.Value) : e.Watched;
            if (e.Status == WatchStatus.Completed)
            {
                item.FinishedUtc = e.Finished?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) ?? nowUtc;
                if (item.TotalEpisodes is > 0) item.Progress = item.TotalEpisodes.Value;
            }
            data.Items.Add(item);
            added++;
        }
        return added;
    }

    public static string LinkFor(MediaItem i) =>
        i.MalId is { } mal ? $"https://myanimelist.net/anime/{mal}"
        : i.AniListId is { } al ? $"https://anilist.co/anime/{al}"
        : i.ImdbId.Length > 0 ? $"https://www.imdb.com/title/{i.ImdbId}/"
        : "";
}
