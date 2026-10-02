using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace AbleKit.Tags;

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
    /// Applies keywords to files under <paramref name="folder"/>, leaving every other entry and its
    /// position alone, and returns how many files were given keywords. A keyword Live does not know
    /// yet becomes a new tag; check against <see cref="FileIndexReader.ReadKnownKeywords"/> first to
    /// catch one made by accident.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// A keyword is not well-formed (see <see cref="IsWellFormed"/>). Nothing is written.
    /// </exception>
    /// <exception cref="TagStoreChangedException">
    /// Live was writing the store, or changed it after it was read. Nothing is written; try again.
    /// </exception>
    /// <exception cref="IOException">The store is locked by another program. Nothing is written.</exception>
    /// <exception cref="UnauthorizedAccessException">Writing there is not permitted. Nothing is written.</exception>
    public int Apply(string folder, IReadOnlyList<TagAssignment> assignments)
    {
        if (assignments.Count is 0)
            return 0;

        if (
            assignments.SelectMany(a => a.Keywords).FirstOrDefault(k => !IsWellFormed(k)) is { } bad
        )
            throw new ArgumentException(
                $"'{bad}' is not a Category|Value keyword",
                nameof(assignments)
            );

        var directory = Path.Combine(folder, FolderInfoFile.Directory);
        var path = ExistingXmp(folder);

        Directory.CreateDirectory(directory);

        var stamp = path is not null ? File.GetLastWriteTimeUtc(path) : (DateTime?)null;
        var document = path is not null ? FolderInfoFile.Load(path) : NewDocument();
        var items = Items(document);

        foreach (var assignment in assignments)
            ApplyOne(items, assignment);

        Stamp(document);
        Replace(path ?? Path.Combine(directory, FolderInfoFile.StoreName), document, stamp);

        return assignments.Count;
    }

    /// <summary>
    /// Points existing entries at new file names, keeping their keywords and their position, and
    /// returns how many were renamed. An entry not in the store, or a folder with no store, is passed over.
    /// </summary>
    /// <exception cref="TagStoreChangedException">
    /// Live was writing the store, or changed it after it was read. Nothing is written; try again.
    /// </exception>
    /// <exception cref="IOException">The store is locked by another program. Nothing is written.</exception>
    /// <exception cref="UnauthorizedAccessException">Writing there is not permitted. Nothing is written.</exception>
    public int Rename(string folder, IReadOnlyList<TagRename> renames)
    {
        if (renames.Count is 0 || ExistingXmp(folder) is not { } path)
            return 0;

        var stamp = File.GetLastWriteTimeUtc(path);
        var document = FolderInfoFile.Load(path);
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
            return 0;

        Stamp(document);
        Replace(path, document, stamp);

        return moved;
    }

    /// <summary>
    /// Whether a keyword has Live's shape: <c>Category|Value</c>, or deeper for Live's nested tags
    /// such as <c>Drums|Cymbal|Crash</c>, with no part empty or padded with spaces. Says nothing
    /// about whether Live knows the keyword.
    /// </summary>
    public static bool IsWellFormed(string keyword) =>
        keyword.Split('|') is [_, _, ..] parts
        && parts.All(part => part.Length > 0 && part.Trim() == part);

    /// <summary>
    /// Writes to a temporary file beside the target and swaps it in, but only if nothing has
    /// touched the target since it was read. Narrows the race with Ableton; does not close it.
    /// </summary>
    /// <exception cref="TagStoreChangedException">The target changed after it was read.</exception>
    internal static void Replace(string path, XDocument document, DateTime? stamp)
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

            throw new TagStoreChangedException("The tag store changed while it was being updated.");
        }

        try
        {
            if (File.Exists(path))
                File.Replace(temporary, path, destinationBackupFileName: null);
            else
                File.Move(temporary, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            File.Delete(temporary);

            throw;
        }
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
    /// <summary>The file's path relative to the folder.</summary>
    public required string RelativePath { get; init; }

    /// <summary>Every keyword the file should carry, as <c>Category|Value</c>, replacing what it has.</summary>
    public required IReadOnlyList<string> Keywords { get; init; }
}

/// <summary>One entry's move to a new name, both relative to the folder.</summary>
public sealed record TagRename
{
    /// <summary>The name the entry has now.</summary>
    public required string From { get; init; }

    /// <summary>The name it moves to.</summary>
    public required string To { get; init; }
}
