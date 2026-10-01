using System.IO.Compression;
using AbleKit.Sets;

namespace AbleKit.Tests.Sets;

/// <summary>The author's own sets: looked up in place, and relinked only as copies.</summary>
public sealed class LiveSetsRealLibraryTests : IDisposable
{
    private readonly string _copy = Path.Combine(
        Path.GetTempPath(),
        "AbleKit.Tests",
        Guid.NewGuid().ToString("n")
    );

    public void Dispose()
    {
        if (Directory.Exists(_copy))
            Directory.Delete(_copy, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_sample_a_set_uses_is_found_in_it()
    {
        SkipUnlessPresent();

        var sets = new LiveSets(RealLibrary.ProjectsRoot).Using([
            Path.Combine(RealLibrary.MashupRoot, "Fat Dog - Running (Instrumental - Full).flac"),
        ]);

        Assert.Contains(
            "Hell for Leather (Aiobahn x Fat Dog)",
            sets.Select(Path.GetFileNameWithoutExtension)
        );
    }

    [Fact]
    public void Relinking_a_real_sample_changes_only_the_lines_that_name_it()
    {
        SkipUnlessPresent();

        foreach (
            var set in Directory.EnumerateFiles(
                RealLibrary.ProjectsRoot,
                "*.als",
                SearchOption.AllDirectories
            )
        )
        {
            var relative = Path.GetRelativePath(RealLibrary.ProjectsRoot, set);

            if (relative.Split(Path.DirectorySeparatorChar).Contains("Backup"))
                continue;

            var target = Path.Combine(_copy, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(set, target);
        }

        var from = Path.Combine(
            RealLibrary.MashupRoot,
            "Glen Check - Dazed & Confused (Instrumental - Full).flac"
        );
        var to = Path.Combine(
            RealLibrary.MashupRoot,
            "Renamed & Relinked (Instrumental - Full).flac"
        );
        var copies = new LiveSets(_copy);
        var sets = copies.Using([from]);
        var before = sets.ToDictionary(set => set, Lines);

        var result = copies.Relink([new(from, to)], "test");

        Assert.NotEmpty(sets);
        Assert.Equal(sets, result.Relinked);
        Assert.Empty(result.Failed);
        Assert.Empty(copies.Using([from]));
        Assert.Equal(sets, copies.Using([to]));

        foreach (var set in sets)
        {
            var after = Lines(set);

            Assert.Equal(before[set].Length, after.Length);

            var changed = before[set].Zip(after).Where(pair => pair.First != pair.Second).ToList();

            Assert.NotEmpty(changed);
            Assert.All(
                changed,
                pair =>
                    Assert.Equal(
                        pair.First.Replace(
                            "Glen Check - Dazed &amp; Confused",
                            "Renamed &amp; Relinked"
                        ),
                        pair.Second
                    )
            );
        }
    }

    private static void SkipUnlessPresent() =>
        Assert.SkipUnless(
            Directory.Exists(RealLibrary.ProjectsRoot),
            $"{RealLibrary.ProjectsRoot} is not on this machine."
        );

    private static string[] Lines(string set)
    {
        using var file = File.OpenRead(set);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);

        return reader.ReadToEnd().Split('\n');
    }
}
