using AbleKit.Analysis;

namespace AbleKit.Tests.Analysis;

public sealed class AnalysisFileTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "AbleKit.Tests",
        Guid.NewGuid().ToString("n")
    );

    public AnalysisFileTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_file_Live_wrote_is_read()
    {
        var analysis = AnalysisFile.Read(ClickTrack.Fixture("sidecar-clicks.asd"));

        Assert.Equal(new DefaultClip(0, 32), analysis.DefaultClip);
    }

    [Fact]
    public void Trying_a_file_Live_wrote_reads_it()
    {
        Assert.True(
            AnalysisFile.TryRead(ClickTrack.Fixture("sidecar-clicks.asd"), out var analysis)
        );
        Assert.Equal(new DefaultClip(0, 32), analysis.DefaultClip);
    }

    [Fact]
    public void Trying_a_path_with_no_file_reads_nothing()
    {
        Assert.False(AnalysisFile.TryRead(Path.Combine(_root, "none.wav.asd"), out _));
    }

    [Fact]
    public void Trying_a_path_with_no_folder_reads_nothing_too()
    {
        Assert.False(AnalysisFile.TryRead(Path.Combine(_root, "gone", "none.wav.asd"), out _));
    }

    [Fact]
    public void Reading_a_path_with_no_file_throws_that_it_is_not_found()
    {
        Assert.Throws<FileNotFoundException>(() =>
            AnalysisFile.Read(Path.Combine(_root, "none.wav.asd"))
        );
    }

    [Fact]
    public void A_file_held_open_by_another_program_throws_rather_than_reading_as_absent()
    {
        var path = Copy("sidecar-clicks.asd");
        using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.Throws<IOException>(() => AnalysisFile.TryRead(path, out _));
    }

    [Fact]
    public void A_file_cut_short_is_invalid_and_says_what_it_did_not_find()
    {
        var path = Path.Combine(_root, "short.wav.asd");
        File.WriteAllBytes(
            path,
            File.ReadAllBytes(ClickTrack.Fixture("sidecar-clicks.asd"))[..30000]
        );

        var invalid = Assert.Throws<InvalidDataException>(() => AnalysisFile.TryRead(path, out _));
        Assert.Contains("OverView", invalid.Message);
    }

    [Fact]
    public void A_file_that_is_not_an_analysis_file_is_invalid()
    {
        var path = Path.Combine(_root, "notes.wav.asd");
        File.WriteAllText(path, "not an analysis file");

        var invalid = Assert.Throws<InvalidDataException>(() => AnalysisFile.Read(path));
        Assert.Contains("SampleData", invalid.Message);
    }

    [Fact]
    public void A_head_that_does_not_lead_to_the_clip_is_invalid()
    {
        var asd = File.ReadAllBytes(ClickTrack.Fixture("sidecar-clicks.asd"));
        asd[2]++;

        var invalid = Assert.Throws<InvalidDataException>(() => AnalysisFile.Parse(asd));
        Assert.Contains("head", invalid.Message);
        Assert.False(AnalysisFile.TryParse(asd, out _));
    }

    [Fact]
    public void The_file_written_is_the_model_as_bytes()
    {
        var analysis = Clicks();
        var path = Path.Combine(_root, "Break (copy).wav.asd");

        analysis.Write(path);

        Assert.Equal(analysis.ToBytes(), File.ReadAllBytes(path));
    }

    [Fact]
    public void An_existing_file_is_replaced_and_nothing_is_left_beside_it()
    {
        var path = Path.Combine(_root, "clicks.wav.asd");
        File.WriteAllBytes(path, [1, 2, 3]);

        Clicks().Write(path);

        Assert.Equal(Clicks().ToBytes(), AnalysisFile.Read(path).ToBytes());
        Assert.Equal(["clicks.wav.asd"], Directory.GetFiles(_root).Select(Path.GetFileName));
    }

    [Fact]
    public void A_folder_that_is_not_there_throws_without_writing()
    {
        var path = Path.Combine(_root, "missing", "clicks.wav.asd");

        Assert.Throws<DirectoryNotFoundException>(() => Clicks().Write(path));
        Assert.False(Directory.Exists(Path.Combine(_root, "missing")));
    }

    [Theory]
    [InlineData("sidecar-clicks.asd")]
    [InlineData("sidecar-clicks-unsaved.asd")]
    [InlineData("sidecar-mono.asd")]
    [InlineData("sidecar-tail-tempo.asd")]
    public void A_file_Live_wrote_writes_back_as_Live_wrote_it(string fixture)
    {
        // Live's list ids record the file's editing history; the writer numbers them afresh.
        var asd = File.ReadAllBytes(ClickTrack.Fixture(fixture));

        Assert.Equal(RoundTrip.WithFreshIds(asd), RoundTrip.Rewrite(asd));
    }

    [Fact]
    public void A_file_from_an_earlier_Live_12_differs_only_in_the_order_its_schema_lists_types()
    {
        // DBraun's file: the same type definitions, listed in another build's order.
        var asd = RoundTrip.WithFreshIds(
            File.ReadAllBytes(ClickTrack.Fixture("sidecar-loop-on.asd"))
        );
        var written = RoundTrip.Rewrite(asd);
        var (start, end) = RoundTrip.Schema(asd);

        Assert.Equal(asd.Length, written.Length);
        Assert.Equal(asd[..start], written[..start]);
        Assert.Equal(asd[end..], written[end..]);
        Assert.Equal(RoundTrip.Types(asd), RoundTrip.Types(written));
    }

    [Fact]
    public void A_changed_setting_is_the_only_thing_that_changes()
    {
        var asd = RoundTrip.WithFreshIds(
            File.ReadAllBytes(ClickTrack.Fixture("sidecar-clicks.asd"))
        );
        Assert.True(AnalysisFile.TryParse(asd, out var analysis));
        Assert.True(AnalysisFileParser.TryScan(asd, out var scan));

        var raised = analysis with { Clip = analysis.Clip with { PitchCoarse = 1 } };
        var written = raised.ToBytes();

        var pitch = scan.Layout.Values["PitchCoarse.Value"];
        var changed = Enumerable.Range(0, asd.Length).Where(i => asd[i] != written[i]).ToList();

        Assert.Equal(asd.Length, written.Length);
        Assert.All(changed, i => Assert.InRange(i, pitch, pitch + sizeof(float) - 1));
        Assert.True(AnalysisFile.TryParse(written, out var reread));
        Assert.Equal(1, reread.Clip.PitchCoarse);
    }

    [Fact]
    public void A_moved_marker_reads_back_where_it_was_put()
    {
        Assert.True(
            AnalysisFile.TryParse(
                File.ReadAllBytes(ClickTrack.Fixture("sidecar-clicks.asd")),
                out var analysis
            )
        );
        var markers = analysis.Warp.Markers;

        var moved = analysis with
        {
            Warp = analysis.Warp with
            {
                Markers = [markers[0] with { Beat = -4 }, .. markers.Skip(1)],
            },
        };

        Assert.True(AnalysisFile.TryParse(moved.ToBytes(), out var reread));
        Assert.Equal(moved.Warp.Markers, reread.Warp.Markers);
    }

    [Fact]
    public void A_file_with_no_markers_reads_back_with_none()
    {
        Assert.True(
            AnalysisFile.TryParse(
                File.ReadAllBytes(ClickTrack.Fixture("sidecar-clicks.asd")),
                out var analysis
            )
        );

        var cleared = analysis with { Warp = analysis.Warp with { Markers = [] } };

        Assert.True(AnalysisFile.TryParse(cleared.ToBytes(), out var reread));
        Assert.Empty(reread.Warp.Markers);
    }

    [Fact]
    public void A_marker_before_the_one_ahead_of_it_is_refused()
    {
        Assert.True(
            AnalysisFile.TryParse(
                File.ReadAllBytes(ClickTrack.Fixture("sidecar-clicks.asd")),
                out var analysis
            )
        );
        var markers = analysis.Warp.Markers;

        var crossed = analysis with
        {
            Warp = analysis.Warp with
            {
                Markers = [markers[0] with { Beat = 40 }, .. markers.Skip(1)],
            },
        };

        Assert.Throws<InvalidOperationException>(crossed.ToBytes);
    }

    [Fact]
    public void An_overview_Live_could_not_have_drawn_is_refused()
    {
        Assert.True(
            AnalysisFile.TryParse(
                File.ReadAllBytes(ClickTrack.Fixture("sidecar-clicks.asd")),
                out var analysis
            )
        );
        var overview = analysis.Audio.Overview;

        var truncated = analysis with
        {
            Audio = analysis.Audio with
            {
                Overview = overview with { Levels = [.. overview.Levels.SkipLast(1)] },
            },
        };

        Assert.Throws<InvalidOperationException>(truncated.ToBytes);
    }

    private static AnalysisFile Clicks() =>
        AnalysisFile.Parse(File.ReadAllBytes(ClickTrack.Fixture("sidecar-clicks.asd")));

    private string Copy(string fixture)
    {
        var path = Path.Combine(_root, fixture);
        File.Copy(ClickTrack.Fixture(fixture), path);

        return path;
    }
}
