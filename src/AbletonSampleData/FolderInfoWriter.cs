using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace AbletonSampleData;

/// <summary>
/// Writes keywords into a folder's <c>Ableton Folder Info\&lt;uuid&gt;.xmp</c>, alongside whatever
/// Live has put there.
/// </summary>
public sealed class FolderInfoWriter
{
    private static readonly XNamespace X = "adobe:ns:meta/";
    private static readonly XNamespace Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private static readonly XNamespace AblFr = "https://ns.ableton.com/xmp/fs-resources/1.0/";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Xmp = "http://ns.adobe.com/xap/1.0/";

    /// <summary>
    /// Live's built-in <c>Key|</c> tonics: sharps only, spelled with U+266F. An ASCII '#' silently
    /// makes a separate user tag that has to be removed by hand.
    /// </summary>
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

    /// <summary>Live's built-in <c>Key|</c> modes.</summary>
    public static readonly IReadOnlySet<string> Modes = new HashSet<string>(StringComparer.Ordinal)
    {
        "Major",
        "Minor",
    };

    /// <summary>
    /// The exact bytes Ableton writes: no XML declaration, no BOM, LF line endings, three-space
    /// indents.
    /// </summary>
    private static readonly XmlWriterSettings Format = new()
    {
        OmitXmlDeclaration = true,
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        NewLineChars = "\n",
        Indent = true,
        IndentChars = "   ",
    };

    /// <summary>
    /// Applies keywords to files under <paramref name="folder"/>, leaving every other entry
    /// and its position alone, or nothing at all unless every keyword is well-formed.
    /// </summary>
    public TagWriteOutcome Apply(string folder, IReadOnlyList<TagAssignment> assignments)
    {
        if (assignments.Count is 0)
            return TagWriteOutcome.Written(0);

        if (
            assignments.SelectMany(a => a.Keywords).FirstOrDefault(k => !IsWellFormed(k)) is { } bad
        )
            return TagWriteOutcome.Rejected($"'{bad}' is not a Category|Value keyword");

        var directory = Path.Combine(folder, FolderInfoFile.Directory);
        var path = ExistingXmp(folder);

        try
        {
            Directory.CreateDirectory(directory);

            var stamp = path is not null ? File.GetLastWriteTimeUtc(path) : (DateTime?)null;
            var document = path is not null ? Load(path) : NewDocument();

            if (document is null)
                return TagWriteOutcome.Stale("the tag store was mid-write and could not be read");

            var items = Items(document);

            foreach (var assignment in assignments)
                ApplyOne(items, assignment);

            Stamp(document);

            return Replace(
                path ?? Path.Combine(directory, FolderInfoFile.StoreName),
                document,
                stamp
            )
                ? TagWriteOutcome.Written(assignments.Count)
                : TagWriteOutcome.Stale("the tag store changed while it was being updated");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return TagWriteOutcome.Stale(ex.Message);
        }
    }

    /// <summary>
    /// Points existing entries at new file names, keeping their keywords and their position.
    /// </summary>
    public TagWriteOutcome Rename(string folder, IReadOnlyList<TagRename> renames)
    {
        if (renames.Count is 0)
            return TagWriteOutcome.Written(0);

        var path = ExistingXmp(folder);

        // No store yet is not a failure — nothing carries the old names to rename.
        if (path is null)
            return TagWriteOutcome.Written(0);

        try
        {
            var stamp = File.GetLastWriteTimeUtc(path);
            var document = Load(path);

            if (document is null)
                return TagWriteOutcome.Stale("the tag store was mid-write and could not be read");

            var items = Items(document);

            var moved = 0;

            foreach (var rename in renames)
            {
                if (Find(items, rename.From)?.Element(AblFr + "filePath") is not { } filePath)
                    continue;

                filePath.Value = rename.To;
                moved++;
            }

            if (moved is 0)
                return TagWriteOutcome.Written(0);

            Stamp(document);

            return Replace(path, document, stamp)
                ? TagWriteOutcome.Written(moved)
                : TagWriteOutcome.Stale("the tag store changed while it was being updated");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return TagWriteOutcome.Stale(ex.Message);
        }
    }

    /// <summary>
    /// Whether a keyword has Live's shape, <c>Category|Value</c>; a <c>Key|</c> value must also be
    /// one of Live's own, since any other spelling makes a second tag.
    /// </summary>
    public static bool IsWellFormed(string keyword) =>
        keyword.Split('|') switch
        {
            ["Key", var value] => Tonics.Contains(value) || Modes.Contains(value),
            [{ Length: > 0 } category, { Length: > 0 } value] => category.Trim() == category
                && value.Trim() == value,
            _ => false,
        };

