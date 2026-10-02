using System.Xml.Linq;
using AbleKit.Tags;

namespace AbleKit.Tests.Tags;

/// <summary>The writer against the tag store Ableton actually wrote, only ever on a copy.</summary>
public sealed class FolderInfoWriterRealDataTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "AbleKit.Tests",
        Guid.NewGuid().ToString("n")
    );

    public FolderInfoWriterRealDataTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    private string? CopyRealStore()
    {
        var source = RealLibrary
            .SampleFolders()
            .Select(folder => Path.Combine(folder, "Ableton Folder Info"))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.xmp"))
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();

        if (source is null)
            return null;

        var directory = Path.Combine(_root, "Ableton Folder Info");
        Directory.CreateDirectory(directory);

        var destination = Path.Combine(directory, Path.GetFileName(source));
        File.Copy(source, destination);

        return destination;
    }

    /// <summary>
    /// Not byte-for-byte: <c>XmlWriter</c> cannot match how Ableton wraps namespace declarations,
    /// and Live reads the result.
    /// </summary>
    [Fact]
    public void Writing_Abletons_own_file_back_preserves_it()
    {
        var path = CopyRealStore();
        Assert.SkipWhen(path is null, "no Ableton tag store on this machine");

        var reader = new FolderInfoReader();
        var before = reader.Read(_root).Entries;

        FolderInfoWriter.Replace(
            path,
            XDocument.Parse(File.ReadAllText(path)),
            File.GetLastWriteTimeUtc(path)
        );

        var after = reader.Read(_root).Entries;

        Assert.Equal(
            before.Select(e => (e.RelativePath, string.Join(",", e.Keywords))),
            after.Select(e => (e.RelativePath, string.Join(",", e.Keywords)))
        );

        var bytes = File.ReadAllBytes(path);

        Assert.False(bytes[0] is 0xEF && bytes[1] is 0xBB && bytes[2] is 0xBF, "wrote a BOM");
        Assert.StartsWith("<x:xmpmeta", System.Text.Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain((byte)'\r', bytes);
        Assert.Equal((byte)'\n', bytes[^1]);
    }
}
