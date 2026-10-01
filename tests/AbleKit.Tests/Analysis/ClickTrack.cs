using System.Buffers.Binary;
using System.IO.Compression;

namespace AbleKit.Tests.Analysis;

/// <summary>
/// A 16-second click track Live analysed for the fixtures, kept Brotli-compressed, and its samples
/// decoded the way Live reads 16-bit audio.
/// </summary>
internal sealed record ClickTrack(byte[] Wav, float[] Samples, int ChannelCount)
{
    /// <summary>The audio behind <c>sidecar-clicks.asd</c> and <c>sidecar-clicks-unsaved.asd</c>.</summary>
    public static ClickTrack Stereo { get; } = Load("clicks.wav.br");

    /// <summary>The audio behind <c>sidecar-mono.asd</c>.</summary>
    public static ClickTrack Mono { get; } = Load("clicks-mono.wav.br");

    public int Frames => Samples.Length / ChannelCount;

    public static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static ClickTrack Load(string name)
    {
        using var input = new BrotliStream(
            File.OpenRead(Fixture(name)),
            CompressionMode.Decompress
        );
        using var buffer = new MemoryStream();
        input.CopyTo(buffer);

        var wav = buffer.ToArray();
        var channels = 0;
        var data = ReadOnlySpan<byte>.Empty;

        // RIFF, its size and WAVE, then chunks: an id, a size, and the body padded to an even length.
        for (var p = 12; p + 8 <= wav.Length; )
        {
            var size = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(p + 4));
            var body = wav.AsSpan(p + 8, size);

            if (wav.AsSpan(p, 4).SequenceEqual("fmt "u8))
                channels = BinaryPrimitives.ReadInt16LittleEndian(body[2..]);
            else if (wav.AsSpan(p, 4).SequenceEqual("data"u8))
                data = body;

            p += 8 + size + (size & 1);
        }

        var samples = new float[data.Length / sizeof(short)];

        for (var i = 0; i < samples.Length; i++)
            samples[i] =
                BinaryPrimitives.ReadInt16LittleEndian(data[(i * sizeof(short))..]) / 32768f;

        return new ClickTrack(wav, samples, channels);
    }
}
