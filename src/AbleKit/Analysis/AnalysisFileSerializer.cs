using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace AbleKit.Analysis;

/// <summary>
/// Writes an <see cref="AnalysisFile"/> in the layout Live 12 writes, schema included. Fields that
/// held one value in every file measured are written as that value; the model does not carry them.
/// </summary>
internal static class AnalysisFileSerializer
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

    private const int ListFieldCount = -1;
    private const int ArrayFieldCount = -3;

    private const int SampleDataChunk = 365;
    private const int AufTaktChunk = 0;
    private const int OverviewVersion = 2;
    private const int TempoEstimateVersion = 5;
    private const int UnsetTempoEstimateVersion = int.MinValue;

    private const string MarkerClass = "WarpMarker";
    private const string OnsetClass = "OnsetEvent";
    private const string LevelClass = "SampleOverViewLevel";

    private static readonly byte[] HeadStart = [0x06, 0x49];
    private static readonly byte[] ChunkMagic = [0xAB, 0x1E, 0x56, 0x78, 0x05];

    private static readonly SchemaType AufTaktData = new(
        "AufTaktData",
        [
            new("PreprocessedDataChunk", ByteArrayCode),
            new("UnbiasedTempoEstimate", DoubleCode),
            new("IsSet", BoolCode),
            new("Version", Int32Code),
        ]
    );

    /// <summary>Every type a Live 12 file declares, in the order Live declares them.</summary>
    private static readonly SchemaType[] Schema =
    [
        new(
            "SampleData",
            [
                new("LoopStart", "RemoteableDouble"),
                new("LoopEnd", "RemoteableDouble"),
                new("SampleOffset", "RemoteableDouble"),
                new("HiddenLoopStart", "RemoteableDouble"),
                new("HiddenLoopEnd", "RemoteableDouble"),
                new("OutMarker", "RemoteableDouble"),
                new("Sync", "RemoteableBool"),
                new("HiQ", "RemoteableBool"),
                new("Fade", "RemoteableBool"),
                new("IsWarped", "RemoteableBool"),
                new("SampleVolume", "UserFloat"),
                new("VelocityAmount", "UserFloat"),
                new("PitchCoarse", "UserFloat"),
                new("PitchFine", "UserFloat"),
                new("WarpMode", "RemoteableEnum"),
                new("TransientResolution", "RemoteableEnum"),
                new("GranularityTones", "UserFloat"),
                new("GranularityTexture", "UserFloat"),
                new("FluctuationTexture", "UserFloat"),
                new("TransientLoopMode", "RemoteableEnum"),
                new("TransientEnvelope", "UserFloat"),
                new("ComplexProFormants", "UserFloat"),
                new("ComplexProEnvelope", "UserFloat"),
                new("TimeSignature", "RemoteableTimeSignature"),
                new("ColorIndex", "RemoteableInt"),
                new("WarpMarkers", "RemoteableList"),
                new("MarkersGenerated", "RemoteableBool"),
                new("LaunchMode", "RemoteableEnum"),
                new("LoopOn", "RemoteableBool"),
                new("LaunchQuantisation", "RemoteableEnum"),
                new("OnSets", "OnSets"),
                new("UserOnsets", "OnsetArray"),
                new("AufTaktData", "AufTaktData"),
                new("ExtraLength", "RemoteableInt"),
                new("OriginalFileSize", "RemoteableInt"),
                new("OverView", "SampleOverView"),
            ]
        ),
        new("List<SampleOverViewLevel>", ListFieldCount),
        new("TimeSignatureDenominator", [new("Value", FloatCode)]),
        new("RemoteableDouble", [new("Value", DoubleCode)]),
        new("RemoteableBool", [new("Value", BoolCode)]),
        new("TimeSignatureNumerator", [new("Value", FloatCode)]),
        new("UserFloat", [new("Value", FloatCode)]),
        new("RemoteableEnum", [new("Value", Int32Code)]),
        new(
            "RemoteableTimeSignature",
            [
                new("Numerator", "TimeSignatureNumerator"),
                new("Denominator", "TimeSignatureDenominator"),
                new("Time", "RemoteableDouble"),
            ]
        ),
        new("RemoteableInt", [new("Value", Int32Code)]),
        new("RemoteableList", ListFieldCount),
        // Declared only when a marker is there to need it.
        new(MarkerClass, [new("SecTime", DoubleCode), new("BeatTime", DoubleCode)]),
        new(
            "OnSets",
            [
                new("Positions", Int32ArrayCode),
                new("TransitionEnergies", FloatArrayCode),
                new("IsSet", BoolCode),
                new("Version", Int32Code),
            ]
        ),
        new(
            "OnsetArray",
            [new("UserOnsets", "RemoteableArray"), new("HasUserOnsets", "RemoteableBool")]
        ),
        new("RemoteableArray", ArrayFieldCount),
        new(
            OnsetClass,
            [new("Time", DoubleCode), new("Energy", DoubleCode), new("IsVolatile", BoolCode)]
        ),
        AufTaktData,
        new(
            "SampleOverView",
            [
                new("OverViewLevels", "List<SampleOverViewLevel>"),
                new("SamplesPerBinLog2", Int32Code),
                new("ChannelCount", Int32Code),
                new("Version", Int32Code),
            ]
        ),
        new(LevelClass, [new("InterleavedBinData", HalfArrayCode)]),
    ];

    internal static byte[] Serialize(AnalysisFile analysis)
    {
        Validate(analysis);

        var (clip, warp, audio) = (analysis.Clip, analysis.Warp, analysis.Audio);
        var file = new FileWriter();

        // The head: its table, 0, 0, 100, two zero bytes per channel, and the saved byte.
        file.Bytes(HeadStart);
        file.Int32(audio.HeadTable.Count);

        foreach (var entry in audio.HeadTable)
            file.Int32(entry);

        file.Int32(0);
        file.Int32(0);
        file.Int32(100);
        file.Int32(2 * audio.Overview.ChannelCount);
        file.Bytes(new byte[2 * audio.Overview.ChannelCount]);
        file.Bool(analysis.IsDefaultClipSaved);

        file.Bytes(ChunkMagic);
        file.Int32(SampleDataChunk);
        file.ClassName("SampleData");
        WriteSchema(
            file,
            [.. Schema.Where(type => type.Name is not MarkerClass || warp.Markers.Count > 0)]
        );

        file.Double(clip.LoopStart);
        file.Double(clip.LoopEnd);
        file.Double(clip.SampleOffset);
        file.Double(clip.HiddenLoopStart);
        file.Double(clip.HiddenLoopEnd);
        file.Double(clip.OutMarker);
        file.Bool(clip.Sync);
        file.Bool(clip.HiQ);
        file.Bool(clip.Fade);
        file.Bool(warp.IsWarped);
        file.Float(clip.SampleVolume);
        file.Float(clip.VelocityAmount);
        file.Float(clip.PitchCoarse);
        file.Float(clip.PitchFine);
        file.Int32((int)warp.Mode);
        file.Int32(warp.TransientResolution);
        file.Float(warp.GranularityTones);
        file.Float(warp.GranularityTexture);
        file.Float(warp.FluctuationTexture);
        file.Int32(warp.TransientLoopMode);
        file.Float(warp.TransientEnvelope);
        file.Float(warp.ComplexProFormants);
        file.Float(warp.ComplexProEnvelope);
        file.Float(warp.TimeSignature.Numerator);
        file.Float(warp.TimeSignature.Denominator);
        file.Double(warp.TimeSignature.Time);
        file.Int32(clip.ColorIndex);

        // A list: the next id, then each element's class name, id and fields, then an empty name.
        // Ids are numbered afresh, as Live numbers a warp it has just made.
        file.Int32(warp.Markers.Count);

        for (var i = 0; i < warp.Markers.Count; i++)
        {
            file.ClassName(MarkerClass);
            file.Int32(i);
            file.Double(warp.Markers[i].Seconds);
            file.Double(warp.Markers[i].Beat);
        }

        file.ClassName(string.Empty);

        file.Bool(warp.MarkersGenerated);
        file.Int32(clip.LaunchMode);
        file.Bool(clip.LoopOn);
        file.Int32(clip.LaunchQuantisation);

        file.Array([.. audio.Transients.Select(transient => transient.Position)]);
        file.Array([.. audio.Transients.Select(transient => transient.Energy)]);
        file.Bool(true);
        file.Int32(audio.TransientsVersion);

        // An array of classes: the count, the element's class name even when empty, then the elements packed.
        file.Int32(audio.UserOnsets.Count);
        file.ClassName(OnsetClass);

        foreach (var onset in audio.UserOnsets)
        {
            file.Double(onset.Time);
            file.Double(onset.Energy);
            file.Bool(onset.IsVolatile);
        }

        file.Bool(audio.HasUserOnsets);

        WriteTempoEstimate(file, audio.TempoEstimate);

        file.Int32(0);
        file.Int32(audio.OriginalFileSize);

        file.Int32(audio.Overview.Levels.Count);

        for (var i = 0; i < audio.Overview.Levels.Count; i++)
        {
            file.ClassName(LevelClass);
            file.Int32(i);
            file.Array(audio.Overview.Levels[i].Span);
        }

        file.ClassName(string.Empty);

        file.Int32(audio.Overview.SamplesPerBinLog2);
        file.Int32(audio.Overview.ChannelCount);
        file.Int32(OverviewVersion);

        // The second chunk repeats the tempo estimate under its own schema.
        file.Bytes(ChunkMagic);
        file.Int32(AufTaktChunk);
        file.ClassName(AufTaktData.Name);
        WriteSchema(file, [AufTaktData]);
        WriteTempoEstimate(file, audio.TempoEstimate);

        return file.ToArray();
    }

    private static void WriteTempoEstimate(FileWriter file, TempoEstimate? estimate)
    {
        file.Array(estimate is { } set ? set.PreprocessedData.Span : []);
        file.Double(estimate?.Bpm ?? double.MaxValue);
        file.Bool(estimate is not null);
        file.Int32(estimate is not null ? TempoEstimateVersion : UnsetTempoEstimateVersion);
    }

    private static void WriteSchema(FileWriter file, SchemaType[] types)
    {
        file.Int32(types.Length);

        foreach (var type in types)
        {
            file.ClassName(type.Name);

            if (type.Fields is not { } fields)
            {
                file.Int32(type.FieldCount);

                continue;
            }

            file.Int32(fields.Length);

            foreach (var field in fields)
            {
                file.Int32(field.Name.Length);
                file.Bytes(Encoding.Unicode.GetBytes(field.Name));
                file.Byte(field.Code);

                if (field.Class is not { } name)
                    continue;

                file.Byte((byte)name.Length);
                file.Bytes(Encoding.ASCII.GetBytes(name));
            }
        }
    }

    /// <summary>Refuses what Live could not have written, so a mistake shows here rather than in Live.</summary>
    private static void Validate(AnalysisFile analysis)
    {
        var markers = analysis.Warp.Markers;

        for (var i = 1; i < markers.Count; i++)
        {
            if (
                markers[i].Seconds < markers[i - 1].Seconds
                || markers[i].Beat <= markers[i - 1].Beat
            )
                throw new InvalidOperationException(
                    $"warp marker {i} does not come after the one before it"
                );
        }

        var transients = analysis.Audio.Transients;

        for (var i = 1; i < transients.Count; i++)
        {
            if (transients[i].Position < transients[i - 1].Position)
                throw new InvalidOperationException($"transient {i} is before the one ahead of it");
        }

        if (
            analysis.Audio.Overview
            is not { ChannelCount: >= 1, SamplesPerBinLog2: >= 1 and <= 24 } overview
        )
            throw new InvalidOperationException(
                "the overview's channels or bin size are out of range"
            );

        // Each level has 2^log2 of the previous level's bins, rounded up, down to a single bin.
        var perBin = 2 * overview.ChannelCount;
        var levels = overview.Levels;

        for (var i = 0; i < levels.Count; i++)
        {
            var bins = levels[i].Length / perBin;

            if (levels[i].Length % perBin is not 0 || bins is 0)
                throw new InvalidOperationException($"overview level {i} is not whole bins");

            if (i > 0 && bins != Ceiling(levels[i - 1].Length / perBin, overview.SamplesPerBin))
                throw new InvalidOperationException(
                    $"overview level {i} is not the size Live draws"
                );
        }

        if (levels is not [.., { } last] || last.Length != perBin)
            throw new InvalidOperationException("the overview does not end in a single bin");
    }

    private static int Ceiling(int value, int divisor) => (value + divisor - 1) / divisor;

    private sealed record SchemaField(string Name, byte Code, string? Class = null)
    {
        public SchemaField(string name, string @class)
            : this(name, ClassCode, @class) { }
    }

    /// <summary>A declared type: its fields, or, for a list or array, the negative count marking it.</summary>
    private sealed record SchemaType(string Name, SchemaField[]? Fields, int FieldCount = 0)
    {
        public SchemaType(string name, int fieldCount)
            : this(name, null, fieldCount) { }
    }

    /// <summary>Little-endian values, but class names with a big-endian length, as the format has them.</summary>
    private sealed class FileWriter
    {
        private readonly MemoryStream _stream = new();

        public byte[] ToArray() => _stream.ToArray();

        public void Byte(byte value) => _stream.WriteByte(value);

        public void Bytes(ReadOnlySpan<byte> bytes) => _stream.Write(bytes);

        public void Bool(bool value) => Byte(value ? (byte)1 : (byte)0);

        public void Int32(int value) => Fixed(value, BinaryPrimitives.WriteInt32LittleEndian);

        public void Float(float value) => Fixed(value, BinaryPrimitives.WriteSingleLittleEndian);

        public void Double(double value) => Fixed(value, BinaryPrimitives.WriteDoubleLittleEndian);

        /// <summary>An array as the file stores it: an int32 count, then the values.</summary>
        public void Array<T>(ReadOnlySpan<T> values)
            where T : unmanaged
        {
            Int32(values.Length);
            Bytes(MemoryMarshal.AsBytes(values));
        }

        public void ClassName(string name)
        {
            Span<byte> length = stackalloc byte[sizeof(ushort)];
            BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)name.Length);
            Bytes(length);
            Bytes(Encoding.ASCII.GetBytes(name));
        }

        private void Fixed<T>(T value, SpanWriter<T> write)
            where T : unmanaged
        {
            Span<byte> bytes = stackalloc byte[Unsafe.SizeOf<T>()];
            write(bytes, value);
            Bytes(bytes);
        }

        private delegate void SpanWriter<in T>(Span<byte> destination, T value);
    }
}
