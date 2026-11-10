namespace CommandHub.Tiles.Greeting;

public static class GreetingText
{
    public static string Salutation(int hour) => hour switch
    {
        >= 5 and < 12 => "Good morning",
        >= 12 and < 17 => "Good afternoon",
        >= 17 and < 22 => "Good evening",
        _ => "Good night",
    };

    /// <summary>"john.smith" → "John". Uses the first part of the Windows user name.</summary>
    public static string FriendlyName(string userName)
    {
        var first = (userName ?? "").Split('.', '_', ' ', '-')[0];
        return first.Length == 0 ? "" : char.ToUpperInvariant(first[0]) + first[1..];
    }

    private static readonly string[] Lines =
    [
        "Small steps still count as steps.",
        "Do the hard thing first; the rest gets lighter.",
        "Make today the kind of day future you thanks you for.",
        "Progress, not perfection.",
        "One focused hour beats a distracted afternoon.",
        "Start before you feel ready.",
        "Rest is part of the work.",
        "Curiosity is a superpower. Use it.",
        "Done is a beautiful word.",
        "You don't need more time, just fewer tabs.",
        "Be kind to yourself today.",
        "A clear desk, a clear head.",
        "Learn one new thing before the day ends.",
        "Momentum loves a quick win.",
        "Drink some water. Seriously.",
        "Your future self is watching. Give them a show.",
        "Slow is smooth, smooth is fast.",
        "Plan the day, then let it surprise you.",
        "Finish what matters, forgive the rest.",
        "Today is a good day to start something.",
        "Keep going; you're closer than it looks.",
    ];

    /// <summary>One line per day, the same all day, changing at midnight.</summary>
    public static string LineFor(DateOnly day) => Lines[day.DayNumber % Lines.Length];
}
