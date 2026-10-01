using System.Diagnostics.CodeAnalysis;

namespace AbletonSampleData;

/// <summary>The keywords Live holds for the files in one folder, from its XMP store or Live's index.</summary>
/// <param name="folder">The folder the entries' paths are relative to.</param>
/// <param name="entries">Every entry, in document order.</param>
public sealed class FolderInfo(string folder, IReadOnlyList<FileTags> entries)
{
    private readonly Dictionary<string, FileTags> _byRelativePath = entries
        .GroupBy(e => Normalize(e.RelativePath), StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

    /// <summary>The folder the entries' paths are relative to.</summary>
    public string Folder { get; } = folder;

    /// <summary>Every entry, in document order.</summary>
    public IReadOnlyList<FileTags> Entries { get; } = entries;

    /// <summary>What an unreadable or absent index yields.</summary>
    public static FolderInfo Empty(string folder) => new(folder, []);

    /// <summary>
    /// Looks up tags by path relative to <see cref="Folder"/>, or by absolute path under
    /// it. Returns false when the file carries no keywords.
    /// </summary>
    public bool TryGet(string path, [NotNullWhen(true)] out FileTags? tags)
    {
        var relative = Path.IsPathRooted(path) ? Path.GetRelativePath(Folder, path) : path;

        return _byRelativePath.TryGetValue(Normalize(relative), out tags);
    }

    private static string Normalize(string relativePath) =>
        relativePath.Replace('\\', '/').Trim('/');
}

/// <summary>The keywords Live holds for a single file.</summary>
public sealed record FileTags
{
    /// <summary>Path relative to the folder, as Live spells it.</summary>
    public required string RelativePath { get; init; }

    /// <summary>Every keyword on the file, raw, as <c>Category|Value</c>.</summary>
    public required IReadOnlyList<string> Keywords { get; init; }

    /// <summary>
    /// Keywords the user took off this file, never to be written back. Only the XMP carries them;
    /// read from the index, always empty.
    /// </summary>
    public IReadOnlyList<string> HiddenKeywords { get; init; } = [];
}
