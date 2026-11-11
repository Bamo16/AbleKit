using AbleKit.Analysis;

namespace AbleKit.Tests.Analysis;

public sealed class WarpTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void A_tempo_needs_two_markers_to_exist(int markers)
    {
        Assert.Null(
            Warp([.. Enumerable.Range(0, markers).Select(i => new WarpMarker(i, i))]).TempoAt(0)
        );
    }

    [Fact]
    public void Off_gives_the_pair_Live_writes_at_the_clip_start()
    {
        var warp = AbleKit.Analysis.Warp.Off(22.0687449, 174);

        Assert.False(warp.IsWarped);
        Assert.True(warp.MarkersGenerated);
        Assert.Equal(new WarpMarker(22.0687449, 0), warp.Markers[0]);
        Assert.Equal(2, warp.Markers.Count);
        Assert.Equal(22.0795206, warp.Markers[1].Seconds, 6);
        Assert.Equal(0.03125, warp.Markers[1].Beat);
    }

    [Fact]
    public void Off_spaces_the_pair_at_a_hundred_and_twenty_unless_told_otherwise()
    {
        var warp = AbleKit.Analysis.Warp.Off(1);

        Assert.Equal(1 + 1.0 / 64, warp.Markers[1].Seconds, 12);
        Assert.Equal(120, warp.TempoAt(1)!.Value, 9);
    }

    [Theory]
    [InlineData(-1, 120)]
    [InlineData(double.NaN, 120)]
    [InlineData(0, 0)]
    [InlineData(0, -5)]
    [InlineData(0, double.PositiveInfinity)]
    public void Off_refuses_a_start_or_tempo_that_cannot_be_real(double clipStart, double tempo) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AbleKit.Analysis.Warp.Off(clipStart, tempo)
        );

    [Fact]
    public void Four_beats_across_two_seconds_is_a_hundred_and_twenty()
    {
        var warp = Warp(new WarpMarker(0, 0), new WarpMarker(2, 4));

        Assert.Equal(120, warp.TempoAt(1)!.Value, 9);
    }

    [Fact]
    public void The_tempo_is_the_one_of_the_segment_the_time_falls_in()
    {
        var warp = Warp(new WarpMarker(0, 0), new WarpMarker(2, 4), new WarpMarker(3, 8));

        Assert.Equal(120, warp.TempoAt(1)!.Value, 9);
        Assert.Equal(240, warp.TempoAt(2.5)!.Value, 9);
    }

    [Fact]
    public void A_time_outside_the_markers_reads_the_nearest_segment()
    {
        var warp = Warp(new WarpMarker(10, 0), new WarpMarker(12, 4), new WarpMarker(13, 8));

        Assert.Equal(120, warp.TempoAt(0)!.Value, 9);
        Assert.Equal(240, warp.TempoAt(600)!.Value, 9);
    }

    [Fact]
    public void Two_markers_at_the_same_second_have_no_tempo_between_them()
    {
        Assert.Null(Warp(new WarpMarker(4, 0), new WarpMarker(4, 8)).TempoAt(4));
    }

    private static Warp Warp(params WarpMarker[] markers) => new() { Markers = markers };
}
