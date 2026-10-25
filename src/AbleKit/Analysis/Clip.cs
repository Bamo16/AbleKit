namespace AbleKit.Analysis;

/// <summary>
/// The clip's settings, as Live's clip view shows them and names them. The defaults are those of a
/// clip Live has just saved, apart from the positions, which depend on the sample.
/// </summary>
public sealed record Clip
{
    /// <summary>With Loop off, the start marker; with Loop on, the loop start. In beats.</summary>
    public double LoopStart { get; init; }

    /// <summary>With Loop off, the end marker; with Loop on, the loop end. In beats.</summary>
    public double LoopEnd { get; init; }

    /// <summary>With Loop on, the start marker's distance from the loop start, in beats.</summary>
    public double SampleOffset { get; init; }

    /// <summary>The loop start, remembered while Loop is off. In beats.</summary>
    public double HiddenLoopStart { get; init; }

    /// <summary>The loop end, remembered while Loop is off. In beats.</summary>
    public double HiddenLoopEnd { get; init; }

    /// <summary>The end marker, in beats. With Loop off, <see cref="LoopEnd"/> is the one Live uses.</summary>
    public double OutMarker { get; init; }

    /// <summary>The Loop switch.</summary>
    public bool LoopOn { get; init; } = true;

    /// <summary>Thought to make the clip follow the set's tempo; on in every file measured.</summary>
    public bool Sync { get; init; } = true;

    /// <summary>The HiQ switch.</summary>
    public bool HiQ { get; init; } = true;

    /// <summary>The Fade switch.</summary>
    public bool Fade { get; init; }

    /// <summary>The clip's gain, linear: 1 is 0 dB, and 20 × log₁₀ of it is the clip view's dB.</summary>
    public float SampleVolume { get; init; } = 1;

    /// <summary>Thought to be a Session clip's launch velocity; 0 in every file measured.</summary>
    public float VelocityAmount { get; init; }

    /// <summary>Transpose, in semitones.</summary>
    public float PitchCoarse { get; init; }

    /// <summary>Detune, in cents.</summary>
    public float PitchFine { get; init; }

    /// <summary>
    /// The clip's color, a cell of Live's palette. <see cref="ClipColor.Random"/> before the first save.
    /// </summary>
    public ClipColor Color { get; init; } = ClipColor.Random;

    /// <summary>The clip's launch mode, as Live numbers it.</summary>
    public int LaunchMode { get; init; }

    /// <summary>The clip's launch quantization, as Live numbers it.</summary>
    public int LaunchQuantization { get; init; }
}
