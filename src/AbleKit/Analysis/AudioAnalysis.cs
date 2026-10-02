namespace AbleKit.Analysis;

/// <summary>
/// What Live measured in the audio: the waveform it draws, the transients it warps from, and the
/// audio's size. Belongs to one recording; carried to another, it describes the wrong sound.
/// </summary>
public sealed record AudioAnalysis
{
    /// <summary>The waveform Live draws.</summary>
    public required SampleOverview Overview { get; init; }

    /// <summary>
    /// Rising sample positions whose last entry is the audio's length in frames, apparently where some
    /// analysis of Live's looked at the audio. Live warps, draws and plays a sample without them, and
    /// accepts a table holding only 0 and the length, or none at all.
    /// </summary>
    public IReadOnlyList<int> HeadTable { get; init; } = [];

    /// <summary>
    /// The transients Live detected, in order of position; a position can repeat. Live shows these
    /// unless <see cref="HasUserOnsets"/> is on.
    /// </summary>
    public IReadOnlyList<Transient> Transients { get; init; } = [];

    /// <summary>
    /// The version of Live's transient analysis that found <see cref="Transients"/>: 5 in Live 12.3 and
    /// 12.4, 4 in an earlier Live 12 file. Keep the value read.
    /// </summary>
    public int TransientsVersion { get; init; } = 5;

    /// <summary>Whether the clip shows <see cref="UserOnsets"/> in place of <see cref="Transients"/>.</summary>
    public bool HasUserOnsets { get; init; }

    /// <summary>The transients as the saved clip holds them, edits and the ones under warp markers included.</summary>
    public IReadOnlyList<UserOnset> UserOnsets { get; init; } = [];

    /// <summary>Live's own tempo analysis, rarely filled in and not a reliable tempo; null when unset.</summary>
    public TempoEstimate? TempoEstimate { get; init; }

    /// <summary>The audio file's size in bytes when it was analysed. Live does not check it.</summary>
    public int OriginalFileSize { get; init; }

    /// <summary>
    /// An analysis of decoded audio as Live would write it: the overview drawn bit for bit as Live
    /// draws it, the transients given, and the file's size. The library does not detect transients.
    /// </summary>
    /// <param name="samples">The audio decoded to floats from −1 to 1, channels interleaved.</param>
    /// <param name="channelCount">How many channels <paramref name="samples"/> interleaves.</param>
    /// <param name="transients">
    /// In order of position, inside the audio, with energies from 0 to 1. Empty is accepted; Live then
    /// shows none and does not detect its own.
    /// </param>
    /// <param name="fileSize">The audio file's size in bytes.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="channelCount"/> is less than 1, or <paramref name="fileSize"/> is negative or
    /// beyond what the file can record, 2 GiB.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="samples"/> is empty or not a whole number of frames, or a transient is out of
    /// order, outside the audio, or has an energy outside 0 to 1.
    /// </exception>
    public static AudioAnalysis FromSamples(
        ReadOnlySpan<float> samples,
        int channelCount,
        IReadOnlyList<Transient> transients,
        long fileSize
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fileSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fileSize, int.MaxValue);

        var overview = SampleOverview.FromSamples(samples, channelCount);
        var frames = samples.Length / channelCount;
        CheckTransients(transients, frames);

        return new AudioAnalysis
        {
            Overview = overview,
            HeadTable = [0, frames],
            Transients = transients,
            OriginalFileSize = (int)fileSize,
        };
    }

    private static void CheckTransients(IReadOnlyList<Transient> transients, int frames)
    {
        for (var i = 0; i < transients.Count; i++)
        {
            var transient = transients[i];

            if (transient.Position < 0 || transient.Position >= frames)
                throw new ArgumentException(
                    $"transient {i} is at frame {transient.Position}, outside the audio's {frames}",
                    nameof(transients)
                );

            // Live's own lists repeat a position now and then, so only going backwards is refused.
            if (i > 0 && transient.Position < transients[i - 1].Position)
                throw new ArgumentException(
                    $"transient {i} is before the one ahead of it",
                    nameof(transients)
                );

            if (transient.Energy is not (>= 0 and <= 1))
                throw new ArgumentException(
                    $"transient {i} has energy {transient.Energy}, outside 0 to 1",
                    nameof(transients)
                );
        }
    }
}

/// <summary>A transient, which Live draws as a tick in the clip view and warps from.</summary>
/// <param name="Position">Where the attack starts, in sample frames from the start of the audio.</param>
/// <param name="Energy">How strong the transient is, from 0 to 1.</param>
public readonly record struct Transient(int Position, float Energy);

/// <summary>A transient as a saved clip holds it.</summary>
/// <param name="Time">Its position in the audio, in seconds.</param>
/// <param name="Energy">How strong it is; <see cref="double.MaxValue"/> for one under a warp marker.</param>
/// <param name="IsVolatile">Whether Live added it under a warp marker rather than detecting it.</param>
public readonly record struct UserOnset(double Time, double Energy, bool IsVolatile);

/// <summary>Live's own estimate of the sample's tempo.</summary>
/// <param name="PreprocessedData">The analysis Live keeps for it, opaque.</param>
/// <param name="Bpm">The estimate, in beats per minute.</param>
public sealed record TempoEstimate(ReadOnlyMemory<byte> PreprocessedData, double Bpm);
