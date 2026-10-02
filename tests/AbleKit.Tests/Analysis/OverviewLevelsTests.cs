using AbleKit.Analysis;

namespace AbleKit.Tests.Analysis;

public sealed class OverviewLevelsTests
{
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
