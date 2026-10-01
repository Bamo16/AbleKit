using System.Runtime.InteropServices;

namespace AbletonSampleData.Tests;

public sealed class OverviewLevelsTests
{
    [Theory]
    [InlineData("sidecar-clicks.asd", false)]
    [InlineData("sidecar-mono.asd", true)]
    public void Every_level_matches_the_overview_Live_drew_bit_for_bit(string sidecar, bool mono)
    {
        var track = mono ? ClickTrack.Mono : ClickTrack.Stereo;
        var asd = File.ReadAllBytes(ClickTrack.Fixture(sidecar));
        Assert.True(AnalysisFileParser.TryScan(asd, out var scan));

        var levels = OverviewLevels.Compute(track.Samples, track.ChannelCount, log2: 7);

        Assert.Equal(scan.Layout.Counts[AnalysisFileParser.OverviewLevelsPath], levels.Length);

        for (var level = 0; level < levels.Length; level++)
        {
            var array = scan.Layout.Arrays[AnalysisFileParser.LevelPath(level)];
            var live = MemoryMarshal.Cast<byte, ushort>(
                asd.AsSpan(array.Offset, array.End - array.Offset)
            );

            Assert.Equal(live.ToArray(), levels[level]);
        }
    }

    [Fact]
    public void Levels_shrink_by_the_bin_size_down_to_a_single_bin()
    {
        // 128 × 128 × 3 frames: 384 bins, then 3, then 1.
        var levels = OverviewLevels.Compute(new float[2 * 128 * 128 * 3], channelCount: 2, log2: 7);

        Assert.Equal([384 * 4, 3 * 4, 4], levels.Select(level => level.Length));
    }

    [Theory]
    // Just under 1: the nearest half is 1, which is further from zero, so the one below it.
    [InlineData(0.99999994f, 0x3BFF)]
    [InlineData(-0.99999994f, 0xBBFF)]
    // A value a half holds exactly stays as it is.
    [InlineData(0.5f, 0x3800)]
    [InlineData(-0.5f, 0xB800)]
    [InlineData(0f, 0x0000)]
    // 0.1 rounds down to its nearest half already.
    [InlineData(0.1f, 0x2E66)]
    public void A_value_is_truncated_toward_zero_rather_than_rounded(float value, int bits)
    {
        Assert.Equal((ushort)bits, OverviewLevels.TruncateToHalf(value));
    }
}
