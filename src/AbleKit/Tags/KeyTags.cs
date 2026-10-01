namespace AbleKit.Tags;

/// <summary>
/// The values Live ships for its <c>Key|</c> category, spelled Live's way: sharps only, written
/// with ♯ (U+266F). To Live, <c>Key|C#</c> or <c>Key|D♭</c> is a different tag from <c>Key|C♯</c>.
/// </summary>
public static class KeyTags
{
    /// <summary>The twelve tonics, C to B, sharps as ♯.</summary>
    public static readonly IReadOnlySet<string> Tonics = new HashSet<string>(StringComparer.Ordinal)
    {
        "C",
        "C♯",
        "D",
        "D♯",
        "E",
        "F",
        "F♯",
        "G",
        "G♯",
        "A",
        "A♯",
        "B",
    };

    /// <summary>The two modes, Major and Minor.</summary>
    public static readonly IReadOnlySet<string> Modes = new HashSet<string>(StringComparer.Ordinal)
    {
        "Major",
        "Minor",
    };
}
