namespace AbleKit.Analysis;

/// <summary>
/// Writes analysis files Live accepts as its own: one read and changed, or one built for audio Live has
/// not analysed, so a sample arrives in Live already warped.
/// </summary>
public sealed class AnalysisFileWriter
{
    /// <summary>
    /// Writes <paramref name="analysis"/> to <paramref name="path"/>, replacing any file there, through
    /// a temporary file moved into place so Live never reads half a file. Live pairs an analysis file
    /// with the audio file whose name it carries plus <c>.asd</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="analysis"/> holds something Live could not have written; see <see cref="AnalysisFile.ToBytes"/>.
    /// </exception>
    public AnalysisWriteOutcome Write(string path, AnalysisFile analysis)
    {
        var asd = analysis.ToBytes();

        try
        {
            Replace(path, asd);

            return new AnalysisWriteOutcome.Written(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new AnalysisWriteOutcome.Failed(ex.Message);
        }
    }

    /// <summary>Writes beside the target and moves it into place, so Live never reads half a file.</summary>
    private static void Replace(string path, byte[] asd)
    {
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
}

/// <summary>How writing an analysis file turned out: <see cref="Written"/> or <see cref="Failed"/>.</summary>
public abstract record AnalysisWriteOutcome
{
    private AnalysisWriteOutcome() { }

    /// <summary>The analysis file was written.</summary>
    /// <param name="Path">Where it was written.</param>
    public sealed record Written(string Path) : AnalysisWriteOutcome;

    /// <summary>
    /// The file could not be written: its folder missing, the file locked, or not permitted. Nothing
    /// was written.
    /// </summary>
    /// <param name="Error">The file system's message.</param>
    public sealed record Failed(string Error) : AnalysisWriteOutcome;
}
