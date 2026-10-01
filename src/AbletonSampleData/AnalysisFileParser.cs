using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace AbletonSampleData;

/// <summary>
/// Reads the head and warp section of an analysis file, which is undocumented and described by a
/// type schema the file carries.
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
    private const int HeadTrailerLength = 20;
    private const int ChunkPreambleLength = 9;

    private const int MaxTypeCount = 256;

    private const string IsWarpedPath = "IsWarped.Value";
    private const string ModePath = "WarpMode.Value";
    private const string NumeratorPath = "TimeSignature.Numerator.Value";
    private const string DenominatorPath = "TimeSignature.Denominator.Value";
    private const string LoopStartPath = "LoopStart.Value";
    private const string LoopEndPath = "LoopEnd.Value";
    private const string SampleOffsetPath = "SampleOffset.Value";
    private const string OutMarkerPath = "OutMarker.Value";
    private const string LoopOnPath = "LoopOn.Value";
    private const string MarkersPath = "WarpMarkers";
    private const string OverviewBinPath = "OverView.SamplesPerBinLog2";
    private const string OverviewChannelsPath = "OverView.ChannelCount";
    private const string OverviewFinestPath = "OverView.OverViewLevels[0].InterleavedBinData";
    private const string SampleDataClass = "SampleData";
    private const string RemoteableArrayClass = "RemoteableArray";

    private static readonly byte[] ChunkMagic = [0xAB, 0x1E, 0x56, 0x78];
    private static readonly byte[] SampleDataTag = [0x00, 0x0A, .. "SampleData"u8];

    internal static bool TryParse(
        ReadOnlySpan<byte> asd,
        [NotNullWhen(true)] out AnalysisFile? warp
    )
    {
        warp = null;

        if (
            !TryReadSchema(asd, out var types, out var sampleData, out var instance)
            || !TryReadSavedFlag(asd, sampleData, out var saved)
        )
            return false;

        var layout = Walk(asd, types, instance);
        var leaves = new LeafReader(asd, layout.Values);

        if (
            ReadMarkers(asd, layout) is not { } markers
            || !leaves.TryBool(IsWarpedPath, out var isWarped)
            || !leaves.TryInt32(ModePath, out var mode)
            || !leaves.TryFloat(NumeratorPath, out var numerator)
            || !leaves.TryFloat(DenominatorPath, out var denominator)
            || !leaves.TryDouble(LoopStartPath, out var loopStart)
            || !leaves.TryDouble(LoopEndPath, out var loopEnd)
            || !leaves.TryDouble(SampleOffsetPath, out var sampleOffset)
            || !leaves.TryDouble(OutMarkerPath, out var outMarker)
            || !leaves.TryBool(LoopOnPath, out var loopOn)
        )
            return false;

        warp = new AnalysisFile(
            isWarped,
            (WarpMode)mode,
            (int)numerator,
            (int)denominator,
            markers,
            (saved, loopOn) switch
            {
                (false, _) => null,
                // With Loop on, the loop fields hold the loop, not the clip's start and end.
                (true, true) => new DefaultClip(loopStart + sampleOffset, outMarker),
                (true, false) => new DefaultClip(loopStart, loopEnd),
            },
            ReadOverview(asd, layout)
        );

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
    /// The byte ending the head: 1 once <em>Save Default Clip</em> has been pressed. Found by walking
    /// the head's table, and only trusted if that walk lands on the chunk holding the clip.
    /// </summary>
    private static bool TryReadSavedFlag(ReadOnlySpan<byte> asd, int sampleData, out bool saved)
    {
        saved = false;

        var p = HeadCountOffset;

        if (!TryReadInt32(asd, ref p, out var count) || count < 0)
            return false;

        var flag = HeadTableOffset + (long)count * sizeof(int) + HeadTrailerLength;

        if (
            flag + 1 + ChunkPreambleLength != sampleData
            || !asd.Slice((int)flag + 1, ChunkMagic.Length).SequenceEqual(ChunkMagic)
            || asd[(int)flag] is not (0 or 1)
        )
            return false;

        saved = asd[(int)flag] is 1;

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

        if (
            ElementSizeOf(code) is not { } element
            || !TryReadInt32(asd, ref p, out var count)
            || count < 0
            || p + (long)count * element > asd.Length
        )
            return false;

        layout.Arrays[path] = (p, count);
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
        if (!TryReadInt32(asd, ref p, out _))
            return false;

        for (var i = 0; ; i++)
        {
            if (!TryReadClassName(asd, ref p, out var element))
                return false;

            if (element.Length is 0)
            {
                layout.Counts[path] = i;

                return true;
            }

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
        if (
            !TryReadInt32(asd, ref p, out var count)
            || count < 0
            || !TryReadClassName(asd, ref p, out var element)
            || !types.TryGetValue(element, out var fields)
            || fields is null
        )
            return false;

        layout.Counts[path] = count;

        // Packed fixed-width elements are stepped over whole; nothing reads them one by one.
        if (fields.All(field => field.Class is null && SizeOf(field.Code) is not null))
        {
            var size = fields.Sum(field => SizeOf(field.Code)!.Value);

            if (p + (long)count * size > asd.Length)
                return false;

            layout.Arrays[path] = (p, count);
            p += count * size;

            return true;
        }

        for (var i = 0; i < count; i++)
        {
            if (!TryWalkElement(asd, types, fields, $"{path}[{i}]", ref p, layout))
                return false;
        }

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

    private static List<WarpMarker>? ReadMarkers(ReadOnlySpan<byte> asd, Layout layout)
    {
        if (!layout.Counts.TryGetValue(MarkersPath, out var count))
            return null;

        var leaves = new LeafReader(asd, layout.Values);
        List<WarpMarker> markers = [];

        for (var i = 0; i < count; i++)
        {
            if (
                !leaves.TryDouble($"{MarkersPath}[{i}].SecTime", out var seconds)
                || !leaves.TryDouble($"{MarkersPath}[{i}].BeatTime", out var beat)
            )
                return null;

            markers.Add(new WarpMarker(seconds, beat));
        }

        return markers;
    }

    /// <summary>The finest overview level's peaks, or null when the walk did not reach it.</summary>
    private static SampleOverview? ReadOverview(ReadOnlySpan<byte> asd, Layout layout)
    {
        var leaves = new LeafReader(asd, layout.Values);

        if (
            !leaves.TryInt32(OverviewBinPath, out var log2)
            || !leaves.TryInt32(OverviewChannelsPath, out var channels)
            || !layout.Arrays.TryGetValue(OverviewFinestPath, out var finest)
            || log2 is < 0 or > 30
            || channels < 1
        )
            return null;

        // Each bin holds a minimum and a maximum per channel, as half-precision floats. Without the
        // sign bit, a half's bits order the same way as its magnitude.
        var perBin = 2 * channels;
        var peaks = new float[finest.Count / perBin];
        var values = MemoryMarshal.Cast<byte, ushort>(
            asd.Slice(finest.Offset, peaks.Length * perBin * sizeof(ushort))
        );

        for (var bin = 0; bin < peaks.Length; bin++)
        {
            ushort loudest = 0;

            foreach (var bits in values.Slice(bin * perBin, perBin))
                loudest = Math.Max(loudest, (ushort)(bits & 0x7FFF));

            peaks[bin] = (float)BitConverter.UInt16BitsToHalf(loudest);
        }

        return new SampleOverview(1 << log2, peaks);
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

    private sealed class Layout
    {
        public Dictionary<string, int> Values { get; } = [];
        public Dictionary<string, (int Offset, int Count)> Arrays { get; } = [];
        public Dictionary<string, int> Counts { get; } = [];
    }

    /// <summary>Reads the fixed-width values the walk found, by path.</summary>
    private readonly ref struct LeafReader(ReadOnlySpan<byte> asd, Dictionary<string, int> leaves)
    {
        private readonly ReadOnlySpan<byte> _asd = asd;
        private readonly Dictionary<string, int> _leaves = leaves;

        public bool TryBool(string path, out bool value) =>
            TryRead(path, bytes => bytes[0] is not 0, out value);

        public bool TryInt32(string path, out int value) =>
            TryRead(path, BinaryPrimitives.ReadInt32LittleEndian, out value);

        public bool TryFloat(string path, out float value) =>
            TryRead(path, BinaryPrimitives.ReadSingleLittleEndian, out value);

        public bool TryDouble(string path, out double value) =>
            TryRead(path, BinaryPrimitives.ReadDoubleLittleEndian, out value);

        private bool TryRead<T>(string path, Func<ReadOnlySpan<byte>, T> read, out T value)
        {
            value = default!;

            if (
                !_leaves.TryGetValue(path, out var offset)
                || offset < 0
                || offset + Unsafe.SizeOf<T>() > _asd.Length
            )
                return false;

            value = read(_asd[offset..]);

            return true;
        }
    }
}
