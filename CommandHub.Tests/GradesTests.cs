using CommandHub.AddOn.Grades;

namespace CommandHub.Tests;

public class GradesTests
{
    static readonly List<GradeBand> Scale = GradeLogic.DefaultScale();

    static Course Sample()
    {
        var c = GradeLogic.NewCourse("Physics");
        var quizzes = c.Parts[0];
        quizzes.Marks.Add(new Mark { Name = "Quiz 1", Obtained = 8, Total = 10, ClassAverage = 6 });
        quizzes.Marks.Add(new Mark { Name = "Quiz 2", Obtained = 6, Total = 10, ClassAverage = 7 });
        var mid = c.Parts[2];
        mid.Marks.Add(new Mark { Name = "Mid", Obtained = 32, Total = 40, ClassAverage = 24 });
        return c;
    }

    [Fact]
    public void Standing_IsOverWhatsBeenGraded()
    {
        var s = GradeLogic.Standing(Sample());
        // Quizzes 70% of 10, Mid 80% of 25 → 7 + 20 = 27 of 35 = 77.1%
        Assert.Equal(35, s.WeightGraded);
        Assert.Equal(100, s.WeightTotal);
        Assert.Equal(77.1m, s.Percent);
        Assert.Equal(27m, s.Earned);
        // Class: quizzes 65% of 10, mid 60% of 25 → 6.5 + 15 = 21.5 of 35 = 61.4%
        Assert.Equal(61.4m, s.ClassPercent);
        Assert.Equal("B+", GradeLogic.Band(s.Percent!.Value, Scale)!.Letter);
    }

    [Fact]
    public void DropLowest_IgnoresTheWorstMark()
    {
        var c = Sample();
        c.Parts[0].DropLowest = 1;
        Assert.Equal(80m, GradeLogic.PartPercent(c.Parts[0]));
        c.Parts[0].DropLowest = 5; // can't drop everything
        Assert.Equal(80m, GradeLogic.PartPercent(c.Parts[0]));
    }

    [Fact]
    public void Needed_OnTheRestForEachGrade()
    {
        var needed = GradeLogic.Needed(Sample(), Scale).ToDictionary(n => n.Band.Letter, n => n.Needed);
        // 27 earned, 65 left. A (85): (85 - 27) / 65 = 89.2% → 90.
        Assert.Equal(90, needed["A"]);
        // D (50): (50 - 27) / 65 = 35.4% → 36.
        Assert.Equal(36, needed["D"]);

        var hopeless = GradeLogic.NewCourse("x");
        hopeless.Parts[3].Marks.Add(new Mark { Obtained = 0, Total = 100 }); // 0 on the 50% final
        Assert.Null(GradeLogic.Needed(hopeless, Scale).First(n => n.Band.Letter == "A").Needed);
    }

    [Fact]
    public void Gpa_IsCreditWeighted_AndFinalGradesWin()
    {
        var a = Sample(); // projected B+ (3.33)
        a.Credits = 3;
        var b = GradeLogic.NewCourse("Lab");
        b.Credits = 1;
        b.FinalGrade = "a";
        var empty = GradeLogic.NewCourse("Not started"); // no marks: not counted
        Assert.Equal(Math.Round((3.33m * 3 + 4m * 1) / 4, 2), GradeLogic.Gpa([a, b, empty], Scale));
        Assert.Null(GradeLogic.Gpa([empty], Scale));
        var s1 = new Semester { Courses = [a] };
        var s2 = new Semester { Courses = [b] };
        Assert.Equal(GradeLogic.Gpa([a, b], Scale), GradeLogic.Cgpa([s1, s2], Scale));
    }

    [Fact]
    public void NextMarkName_FollowsThePart()
    {
        var c = Sample();
        Assert.Equal("Quiz 3", GradeLogic.NextMarkName(c.Parts[0]));
        Assert.Equal("Assignment 1", GradeLogic.NextMarkName(c.Parts[1]));
        Assert.Equal("Final", GradeLogic.NextMarkName(c.Parts[3]));
    }
}
