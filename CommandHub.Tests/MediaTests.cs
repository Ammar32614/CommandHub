using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CommandHub.AddOn.Media;

namespace CommandHub.Tests;

public class MediaLogicTests
{
    static readonly DateOnly Today = new(2026, 11, 11);
    static readonly DateTime Now = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);

    static MediaItem Anime(int total, WatchStatus status = WatchStatus.PlanToWatch) =>
        new() { Key = "anilist:1", Kind = MediaKind.Anime, Title = "Frieren", TotalEpisodes = total, Status = status };

    [Fact]
    public void Progress_StartsWatching_LogsTheDay_AndCompletesAtTheEnd()
    {
        var item = Anime(12);
        MediaLogic.SetProgress(item, 3, Today, Now);
        Assert.Equal(WatchStatus.Watching, item.Status);
        Assert.Equal(3, Assert.Single(item.Log).Episodes);

        MediaLogic.SetProgress(item, 2, Today, Now); // going back logs nothing
        Assert.Equal(3, item.Log[0].Episodes);

        MediaLogic.SetProgress(item, 99, Today.AddDays(1), Now);
        Assert.Equal(12, item.Progress);
        Assert.Equal(WatchStatus.Completed, item.Status);
        Assert.NotNull(item.FinishedUtc);
        Assert.Equal(10, item.Log[1].Episodes);
    }

    [Fact]
    public void Progress_UnknownLength_HasNoCeiling()
    {
        var item = Anime(0);
        item.TotalEpisodes = null;
        MediaLogic.SetProgress(item, 1100, Today, Now);
        Assert.Equal(1100, item.Progress);
        Assert.Equal(WatchStatus.Watching, item.Status);
    }

    [Fact]
    public void Status_CompletedFillsProgress_AndLeavingItClearsTheFinishDate()
    {
        var item = Anime(24, WatchStatus.Watching);
        MediaLogic.SetStatus(item, WatchStatus.Completed, Now);
        Assert.Equal(24, item.Progress);
        MediaLogic.SetStatus(item, WatchStatus.Watching, Now);
        Assert.Null(item.FinishedUtc);
    }

    [Fact]
    public void Stats_CountEpisodesHoursAndRatings()
    {
        var a = Anime(12, WatchStatus.Watching);
        a.Minutes = 24;
        MediaLogic.SetProgress(a, 5, Today, Now);
        var movie = new MediaItem { Kind = MediaKind.Movie, Status = WatchStatus.Completed, Progress = 1, Minutes = 150, Rating = 9 };
        var show = new MediaItem { Kind = MediaKind.Show, Status = WatchStatus.PlanToWatch, Rating = 7 };
        var s = MediaLogic.Stats([a, movie, show], Today);
        Assert.Equal(1, s.Completed);
        Assert.Equal(5, s.EpisodesThisWeek);
        Assert.Equal(Math.Round((5 * 24 + 150) / 60.0, 1), s.HoursTotal);
        Assert.Equal(8, s.AverageRating);
        Assert.Equal(1, s.CompletedByKind[MediaKind.Movie]);
    }

    [Fact]
    public void ComingUp_OnlyWhatYouFollow_SoonestFirst()
    {
        var soon = Anime(12, WatchStatus.Watching);
        soon.NextEpisodeUtc = Now.AddDays(1);
        var later = Anime(12, WatchStatus.PlanToWatch);
        later.NextEpisodeUtc = Now.AddDays(3);
        var dropped = Anime(12, WatchStatus.Dropped);
        dropped.NextEpisodeUtc = Now.AddDays(1);
        var far = Anime(12, WatchStatus.Watching);
        far.NextEpisodeUtc = Now.AddDays(30);
        Assert.Equal([soon, later], MediaLogic.ComingUp([later, dropped, far, soon], Now));
    }

    [Fact]
    public void Pick_OnlyFromTheWatchlist()
    {
        var planned = Anime(12);
        Assert.Same(planned, MediaLogic.Pick([Anime(12, WatchStatus.Completed), planned], new Random(1)));
        Assert.Null(MediaLogic.Pick([Anime(12, WatchStatus.Completed)], new Random(1)));
    }

    const string MalXml = """
        <?xml version="1.0" encoding="UTF-8" ?>
        <myanimelist>
          <anime><series_animedb_id>52991</series_animedb_id><series_title><![CDATA[Sousou no Frieren]]></series_title>
            <series_episodes>28</series_episodes><my_watched_episodes>28</my_watched_episodes><my_score>10</my_score>
            <my_status>Completed</my_status><my_finish_date>2024-03-23</my_finish_date></anime>
          <anime><series_animedb_id>21</series_animedb_id><series_title>One Piece</series_title>
            <series_episodes>0</series_episodes><my_watched_episodes>1100</my_watched_episodes><my_score>0</my_score>
            <my_status>Watching</my_status><my_finish_date>0000-00-00</my_finish_date></anime>
          <anime><series_animedb_id>x</series_animedb_id></anime>
        </myanimelist>
        """;

    [Fact]
    public void MalExport_ReadsXmlAndGzip()
    {
        var list = MediaLogic.ReadMalExport(new MemoryStream(Encoding.UTF8.GetBytes(MalXml)));
        Assert.Equal(2, list.Count);
        Assert.Equal(new MediaLogic.MalEntry(52991, "Sousou no Frieren", 28, 28, 10, WatchStatus.Completed, new DateOnly(2024, 3, 23)), list[0]);
        Assert.Null(list[1].Finished);

        var gz = new MemoryStream();
        using (var z = new GZipStream(gz, CompressionLevel.Fastest, leaveOpen: true)) z.Write(Encoding.UTF8.GetBytes(MalXml));
        gz.Position = 0;
        Assert.Equal(2, MediaLogic.ReadMalExport(gz).Count);
        Assert.Empty(MediaLogic.ReadMalExport(new MemoryStream(Encoding.UTF8.GetBytes("not xml"))));
    }

    [Fact]
    public void Import_SkipsDuplicates_AndUsesAniListDetails()
    {
        var data = new MediaData();
        var entries = MediaLogic.ReadMalExport(new MemoryStream(Encoding.UTF8.GetBytes(MalXml)));
        var found = new Dictionary<int, Found>
        {
            [52991] = new("anilist:154587", MediaKind.Anime, "Frieren: Beyond Journey's End", "", 2023, "https://img", 28, 24, 154587, 52991, "", "90%", "FINISHED", null, null),
        };
        Assert.Equal(2, MediaLogic.Import(data, entries, found, Now));
        Assert.Equal(0, MediaLogic.Import(data, entries, found, Now));
        var frieren = data.Items.Single(i => i.MalId == 52991);
        Assert.Equal("Frieren: Beyond Journey's End", frieren.Title);
        Assert.Equal(10, frieren.Rating);
        Assert.Equal(new DateTime(2024, 3, 23), frieren.FinishedUtc!.Value.Date);
        Assert.Equal("https://myanimelist.net/anime/52991", MediaLogic.LinkFor(frieren));
        Assert.Equal(1100, data.Items.Single(i => i.MalId == 21).Progress);
    }
}

