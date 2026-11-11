using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace CommandHub.AddOn.Media;

/// <summary>
/// Where titles come from, all free and without an account:
/// AniList for anime (it carries MyAnimeList IDs, so every anime links to its MyAnimeList page),
/// IMDb's search suggestions for movies and shows, and TVmaze (matched by IMDb ID) for show episodes and air dates.
/// </summary>
public sealed class Sources(HttpClient http)
{
    private const string AniListUrl = "https://graphql.anilist.co";

    private const string AniListFields = "id idMal title{romaji english} format episodes duration status seasonYear averageScore nextAiringEpisode{episode airingAt} coverImage{large}";

    private async Task<JsonDocument> AniList(string query, object variables, CancellationToken ct)
    {
        using var content = new StringContent(JsonSerializer.Serialize(new { query, variables }), Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, AniListUrl) { Content = content };
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"AniList answered {(int)response.StatusCode}.");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    }

    public async Task<List<Found>> SearchAnime(string term, CancellationToken ct = default)
    {
        using var doc = await AniList($"query($s:String){{Page(perPage:20){{media(search:$s,type:ANIME,sort:SEARCH_MATCH){{{AniListFields}}}}}}}", new { s = term }, ct);
        return ParseAniListPage(doc.RootElement);
    }

    /// <summary>What's airing this season, most popular first.</summary>
    public async Task<List<Found>> AiringNow(CancellationToken ct = default)
    {
        using var doc = await AniList($"query{{Page(perPage:30){{media(type:ANIME,status:RELEASING,sort:POPULARITY_DESC){{{AniListFields}}}}}}}", new { }, ct);
        return ParseAniListPage(doc.RootElement);
    }

    public async Task<List<Found>> AnimeByIds(IReadOnlyList<int> aniListIds, CancellationToken ct = default)
    {
        if (aniListIds.Count == 0) return [];
        using var doc = await AniList($"query($ids:[Int]){{Page(perPage:50){{media(id_in:$ids,type:ANIME){{{AniListFields}}}}}}}", new { ids = aniListIds }, ct);
        return ParseAniListPage(doc.RootElement);
    }

    public async Task<List<Found>> AnimeByMalIds(IReadOnlyList<int> malIds, CancellationToken ct = default)
    {
        if (malIds.Count == 0) return [];
        using var doc = await AniList($"query($ids:[Int]){{Page(perPage:50){{media(idMal_in:$ids,type:ANIME){{{AniListFields}}}}}}}", new { ids = malIds }, ct);
        return ParseAniListPage(doc.RootElement);
    }

    public async Task<List<Found>> SearchImdb(string term, CancellationToken ct = default)
    {
        var t = term.Trim().ToLowerInvariant();
        if (t.Length == 0) return [];
        var url = $"https://v3.sg.media-imdb.com/suggestion/{Uri.EscapeDataString(t[..1])}/{Uri.EscapeDataString(t)}.json";
        return ParseImdb(await http.GetStringAsync(url, ct));
    }

    /// <summary>A show's episodes so far and its next one, from TVmaze. Null if TVmaze doesn't know it.</summary>
    public async Task<ShowInfo?> Show(string imdbId, int? tvMazeId, CancellationToken ct = default)
    {
        int? id = tvMazeId;
        if (id is null)
        {
            using var lookup = await http.GetAsync($"https://api.tvmaze.com/lookup/shows?imdb={Uri.EscapeDataString(imdbId)}", ct);
            if (!lookup.IsSuccessStatusCode) return null;
            using var l = JsonDocument.Parse(await lookup.Content.ReadAsStringAsync(ct));
            id = l.RootElement.TryGetProperty("id", out var v) && v.TryGetInt32(out var n) ? n : null;
            if (id is null) return null;
        }
        using var response = await http.GetAsync($"https://api.tvmaze.com/shows/{id}?embed[]=nextepisode&embed[]=episodes", ct);
        if (!response.IsSuccessStatusCode) return null;
        return ParseTvMaze(await response.Content.ReadAsStringAsync(ct), DateTime.UtcNow);
    }

    // ---------------- Parsing (public for tests) ----------------

    public sealed record ShowInfo(int TvMazeId, string Status, int AiredEpisodes, int? Runtime, DateTime? NextUtc, int? NextNumber, string Image);

    public static List<Found> ParseAniListPage(JsonElement root)
    {
        var list = new List<Found>();
        if (!root.TryGetProperty("data", out var data) || !data.TryGetProperty("Page", out var page) || !page.TryGetProperty("media", out var media) || media.ValueKind != JsonValueKind.Array) return list;
        foreach (var m in media.EnumerateArray())
        {
            if (!m.TryGetProperty("id", out var idEl) || !idEl.TryGetInt32(out var id)) continue;
            var title = m.TryGetProperty("title", out var t) ? (Str(t, "english") is { Length: > 0 } en ? en : Str(t, "romaji")) : "";
            var romaji = m.TryGetProperty("title", out var t2) ? Str(t2, "romaji") : "";
            DateTime? next = null;
            int? nextNumber = null;
            if (m.TryGetProperty("nextAiringEpisode", out var na) && na.ValueKind == JsonValueKind.Object)
            {
                if (na.TryGetProperty("airingAt", out var at) && at.TryGetInt64(out var secs)) next = DateTimeOffset.FromUnixTimeSeconds(secs).UtcDateTime;
                nextNumber = Int(na, "episode");
            }
            var cover = m.TryGetProperty("coverImage", out var c) ? Str(c, "large") : "";
            var format = Str(m, "format");
            var score = Int(m, "averageScore") is { } s ? $"{s}%" : "";
            list.Add(new Found($"anilist:{id}", MediaKind.Anime, title, romaji != title ? romaji : FormatName(format), Int(m, "seasonYear"), cover,
                Int(m, "episodes"), Int(m, "duration"), id, Int(m, "idMal"), "", score, Str(m, "status"), next, nextNumber));
        }
        return list;
    }

    private static string FormatName(string format) => format switch { "TV" => "TV series", "MOVIE" => "Anime film", "OVA" => "OVA", "ONA" => "Web series", _ => "" };

    public static List<Found> ParseImdb(string json)
    {
        var list = new List<Found>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("d", out var d) || d.ValueKind != JsonValueKind.Array) return list;
            foreach (var e in d.EnumerateArray())
            {
                var id = Str(e, "id");
                if (!id.StartsWith("tt")) continue; // people and other things
                var qid = Str(e, "qid");
                MediaKind? kind = qid switch { "movie" or "tvMovie" => MediaKind.Movie, "tvSeries" or "tvMiniSeries" => MediaKind.Show, _ => null };
                if (kind is null) continue;
                // IMDb serves posters at full size (thousands of pixels); ask for a card-sized one.
                var image = (e.TryGetProperty("i", out var i) ? Str(i, "imageUrl") : "").Replace("._V1_.", "._V1_QL75_UX320_.");
                list.Add(new Found($"imdb:{id}", kind.Value, Str(e, "l"), Str(e, "s"), Int(e, "y"), image, null, null, null, null, id, "", "", null, null));
            }
        }
        catch (JsonException) { }
        return list;
    }

    public static ShowInfo? ParseTvMaze(string json, DateTime nowUtc)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (Int(r, "id") is not { } id) return null;
            int aired = 0;
            DateTime? next = null;
            int? nextNumber = null;
            if (r.TryGetProperty("_embedded", out var emb))
            {
                if (emb.TryGetProperty("episodes", out var eps) && eps.ValueKind == JsonValueKind.Array)
                    aired = eps.EnumerateArray().Count(e => Str(e, "type") != "insignificant_special"
                        && DateTime.TryParse(Str(e, "airstamp"), null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var a) && a <= nowUtc);
                if (emb.TryGetProperty("nextepisode", out var ne) && ne.ValueKind == JsonValueKind.Object)
                {
                    if (DateTime.TryParse(Str(ne, "airstamp"), null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var a)) next = DateTime.SpecifyKind(a, DateTimeKind.Utc);
                    nextNumber = aired + 1;
                }
            }
            var image = r.TryGetProperty("image", out var im) && im.ValueKind == JsonValueKind.Object ? Str(im, "original") : "";
            int? runtime = Int(r, "averageRuntime") ?? Int(r, "runtime");
            return new ShowInfo(id, Str(r, "status"), aired, runtime, next, nextNumber, image);
        }
        catch (JsonException) { return null; }
    }

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static int? Int(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;
}
