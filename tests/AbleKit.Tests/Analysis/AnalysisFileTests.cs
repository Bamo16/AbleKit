using AbleKit.Analysis;

namespace AbleKit.Tests.Analysis;

/// <summary>Writing the model back: Live's own files, edited ones, and ones Live could not have written.</summary>
public sealed class AnalysisFileTests
{
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
            AnalysisFile.TryRead(ClickTrack.Fixture("sidecar-clicks.asd"), out var analysis)
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
            AnalysisFile.TryRead(ClickTrack.Fixture("sidecar-clicks.asd"), out var analysis)
        );

        var cleared = analysis with { Warp = analysis.Warp with { Markers = [] } };

        Assert.True(AnalysisFile.TryParse(cleared.ToBytes(), out var reread));
        Assert.Empty(reread.Warp.Markers);
    }

    [Fact]
    public void A_marker_before_the_one_ahead_of_it_is_refused()
    {
        Assert.True(
            AnalysisFile.TryRead(ClickTrack.Fixture("sidecar-clicks.asd"), out var analysis)
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
            AnalysisFile.TryRead(ClickTrack.Fixture("sidecar-clicks.asd"), out var analysis)
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
}
