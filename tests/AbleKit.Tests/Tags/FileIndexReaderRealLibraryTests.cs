using AbleKit.Tags;

namespace AbleKit.Tests.Tags;

/// <summary>Live's own index on this machine, read for a real library (see <see cref="RealLibrary"/>).</summary>
public sealed class FileIndexReaderRealLibraryTests
{
    [Fact]
    public void Reads_every_real_folder_while_Live_is_running()
    {
        foreach (var folder in Folders())
        {
            var info = Read(folder);

            Assert.NotEmpty(info.Entries);
            Assert.All(info.Entries, e => Assert.NotEmpty(e.RelativePath));
            Assert.All(info.Entries, e => Assert.NotEmpty(e.Keywords));
        }
    }

    [Fact]
    public void Finds_an_entry_by_absolute_path_as_well_as_relative()
    {
        foreach (var folder in Folders())
        {
            var info = Read(folder);
            var entry = info.Entries[0];

            Assert.True(info.TryGet(entry.RelativePath, out var byRelative));
            Assert.True(info.TryGet(Path.Combine(folder, entry.RelativePath), out var byAbsolute));
            Assert.Equal(byRelative.RelativePath, byAbsolute.RelativePath);
        }
    }

    [Fact]
    public void Live_knows_every_key_tag_as_KeyTags_spells_it()
    {
        SkipUnlessIndexed();

        var known = new FileIndexReader(FileIndexReader.DefaultFolder).ReadKnownKeywords();

        Assert.All(
            KeyTags.Tonics.Concat(KeyTags.Modes),
            value => Assert.Contains($"Key|{value}", known)
        );
    }

    [Fact]
    public void Every_keyword_in_a_real_folder_is_a_known_one()
    {
        var folders = Folders();
        var known = new FileIndexReader(FileIndexReader.DefaultFolder).ReadKnownKeywords();

        Assert.All(
            folders.SelectMany(folder => Read(folder).Entries).SelectMany(e => e.Keywords),
            keyword => Assert.Contains(keyword, known)
        );
    }

    [Fact]
    public void A_folder_Ableton_has_never_been_shown_reads_empty()
    {
        SkipUnlessIndexed();

        Assert.Empty(Read(Path.GetTempPath()).Entries);
    }

    [Fact]
    public void An_absent_index_reads_empty_rather_than_throwing()
    {
        var reader = new FileIndexReader(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
        );

        Assert.Empty(reader.Read(Path.GetTempPath()).Entries);
    }

    private static FolderInfo Read(string folder) =>
        new FileIndexReader(FileIndexReader.DefaultFolder).Read(folder);

    private static IReadOnlyList<string> Folders()
    {
        SkipUnlessIndexed();

        return RealLibrary.SampleFolders();
    }

    private static void SkipUnlessIndexed() =>
        Assert.SkipUnless(
            Directory.Exists(FileIndexReader.DefaultFolder),
            "Live's index is not present on this machine."
        );
}
