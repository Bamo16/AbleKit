using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;

namespace AbleKit.Analysis;

/// <summary>
/// Reads an analysis file into an <see cref="AnalysisFile"/>. The file is undocumented but describes
/// itself with a type schema, which the reader walks rather than trusting fixed offsets.
/// </summary>
internal static class AnalysisFileParser
{
    private const byte ClassCode = 0x00;
    private const byte BoolCode = 0x10;
    private const byte Int32Code = 0x11;
    private const byte FloatCode = 0x12;
    private const byte DoubleCode = 0x17;
    private const byte ByteArrayCode = 0x31;
    private const byte HalfArrayCode = 0x32;
    private const byte Int32ArrayCode = 0x35;
    private const byte FloatArrayCode = 0x40;

    private const int HeadCountOffset = 2;
    private const int HeadTableOffset = 6;
    private const int ChunkPreambleLength = 9;

    private const int MaxTypeCount = 256;

    private const string SampleDataClass = "SampleData";

    private const string RemoteableArrayClass = "RemoteableArray";

    private static readonly byte[] ChunkMagic = [0xAB, 0x1E, 0x56, 0x78];
    private static readonly byte[] HeadTrailerStart = [0, 0, 0, 0, 0, 0, 0, 0, 100, 0, 0, 0];
    private static readonly byte[] SampleDataTag = [0x00, 0x0A, .. "SampleData"u8];

    /// <summary>Reads the file, or says why it is not one this library recognises.</summary>
    internal static bool TryParse(
        ReadOnlySpan<byte> asd,
        [NotNullWhen(true)] out AnalysisFile? analysis,
        [NotNullWhen(false)] out string? reason
    )
    {
        analysis = null;

        if (!TryReadSchema(asd, out var types, out var sampleData, out var instance))
        {
            reason = "there is no SampleData chunk, or its schema cannot be read";

            return false;
        }

        if (!TryReadHead(asd, sampleData, out var saved, out var table))
        {
            reason = "the head does not lead to the SampleData chunk";

            return false;
        }

        var scan = new Scan(saved, table, Walk(asd, types, instance));

        var fields = new Fields(asd.ToArray(), scan.Layout);

        var clip = new Clip
        {
            LoopStart = fields.Double("LoopStart.Value"),
            LoopEnd = fields.Double("LoopEnd.Value"),
            SampleOffset = fields.Double("SampleOffset.Value"),
            HiddenLoopStart = fields.Double("HiddenLoopStart.Value"),
            HiddenLoopEnd = fields.Double("HiddenLoopEnd.Value"),
            OutMarker = fields.Double("OutMarker.Value"),
            LoopOn = fields.Bool("LoopOn.Value"),
            Sync = fields.Bool("Sync.Value"),
            HiQ = fields.Bool("HiQ.Value"),
            Fade = fields.Bool("Fade.Value"),
            SampleVolume = fields.Float("SampleVolume.Value"),
            VelocityAmount = fields.Float("VelocityAmount.Value"),
            PitchCoarse = fields.Float("PitchCoarse.Value"),
            PitchFine = fields.Float("PitchFine.Value"),
            ColorIndex = fields.Int32("ColorIndex.Value"),
            LaunchMode = fields.Int32("LaunchMode.Value"),
            LaunchQuantisation = fields.Int32("LaunchQuantisation.Value"),
        };

        var warp = new Warp
        {
            IsWarped = fields.Bool("IsWarped.Value"),
            Mode = (WarpMode)fields.Int32("WarpMode.Value"),
            Markers = fields.List(
                "WarpMarkers",
                path => new WarpMarker(
                    fields.Double($"{path}.SecTime"),
                    fields.Double($"{path}.BeatTime")
                )
            ),
            MarkersGenerated = fields.Bool("MarkersGenerated.Value"),
            TimeSignature = new TimeSignature(
                (int)fields.Float("TimeSignature.Numerator.Value"),
                (int)fields.Float("TimeSignature.Denominator.Value"),
                fields.Double("TimeSignature.Time.Value")
            ),
            TransientResolution = fields.Int32("TransientResolution.Value"),
            TransientLoopMode = fields.Int32("TransientLoopMode.Value"),
            TransientEnvelope = fields.Float("TransientEnvelope.Value"),
            GranularityTones = fields.Float("GranularityTones.Value"),
            GranularityTexture = fields.Float("GranularityTexture.Value"),
            FluctuationTexture = fields.Float("FluctuationTexture.Value"),
            ComplexProFormants = fields.Float("ComplexProFormants.Value"),
            ComplexProEnvelope = fields.Float("ComplexProEnvelope.Value"),
        };

        var positions = fields.Array<int>("OnSets.Positions");
        var energies = fields.Array<float>("OnSets.TransitionEnergies");
        var channels = fields.Int32("OverView.ChannelCount");

        var audio = new AudioAnalysis
        {
            Overview = new SampleOverview
            {
                Levels = fields.List(
                    "OverView.OverViewLevels",
                    path => (ReadOnlyMemory<Half>)fields.Array<Half>($"{path}.InterleavedBinData")
                ),
                SamplesPerBinLog2 = fields.Int32("OverView.SamplesPerBinLog2"),
                ChannelCount = channels,
            },
            HeadTable = scan.HeadTable,
            Transients =
            [
                .. positions
                    .Zip(energies)
                    .Select(transient => new Transient(transient.First, transient.Second)),
            ],
            TransientsVersion = fields.Int32("OnSets.Version"),
            HasUserOnsets = fields.Bool("UserOnsets.HasUserOnsets.Value"),
            UserOnsets = fields.UserOnsets("UserOnsets.UserOnsets"),
            TempoEstimate = fields.Bool("AufTaktData.IsSet")
                ? new TempoEstimate(
                    fields.Array<byte>("AufTaktData.PreprocessedDataChunk"),
                    fields.Double("AufTaktData.UnbiasedTempoEstimate")
                )
                : null,
            OriginalFileSize = fields.Int32("OriginalFileSize.Value"),
        };

        if (fields.Missing is { } missing)
        {
            reason = $"{missing} was not found: the file is cut short, or laid out differently";

            return false;
        }

        if (positions.Length != energies.Length)
        {
            reason = "the transients' positions and energies differ in number";

            return false;
        }

        if (channels < 1)
        {
            reason = "the overview has no channels";

            return false;
        }

        analysis = new AnalysisFile
        {
            Clip = clip,
            Warp = warp,
            Audio = audio,
            IsDefaultClipSaved = scan.Saved,
        };

        reason = null;

        return true;
    }