    /// <summary>
    /// Reads the file, or null if Ableton is mid-write — which calls for a retry, never a rebuild.
    /// </summary>
    private static XDocument? Load(string path)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            );

            return XDocument.Load(stream);
        }
        catch (Exception ex) when (ex is IOException or System.Xml.XmlException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes to a temporary file beside the target and swaps it in, but only if nothing has
    /// touched the target since it was read. Narrows the race with Ableton; does not close it.
    /// </summary>
    internal static bool Replace(string path, XDocument document, DateTime? stamp)
    {
        // Beside the target: a replace across volumes is not atomic.
        var temporary = $"{path}.tmp";

        using (var buffer = new MemoryStream())
        {
            using (var writer = XmlWriter.Create(buffer, Format))
                document.Save(writer);

            // Match Ableton's trailing newline.
            buffer.Write("\n"u8);

            File.WriteAllBytes(temporary, buffer.ToArray());
        }

        if (stamp is { } read && File.GetLastWriteTimeUtc(path) != read)
        {
            File.Delete(temporary);

            return false;
        }

        if (File.Exists(path))
            File.Replace(temporary, path, destinationBackupFileName: null);
        else
            File.Move(temporary, path);

        return true;
    }

    private static void ApplyOne(XElement items, TagAssignment assignment)
    {
        var bag = new XElement(
            Rdf + "Bag",
            assignment.Keywords.Select(k => new XElement(Rdf + "li", k))
        );

        if (Find(items, assignment.RelativePath) is { } existing)
        {
            existing.Element(AblFr + "keywords")?.Remove();
            existing.Add(new XElement(AblFr + "keywords", bag));

            return;
        }

        items.Add(
            new XElement(
                Rdf + "li",
                new XAttribute(Rdf + "parseType", "Resource"),
                new XElement(AblFr + "filePath", assignment.RelativePath),
                new XElement(AblFr + "keywords", bag)
            )
        );
    }

    private static XElement? Find(XElement items, string relativePath) =>
        items
            .Elements(Rdf + "li")
            .FirstOrDefault(li =>
                string.Equals(
                    Normalize(li.Element(AblFr + "filePath")?.Value),
                    Normalize(relativePath),
                    StringComparison.OrdinalIgnoreCase
                )
            );

    private static string Normalize(string? relativePath) =>
        (relativePath ?? string.Empty).Replace('\\', '/').Trim('/');

    private static XElement Items(XDocument document) =>
        document.Descendants(AblFr + "items").Descendants(Rdf + "Bag").First();

    /// <summary>Ableton stamps this on every write; a file it wrote never lacks one.</summary>
    private static void Stamp(XDocument document)
    {
        var now = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
        var description = document.Descendants(Rdf + "Description").First();

        if (description.Element(Xmp + "MetadataDate") is { } existing)
            existing.Value = now;
        else
            description.Add(new XElement(Xmp + "MetadataDate", now));
    }

    /// <summary>Resolved through the reader's own rule, so both find the same file.</summary>
    private static string? ExistingXmp(string folder) => FolderInfoFile.In(folder);

    private static XDocument NewDocument() =>
        new(
            new XElement(
                X + "xmpmeta",
                new XAttribute(XNamespace.Xmlns + "x", X.NamespaceName),
                new XAttribute(X + "xmptk", "XMP Core 6.0.0"),
                new XElement(
                    Rdf + "RDF",
                    new XAttribute(XNamespace.Xmlns + "rdf", Rdf.NamespaceName),
                    new XElement(
                        Rdf + "Description",
                        new XAttribute(Rdf + "about", string.Empty),
                        new XAttribute(XNamespace.Xmlns + "dc", Dc.NamespaceName),
                        new XAttribute(XNamespace.Xmlns + "ablFR", AblFr.NamespaceName),
                        new XAttribute(XNamespace.Xmlns + "xmp", Xmp.NamespaceName),
                        new XElement(Dc + "format", "application/vnd.ableton.folder"),
                        new XElement(AblFr + "resource", "folder"),
                        new XElement(AblFr + "platform", "win"),
                        new XElement(AblFr + "items", new XElement(Rdf + "Bag"))
                    )
                )
            )
        );
}

/// <summary>Keywords to put on one file, named the way Ableton names it.</summary>
public sealed record TagAssignment
{
    public required string RelativePath { get; init; }

    public required IReadOnlyList<string> Keywords { get; init; }
}

/// <summary>One entry's move to a new name, both relative to the folder.</summary>
public sealed record TagRename
{
    public required string From { get; init; }
    public required string To { get; init; }
}

/// <summary>How one write turned out.</summary>
public sealed record TagWriteOutcome
{
    public required bool IsSuccess { get; init; }

    /// <summary>How many files were given keywords.</summary>
    public int Applied { get; init; }

    /// <summary>
    /// True when nothing was wrong with the request — the store simply moved underneath it.
    /// Unlike a rejection, this is fixed by trying again.
    /// </summary>
    public bool IsStale { get; init; }

    public string? Error { get; init; }

    public static TagWriteOutcome Written(int applied) =>
        new() { IsSuccess = true, Applied = applied };

    public static TagWriteOutcome Stale(string error) =>
        new()
        {
            IsSuccess = false,
            IsStale = true,
            Error = error,
        };

    public static TagWriteOutcome Rejected(string error) =>
        new() { IsSuccess = false, Error = error };
}
