namespace CommandHub.Planner;

public enum PlanKind { Class, Deadline, Exam, Event }

/// <summary>One thing on the planner. A weekly class repeats from <see cref="Date"/> until <see cref="Until"/> (or forever).</summary>
public class PlanItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public PlanKind Kind { get; set; } = PlanKind.Deadline;
    public DateOnly Date { get; set; }
    public TimeOnly? Start { get; set; }
    public TimeOnly? End { get; set; }
    /// <summary>Course name, matching a Stint subject when possible (for its colour and study time).</summary>
    public string Course { get; set; } = "";
    public string Notes { get; set; } = "";
    /// <summary>Ticked off (deadlines and events).</summary>
    public bool Done { get; set; }
    public bool Weekly { get; set; }
    public DateOnly? Until { get; set; }
}

/// <summary>An item on a specific day (a weekly class has one per week).</summary>
public record PlanOccurrence(PlanItem Item, DateOnly Date);
