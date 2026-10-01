namespace AbletonSampleData;

/// <summary>
/// Draws the waveform overview the way Live does, bit for bit: a minimum and a maximum per channel
/// per bin, as half-precision floats truncated toward zero.
/// </summary>
internal static class OverviewLevels
{
    /// <summary>
    /// Every level, finest first, as the half-precision bits Live stores: min₀ max₀ min₁ max₁ per
    /// bin. Each level's bins are 2^<paramref name="log2"/> of the level below, down to a single bin.
    /// </summary>
    internal static ushort[][] Compute(ReadOnlySpan<float> samples, int channelCount, int log2)
    {
        var binSize = 1 << log2;
        var frames = samples.Length / channelCount;
        var level = Finest(samples, channelCount, binSize, frames);
        List<ushort[]> levels = [Truncate(level)];

        // Truncation keeps order, so the minimum of truncated values is the truncated minimum.
        while (level.Length > 2 * channelCount)
        {
            level = Coarser(level, channelCount, binSize);
            levels.Add(Truncate(level));
        }

        return [.. levels];
    }

    private static float[] Finest(
        ReadOnlySpan<float> samples,
        int channelCount,
        int binSize,
        int frames
    )
    {
        var bins = (frames + binSize - 1) / binSize;
        var level = new float[bins * 2 * channelCount];

        for (var bin = 0; bin < bins; bin++)
        {
            var first = bin * binSize;
            var last = Math.Min(first + binSize, frames);

            for (var channel = 0; channel < channelCount; channel++)
            {
                var (min, max) = (float.MaxValue, float.MinValue);

                for (var frame = first; frame < last; frame++)
                {
                    var sample = samples[frame * channelCount + channel];
                    min = Math.Min(min, sample);
                    max = Math.Max(max, sample);
                }

                level[(bin * channelCount + channel) * 2] = min;
                level[(bin * channelCount + channel) * 2 + 1] = max;
            }
        }

        return level;
    }

    private static float[] Coarser(float[] finer, int channelCount, int binSize)
    {
        var perBin = 2 * channelCount;
        var finerBins = finer.Length / perBin;
        var bins = (finerBins + binSize - 1) / binSize;
        var level = new float[bins * perBin];

        for (var bin = 0; bin < bins; bin++)
        {
            var first = bin * binSize;
            var last = Math.Min(first + binSize, finerBins);

            for (var channel = 0; channel < channelCount; channel++)
            {
                var (min, max) = (float.MaxValue, float.MinValue);

                for (var source = first; source < last; source++)
                {
                    min = Math.Min(min, finer[(source * channelCount + channel) * 2]);
                    max = Math.Max(max, finer[(source * channelCount + channel) * 2 + 1]);
                }

                level[(bin * channelCount + channel) * 2] = min;
                level[(bin * channelCount + channel) * 2 + 1] = max;
            }
        }

        return level;
    }

    private static ushort[] Truncate(float[] level) => [.. level.Select(TruncateToHalf)];

    /// <summary>The half-precision bits of <paramref name="value"/>, rounded toward zero.</summary>
    internal static ushort TruncateToHalf(float value)
    {
        var half = (Half)value;
        var bits = BitConverter.HalfToUInt16Bits(half);

        // The cast rounds to nearest. A half's magnitude is its bits without the sign, so one step
        // down in those bits is one step toward zero, on either side of it.
        return Math.Abs((float)half) > Math.Abs(value) ? (ushort)(bits - 1) : bits;
    }
}
