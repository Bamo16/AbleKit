using System.Diagnostics.CodeAnalysis;

namespace AbleKit.Analysis;

/// <summary>
/// Everything an analysis file, the <c>.asd</c> beside a sample, holds: the clip Live pulls in, how
/// it is warped, and what Live measured in the audio. Read one with <see cref="TryRead"/>, change it
/// with <c>with</c>, and write it with <see cref="Write"/>.
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

    /// <summary>
    /// Reads the analysis file at <paramref name="path"/>: the sample's <c>.asd</c>, its audio file's
    /// path with <c>.asd</c> added. Live writes one only once a sample has been opened, so many
    /// samples have none; <see cref="TryRead"/> reads one that may not be there.
    /// </summary>
    /// <exception cref="FileNotFoundException">There is no file at <paramref name="path"/>.</exception>
    /// <exception cref="DirectoryNotFoundException">There is no folder at <paramref name="path"/>.</exception>
    /// <exception cref="IOException">The file is locked by another program. Trying again may help.</exception>
    /// <exception cref="UnauthorizedAccessException">Reading the file is not permitted.</exception>
    /// <exception cref="InvalidDataException">
    /// The file is not an analysis file this library recognises: cut short, or laid out differently, as
    /// a Live version other than 12 may write it. The message says where reading gave up.
    /// </exception>
    public static AnalysisFile Read(string path) => Parse(File.ReadAllBytes(path));

    /// <summary>
    /// Reads the analysis file at <paramref name="path"/>, or returns false when there is none. A file
    /// that is there but cannot be read throws, as <see cref="Read"/> does.
    /// </summary>
    /// <exception cref="IOException">The file is locked by another program. Trying again may help.</exception>
    /// <exception cref="UnauthorizedAccessException">Reading the file is not permitted.</exception>
    /// <exception cref="InvalidDataException">
    /// The file is not an analysis file this library recognises. The message says where reading gave up.
    /// </exception>
    public static bool TryRead(string path, [NotNullWhen(true)] out AnalysisFile? analysis)
    {
        byte[] asd;

        try
        {
            asd = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            analysis = null;

            return false;
        }

        analysis = Parse(asd);

        return true;
    }

    /// <summary>Reads an analysis file already in memory.</summary>
    /// <exception cref="InvalidDataException">
    /// The bytes are not an analysis file this library recognises. The message says where reading gave up.
    /// </exception>
    public static AnalysisFile Parse(ReadOnlySpan<byte> asd) =>
        AnalysisFileParser.TryParse(asd, out var analysis, out var reason)
            ? analysis
            : throw new InvalidDataException($"Not an analysis file AbleKit reads: {reason}.");

    /// <summary>
    /// Reads an analysis file already in memory, or returns false when it is not one this library
    /// recognises. <see cref="Parse"/> says why.
    /// </summary>
    public static bool TryParse(
        ReadOnlySpan<byte> asd,
        [NotNullWhen(true)] out AnalysisFile? analysis
    ) => AnalysisFileParser.TryParse(asd, out analysis, out _);

    /// <summary>
    /// Writes the file to <paramref name="path"/>, replacing any file there, through a temporary file
    /// moved into place so Live never reads half a file. Live pairs an analysis file with the audio
    /// file whose name it carries plus <c>.asd</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The file holds something Live could not have written; see <see cref="ToBytes"/>. Nothing is written.
    /// </exception>
    /// <exception cref="DirectoryNotFoundException">The folder is not there. Nothing is written.</exception>
    /// <exception cref="IOException">The file is locked by another program. Nothing is written.</exception>
    /// <exception cref="UnauthorizedAccessException">Writing there is not permitted. Nothing is written.</exception>
    public void Write(string path)
    {
        var asd = ToBytes();
        var temporary = $"{path}.tmp";

        File.WriteAllBytes(temporary, asd);

        try
        {
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            File.Delete(temporary);

            throw;
        }
    }

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
