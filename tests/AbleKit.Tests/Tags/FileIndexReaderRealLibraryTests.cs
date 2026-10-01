using AbleKit.Tags;

namespace AbleKit.Tests.Tags;

/// <summary>Ableton's own index, asserted against the real database on this machine.</summary>
public sealed class FileIndexReaderRealLibraryTests
{
    public static TheoryData<string> Roots => [RealLibrary.StagingRoot, RealLibrary.MashupRoot];

    [Theory]
    [MemberData(nameof(Roots))]
    public void Reads_a_real_root_while_Live_is_running(string root)
    {
        SkipUnlessPresent(root);

        var info = Read(root);

        Assert.NotEmpty(info.Entries);
        Assert.All(info.Entries, e => Assert.NotEmpty(e.RelativePath));
        Assert.All(info.Entries, e => Assert.NotEmpty(e.Keywords));
    }

    [Theory]
    [MemberData(nameof(Roots))]
    public void Finds_an_entry_by_absolute_path_as_well_as_relative(string root)
    {
        SkipUnlessPresent(root);

        var entry = Read(root).Entries[0];

        Assert.True(Read(root).TryGet(entry.RelativePath, out var byRelative));
        Assert.True(Read(root).TryGet(Path.Combine(root, entry.RelativePath), out var byAbsolute));
        Assert.Equal(byRelative.RelativePath, byAbsolute.RelativePath);
    }

    [Fact]
    public void Live_knows_every_key_tag_as_KeyTags_spells_it()
    {
        Assert.SkipUnless(
            Directory.Exists(FileIndexReader.DefaultFolder),
            "Live's index is not present on this machine."
        );

        var known = new FileIndexReader(FileIndexReader.DefaultFolder).ReadKnownKeywords();

        Assert.All(
            KeyTags.Tonics.Concat(KeyTags.Modes),
            value => Assert.Contains($"Key|{value}", known)
        );
    }

    [Theory]
    [MemberData(nameof(Roots))]
    public void Every_keyword_on_a_real_root_is_a_known_one(string root)
    {
        SkipUnlessPresent(root);

        var known = new FileIndexReader(FileIndexReader.DefaultFolder).ReadKnownKeywords();

        Assert.All(Read(root).Entries.SelectMany(e => e.Keywords), k => Assert.Contains(k, known));
    }

    [Fact]
    public void A_folder_Ableton_has_never_been_shown_reads_empty()
    {
        Assert.SkipUnless(
            Directory.Exists(FileIndexReader.DefaultFolder),
            "Live's index is not present on this machine."
        );

        Assert.Empty(Read(Path.GetTempPath()).Entries);
    }

    [Fact]
    public void An_absent_index_reads_empty_rather_than_throwing()
    {
        var reader = new FileIndexReader(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
        );

        Assert.Empty(reader.Read(RealLibrary.MashupRoot).Entries);
    }

    private static FolderInfo Read(string root) =>
        new FileIndexReader(FileIndexReader.DefaultFolder).Read(root);

    private static void SkipUnlessPresent(string root)
    {
        Assert.SkipUnless(RealLibrary.HasStems(root), $"{root} holds no stems on this machine.");
        Assert.SkipUnless(
            Directory.Exists(FileIndexReader.DefaultFolder),
            "Live's index is not present on this machine."
        );
    }
}