    /// <summary>Reads the head and the schema, and walks the instance to find where each value is.</summary>
    internal static bool TryScan(ReadOnlySpan<byte> asd, [NotNullWhen(true)] out Scan? scan)
    {
        scan = null;

        if (
            !TryReadSchema(asd, out var types, out var sampleData, out var instance)
            || !TryReadHead(asd, sampleData, out var saved, out var table)
        )
            return false;

        scan = new Scan(saved, table, Walk(asd, types, instance));

        return true;
    }

    private static bool TryReadSchema(
        ReadOnlySpan<byte> asd,
        [NotNullWhen(true)] out Dictionary<string, SchemaField[]?>? types,
        out int sampleData,
        out int instance
    )
    {
        types = null;
        instance = 0;
        sampleData = asd.IndexOf(SampleDataTag);

        if (sampleData < 0)
            return false;

        var p = sampleData + SampleDataTag.Length;

        if (!TryReadInt32(asd, ref p, out var typeCount) || typeCount is < 1 or > MaxTypeCount)
            return false;

        types = [];

        for (var t = 0; t < typeCount; t++)
        {
            if (
                !TryReadClassName(asd, ref p, out var name)
                || !TryReadInt32(asd, ref p, out var fieldCount)
            )
                return false;

            if (fieldCount < 0)
            {
                types[name] = null;

                continue;
            }

            var fields = new SchemaField[fieldCount];

            for (var f = 0; f < fieldCount; f++)
            {
                if (!TryReadField(asd, ref p, out fields[f]))
                    return false;
            }

            types[name] = fields;
        }

        instance = p;

        return true;
    }

