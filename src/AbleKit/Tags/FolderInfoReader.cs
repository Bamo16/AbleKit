using System.Xml.Linq;

namespace AbleKit.Tags;

/// <summary>
/// Reads a folder's <c>Ableton Folder Info\&lt;uuid&gt;.xmp</c>, which holds the keywords of the files
/// directly in it; each subfolder has its own.
/// </summary>
public sealed class FolderInfoReader
{
    private static readonly XNamespace Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private static readonly XNamespace AblFr = "https://ns.ableton.com/xmp/fs-resources/1.0/";

    /// <summary>
    /// Reads the store for <paramref name="folder"/>. A folder with no store has no tags, and reads as
    /// empty.
    /// </summary>
    /// <exception cref="TagStoreChangedException">
    /// Live was writing the store: it is half-written, or went away while being read. Try again.
    /// </exception>
    /// <exception cref="IOException">The store is locked by another program.</exception>
    /// <exception cref="UnauthorizedAccessException">Reading the store is not permitted.</exception>
    public FolderInfo Read(string folder)
    {
        if (FindXmp(folder) is not { } xmpPath)
            return FolderInfo.Empty(folder);

        var entries = FolderInfoFile
            .Load(xmpPath)
            .Descendants(AblFr + "items")
            .Descendants(Rdf + "li")
            .Select(ParseEntry)
            .OfType<FileTags>()
            .ToArray();

        return new FolderInfo(folder, entries);
    }

    private static string? FindXmp(string folder) => FolderInfoFile.In(folder);

    private static FileTags? ParseEntry(XElement item)
    {
        if (item.Element(AblFr + "filePath")?.Value is not { Length: > 0 } filePath)
            return null;

        return new FileTags
        {
            RelativePath = filePath,
            Keywords = Values(item, "keywords"),
            HiddenKeywords = Values(item, "hideKeywords"),
        };
    }

    private static string[] Values(XElement item, string element) =>
        item.Element(AblFr + element)
            ?.Descendants(Rdf + "li")
            .Select(li => li.Value)
            .Where(value => value.Length > 0)
            .ToArray()
        ?? [];
}
