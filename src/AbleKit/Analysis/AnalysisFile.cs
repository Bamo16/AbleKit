using System.Diagnostics.CodeAnalysis;

namespace AbleKit.Analysis;

/// <summary>
/// Everything an analysis file, the <c>.asd</c> beside a sample, holds: the clip Live pulls in, how
/// it is warped, and what Live measured in the audio. Read one, change it with <c>with</c>, and write
/// it back with <see cref="ToBytes"/> or <see cref="AnalysisFileWriter"/>.
/// </summary>
public sealed record AnalysisFile
{
    /// <summary>The clip's settings: where it starts and ends, its loop, gain, pitch and launch settings.</summary>
    public Clip Clip { get; init; } = new();

    /// <summary>How the sample is warped: the markers, the warp mode and its settings, the time signature.</summary>
    public Warp Warp { get; init; } = new();

    /// <summary>What Live measured in the audio: the waveform overview, the transients and more.</summary>
    public required AudioAnalysis Audio { get; init; }

    /// <summary>
    /// Whether <em>Save Default Clip</em> has been pressed. Live ignores <see cref="Clip"/> and
    /// <see cref="Warp"/> without it, and brings the sample in unwarped.
    /// </summary>
    public bool IsDefaultClipSaved { get; init; }

    /// <summary>
    /// Where the saved clip starts and ends, in beats, read from <see cref="Clip"/> the way Live does
    /// for its Loop setting; null until the default clip is saved.
    /// </summary>
    public DefaultClip? DefaultClip =>
        (IsDefaultClipSaved, Clip.LoopOn) switch
        {
            (false, _) => null,
            // With Loop on, the loop fields hold the loop, not the clip's start and end.
            (true, true) => new DefaultClip(Clip.LoopStart + Clip.SampleOffset, Clip.OutMarker),
            (true, false) => new DefaultClip(Clip.LoopStart, Clip.LoopEnd),
        };

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
    /// The file as Live 12 writes it. Reading a file Live wrote and writing it back gives the same
    /// bytes, except that the warp markers' internal ids are numbered afresh.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The markers do not advance, the transients go backwards, or the overview is not one Live could draw.
    /// </exception>
    public byte[] ToBytes() => AnalysisFileSerializer.Serialize(this);
}

/// <summary>The range Live pulls in when the sample is dragged out.</summary>
/// <param name="Start">The start marker, in beats on Live's grid.</param>
/// <param name="End">The end marker, in beats on Live's grid.</param>
public sealed record DefaultClip(double Start, double End);