    /// <summary>
    /// The byte ending the head, 1 once <em>Save Default Clip</em> has been pressed, and the head's
    /// table. Found by walking the table, and only trusted if that walk lands on the chunk holding the clip.
    /// </summary>
    private static bool TryReadHead(
        ReadOnlySpan<byte> asd,
        int sampleData,
        out bool saved,
        out int[] table
    )
    {
        saved = false;
        table = [];

        var p = HeadCountOffset;

        if (!TryReadInt32(asd, ref p, out var count) || count < 0)
            return false;

        var trailer = HeadTableOffset + (long)count * sizeof(int);

        // After the table come 0, 0, 100, then a byte count (4 in a stereo file, 2 in a mono one)
        // and that many bytes.
        if (
            trailer + HeadTrailerStart.Length > asd.Length
            || !asd.Slice((int)trailer, HeadTrailerStart.Length).SequenceEqual(HeadTrailerStart)
        )
            return false;

        p = (int)trailer + HeadTrailerStart.Length;

        if (!TryReadInt32(asd, ref p, out var length) || length < 0)
            return false;

        var flag = (long)p + length;

        if (
            flag + 1 + ChunkPreambleLength != sampleData
            || !asd.Slice((int)flag + 1, ChunkMagic.Length).SequenceEqual(ChunkMagic)
            || asd[(int)flag] is not (0 or 1)
        )
            return false;

        saved = asd[(int)flag] is 1;

        table = MemoryMarshal
            .Cast<byte, int>(asd.Slice(HeadTableOffset, count * sizeof(int)))
            .ToArray();

        return true;
    }

    private static bool TryReadField(ReadOnlySpan<byte> asd, ref int p, out SchemaField field)
    {
        field = default;

        if (!TryReadUtf16(asd, ref p, out var name) || p >= asd.Length)
            return false;

        var code = asd[p++];

        if (code is not ClassCode)
        {
            field = new SchemaField(name, code, null);

            return true;
        }

        if (p >= asd.Length)
            return false;

        int length = asd[p++];

        if (p + length > asd.Length)
            return false;

        field = new SchemaField(name, code, Encoding.ASCII.GetString(asd.Slice(p, length)));
        p += length;

        return true;
    }

    /// <summary>
    /// Walks the instance field by field and records where each value is, keyed by dotted path with
    /// list and array elements indexed. Stops at the first value it cannot read, keeping what it has.
    /// </summary>
    private static Layout Walk(
        ReadOnlySpan<byte> asd,
        Dictionary<string, SchemaField[]?> types,
        int instance
    )
    {
        var layout = new Layout();

        if (!types.TryGetValue(SampleDataClass, out var fields) || fields is null)
            return layout;

        var p = instance;

        foreach (var field in fields)
        {
            if (!TryWalk(asd, types, field, field.Name, ref p, layout))
                break;
        }

        return layout;
    }

    private static bool TryWalk(
        ReadOnlySpan<byte> asd,
        Dictionary<string, SchemaField[]?> types,
        SchemaField field,
        string path,
        ref int p,
        Layout layout
    )
    {
        if (field.Class is not { } name)
            return TryWalkValue(asd, field.Code, path, ref p, layout);

        if (!types.TryGetValue(name, out var nested))
            return false;

        if (nested is null)
            return name is RemoteableArrayClass
                ? TryWalkArray(asd, types, path, ref p, layout)
                : TryWalkList(asd, types, path, ref p, layout);

        return TryWalkElement(asd, types, nested, path, ref p, layout);
    }

    private static bool TryWalkValue(
        ReadOnlySpan<byte> asd,
        byte code,
        string path,
        ref int p,
        Layout layout
    )
    {
        if (SizeOf(code) is { } size)
        {
            if (p + size > asd.Length)
                return false;

            layout.Values[path] = p;
            p += size;

            return true;
        }

        var start = p;

        if (
            ElementSizeOf(code) is not { } element
            || !TryReadInt32(asd, ref p, out var count)
            || count < 0
            || p + (long)count * element > asd.Length
        )
            return false;

        layout.Arrays[path] = new ArrayField(start, p, count, p + count * element);
        p += count * element;

        return true;
    }

