using System.Runtime.InteropServices;
using AbleKit.Analysis;

namespace AbleKit.Tests.Analysis;

public sealed class SampleOverviewTests
{
    [Theory]
    [InlineData("sidecar-clicks.asd", false)]
    [InlineData("sidecar-mono.asd", true)]
    public void Every_level_matches_the_overview_Live_drew_bit_for_bit(string sidecar, bool mono)
    {
        var track = mono ? ClickTrack.Mono : ClickTrack.Stereo;
        Assert.True(AnalysisFile.TryRead(ClickTrack.Fixture(sidecar), out var analysis));
        var live = analysis.Audio.Overview;

        var drawn = SampleOverview.FromSamples(track.Samples, track.ChannelCount);

        Assert.Equal(live.SamplesPerBinLog2, drawn.SamplesPerBinLog2);
        Assert.Equal(live.Levels.Count, drawn.Levels.Count);

        for (var level = 0; level < live.Levels.Count; level++)
            Assert.Equal(Bits(live.Levels[level]), Bits(drawn.Levels[level]));
    }

    [Fact]
    public void Audio_past_two_to_the_24_frames_is_drawn_with_bins_twice_as_wide()
    {
        var overview = SampleOverview.FromSamples(new float[(1 << 24) + 1], channelCount: 1);

        Assert.Equal(8, overview.SamplesPerBinLog2);
    }

    [Theory]
    [InlineData(0, 256, 0.5f)]
    [InlineData(256, 512, 0.25f)]
    [InlineData(384, 10_000, 0f)]
    [InlineData(10_000, 20_000, 0f)]
    public void A_peak_is_the_loudest_bin_in_range_and_nothing_past_the_end(
        long from,
        long to,
        float expected
    )
    {
        // Three 128-frame bins, mono, the middle one reaching furthest below zero.
        var overview = new SampleOverview
        {
            Levels =
            [
                new Half[] { (Half)0, (Half)0.5f, (Half)(-0.1f), (Half)0, (Half)0, (Half)0.25f },
            ],
            SamplesPerBinLog2 = 7,
            ChannelCount = 1,
        };

        Assert.Equal(expected, overview.PeakBetween(from, to), 3);
    }

    private static ushort[] Bits(ReadOnlyMemory<Half> level) =>
        MemoryMarshal.Cast<Half, ushort>(level.Span).ToArray();
}
