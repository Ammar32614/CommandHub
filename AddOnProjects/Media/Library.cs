using System.IO;
using System.Net.Http;
using CommandHub.AddOns;

namespace CommandHub.AddOn.Media;

/// <summary>Owns the lists: saving, adding, progress, the daily refresh of air dates, and MyAnimeList import.</summary>
public sealed class Library(IAddOnHost host)
{
    private const string File = "media";
    public Sources Sources { get; } = new(host.Http);
    public MediaData Data { get; private set; } = new();
    public bool Refreshing { get; private set; }
    public event Action? Changed;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    public void Load()
    {
        Data = host.Load<MediaData>(File);
        Data.Items = (Data.Items ?? []).Where(i => i is not null && i.Key.Length > 0).GroupBy(i => i.Key).Select(g => g.First()).ToList();
        foreach (var i in Data.Items) { i.Log ??= []; i.Title ??= ""; i.Note ??= ""; i.Image ??= ""; i.ImdbId ??= ""; }
    }

    public void Save()
    {
        try { host.Save(File, Data); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { host.Notify("Media: couldn't save. " + ex.Message); }
    }

    private void Commit() { Save(); Changed?.Invoke(); }

    public MediaItem? Find(string key) => Data.Items.FirstOrDefault(i => i.Key == key);

    public async Task<MediaItem> Add(Found found, WatchStatus status)
    {
        // Already there, perhaps imported from MyAnimeList under another key.
        var existing = Find(found.Key) ?? (found.MalId is { } mal ? Data.Items.FirstOrDefault(i => i.MalId == mal) : null);
        if (existing is not null) return existing;
        var item = MediaLogic.FromFound(found);
        MediaLogic.SetStatus(item, status, DateTime.UtcNow);
        Data.Items.Insert(0, item);
        Commit();
        if (item.Kind == MediaKind.Show) await RefreshShow(item);
        return item;
    }

    public void Remove(MediaItem item) { Data.Items.Remove(item); Commit(); }

    public void SetProgress(MediaItem item, int episodes) { MediaLogic.SetProgress(item, episodes, Today, DateTime.UtcNow); Commit(); }
    public void SetStatus(MediaItem item, WatchStatus status) { MediaLogic.SetStatus(item, status, DateTime.UtcNow); Commit(); }
    public void SetRating(MediaItem item, int rating) { item.Rating = Math.Clamp(rating, 0, 10); Commit(); }
    public void SetNote(MediaItem item, string note) { item.Note = note; Save(); }

    private async Task RefreshShow(MediaItem item)
    {
        if (item.ImdbId.Length == 0) return;
        try
        {
            if (await Sources.Show(item.ImdbId, item.TvMazeId) is not { } info) return;
            item.TvMazeId = info.TvMazeId;
            item.Minutes ??= info.Runtime;
            item.AiringStatus = info.Status;
            item.NextEpisodeUtc = info.NextUtc;
            item.NextEpisodeNumber = info.NextNumber;
            // A finished show's length is known; a running one's is what has aired so far.
            if (info.Status == "Ended") item.TotalEpisodes = info.AiredEpisodes;
            else item.TotalEpisodes = null;
            if (item.Image.Length == 0) item.Image = info.Image;
            Commit();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { }
    }

    /// <summary>The daily refresh: air dates and episode counts for what you're watching or planning to. Mentions today's new episodes.</summary>
    public async Task Refresh(bool quiet = false)
    {
        if (Refreshing) return;
        Refreshing = true;
        Changed?.Invoke();
        try
        {
            var follow = Data.Items.Where(i => i.Status is WatchStatus.Watching or WatchStatus.PlanToWatch or WatchStatus.OnHold).ToList();
            // Titles imported while AniList was unreachable: fill in their AniList details now.
            var bare = Data.Items.Where(i => i.AniListId is null && i.MalId is not null).ToList();
            foreach (var chunk in bare.Chunk(50))
            {
                var byMal = (await Sources.AnimeByMalIds(chunk.Select(i => i.MalId!.Value).ToList())).Where(f => f.MalId is not null).ToDictionary(f => f.MalId!.Value);
                foreach (var i in chunk)
                {
                    if (!byMal.TryGetValue(i.MalId!.Value, out var f)) continue;
                    i.AniListId = f.AniListId;
                    if (i.Image.Length == 0) i.Image = f.Image;
                    i.TotalEpisodes ??= f.Episodes;
                    i.Minutes ??= f.Minutes;
                }
            }
            var anime = follow.Where(i => i.AniListId is not null).ToList();
            foreach (var chunk in anime.Chunk(50))
            {
                var found = (await Sources.AnimeByIds(chunk.Select(i => i.AniListId!.Value).ToList())).ToDictionary(f => f.AniListId!.Value);
                foreach (var i in chunk)
                {
                    if (!found.TryGetValue(i.AniListId!.Value, out var f)) continue;
                    i.TotalEpisodes = f.Episodes ?? i.TotalEpisodes;
                    i.Minutes ??= f.Minutes;
                    i.NextEpisodeUtc = f.NextEpisodeUtc;
                    i.NextEpisodeNumber = f.NextEpisodeNumber;
                    i.AiringStatus = f.AiringStatus;
                    if (i.Image.Length == 0) i.Image = f.Image;
                }
            }
            foreach (var show in follow.Where(i => i.Kind == MediaKind.Show)) await RefreshShow(show);
            Data.LastCheckUtc = DateTime.UtcNow;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { } // try again tomorrow
        finally
        {
            Refreshing = false;
            Commit();
        }
        var today = Data.Items.Where(i => i.Status == WatchStatus.Watching && i.NextEpisodeUtc is { } t && t.ToLocalTime().Date == DateTime.Today).ToList();
        if (!quiet && today.Count > 0)
            host.Notify("New today: " + string.Join(", ", today.Take(3).Select(i => $"{i.Title} episode {i.NextEpisodeNumber}")) + (today.Count > 3 ? $" and {today.Count - 3} more." : "."));
    }

    /// <summary>Imports a MyAnimeList export. Returns how many titles were added.</summary>
    public async Task<int> ImportMal(Stream file)
    {
        var entries = MediaLogic.ReadMalExport(file);
        if (entries.Count == 0) throw new InvalidDataException("That file isn't a MyAnimeList export. On MyAnimeList, use Profile › Export list, then pick the downloaded file.");
        var byMal = new Dictionary<int, Found>();
        foreach (var chunk in entries.Select(e => e.MalId).Distinct().Chunk(50))
        {
            try
            {
                foreach (var f in await Sources.AnimeByMalIds(chunk))
                    if (f.MalId is { } m) byMal[m] = f;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { } // import what we can; titles come from the file
            await Task.Delay(700); // AniList allows about 90 requests a minute
        }
        var added = MediaLogic.Import(Data, entries, byMal, DateTime.UtcNow);
        Commit();
        return added;
    }
}
