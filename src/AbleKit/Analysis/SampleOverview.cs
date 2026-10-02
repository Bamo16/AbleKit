using System.Runtime.InteropServices;

namespace AbleKit.Analysis;

/// <summary>
/// The waveform Live draws: for each bin, each channel's lowest and highest sample, at several levels
/// of detail.
/// </summary>
public sealed record SampleOverview
{
    /// <summary>
    /// Each level, finest first, as min₀ max₀ min₁ max₁ … per bin. Each level's bin covers
    /// 2^<see cref="SamplesPerBinLog2"/> bins of the level before, down to a single bin.
    /// </summary>
    public required IReadOnlyList<ReadOnlyMemory<Half>> Levels { get; init; }

    /// <summary>The finest level's bin size, as a power of two: 7 is 128 frames.</summary>
    public required int SamplesPerBinLog2 { get; init; }

    /// <summary>How many channels each bin holds.</summary>
    public required int ChannelCount { get; init; }

    /// <summary>How many sample frames each of the finest level's bins covers.</summary>
    public int SamplesPerBin => 1 << SamplesPerBinLog2;

    /// <summary>How many bins the finest level has.</summary>
    public int BinCount => Levels is [var finest, ..] ? finest.Length / (2 * ChannelCount) : 0;

    /// <summary>
    /// The overview Live would draw for <paramref name="samples"/>, bit for bit. The bin size is
    /// Live's: 128 frames up to 2^24 frames of audio (about 6:20 at 44.1 kHz), doubling past that.
    /// </summary>
    /// <param name="samples">The audio decoded to floats from −1 to 1, channels interleaved.</param>
    /// <param name="channelCount">How many channels <paramref name="samples"/> interleaves.</param>
    /// <exception cref="ArgumentException"><paramref name="samples"/> is empty or not a whole number of frames.</exception>
    public static SampleOverview FromSamples(ReadOnlySpan<float> samples, int channelCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channelCount, 1);

        if (samples.Length is 0 || samples.Length % channelCount is not 0)
            throw new ArgumentException(
                $"{samples.Length} samples are not a whole number of {channelCount}-channel frames",
                nameof(samples)
            );

        var frames = samples.Length / channelCount;
        var log2 = 7;

        // Measured: 7 up to 16,716,224 frames, 8 from 17,070,528; 2^24 lies between.
        while (frames > 1L << (17 + log2))
            log2++;

        return new SampleOverview
        {
            Levels = OverviewLevels.Compute(samples, channelCount, log2),
            SamplesPerBinLog2 = log2,
            ChannelCount = channelCount,
        };
    }

    /// <summary>The loudest sample between two sample frames, 0 where the overview ends.</summary>
    public float PeakBetween(long fromSample, long toSample)
    {
        var perBin = 2 * ChannelCount;
        var finest = MemoryMarshal.Cast<Half, ushort>(Levels[0].Span);
        var first = Math.Max(fromSample / SamplesPerBin, 0);
        var last = Math.Min((toSample - 1) / SamplesPerBin, finest.Length / perBin - 1);
        ushort loudest = 0;

        // Without the sign bit, a half's bits order the same way as its magnitude.
        for (var bin = (int)first; bin <= last; bin++)
        {
            foreach (var bits in finest.Slice(bin * perBin, perBin))
                loudest = Math.Max(loudest, (ushort)(bits & 0x7FFF));
        }

        return (float)BitConverter.UInt16BitsToHalf(loudest);
    }
}