    /// <summary>A next id, then each element's class name, id and fields, then an empty name.</summary>
    private static bool TryWalkList(
        ReadOnlySpan<byte> asd,
        Dictionary<string, SchemaField[]?> types,
        string path,
        ref int p,
        Layout layout
    )
    {
        var next = p;
        List<int> ids = [];

        if (!TryReadInt32(asd, ref p, out _))
            return false;

        for (var i = 0; ; i++)
        {
            if (!TryReadClassName(asd, ref p, out var element))
                return false;

            if (element.Length is 0)
            {
                layout.Counts[path] = i;
                layout.Ids[path] = new ListIds(next, [.. ids]);

                return true;
            }

            ids.Add(p);

            if (
                !types.TryGetValue(element, out var fields)
                || fields is null
                || !TryReadInt32(asd, ref p, out _)
                || !TryWalkElement(asd, types, fields, $"{path}[{i}]", ref p, layout)
            )
                return false;
        }
    }

    /// <summary>A count, the element class name once, then the elements packed.</summary>
    private static bool TryWalkArray(
        ReadOnlySpan<byte> asd,
        Dictionary<string, SchemaField[]?> types,
        string path,
        ref int p,
        Layout layout
    )
    {
        var start = p;

        if (
            !TryReadInt32(asd, ref p, out var count)
            || count < 0
            || !TryReadClassName(asd, ref p, out var element)
            || !types.TryGetValue(element, out var fields)
            || fields is null
        )
            return false;

        layout.Counts[path] = count;

        var offset = p;

        // Packed fixed-width elements are stepped over whole; nothing reads them one by one.
        if (fields.All(field => field.Class is null && SizeOf(field.Code) is not null))
        {
            var size = fields.Sum(field => SizeOf(field.Code)!.Value);

            if (p + (long)count * size > asd.Length)
                return false;

            p += count * size;
        }
        else
        {
            for (var i = 0; i < count; i++)
            {
                if (!TryWalkElement(asd, types, fields, $"{path}[{i}]", ref p, layout))
                    return false;
            }
        }

        layout.Arrays[path] = new ArrayField(start, offset, count, p);

        return true;
    }

    private static bool TryWalkElement(
        ReadOnlySpan<byte> asd,
        Dictionary<string, SchemaField[]?> types,
        SchemaField[] fields,
        string path,
        ref int p,
        Layout layout
    )
    {
        foreach (var field in fields)
        {
            if (!TryWalk(asd, types, field, $"{path}.{field.Name}", ref p, layout))
                return false;
        }

        return true;
    }

    private static int? ElementSizeOf(byte code) =>
        code switch
        {
            ByteArrayCode => sizeof(byte),
            HalfArrayCode => sizeof(short),
            Int32ArrayCode => sizeof(int),
            FloatArrayCode => sizeof(float),
            _ => null,
        };

    private static int? SizeOf(byte code) =>
        code switch
        {
            BoolCode => sizeof(bool),
            Int32Code => sizeof(int),
            FloatCode => sizeof(float),
            DoubleCode => sizeof(double),
            _ => null,
        };

    private static bool TryReadInt32(ReadOnlySpan<byte> asd, ref int p, out int value)
    {
        value = 0;

        if (p + sizeof(int) > asd.Length)
            return false;

        value = BinaryPrimitives.ReadInt32LittleEndian(asd[p..]);
        p += sizeof(int);

        return true;
    }

    /// <summary>A big-endian length and the ASCII name that follows it.</summary>
    private static bool TryReadClassName(
        ReadOnlySpan<byte> asd,
        ref int p,
        [NotNullWhen(true)] out string? name
    )
    {
        name = null;

        if (p + sizeof(ushort) > asd.Length)
            return false;

        int length = BinaryPrimitives.ReadUInt16BigEndian(asd[p..]);

        if (p + sizeof(ushort) + length > asd.Length)
            return false;

        name = Encoding.ASCII.GetString(asd.Slice(p + sizeof(ushort), length));
        p += sizeof(ushort) + length;

        return true;
    }

