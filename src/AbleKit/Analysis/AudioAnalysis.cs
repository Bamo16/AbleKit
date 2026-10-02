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
    /// Rising sample positions whose last entry is the audio's length in frames. What the others mark
    /// is not known, and they come from the audio's content, so keep the table of a file read for the
    /// same audio.
    /// </summary>
    public required IReadOnlyList<int> HeadTable { get; init; }

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
