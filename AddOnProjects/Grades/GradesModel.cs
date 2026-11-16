namespace CommandHub.AddOn.Grades;

/// <summary>Everything the add-on saves (grades.json in its data folder).</summary>
public sealed class GradesData
{
    public List<Semester> Semesters { get; set; } = [];
    public string CurrentSemesterId { get; set; } = "";
    public List<GradeBand> Scale { get; set; } = GradeLogic.DefaultScale();
}

public sealed class Semester
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public List<Course> Courses { get; set; } = [];
}

public sealed class Course
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public decimal Credits { get; set; } = 3;
    /// <summary>The grade you actually got, once it's out (overrides the projection). Blank while the course runs.</summary>
    public string FinalGrade { get; set; } = "";
    public List<Part> Parts { get; set; } = [];
}

/// <summary>A weighted part of a course: "Quizzes, 10%", "Mid, 25%".</summary>
public sealed class Part
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    /// <summary>Percent of the course this part is worth.</summary>
    public decimal Weight { get; set; }
    /// <summary>Drop this many of the lowest marks (e.g. "best 3 of 4 quizzes" = drop 1).</summary>
    public int DropLowest { get; set; }
    /// <summary>How many there will be in all (e.g. 4 quizzes), so a part only counts for what's been marked. Blank = the marks entered so far.</summary>
    public int? Expected { get; set; }
    public List<Mark> Marks { get; set; } = [];
}

/// <summary>One assessment: "Quiz 3: 8 / 10, class average 6.5".</summary>
public sealed class Mark
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    /// <summary>Your marks; null until it's graded.</summary>
    public decimal? Obtained { get; set; }
    public decimal Total { get; set; } = 10;
    public decimal? ClassAverage { get; set; }
    public decimal? Highest { get; set; }
    public DateOnly? Date { get; set; }
}

/// <summary>A row of the grading scale: at least <see cref="Min"/> percent earns this letter and these grade points.</summary>
public sealed class GradeBand
{
    public string Letter { get; set; } = "";
    public decimal Min { get; set; }
    public decimal Points { get; set; }
}

/// <summary>A course's standing: how much is graded, your percentage and the class's, on what's been marked so far.</summary>
public sealed record Standing(decimal WeightGraded, decimal WeightTotal, decimal? Percent, decimal? ClassPercent, decimal EarnedWeighted)
{
    /// <summary>Percent points of the whole course already in the bag (e.g. 32 of the 40 marked so far).</summary>
    public decimal Earned => EarnedWeighted;
    public decimal Remaining => Math.Max(0, WeightTotal - WeightGraded);
}

public static class GradeLogic
{
    /// <summary>A common 4.0 scale used by many universities in Pakistan. Editable in the add-on.</summary>
    public static List<GradeBand> DefaultScale() =>
    [
        new() { Letter = "A", Min = 85, Points = 4.00m },
        new() { Letter = "A-", Min = 80, Points = 3.67m },
        new() { Letter = "B+", Min = 75, Points = 3.33m },
        new() { Letter = "B", Min = 71, Points = 3.00m },
        new() { Letter = "B-", Min = 68, Points = 2.67m },
        new() { Letter = "C+", Min = 64, Points = 2.33m },
        new() { Letter = "C", Min = 61, Points = 2.00m },
        new() { Letter = "C-", Min = 58, Points = 1.67m },
        new() { Letter = "D+", Min = 54, Points = 1.33m },
        new() { Letter = "D", Min = 50, Points = 1.00m },
        new() { Letter = "F", Min = 0, Points = 0m },
    ];

    public static decimal? Percent(decimal? obtained, decimal total) =>
        obtained is { } o && total > 0 ? Math.Round(o / total * 100, 1) : null;

    /// <summary>The marks that count in a part: graded ones, minus the lowest few (by percentage) if it drops some.</summary>
    public static List<Mark> Counted(Part part)
    {
        var graded = part.Marks.Where(m => m.Obtained is not null && m.Total > 0).ToList();
        // "Best 3 of 4": only drop once more than 3 are in, so an early low mark isn't thrown away too soon.
        int planned = Math.Max(part.Expected ?? 0, part.Marks.Count);
        int keep = Math.Max(1, planned - Math.Max(0, part.DropLowest));
        int drop = Math.Clamp(graded.Count - keep, 0, Math.Max(0, graded.Count - 1));
        return graded.OrderBy(m => m.Obtained!.Value / m.Total).Skip(drop).ToList();
    }

    /// <summary>
    /// How much of a part has been marked (0–1): the counted marks out of how many will count in the end.
    /// With 4 quizzes planned (best 3 count) and 2 marked, that's 2/3.
    /// </summary>
    public static decimal MarkedShare(Part part)
    {
        int counted = Counted(part).Count;
        if (counted == 0) return 0;
        int planned = Math.Max(part.Expected ?? 0, part.Marks.Count);
        int willCount = Math.Max(1, planned - Math.Max(0, part.DropLowest));
        return Math.Min(1, counted / (decimal)willCount);
    }

    /// <summary>Your percentage in a part (marks summed, so a 20-mark quiz counts twice a 10-mark one), or null.</summary>
    public static decimal? PartPercent(Part part)
    {
        var c = Counted(part);
        var total = c.Sum(m => m.Total);
        return total > 0 ? c.Sum(m => m.Obtained!.Value) / total * 100 : null;
    }

