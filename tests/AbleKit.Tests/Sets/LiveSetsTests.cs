using System.IO.Compression;
using System.Text;
using AbleKit.Sets;
using Microsoft.Extensions.Time.Testing;

namespace AbleKit.Tests.Sets;

public sealed class LiveSetsTests : IDisposable
{
    private const string Library = "C:/Samples";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "AbleKit.Tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly LiveSets _sets;

    public LiveSetsTests() =>
        _sets = new LiveSets(
            _root,
            new FakeTimeProvider(new(2026, 9, 27, 10, 15, 0, TimeSpan.Zero))
        );

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_set_that_uses_a_sample_by_path_is_found()
    {
        var one = Set("One Project/One.als", $"{Library}/T &amp; Sugah - TumDaDaDum (Vocal).flac");
        Set("Two Project/Two.als", $"{Library}/Something Else (Vocal).flac");

        var sets = _sets.Using([@"C:\Samples\T & Sugah - TumDaDaDum (Vocal).flac"]);

        Assert.Equal([one], sets);
    }

    [Fact]
    public void Lives_backups_are_not_sets()
    {
        Set("One Project/Backup/One [2026-09-01 101010].als", $"{Library}/A (Vocal).flac");

        Assert.Empty(_sets.Using([$"{Library}/A (Vocal).flac"]));
    }

    [Fact]
    public void A_file_that_is_not_a_set_is_passed_over()
    {
        var path = Path.Combine(_root, "Broken Project", "Broken.als");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "not gzip");

        Assert.Empty(_sets.Using([$"{Library}/A (Vocal).flac"]));
    }

    [Fact]
    public void A_missing_projects_folder_holds_no_sets()
    {
        Assert.Empty(new LiveSets(Path.Combine(_root, "missing")).Using([$"{Library}/A.flac"]));
    }

    [Fact]
    public void Relinking_rewrites_the_path_and_the_relative_path_and_nothing_else()
    {
        var set = Set(
            "One Project/One.als",
            $"{Library}/Glen Check - Dazed &amp; Confused (Vocal).flac",
            $"{Library}/Untouched (Vocal).flac"
        );
        var before = Xml(set);

        var result = _sets.Relink(
            [
                new(
                    @"C:\Samples\Glen Check - Dazed & Confused (Vocal).flac",
                    @"C:\Samples\Dazed & Confused (Vocal).flac"
                ),
            ],
            "test"
        );

        Assert.Equal([set], result.Relinked);
        Assert.Equal(
            before
                .Replace(
                    "../../Samples/Glen Check - Dazed &amp; Confused (Vocal).flac",
                    "../../Samples/Dazed &amp; Confused (Vocal).flac"
                )
                .Replace(
                    $"{Library}/Glen Check - Dazed &amp; Confused (Vocal).flac",
                    $"{Library}/Dazed &amp; Confused (Vocal).flac"
                ),
            Xml(set)
        );
        Assert.Equal([set], _sets.Using([@"C:\Samples\Dazed & Confused (Vocal).flac"]));
    }

    [Fact]
    public void Relinking_keeps_the_set_as_it_was_in_the_projects_backup_folder()
    {
        var set = Set("One Project/One.als", $"{Library}/A (Vocal).flac");
        var before = File.ReadAllBytes(set);

        _sets.Relink([new($"{Library}/A (Vocal).flac", $"{Library}/B (Vocal).flac")], "crateside");

        var backup = Path.Combine(
            _root,
            "One Project",
            "Backup",
            "One [crateside 2026-09-27 101500].als"
        );
        Assert.Equal(before, File.ReadAllBytes(backup));
    }

    [Fact]
    public void A_set_that_does_not_use_the_sample_is_not_touched()
    {
        var set = Set("Two Project/Two.als", $"{Library}/Other (Vocal).flac");
        var written = File.GetLastWriteTimeUtc(set);

        var result = _sets.Relink(
            [new($"{Library}/A (Vocal).flac", $"{Library}/B (Vocal).flac")],
            "test"
        );

        Assert.Empty(result.Relinked);
        Assert.Equal(written, File.GetLastWriteTimeUtc(set));
        Assert.False(Directory.Exists(Path.Combine(_root, "Two Project", "Backup")));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a/b")]
    [InlineData("what?")]
    public void A_backup_label_that_cannot_go_in_a_file_name_throws(string label)
    {
        Assert.Throws<ArgumentException>(
            "backupLabel",
            () => _sets.Relink([new($"{Library}/A.flac", $"{Library}/B.flac")], label)
        );
    }

    private static string Xml(string set)
    {
        using var file = File.OpenRead(set);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);

        return reader.ReadToEnd();
    }

    /// <summary>A set holding one sample reference per path, laid out as Live writes them.</summary>
    private string Set(string relative, params string[] samplePaths)
    {
        var path = Path.GetFullPath(Path.Combine(_root, relative));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.Fastest);
        gzip.Write(
            Encoding.UTF8.GetBytes(
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n<Ableton>\r\n"
                    + string.Concat(
                        samplePaths.Select(sample =>
                            $"<SampleRef><FileRef>\r\n"
                            + $"\t<RelativePath Value=\"../../Samples/{sample[(sample.LastIndexOf('/') + 1)..]}\" />\r\n"
                            + $"\t<Path Value=\"{sample}\" />\r\n"
                            + "\t<OriginalFileSize Value=\"123\" />\r\n"
                            + "</FileRef><Name Value=\"clip\" /></SampleRef>\r\n"
                        )
                    )
                    + "</Ableton>\r\n"
            )
        );

        return path;
    }
}
