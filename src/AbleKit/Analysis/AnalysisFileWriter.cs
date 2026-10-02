namespace AbleKit.Analysis;

/// <summary>
/// Writes analysis files Live accepts as its own, so a sample arrives in Live already warped, without
/// anyone opening it there first.
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

    /// <summary>
    /// Writes the analysis file beside <paramref name="audioPath"/>, warped as the sibling's saved
    /// default clip, with the waveform drawn from <paramref name="samples"/> and the transients given.
    /// The sibling must be as long as the audio, as another stem of the same song usually is.
    /// </summary>
    /// <param name="siblingPath">An analysis file with a saved default clip, whose clip and warp are copied.</param>
    /// <param name="audioPath">The audio file the new analysis file is for. It is not read; only its size is recorded.</param>
    /// <param name="samples">The audio decoded to floats from −1 to 1, channels interleaved, as <paramref name="audioPath"/> holds it.</param>
    /// <param name="channelCount">How many channels <paramref name="samples"/> interleaves.</param>
    /// <param name="transients">
    /// The transients Live shows and warps from, in order of position. Empty is accepted; Live then
    /// shows none and does not detect its own.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channelCount"/> is less than 1.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="samples"/> is empty or not a whole number of frames, or a transient is out of
    /// order, outside the audio, or has an energy outside 0 to 1.
    /// </exception>
    public AnalysisWriteOutcome WriteFromSibling(
        string siblingPath,
        string audioPath,
        ReadOnlySpan<float> samples,
        int channelCount,
        IReadOnlyList<Transient> transients
    )
    {
        var overview = SampleOverview.FromSamples(samples, channelCount);
        var frames = samples.Length / channelCount;
        CheckTransients(transients, frames);

        long fileSize;

        try
        {
            fileSize = new FileInfo(audioPath).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new AnalysisWriteOutcome.Failed(ex.Message);
        }

        if (!AnalysisFile.TryRead(siblingPath, out var sibling))
            return File.Exists(siblingPath)
                ? new AnalysisWriteOutcome.Rejected(
                    "the sibling is not an analysis file this library recognises"
                )
                : new AnalysisWriteOutcome.Failed($"the sibling {siblingPath} could not be read");

        if (Misfit(sibling, frames, channelCount, fileSize) is { } misfit)
            return new AnalysisWriteOutcome.Rejected(misfit);

        // The sibling's user onsets sit on its own audio, and a clip with them switched on shows them
        // in place of the transients. Off and empty is how Live leaves a file it has not saved.
        var analysis = sibling with
        {
            Audio = sibling.Audio with
            {
                Overview = overview,
                Transients = transients,
                HasUserOnsets = false,
                UserOnsets = [],
                OriginalFileSize = (int)fileSize,
            },
        };

        return Write($"{audioPath}.asd", analysis);
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

    /// <summary>Why the sibling cannot lend its warp to this audio, or null when it can.</summary>
    private static string? Misfit(AnalysisFile sibling, int frames, int channelCount, long fileSize)
    {
        if (!sibling.IsDefaultClipSaved)
            return "the sibling has no saved default clip, so no warp to lend";

        // The head's table ends at the audio's length, and the sibling's table is kept.
        if (sibling.Audio.HeadTable is not [.., var siblingFrames])
            return "the sibling's head does not record its length";

        if (siblingFrames != frames)
            return $"the sibling's length is {siblingFrames} frames and the audio's {frames}";

        if (sibling.Audio.Overview.ChannelCount != channelCount)
            return $"the sibling's channel count is {sibling.Audio.Overview.ChannelCount} and the audio's {channelCount}";

        return fileSize > int.MaxValue
            ? "the audio is larger than an analysis file can record"
            : null;
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

/// <summary>
/// How writing an analysis file turned out: <see cref="Written"/>, <see cref="Rejected"/> or
/// <see cref="Failed"/>.
/// </summary>
public abstract record AnalysisWriteOutcome
{
    private AnalysisWriteOutcome() { }

    /// <summary>The analysis file was written.</summary>
    /// <param name="Path">Where it was written.</param>
    public sealed record Written(string Path) : AnalysisWriteOutcome;

    /// <summary>
    /// The sibling cannot lend its warp to this audio: it is unrecognised, has no saved default clip,
    /// or differs in length or channels. Nothing was written, and trying again will not change that.
    /// </summary>
    /// <param name="Error">What does not fit.</param>
    public sealed record Rejected(string Error) : AnalysisWriteOutcome;

    /// <summary>
    /// A file could not be read or written: missing, locked or not permitted. Nothing was written.
    /// </summary>
    /// <param name="Error">The file system's message.</param>
    public sealed record Failed(string Error) : AnalysisWriteOutcome;
}
