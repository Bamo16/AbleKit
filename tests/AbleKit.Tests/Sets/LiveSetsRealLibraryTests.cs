using System.IO.Compression;
using AbleKit.Sets;

namespace AbleKit.Tests.Sets;

/// <summary>Relinking copies of real sets (see <see cref="RealLibrary"/>); the originals are only read.</summary>
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
    public void Relinking_a_real_sample_changes_only_the_lines_that_name_it()
    {
        var projects = RealLibrary.ProjectsFolder();
        CopySets(projects);

        var copies = new LiveSets(_copy);

        // The first sample some set uses, renamed in place.
        var from = RealLibrary.AudioFiles().FirstOrDefault(file => copies.Using([file]).Count > 0);
        Assert.SkipWhen(from is null, "No set uses a sample in the sample folders.");

        var to = Path.Combine(
            Path.GetDirectoryName(from)!,
            $"{Path.GetFileNameWithoutExtension(from)} (Relinked){Path.GetExtension(from)}"
        );
        var sets = copies.Using([from]);
        var before = sets.ToDictionary(set => set, Lines);

        var result = copies.Relink([new(from, to)], "test");

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
                            Escaped(Path.GetFileName(from)),
                            Escaped(Path.GetFileName(to))
                        ),
                        pair.Second
                    )
            );
        }
    }

    /// <summary>Every set but Live's backups, copied with its folders into <see cref="_copy"/>.</summary>
    private void CopySets(string projects)
    {
        foreach (
            var set in Directory.EnumerateFiles(projects, "*.als", SearchOption.AllDirectories)
        )
        {
            var relative = Path.GetRelativePath(projects, set);

            if (relative.Split(Path.DirectorySeparatorChar).Contains("Backup"))
                continue;

            var target = Path.Combine(_copy, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(set, target);
        }
    }

    private static string Escaped(string name) => name.Replace("&", "&amp;");

    private static string[] Lines(string set)
    {
        using var file = File.OpenRead(set);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);

        return reader.ReadToEnd().Split('\n');
    }
}
