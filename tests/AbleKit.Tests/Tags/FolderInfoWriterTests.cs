using System.Text;
using System.Xml.Linq;
using AbleKit.Tags;

namespace AbleKit.Tests.Tags;

public sealed class FolderInfoWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "AbleKit.Tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly FolderInfoWriter _writer = new();
    private readonly FolderInfoReader _reader = new();

    public FolderInfoWriterTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    private string XmpPath =>
        Directory.EnumerateFiles(Path.Combine(_root, "Ableton Folder Info"), "*.xmp").Single();

    private static TagAssignment Assign(string name, params string[] keywords) =>
        new() { RelativePath = name, Keywords = keywords };

    [Fact]
    public void A_store_is_created_where_there_is_none()
    {
        var outcome = _writer.Apply(_root, [Assign("a.flac", "Key|A", "Key|Minor")]);

        Assert.IsType<TagWriteOutcome.Written>(outcome);
        Assert.Equal("dc66a3fa-0fe1-5352-91cf-3ec237e9ee90.xmp", Path.GetFileName(XmpPath));
        Assert.True(_reader.Read(_root).TryGet("a.flac", out var tags));
        Assert.Equal(["Key|A", "Key|Minor"], tags.Keywords);
    }

    [Fact]
    public void The_file_has_no_declaration_no_bom_and_lf_endings()
    {
        // Measured from Ableton's own file.
        _writer.Apply(_root, [Assign("a.flac", "Key|A", "Key|Minor")]);

        var bytes = File.ReadAllBytes(XmpPath);

        Assert.False(bytes[0] is 0xEF && bytes[1] is 0xBB && bytes[2] is 0xBF, "wrote a BOM");
        Assert.StartsWith("<x:xmpmeta", Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain((byte)'\r', bytes);
    }

    [Fact]
    public void Entries_the_write_did_not_name_survive_with_their_order()
    {
        _writer.Apply(
            _root,
            [
                Assign("first.flac", "Key|C", "Key|Major"),
                Assign("second.flac", "Key|D", "Key|Minor"),
                Assign("third.flac", "Key|E", "Key|Major"),
            ]
        );

        _writer.Apply(_root, [Assign("second.flac", "Key|F", "Key|Major", "Type|Vocal")]);

        var info = _reader.Read(_root);

        Assert.Equal(
            ["first.flac", "second.flac", "third.flac"],
            info.Entries.Select(e => e.RelativePath)
        );

        Assert.True(info.TryGet("second.flac", out var second));
        Assert.Equal(["Key|F", "Key|Major", "Type|Vocal"], second.Keywords);
    }

    [Fact]
    public void A_keyword_Live_does_not_know_yet_is_written_as_given()
    {
        // Live spells this key C♯; a user may still want a tag of their own.
        var outcome = _writer.Apply(_root, [Assign("a.flac", "Key|C#", "Mood|Wistful")]);

        Assert.IsType<TagWriteOutcome.Written>(outcome);
        Assert.True(_reader.Read(_root).TryGet("a.flac", out var tags));
        Assert.Equal(["Key|C#", "Mood|Wistful"], tags.Keywords);
    }

    [Fact]
    public void One_malformed_keyword_stops_the_whole_write()
    {
        var outcome = _writer.Apply(
            _root,
            [Assign("good.flac", "Key|A", "Key|Minor"), Assign("bad.flac", "Key|", "Key|Major")]
        );

        // Rejected, not Stale: a bad keyword is not fixed by retrying.
        var rejected = Assert.IsType<TagWriteOutcome.Rejected>(outcome);
        Assert.Contains("'Key|'", rejected.Error);
        Assert.False(Directory.Exists(Path.Combine(_root, "Ableton Folder Info")));
    }

    /// <summary>Driven directly: the moment between read and replace is unreachable from Apply.</summary>
    [Fact]
    public void A_store_that_changed_under_the_write_is_left_alone()
    {
        _writer.Apply(_root, [Assign("a.flac", "Key|A", "Key|Minor")]);

        var path = XmpPath;
        var before = File.ReadAllBytes(path);
        var stale = File.GetLastWriteTimeUtc(path).AddSeconds(-30);

        var replaced = FolderInfoWriter.Replace(
            path,
            XDocument.Parse("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\" />"),
            stale
        );

        Assert.False(replaced);
        Assert.Equal(before, File.ReadAllBytes(path));

        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_root, "Ableton Folder Info"), "*.tmp"));
    }

    [Fact]
    public void A_store_nobody_touched_is_replaced()
    {
        _writer.Apply(_root, [Assign("a.flac", "Key|A", "Key|Minor")]);

        var path = XmpPath;

        Assert.True(
            FolderInfoWriter.Replace(
                path,
                XDocument.Parse("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\" />"),
                File.GetLastWriteTimeUtc(path)
            )
        );
    }

    [Theory]
    [InlineData("Mood|Dark", true)]
    [InlineData("Key|C#", true)]
    [InlineData("Drums|Cymbal|Crash", true)]
    [InlineData("Mood", false)]
    [InlineData("Mood|", false)]
    [InlineData("|Dark", false)]
    [InlineData("Drums|Cymbal|", false)]
    [InlineData(" Mood|Dark", false)]
    [InlineData("Mood|Dark ", false)]
    public void A_keyword_needs_a_category_and_at_least_one_value(string keyword, bool accepted)
    {
        Assert.Equal(accepted, FolderInfoWriter.IsWellFormed(keyword));
    }
}