    /// <summary>A little-endian character count and the UTF-16 name that follows it.</summary>
    private static bool TryReadUtf16(
        ReadOnlySpan<byte> asd,
        ref int p,
        [NotNullWhen(true)] out string? name
    )
    {
        name = null;

        if (!TryReadInt32(asd, ref p, out var characters) || characters < 0)
            return false;

        var length = characters * sizeof(char);

        if (p + length > asd.Length)
            return false;

        name = Encoding.Unicode.GetString(asd.Slice(p, length));
        p += length;

        return true;
    }

    private readonly record struct SchemaField(string Name, byte Code, string? Class);

    /// <summary>What a scan found: the saved byte, the head's table, and the layout.</summary>
    internal sealed record Scan(bool Saved, int[] HeadTable, Layout Layout)
    {
        /// <summary>The audio's length in frames, the table's last entry; null for an empty table.</summary>
        public int? Frames => HeadTable is [.., var last] ? last : null;
    }

    /// <summary>Where each value, array and list of the instance is, keyed by dotted path.</summary>
    internal sealed class Layout
    {
        public Dictionary<string, int> Values { get; } = [];
        public Dictionary<string, ArrayField> Arrays { get; } = [];
        public Dictionary<string, int> Counts { get; } = [];
        public Dictionary<string, ListIds> Ids { get; } = [];
    }

    /// <summary>Where a list keeps its next id and each element's id.</summary>
    internal readonly record struct ListIds(int Next, int[] Elements);

    /// <summary>
    /// An array from its count, at <see cref="Start"/>, to one past its last byte, at <see cref="End"/>;
    /// the elements begin at <see cref="Offset"/>.
    /// </summary>
    internal readonly record struct ArrayField(int Start, int Offset, int Count, int End);

    /// <summary>
    /// Reads values by path, noting rather than stopping at one the walk did not find, so a whole
    /// model can be read before it is checked.
    /// </summary>
    private sealed class Fields(byte[] asd, Layout layout)
    {
        /// <summary>A packed <c>OnsetEvent</c>: <c>Time</c> and <c>Energy</c> as doubles, then <c>IsVolatile</c>.</summary>
        private const int OnsetEventSize = sizeof(double) + sizeof(double) + sizeof(bool);

        /// <summary>The first path the walk did not find, or null when every one was there.</summary>
        public string? Missing { get; private set; }

        public bool Bool(string path) => Value(path, sizeof(bool), bytes => bytes[0] is not 0);

        public int Int32(string path) =>
            Value(path, sizeof(int), BinaryPrimitives.ReadInt32LittleEndian);

        public float Float(string path) =>
            Value(path, sizeof(float), BinaryPrimitives.ReadSingleLittleEndian);

        public double Double(string path) =>
            Value(path, sizeof(double), BinaryPrimitives.ReadDoubleLittleEndian);

        public T[] Array<T>(string path)
            where T : unmanaged =>
            Bytes(path) is { } bytes ? MemoryMarshal.Cast<byte, T>(bytes).ToArray() : [];

        public List<T> List<T>(string path, Func<string, T> element)
        {
            if (!layout.Counts.TryGetValue(path, out var count))
            {
                Missing ??= path;

                return [];
            }

            return [.. Enumerable.Range(0, count).Select(i => element($"{path}[{i}]"))];
        }

        public List<UserOnset> UserOnsets(string path)
        {
            if (Bytes(path) is not { } bytes || bytes.Length % OnsetEventSize is not 0)
            {
                Missing ??= path;

                return [];
            }

            List<UserOnset> onsets = [];

            for (var p = 0; p < bytes.Length; p += OnsetEventSize)
            {
                onsets.Add(
                    new UserOnset(
                        BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(p)),
                        BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(p + sizeof(double))),
                        bytes[p + 2 * sizeof(double)] is not 0
                    )
                );
            }

            return onsets;
        }

        private byte[]? Bytes(string path)
        {
            if (layout.Arrays.TryGetValue(path, out var array))
                return asd[array.Offset..array.End];

            Missing ??= path;

            return null;
        }

        private T Value<T>(string path, int size, Func<ReadOnlySpan<byte>, T> read)
        {
            if (layout.Values.TryGetValue(path, out var offset) && offset + size <= asd.Length)
                return read(asd.AsSpan(offset, size));

            Missing ??= path;

            return default!;
        }
    }
}
