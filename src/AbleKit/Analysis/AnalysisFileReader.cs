namespace AbleKit.Analysis;

/// <summary>Reads analysis files from disk, saying why when one cannot be read.</summary>
public sealed class AnalysisFileReader
{
    /// <summary>
    /// Reads the analysis file at <paramref name="path"/>: the sample's <c>.asd</c>, its audio file's
    /// path with <c>.asd</c> added. Never throws for a missing, locked or unfamiliar file.
    /// </summary>
    public AnalysisReadOutcome Read(string path)
    {
        byte[] asd;

        try
        {
            asd = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new AnalysisReadOutcome.Missing();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new AnalysisReadOutcome.Failed(ex.Message);
        }

        return AnalysisFileParser.Parse(asd);
    }
}

/// <summary>
/// How reading an analysis file turned out: <see cref="Read"/>, <see cref="Missing"/>,
/// <see cref="Failed"/> or <see cref="Unrecognised"/>.
/// </summary>
public abstract record AnalysisReadOutcome
{
    private AnalysisReadOutcome() { }

    /// <summary>The file was read.</summary>
    /// <param name="Analysis">Everything it holds.</param>
    public sealed record Read(AnalysisFile Analysis) : AnalysisReadOutcome;

    /// <summary>
    /// There is no file at the path. Live writes one only once the sample has been opened, so for
    /// many samples there is none.
    /// </summary>
    public sealed record Missing : AnalysisReadOutcome;

    /// <summary>The file is there but could not be read: locked, or not permitted. Trying again may help.</summary>
    /// <param name="Error">The file system's message.</param>
    public sealed record Failed(string Error) : AnalysisReadOutcome;

    /// <summary>
    /// The file was read but is not an analysis file this library recognises: cut short, or laid out
    /// differently, as a Live version other than 12 may write it.
    /// </summary>
    /// <param name="Reason">Where the reading gave up.</param>
    public sealed record Unrecognised(string Reason) : AnalysisReadOutcome;
}
