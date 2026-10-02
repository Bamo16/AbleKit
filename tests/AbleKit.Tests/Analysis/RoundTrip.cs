using System.Buffers.Binary;
using AbleKit.Analysis;

namespace AbleKit.Tests.Analysis;

/// <summary>What writing a file back should give: Live's bytes, with list ids numbered as on a fresh save.</summary>
internal static class RoundTrip
{
    /// <summary>The file with every list's ids renumbered 0, 1, 2… and its next id set to the count.</summary>
    public static byte[] WithFreshIds(byte[] asd)
    {
        Assert.True(AnalysisFileParser.TryScan(asd, out var scan));

        var fresh = asd.ToArray();

        foreach (var list in scan.Layout.Ids.Values)
        {
            BinaryPrimitives.WriteInt32LittleEndian(fresh.AsSpan(list.Next), list.Elements.Length);

            for (var i = 0; i < list.Elements.Length; i++)
                BinaryPrimitives.WriteInt32LittleEndian(fresh.AsSpan(list.Elements[i]), i);
        }

        return fresh;
    }

    /// <summary>Where the first chunk's schema lies: from its type count to the instance.</summary>
    public static (int Start, int End) Schema(byte[] asd)
    {
        Assert.True(AnalysisFileParser.TryScan(asd, out var scan));

        ReadOnlySpan<byte> tag = [0x00, 0x0A, .. "SampleData"u8];

        return (asd.AsSpan().IndexOf(tag) + tag.Length, scan.Layout.Values["LoopStart.Value"]);
    }

    /// <summary>The schema's type definitions, each as its bytes in hex, in a fixed order.</summary>
    public static IReadOnlyList<string> Types(byte[] asd)
    {
        var (start, end) = Schema(asd);
        var p = start;
        var count = Int32(asd, ref p);
        List<string> types = [];

        for (var t = 0; t < count; t++)
        {
            var from = p;
            var nameLength = BinaryPrimitives.ReadUInt16BigEndian(asd.AsSpan(p));
            p += sizeof(ushort) + nameLength;
            var fields = Int32(asd, ref p);

            // A list or array declares a negative count and no fields.
            for (var f = 0; f < fields; f++)
            {
                var characters = Int32(asd, ref p);
                p += characters * sizeof(char);

                if (asd[p++] is 0x00)
                    p += 1 + asd[p];
            }

            types.Add(Convert.ToHexString(asd, from, p - from));
        }

        Assert.Equal(end, p);

        return [.. types.Order(StringComparer.Ordinal)];
    }

    public static byte[] Rewrite(byte[] asd)
    {
        Assert.True(AnalysisFile.TryParse(asd, out var analysis));

        return analysis.ToBytes();
    }

    private static int Int32(byte[] asd, ref int p)
    {
        var value = BinaryPrimitives.ReadInt32LittleEndian(asd.AsSpan(p));
        p += sizeof(int);

        return value;
    }
}
