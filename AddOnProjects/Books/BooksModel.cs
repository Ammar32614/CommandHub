using System.Text.Json;

namespace CommandHub.AddOn.Books;

public enum ReadStatus { WantToRead, Reading, Read, Abandoned }

public sealed class BooksData
{
    public List<Book> Books { get; set; } = [];
    /// <summary>Books to finish this year.</summary>
    public int YearGoal { get; set; } = 12;
}

public sealed class Book
{
    /// <summary>"ol:/works/OL123W" for Open Library books, a random id for ones you typed in.</summary>
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public int? Year { get; set; }
    public int? Pages { get; set; }
    public long? CoverId { get; set; }
    public ReadStatus Status { get; set; } = ReadStatus.WantToRead;
    public int CurrentPage { get; set; }
    /// <summary>1–5 stars, 0 = not rated.</summary>
    public int Rating { get; set; }
    public DateTime AddedUtc { get; set; } = DateTime.UtcNow;
    public DateOnly? Started { get; set; }
    public DateOnly? Finished { get; set; }
    public string Notes { get; set; } = "";
    public List<Quote> Quotes { get; set; } = [];
    public List<PageDay> Log { get; set; } = [];

    public string CoverUrl(char size = 'M') => CoverId is { } id ? $"https://covers.openlibrary.org/b/id/{id}-{size}.jpg" : "";
}

public sealed class Quote
{
    public string Text { get; set; } = "";
    public int? Page { get; set; }
    public DateOnly Added { get; set; }
}

public sealed class PageDay
{
    public DateOnly Date { get; set; }
    public int Pages { get; set; }
}

public sealed record FoundBook(string Key, string Title, string Author, int? Year, int? Pages, long? CoverId);

public sealed record YearStats(int Finished, int PagesRead, double? AverageRating, int Goal, int ExpectedByNow);

public static class BookLogic
{
    public static string StatusName(ReadStatus s) => s switch
    {
        ReadStatus.WantToRead => "Want to read",
        ReadStatus.Reading => "Reading",
        ReadStatus.Read => "Read",
        _ => "Stopped",
    };

    public static Book FromFound(FoundBook f, ReadStatus status, DateOnly today)
    {
        var b = new Book { Key = f.Key, Title = f.Title, Author = f.Author, Year = f.Year, Pages = f.Pages, CoverId = f.CoverId };
        SetStatus(b, status, today);
        return b;
    }

    public static void SetStatus(Book b, ReadStatus status, DateOnly today)
    {
        b.Status = status;
        if (status == ReadStatus.Reading) b.Started ??= today;
        if (status == ReadStatus.Read)
        {
            b.Started ??= today;
            b.Finished ??= today;
            if (b.Pages is > 0) b.CurrentPage = b.Pages.Value;
        }
        else b.Finished = null;
    }

    /// <summary>Moves your bookmark, logging the pages read today. The last page finishes the book; any page starts it.</summary>
    public static void SetPage(Book b, int page, DateOnly today)
    {
        var max = b.Pages is > 0 ? b.Pages.Value : int.MaxValue;
        var value = Math.Clamp(page, 0, max);
        var delta = value - b.CurrentPage;
        b.CurrentPage = value;
        if (delta > 0)
        {
            var day = b.Log.FirstOrDefault(d => d.Date == today);
            if (day is null) b.Log.Add(day = new PageDay { Date = today });
            day.Pages += delta;
        }
        if (value > 0 && b.Status == ReadStatus.WantToRead) SetStatus(b, ReadStatus.Reading, today);
        if (b.Pages is > 0 && value >= b.Pages && b.Status != ReadStatus.Read) SetStatus(b, ReadStatus.Read, today);
    }

    public static double Progress(Book b) => b.Pages is > 0 ? Math.Clamp(b.CurrentPage / (double)b.Pages.Value, 0, 1) : 0;

    /// <summary>Pages a day over the last 30 days (days you didn't read count too).</summary>
    public static double PagesPerDay(IEnumerable<Book> books, DateOnly today) =>
        books.SelectMany(b => b.Log).Where(d => d.Date > today.AddDays(-30) && d.Date <= today).Sum(d => d.Pages) / 30.0;

    /// <summary>Days until you finish at your recent pace; null if unknown.</summary>
    public static int? DaysToFinish(Book b, double pagesPerDay) =>
        b.Pages is > 0 && pagesPerDay > 0 ? (int)Math.Ceiling((b.Pages.Value - b.CurrentPage) / pagesPerDay) : null;

    public static YearStats Year(IReadOnlyList<Book> books, int goal, DateOnly today)
    {
        var finished = books.Where(b => b.Status == ReadStatus.Read && b.Finished?.Year == today.Year).ToList();
        int pages = books.SelectMany(b => b.Log).Where(d => d.Date.Year == today.Year).Sum(d => d.Pages);
        var rated = finished.Where(b => b.Rating > 0).ToList();
        int expected = (int)Math.Floor(goal * today.DayOfYear / (double)(DateTime.IsLeapYear(today.Year) ? 366 : 365));
        return new YearStats(finished.Count, pages, rated.Count == 0 ? null : Math.Round(rated.Average(b => b.Rating), 1), goal, expected);
    }

    public static List<FoundBook> ParseOpenLibrary(string json)
    {
        var list = new List<FoundBook>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("docs", out var docs) || docs.ValueKind != JsonValueKind.Array) return list;
            foreach (var d in docs.EnumerateArray())
            {
                if (d.ValueKind != JsonValueKind.Object) continue;
                var key = d.TryGetProperty("key", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString() ?? "" : "";
                var title = d.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "";
                if (key.Length == 0 || title.Length == 0) continue;
                var author = d.TryGetProperty("author_name", out var a) && a.ValueKind == JsonValueKind.Array && a.GetArrayLength() > 0 ? a[0].GetString() ?? "" : "";
                int? Int(string name) => d.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;
                long? cover = d.TryGetProperty("cover_i", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt64(out var ci) ? ci : null;
                list.Add(new FoundBook("ol:" + key, title, author, Int("first_publish_year"), Int("number_of_pages_median"), cover));
            }
        }
        catch (JsonException) { }
        return list;
    }
}
