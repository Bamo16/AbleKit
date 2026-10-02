using AbleKit.Tags;

namespace AbleKit.Tests.Tags;

/// <summary>The XMP contract, asserted against a real library (see <see cref="RealLibrary"/>).</summary>
public sealed class FolderInfoRealLibraryTests
{
    [Fact]
    public void Reads_every_entry_in_a_real_folder()
    {
        foreach (var folder in RealLibrary.SampleFolders())
        {
            var info = new FolderInfoReader().Read(folder);

            Assert.NotEmpty(info.Entries);
            Assert.All(info.Entries, e => Assert.NotEmpty(e.RelativePath));
            Assert.All(info.Entries, e => Assert.NotEmpty(e.Keywords));
        }
    }

    [Fact]
    public void A_name_in_two_folders_still_means_its_own_folders_file()
    {
        var folders = RealLibrary.SampleFolders();
        Assert.SkipWhen(folders.Count < 2, "Needs two real sample folders.");

        // The two folders' stores have different UUIDs; the reader keys on the containing folder.
        var (first, second) = (folders[0], folders[1]);
        var reader = new FolderInfoReader();
        var (one, two) = (reader.Read(first), reader.Read(second));

        foreach (
            var shared in one
                .Entries.Select(e => e.RelativePath)
                .Intersect(
                    two.Entries.Select(e => e.RelativePath),
                    StringComparer.OrdinalIgnoreCase
                )
        )
        {
            Assert.True(one.TryGet(Path.Combine(first, shared), out _));
            Assert.False(one.TryGet(Path.Combine(second, shared), out _));
            Assert.False(two.TryGet(Path.Combine(first, shared), out _));
        }
    }
}
