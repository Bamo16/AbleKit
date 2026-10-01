using System.Diagnostics.CodeAnalysis;

namespace AbleKit.Analysis;

/// <summary>What an analysis file, the <c>.asd</c> beside a sample, records about how Live warps it.</summary>
/// <param name="IsWarped">The clip's Warp switch, which is on even for samples nobody has warped.</param>
/// <param name="Mode">The algorithm Live stretches the sample with.</param>
/// <param name="TimeSignatureNumerator">The clip's time signature, 4 in 4/4.</param>
/// <param name="TimeSignatureDenominator">The clip's time signature, the second 4 in 4/4.</param>
/// <param name="Markers">The warp markers, in order, Live's hidden last one included; empty until the default clip is saved.</param>
/// <param name="DefaultClip">The saved default clip's start and end; null until it is saved.</param>
/// <param name="Overview">The waveform Live draws; null when the file ends before it.</param>
/// <param name="Transients">The transients Live detected, in order; null when the file ends before them.</param>
public sealed record AnalysisFile(
    bool IsWarped,
    WarpMode Mode,
    int TimeSignatureNumerator,
    int TimeSignatureDenominator,
    IReadOnlyList<WarpMarker> Markers,
    DefaultClip? DefaultClip = null,
    SampleOverview? Overview = null,
    IReadOnlyList<Transient>? Transients = null
)
{
    /// <summary>Reads the analysis file at <paramref name="path"/>; false when it is unreadable or unrecognised.</summary>
    public static bool TryRead(string path, [NotNullWhen(true)] out AnalysisFile? analysis)
    {
        try
        {
            return TryParse(File.ReadAllBytes(path), out analysis);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            analysis = null;

            return false;
        }
    }

    /// <summary>Reads an analysis file already in memory; false when it is unrecognised.</summary>
    public static bool TryParse(
        ReadOnlySpan<byte> asd,
        [NotNullWhen(true)] out AnalysisFile? analysis
    ) => AnalysisFileParser.TryParse(asd, out analysis);

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

    /// <summary>The tempo of the segment <paramref name="seconds"/> falls in.</summary>
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

/// <summary>
/// The range Ableton pulls in when the sample is dragged out, in beats; null until the user
/// presses <em>Save Default Clip</em>, which is also what writes the markers.
/// </summary>
/// <param name="Start">The start marker, in beats on Live's grid.</param>
/// <param name="End">The end marker, in beats on Live's grid.</param>
public sealed record DefaultClip(double Start, double End);

/// <summary>
/// The loudest sample in each bin of the waveform Ableton draws, at its finest level; enough to
/// tell silence from sound.
/// </summary>
/// <param name="SamplesPerBin">How many sample frames each peak covers.</param>
/// <param name="Peaks">The loudest absolute sample value in each bin, from 0 to 1, across all channels.</param>
public sealed record SampleOverview(int SamplesPerBin, IReadOnlyList<float> Peaks)
{
    /// <summary>The loudest sample between two sample positions, 0 where the overview ends.</summary>
    public float PeakBetween(long fromSample, long toSample)
    {
        var first = Math.Max(fromSample / SamplesPerBin, 0);
        var last = Math.Min((toSample - 1) / SamplesPerBin, Peaks.Count - 1);
        var peak = 0f;

        for (var bin = (int)first; bin <= last; bin++)
            peak = Math.Max(peak, Peaks[bin]);

        return peak;
    }
}

/// <summary>A point pinning the audio to the grid; a tempo exists only between two of them.</summary>
/// <param name="Seconds">Position in the audio file.</param>
/// <param name="Beat">Position on Ableton's grid.</param>
public readonly record struct WarpMarker(double Seconds, double Beat);

/// <summary>A transient, which Live draws as a tick in the clip view and warps from.</summary>
/// <param name="Position">Where the attack starts, in sample frames from the start of the audio.</param>
/// <param name="Energy">How strong the transient is, from 0 to 1.</param>
public readonly record struct Transient(int Position, float Energy);

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
