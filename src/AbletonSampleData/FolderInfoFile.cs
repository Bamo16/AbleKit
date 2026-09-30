namespace AbletonSampleData;

/// <summary>Finds a folder's XMP store, so the reader and the writer agree on which of several it is.</summary>
internal static class FolderInfoFile
{
    internal const string Directory = "Ableton Folder Info";

    /// <summary>The name Live gives every folder's store, on every machine seen so far.</summary>
    internal const string StoreName = "dc66a3fa-0fe1-5352-91cf-3ec237e9ee90.xmp";

    /// <summary>
    /// The store this folder's tags live in — the most recently written, since that is the live one
    /// — or null when it has none.
    /// </summary>
    internal static string? In(string folder) => All(folder).MaxBy(File.GetLastWriteTimeUtc);

    /// <summary>Every store file in this folder, newest first.</summary>
    internal static IReadOnlyList<string> All(string folder)
    {
        var directory = Path.Combine(folder, Directory);

        return System.IO.Directory.Exists(directory)
            ?
            [
                .. System
                    .IO.Directory.EnumerateFiles(directory, "*.xmp")
                    .OrderByDescending(File.GetLastWriteTimeUtc),
            ]
            : [];
    }
}
