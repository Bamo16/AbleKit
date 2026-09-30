namespace AbletonSampleData.Tests;

public class AnalysisFileTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void A_tempo_needs_two_markers_to_exist(int markers)
    {
        Assert.Null(
            Warp(Enumerable.Range(0, markers).Select(i => new WarpMarker(i, i)).ToArray())
                .TempoAt(0)
        );
    }

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

    private static AnalysisFile Warp(params WarpMarker[] markers) =>
        new(true, WarpMode.Complex, 4, 4, markers);
}
