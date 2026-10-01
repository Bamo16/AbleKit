using System.Buffers.Binary;
using System.Runtime.InteropServices;
using static AbleKit.Analysis.AnalysisFileParser;

namespace AbleKit.Analysis;

/// <summary>
/// Writes analysis files Live accepts as its own, so a sample arrives in Live already warped, without
/// anyone opening it there first.
/// </summary>
public sealed class AnalysisFileWriter
{
    private const string UserOnsetsPath = "UserOnsets.UserOnsets";
    private const string HasUserOnsetsPath = "UserOnsets.HasUserOnsets.Value";
    private const string FileSizePath = "OriginalFileSize.Value";

    /// <summary>
    /// Writes the analysis file beside <paramref name="audioPath"/>, warped as the sibling's saved
    /// default clip, with the waveform drawn from <paramref name="samples"/> and the transients given.
    /// The sibling must be as long as the audio, as another stem of the same song is. Replaces any
    /// analysis file already beside the audio.
    /// </summary>
    /// <param name="siblingPath">An analysis file with a saved default clip, whose warp, clip settings and warp mode are copied.</param>
    /// <param name="audioPath">The audio file the new analysis file is for. It is not read; only its size is recorded.</param>
    /// <param name="samples">The audio decoded to floats from −1 to 1, channels interleaved, as <paramref name="audioPath"/> holds it.</param>
    /// <param name="channelCount">How many channels <paramref name="samples"/> interleaves.</param>
    /// <param name="transients">
    /// The transients Live shows and warps from, in order of position. Empty is accepted; Live then
    /// shows none and does not detect its own.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channelCount"/> is less than 1.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="samples"/> is not a whole number of frames, or a transient is out of order, outside
    /// the audio, or has an energy outside 0 to 1.
    /// </exception>
    public AnalysisWriteOutcome WriteFromSibling(
        string siblingPath,
        string audioPath,
        ReadOnlySpan<float> samples,
        int channelCount,
        IReadOnlyList<Transient> transients
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channelCount, 1);

        if (samples.Length % channelCount is not 0)
            throw new ArgumentException(
                $"{samples.Length} samples are not a whole number of {channelCount}-channel frames",
                nameof(samples)
            );

        var frames = samples.Length / channelCount;
        CheckTransients(transients, frames);

        var path = $"{audioPath}.asd";

