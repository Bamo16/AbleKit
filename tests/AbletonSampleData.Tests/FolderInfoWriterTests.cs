using System.Text;
using System.Xml.Linq;

namespace AbletonSampleData.Tests;

public class FolderInfoWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "AbletonSampleData.Tests",
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

        Assert.True(outcome.IsSuccess);
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
    public void A_key_Live_does_not_spell_that_way_is_refused()
    {
        // Live would register a second, permanent "F#".
        var outcome = _writer.Apply(_root, [Assign("a.flac", "Key|F#", "Key|Major")]);

        Assert.False(outcome.IsSuccess);
        Assert.False(outcome.IsStale, "a bad keyword is not fixed by retrying");
        Assert.Contains("Key|F#", outcome.Error);
        Assert.False(Directory.Exists(Path.Combine(_root, "Ableton Folder Info")));
    }

    [Fact]
    public void One_bad_keyword_stops_the_whole_write()
    {
        var outcome = _writer.Apply(
            _root,
            [Assign("good.flac", "Key|A", "Key|Minor"), Assign("bad.flac", "Key|H", "Key|Major")]
        );

        Assert.False(outcome.IsSuccess);
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

    [Fact]
    public void Every_tonic_Live_knows_is_accepted_and_nothing_else_is()
    {
        // Taken from the 451 key keywords across the real roots: sharps as U+266F, no flats.
        Assert.Equal(12, FolderInfoWriter.Tonics.Count);

        Assert.All(
            FolderInfoWriter.Tonics,
            t => Assert.True(FolderInfoWriter.IsWellFormed($"Key|{t}"))
        );

        Assert.False(FolderInfoWriter.IsWellFormed("Key|Bb"));
        Assert.False(FolderInfoWriter.IsWellFormed("Key|A♭"));
        Assert.False(FolderInfoWriter.IsWellFormed("Key|C#"));
    }

    [Theory]
    [InlineData("Mood|Dark", true)]
    [InlineData("Mood", false)]
    [InlineData("Mood|", false)]
    [InlineData("|Dark", false)]
    [InlineData("Mood|Dark|Red", false)]
    [InlineData(" Mood|Dark", false)]
    public void Any_other_keyword_needs_only_a_category_and_a_value(string keyword, bool accepted)
    {
        Assert.Equal(accepted, FolderInfoWriter.IsWellFormed(keyword));
    }
}
