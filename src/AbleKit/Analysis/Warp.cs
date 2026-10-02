namespace AbleKit.Analysis;

/// <summary>
/// How Live warps the sample: the markers pinning it to the grid, the warp mode with every mode's
/// settings, and the time signature. The defaults are Live's own.
/// </summary>
public sealed record Warp
{
    /// <summary>The clip's Warp switch. On even for samples nobody has warped.</summary>
    public bool IsWarped { get; init; } = true;

    /// <summary>The algorithm Live stretches the sample with.</summary>
    public WarpMode Mode { get; init; } = WarpMode.Complex;

    /// <summary>
    /// The markers, in order. Live stores one more than it shows, 1/32 beat after the last, and it
    /// sets the tempo past the last visible marker. Empty until the default clip is saved.
    /// </summary>
    public IReadOnlyList<WarpMarker> Markers { get; init; } = [];

    /// <summary>Thought to mean Live's auto-warp placed the markers.</summary>
    public bool MarkersGenerated { get; init; }

    /// <summary>The time signature at <see cref="TimeSignature.Time"/>, 4/4 by default.</summary>
    public TimeSignature TimeSignature { get; init; } = new(4, 4);

    /// <summary>Beats mode's Preserve setting, as Live numbers it.</summary>
    public int TransientResolution { get; init; } = 6;

    /// <summary>Beats mode's loop mode, as Live numbers it.</summary>
    public int TransientLoopMode { get; init; } = 2;

    /// <summary>Beats mode's Envelope.</summary>
    public float TransientEnvelope { get; init; } = 100;

    /// <summary>Tones mode's Grain Size.</summary>
    public float GranularityTones { get; init; } = 30;

    /// <summary>Texture mode's Grain Size.</summary>
    public float GranularityTexture { get; init; } = 65;

    /// <summary>Texture mode's Flux.</summary>
    public float FluctuationTexture { get; init; } = 25;

    /// <summary>Complex Pro mode's Formants.</summary>
    public float ComplexProFormants { get; init; } = 100;

    /// <summary>Complex Pro mode's Envelope.</summary>
    public float ComplexProEnvelope { get; init; } = 128;

    /// <summary>
    /// Where <paramref name="beat"/> falls in the audio, extending the first and last segments past
    /// the markers. Null with fewer than two markers.
    /// </summary>
    public double? SecondsAt(double beat)
    {
        if (Markers is not { Count: > 1 })
            return null;

        var i = 1;

        while (i < Markers.Count - 1 && beat > Markers[i].Beat)
            i++;

        var (from, to) = (Markers[i - 1], Markers[i]);

        return from.Seconds
            + (beat - from.Beat) * (to.Seconds - from.Seconds) / (to.Beat - from.Beat);
    }

    /// <summary>The tempo of the segment <paramref name="seconds"/> falls in, in BPM.</summary>
    public double? TempoAt(double seconds)
    {
        if (Markers is not { Count: > 1 })
            return null;

        for (var i = Markers.Count - 2; i > 0; i--)
        {
            if (seconds >= Markers[i].Seconds)
                return Tempo(Markers[i], Markers[i + 1]);
        }

        return Tempo(Markers[0], Markers[1]);
    }

    private static double? Tempo(WarpMarker from, WarpMarker to)
    {
        var span = to.Seconds - from.Seconds;

        return span > 0 ? (to.Beat - from.Beat) / span * 60 : null;
    }
}

/// <summary>A point pinning the audio to the grid; a tempo exists only between two of them.</summary>
/// <param name="Seconds">Position in the audio, from its start.</param>
/// <param name="Beat">Position on Live's grid.</param>
public readonly record struct WarpMarker(double Seconds, double Beat);

/// <summary>A time signature and where on the grid it takes effect.</summary>
/// <param name="Numerator">Beats to the bar: the 3 in 3/4.</param>
/// <param name="Denominator">The note that counts as a beat: the 4 in 3/4.</param>
/// <param name="Time">Where it takes effect, in beats; 0 in every file measured.</param>
public readonly record struct TimeSignature(int Numerator, int Denominator, double Time = 0);

/// <summary>The algorithm Live stretches the sample with, as chosen in its clip view.</summary>
public enum WarpMode
{
    /// <summary>Slices at transients; for drums and other rhythmic material.</summary>
    Beats,

    /// <summary>For monophonic, clearly pitched material such as vocals or bass.</summary>
    Tones,

    /// <summary>For polyphonic textures without a clear pitch.</summary>
    Texture,

    /// <summary>No stretching: the pitch follows the speed, like a record.</summary>
    RePitch,

    /// <summary>For full mixes.</summary>
    Complex,

    /// <summary>For REX files, which carry their own slices.</summary>
    Rex,

    /// <summary>Complex with formant control; for full mixes and vocals.</summary>
    ComplexPro,
}