        try
        {
            var sibling = File.ReadAllBytes(siblingPath);
            var fileSize = new FileInfo(audioPath).Length;

            if (!TryScan(sibling, out var scan))
                return new AnalysisWriteOutcome.Rejected(
                    "the sibling is not an analysis file this library recognises"
                );

            if (Misfit(sibling, scan, frames, channelCount, fileSize) is { } misfit)
                return new AnalysisWriteOutcome.Rejected(misfit);

            Replace(
                path,
                Compose(sibling, scan.Layout, samples, channelCount, fileSize, transients)
            );

            return new AnalysisWriteOutcome.Written(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new AnalysisWriteOutcome.Failed(ex.Message);
        }
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

    /// <summary>Why the sibling cannot lend its file to this audio, or null when it can.</summary>
    private static string? Misfit(
        ReadOnlySpan<byte> sibling,
        Scan scan,
        int frames,
        int channelCount,
        long fileSize
    )
    {
        var layout = scan.Layout;
        var leaves = new LeafReader(sibling, layout.Values);

        if (!scan.Saved)
            return "the sibling has no saved default clip, so no warp to lend";

        // The head's table ends at the audio's length, so this compares lengths without the sibling's audio.
        if (scan.Frames is not { } siblingFrames)
            return "the sibling's head does not record its length";

        if (siblingFrames != frames)
            return $"the sibling's length is {siblingFrames} frames and the audio's {frames}";

        if (
            !leaves.TryInt32(OverviewChannelsPath, out var siblingChannels)
            || !leaves.TryInt32(OverviewBinPath, out var log2)
            || log2 is < 1 or > 30
            || !layout.Counts.TryGetValue(OverviewLevelsPath, out var levels)
            || !layout.Arrays.ContainsKey(TransientPositionsPath)
            || !layout.Arrays.ContainsKey(TransientEnergiesPath)
            || !layout.Arrays.ContainsKey(UserOnsetsPath)
            || !layout.Values.ContainsKey(HasUserOnsetsPath)
            || !layout.Values.ContainsKey(FileSizePath)
        )
            return "the sibling ends before its analysis";

        if (siblingChannels != channelCount)
            return $"the sibling's channel count is {siblingChannels} and the audio's {channelCount}";

        if (fileSize > int.MaxValue)
            return "the audio is larger than an analysis file can record";

        // Same length, so the overview has the same shape; a difference means a rule here is wrong.
        var binSize = 1L << log2;
        var bins = (frames + binSize - 1) / binSize;

        for (var level = 0; level < levels; level++)
        {
            if (
                !layout.Arrays.TryGetValue(LevelPath(level), out var array)
                || array.Count != bins * 2 * channelCount
            )
                return $"the sibling's overview level {level} does not fit the audio's length";

            bins = (bins + binSize - 1) / binSize;
        }

        return null;
    }

    /// <summary>The sibling's bytes with everything drawn from its own audio replaced.</summary>
    private static byte[] Compose(
        ReadOnlySpan<byte> sibling,
        Layout layout,
        ReadOnlySpan<float> samples,
        int channelCount,
        long fileSize,
        IReadOnlyList<Transient> transients
    )
    {
        var log2 = BinaryPrimitives.ReadInt32LittleEndian(
            sibling[layout.Values[OverviewBinPath]..]
        );
        var levels = OverviewLevels.Compute(samples, channelCount, log2);
        var userOnsets = layout.Arrays[UserOnsetsPath];

        // The sibling's user onsets sit on its own audio, and a clip with them switched on shows them
        // in place of the transients. Off and empty is how Live leaves a file it has not saved.
        var emptyUserOnsets = sibling[userOnsets.Start..userOnsets.Offset].ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(emptyUserOnsets, 0);

        List<Edit> edits =
        [
            Edit.Whole(
                layout.Arrays[TransientPositionsPath],
                Counted(transients.Select(transient => transient.Position).ToArray())
            ),
            Edit.Whole(
                layout.Arrays[TransientEnergiesPath],
                Counted(transients.Select(transient => transient.Energy).ToArray())
            ),
            Edit.Whole(userOnsets, emptyUserOnsets),
            new(layout.Values[HasUserOnsetsPath], sizeof(bool), [0]),
            new(layout.Values[FileSizePath], sizeof(int), Int32Bytes((int)fileSize)),
            .. levels.Select(
                (level, index) =>
                    Edit.Elements(
                        layout.Arrays[LevelPath(index)],
                        MemoryMarshal.AsBytes(level.AsSpan()).ToArray()
                    )
            ),
        ];

        return Apply(sibling, edits);
    }

    private static byte[] Apply(ReadOnlySpan<byte> asd, List<Edit> edits)
    {
        edits.Sort((a, b) => a.Offset.CompareTo(b.Offset));

        var result = new byte[asd.Length + edits.Sum(edit => edit.Bytes.Length - edit.Length)];
        var (from, to) = (0, 0);

        foreach (var edit in edits)
        {
            asd[from..edit.Offset].CopyTo(result.AsSpan(to));
            to += edit.Offset - from;
            edit.Bytes.CopyTo(result, to);
            to += edit.Bytes.Length;
            from = edit.Offset + edit.Length;
        }

        asd[from..].CopyTo(result.AsSpan(to));

        return result;
    }

    /// <summary>An array as the file stores it: an int32 count, then the values.</summary>
    private static byte[] Counted<T>(T[] values)
        where T : unmanaged =>
        [.. Int32Bytes(values.Length), .. MemoryMarshal.AsBytes(values.AsSpan())];

    private static byte[] Int32Bytes(int value)
    {
        var bytes = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);

        return bytes;
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

    /// <summary>Replaces <see cref="Length"/> bytes at <see cref="Offset"/> with <see cref="Bytes"/>.</summary>
    private readonly record struct Edit(int Offset, int Length, byte[] Bytes)
    {
        /// <summary>Replaces the array, its count included.</summary>
        public static Edit Whole(ArrayField array, byte[] bytes) =>
            new(array.Start, array.End - array.Start, bytes);

        /// <summary>Replaces the array's elements, leaving its count.</summary>
        public static Edit Elements(ArrayField array, byte[] bytes) =>
            new(array.Offset, array.End - array.Offset, bytes);
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
    /// <param name="Path">Where: the audio's path with <c>.asd</c> added.</param>
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
