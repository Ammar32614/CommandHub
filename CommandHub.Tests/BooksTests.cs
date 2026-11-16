using CommandHub.AddOn.Books;

namespace CommandHub.Tests;

public class BooksTests
{
    static readonly DateOnly Today = new(2026, 11, 16);

    [Fact]
    public void Page_StartsReading_LogsPages_FinishesAtTheEnd()
    {
        var b = new Book { Title = "Project Hail Mary", Pages = 496 };
        BookLogic.SetPage(b, 40, Today);
        Assert.Equal(ReadStatus.Reading, b.Status);
        Assert.Equal(Today, b.Started);
        BookLogic.SetPage(b, 30, Today); // a correction takes those pages back
        Assert.Equal(30, b.Log.Single().Pages);
        BookLogic.SetPage(b, 999, Today.AddDays(1));
        Assert.Equal(496, b.CurrentPage);
        Assert.Equal(ReadStatus.Read, b.Status);
        Assert.Equal(Today.AddDays(1), b.Finished);
        Assert.Equal(466, b.Log[1].Pages);
        // Marking a part-read book Read counts the pages you jumped; one added as read long ago doesn't.
        var c = new Book { Pages = 100 };
        BookLogic.SetPage(c, 20, Today);
        BookLogic.SetStatus(c, ReadStatus.Read, Today);
        Assert.Equal(100, c.Log.Single().Pages);
        var old = new Book { Pages = 300 };
        BookLogic.SetStatus(old, ReadStatus.Read, Today);
        Assert.Empty(old.Log);
        Assert.Equal(300, old.CurrentPage);
    }

    [Fact]
    public void Year_GoalPace_AndPagesPerDay()
    {
        var read = new Book { Status = ReadStatus.Read, Finished = Today.AddDays(-3), Rating = 4 };
        var older = new Book { Status = ReadStatus.Read, Finished = new DateOnly(2025, 12, 1), Rating = 2 };
        var current = new Book { Status = ReadStatus.Reading, Pages = 300, CurrentPage = 60 };
        current.Log.Add(new PageDay { Date = Today, Pages = 60 });
        var s = BookLogic.Year([read, older, current], 12, Today);
        Assert.Equal(1, s.Finished);
        Assert.Equal(60, s.PagesRead);
        Assert.Equal(4, s.AverageRating);
        Assert.Equal(10, s.ExpectedByNow); // 12 × 320/365 = 10.5
        Assert.Equal(2.0, BookLogic.PagesPerDay([current], Today));
        Assert.Equal(120, BookLogic.DaysToFinish(current, 2.0));
    }

    [Fact]
    public void OpenLibrary_ParsesSearchResults()
    {
        var list = BookLogic.ParseOpenLibrary("""
            {"docs":[{"key":"/works/OL1W","title":"Project Hail Mary","author_name":["Andy Weir"],"first_publish_year":2021,"number_of_pages_median":496,"cover_i":11200092},
                     {"key":"/works/OL2W"},{"title":"no key"}]}
            """);
        var f = Assert.Single(list);
        Assert.Equal("ol:/works/OL1W", f.Key);
        Assert.Equal("Andy Weir", f.Author);
        Assert.Equal("https://covers.openlibrary.org/b/id/11200092-M.jpg", BookLogic.FromFound(f, ReadStatus.WantToRead, Today).CoverUrl());
        Assert.Empty(BookLogic.ParseOpenLibrary("nope"));
    }
}
