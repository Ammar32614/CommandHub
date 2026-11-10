namespace CommandHub.Services;

/// <summary>A named colour theme. Keys in <see cref="Colours"/> match the Color resources in Theme.xaml.</summary>
public sealed record ThemeDef(string Name, string Blurb, bool IsLight, IReadOnlyDictionary<string, string> Colours)
{
    public string this[string key] => Colours[key];
}

public static class ThemeCatalog
{
    public static readonly string[] Keys =
    [
        "BgColor", "SurfaceColor", "RaisedColor", "TextColor", "MutedColor", "HairlineColor", "StrongColor",
        "ScrimColor", "OnAccentColor", "AccentColor", "Accent2Color", "Accent3Color", "Accent4Color",
    ];

    public static IReadOnlyList<ThemeDef> All { get; } =
    [
        Make("Midnight", "Dark ink with a warm lamp glow", false,
            bg: "#14161C", surface: "#1C1F27", raised: "#252A35", text: "#ECEEF3", muted: "#8A90A2",
            hairline: "#2C313C", strong: "#3A4150", onAccent: "#14161C",
            focus: "#F5B14C", brk: "#4FD1C5", timer: "#FF7A6B", stopwatch: "#9B8CFF"),
        Make("Daylight", "Clean and bright for daytime", true,
            bg: "#F5F4F1", surface: "#FFFFFF", raised: "#EAE8E3", text: "#1D2027", muted: "#6A6F7E",
            hairline: "#DCD9D2", strong: "#C3BFB6", onAccent: "#1D2027",
            focus: "#E8961E", brk: "#14A898", timer: "#EC5E4A", stopwatch: "#7D6EEB"),
        Make("Paper", "Soft sepia, easy on the eyes", true,
            bg: "#F2EBDC", surface: "#FBF7EE", raised: "#E7DECB", text: "#3A3127", muted: "#84786A",
            hairline: "#DCD1BB", strong: "#C4B79D", onAccent: "#2A231B",
            focus: "#CF8526", brk: "#4C9C82", timer: "#C9594A", stopwatch: "#8A78C4"),
        Make("Forest", "Deep green with gold and mint", false,
            bg: "#111915", surface: "#17211C", raised: "#202D26", text: "#E5EDE8", muted: "#85988D",
            hairline: "#26352D", strong: "#36493F", onAccent: "#111915",
            focus: "#E9C46A", brk: "#74D3A8", timer: "#F2937A", stopwatch: "#A9B9F5"),
        Make("Ocean", "Navy night with peach and cyan", false,
            bg: "#0E1623", surface: "#142033", raised: "#1B2A41", text: "#E5ECF6", muted: "#8393AC",
            hairline: "#22324B", strong: "#31445F", onAccent: "#0E1623",
            focus: "#FFB870", brk: "#62D5E8", timer: "#FF8394", stopwatch: "#A3ABFF"),
        Make("Dusk", "Plum evening with lavender and coral", false,
            bg: "#19141F", surface: "#211A29", raised: "#2B2235", text: "#F1EAF5", muted: "#9B8EA7",
            hairline: "#33293E", strong: "#463A54", onAccent: "#19141F",
            focus: "#F6B26B", brk: "#82D9C0", timer: "#F48A68", stopwatch: "#C6AAFF"),
    ];

    public static ThemeDef Get(string? name) =>
        All.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) ?? All[0];

    private static ThemeDef Make(string name, string blurb, bool light,
        string bg, string surface, string raised, string text, string muted, string hairline, string strong,
        string onAccent, string focus, string brk, string timer, string stopwatch) =>
        new(name, blurb, light, new Dictionary<string, string>
        {
            ["BgColor"] = bg, ["SurfaceColor"] = surface, ["RaisedColor"] = raised, ["TextColor"] = text,
            ["MutedColor"] = muted, ["HairlineColor"] = hairline, ["StrongColor"] = strong,
            ["ScrimColor"] = "#B3" + bg[1..], // the page background at 70%, behind sheets and dialogs
            ["OnAccentColor"] = onAccent,
            ["AccentColor"] = focus, ["Accent2Color"] = brk, ["Accent3Color"] = timer, ["Accent4Color"] = stopwatch,
        });
}