public class MediaSourceTests
{
    [Fact]
    public void AniList_ReadsTitlesEpisodesAndNextAiring()
    {
        using var doc = JsonDocument.Parse("""
            {"data":{"Page":{"media":[{"id":154587,"idMal":52991,"title":{"romaji":"Sousou no Frieren","english":"Frieren: Beyond Journey's End"},
              "format":"TV","episodes":28,"duration":24,"status":"RELEASING","seasonYear":2023,"averageScore":90,
              "nextAiringEpisode":{"episode":12,"airingAt":1795000000},"coverImage":{"large":"https://img/f.jpg"}}]}}}
            """);
        var f = Assert.Single(Sources.ParseAniListPage(doc.RootElement));
        Assert.Equal("anilist:154587", f.Key);
        Assert.Equal("Frieren: Beyond Journey's End", f.Title);
        Assert.Equal("Sousou no Frieren", f.Subtitle);
        Assert.Equal(52991, f.MalId);
        Assert.Equal(12, f.NextEpisodeNumber);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1795000000).UtcDateTime, f.NextEpisodeUtc);
        Assert.Equal("90%", f.Score);
    }

    [Fact]
    public void Imdb_KeepsMoviesAndShows_SkipsPeople()
    {
        var list = Sources.ParseImdb("""
            {"d":[{"i":{"imageUrl":"https://m/1.jpg"},"id":"tt1160419","l":"Dune","q":"feature","qid":"movie","s":"Timothée Chalamet","y":2021},
                  {"id":"nm0000123","l":"A Person"},
                  {"id":"tt11280740","l":"Severance","qid":"tvSeries","y":2022},
                  {"id":"tt999","l":"A game","qid":"videoGame"}]}
            """);
        Assert.Equal(["Dune", "Severance"], list.Select(f => f.Title));
        Assert.Equal(MediaKind.Movie, list[0].Kind);
        Assert.Equal(MediaKind.Show, list[1].Kind);
        Assert.Equal("tt1160419", list[0].ImdbId);
    }

    [Fact]
    public void TvMaze_CountsAiredEpisodes_AndFindsTheNextOne()
    {
        var info = Sources.ParseTvMaze("""
            {"id":44933,"status":"Running","averageRuntime":50,"image":{"original":"https://i/s.jpg"},
             "_embedded":{"episodes":[{"airstamp":"2025-01-17T02:00:00+00:00"},{"airstamp":"2025-01-24T02:00:00+00:00"},{"airstamp":"2027-01-01T02:00:00+00:00"},
                                      {"airstamp":"2025-02-01T02:00:00+00:00","type":"insignificant_special"}],
                          "nextepisode":{"airstamp":"2027-01-01T02:00:00+00:00"}}}
            """, new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc))!;
        Assert.Equal(2, info.AiredEpisodes);
        Assert.Equal(3, info.NextNumber);
        Assert.Equal(new DateTime(2027, 1, 1, 2, 0, 0, DateTimeKind.Utc), info.NextUtc);
        Assert.Equal(50, info.Runtime);
    }
}