    /// <summary>The class's percentage in a part, from the marks that have a class average.</summary>
    public static decimal? PartClassPercent(Part part)
    {
        var c = part.Marks.Where(m => m.ClassAverage is not null && m.Total > 0).ToList();
        var total = c.Sum(m => m.Total);
        return total > 0 ? c.Sum(m => m.ClassAverage!.Value) / total * 100 : null;
    }

    /// <summary>
    /// Where a course stands: parts with any graded marks count at their full weight; the percentage is over the
    /// weight graded so far. The class's figure uses the same parts (where averages were entered).
    /// </summary>
    public static Standing Standing(Course course)
    {
        decimal graded = 0, earned = 0, classGraded = 0, classEarned = 0;
        foreach (var p in course.Parts.Where(p => p.Weight > 0))
        {
            if (PartPercent(p) is { } mine)
            {
                var w = p.Weight * MarkedShare(p);
                graded += w;
                earned += mine * w / 100;
            }
            if (PartClassPercent(p) is { } cls)
            {
                classGraded += p.Weight;
                classEarned += cls * p.Weight / 100;
            }
        }
        decimal total = course.Parts.Sum(p => Math.Max(0, p.Weight));
        return new Standing(graded, total,
            graded > 0 ? Math.Round(earned / graded * 100, 1) : null,
            classGraded > 0 ? Math.Round(classEarned / classGraded * 100, 1) : null,
            Math.Round(earned, 2));
    }

    public static GradeBand? Band(decimal percent, IReadOnlyList<GradeBand> scale) =>
        scale.OrderByDescending(b => b.Min).FirstOrDefault(b => percent >= b.Min);

    public static GradeBand? BandForLetter(string letter, IReadOnlyList<GradeBand> scale) =>
        scale.FirstOrDefault(b => b.Letter.Equals(letter.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// For each grade: the average you'd need on everything not yet graded to finish with it.
    /// Null for a grade already out of reach (needs over 100%); 0 when it's already safe.
    /// </summary>
    public static List<(GradeBand Band, decimal? Needed)> Needed(Course course, IReadOnlyList<GradeBand> scale)
    {
        var s = Standing(course);
        var list = new List<(GradeBand, decimal?)>();
        foreach (var b in scale.Where(b => b.Min > 0).OrderByDescending(b => b.Min))
        {
            // Over a course worth 100%: earned + remaining × x / 100 ≥ min
            var target = b.Min * s.WeightTotal / 100;
            if (s.Remaining <= 0) { list.Add((b, s.Earned >= target ? 0 : null)); continue; }
            var x = (target - s.Earned) / s.Remaining * 100;
            list.Add((b, x <= 0 ? 0 : x > 100 ? null : Math.Ceiling(x)));
        }
        return list;
    }

    /// <summary>The grade a course counts as: the final grade once entered, else the projection from marks so far.</summary>
    public static GradeBand? CourseGrade(Course course, IReadOnlyList<GradeBand> scale) =>
        course.FinalGrade.Trim().Length > 0 ? BandForLetter(course.FinalGrade, scale)
        : Standing(course).Percent is { } p ? Band(p, scale) : null;

    /// <summary>Credit-weighted GPA over courses with a grade (final or projected). Null if none.</summary>
    public static decimal? Gpa(IEnumerable<Course> courses, IReadOnlyList<GradeBand> scale)
    {
        decimal points = 0, credits = 0;
        foreach (var c in courses)
        {
            if (c.Credits <= 0 || CourseGrade(c, scale) is not { } g) continue;
            points += g.Points * c.Credits;
            credits += c.Credits;
        }
        return credits > 0 ? Math.Round(points / credits, 2) : null;
    }

    public static decimal? Cgpa(IEnumerable<Semester> semesters, IReadOnlyList<GradeBand> scale) =>
        Gpa(semesters.SelectMany(s => s.Courses), scale);

    /// <summary>A new course with the usual parts, ready for marks.</summary>
    public static Course NewCourse(string name) => new()
    {
        Name = name,
        Parts =
        [
            new() { Name = "Quizzes", Weight = 10 },
            new() { Name = "Assignments", Weight = 15 },
            new() { Name = "Mid", Weight = 25 },
            new() { Name = "Final", Weight = 50 },
        ],
    };

    /// <summary>"Quiz 4" after Quiz 1–3; "Assignment 2" after Assignment 1. Singular of the part's name.</summary>
    public static string NextMarkName(Part part)
    {
        var name = part.Name.Trim();
        var single = name.EndsWith("zes", StringComparison.OrdinalIgnoreCase) ? name[..^3]
            : name.EndsWith("s", StringComparison.OrdinalIgnoreCase) && name.Length > 3 ? name[..^1] : name;
        if (single.Equals("Quiz", StringComparison.OrdinalIgnoreCase) || name.Equals("Quizzes", StringComparison.OrdinalIgnoreCase)) single = "Quiz";
        return part.Marks.Count == 0 && part.Weight >= 20 && !name.EndsWith("s", StringComparison.OrdinalIgnoreCase) ? single : $"{single} {part.Marks.Count + 1}";
    }
}
