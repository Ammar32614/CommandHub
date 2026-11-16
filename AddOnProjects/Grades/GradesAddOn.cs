using System.IO;
using System.Windows;
using CommandHub.AddOns;

namespace CommandHub.AddOn.Grades;

/// <summary>The Grades add-on: marks you enter, class averages beside them, grades and GPA.</summary>
public sealed class GradesAddOn : IAddOn
{
    private Book? _book;

    public void Start(IAddOnHost host)
    {
        _book = new Book(host);
        _book.Load();
    }

    public UIElement CreatePage() => new GradesPage(_book!);
}

/// <summary>Owns the saved data.</summary>
public sealed class Book(IAddOnHost host)
{
    public GradesData Data { get; private set; } = new();
    public event Action? Changed;

    public void Load()
    {
        Data = host.Load<GradesData>("grades");
        Data.Semesters ??= [];
        if (Data.Scale is null || Data.Scale.Count == 0) Data.Scale = GradeLogic.DefaultScale();
        foreach (var s in Data.Semesters)
        {
            s.Courses ??= [];
            foreach (var c in s.Courses)
            {
                c.Parts ??= [];
                c.FinalGrade ??= "";
                foreach (var p in c.Parts) p.Marks ??= [];
            }
        }
        if (Data.Semesters.Count == 0)
        {
            var now = DateTime.Today;
            var first = new Semester { Name = (now.Month >= 8 ? "Fall " : now.Month <= 5 ? "Spring " : "Summer ") + now.Year };
            Data.Semesters.Add(first);
            Data.CurrentSemesterId = first.Id;
        }
        if (Current is null) Data.CurrentSemesterId = Data.Semesters[^1].Id;
    }

    public Semester? Current => Data.Semesters.FirstOrDefault(s => s.Id == Data.CurrentSemesterId);

    public void Save(bool notify = true)
    {
        try { host.Save("grades", Data); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { host.Notify("Grades: couldn't save. " + ex.Message); }
        if (notify) Changed?.Invoke();
    }
}
