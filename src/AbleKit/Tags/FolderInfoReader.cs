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
    /// Reads the index for <paramref name="folder"/>. Never throws — returns an empty
    /// index when the folder has no XMP yet, the file is mid-write, or it is malformed.
    /// </summary>
    public FolderInfo Read(string folder)
    {
        var xmpPath = FindXmp(folder);

        if (xmpPath is null)
            return FolderInfo.Empty(folder);

        try
        {
            using var stream = new FileStream(
                xmpPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            );

            var document = XDocument.Load(stream);

            var entries = document
                .Descendants(AblFr + "items")
                .Descendants(Rdf + "li")
                .Select(ParseEntry)
                .OfType<FileTags>()
                .ToArray();

            return new FolderInfo(folder, entries);
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return FolderInfo.Empty(folder);
        }
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
