namespace AbletonSampleData.Tests;

/// <summary>The XMP contract, asserted against both real roots.</summary>
public class FolderInfoRealLibraryTests
{
    private const string StagingRoot = @"P:\RYAN\Ableton\Sample Staging";
    private const string MashupRoot = @"P:\RYAN\Ableton\Mashup Samples";

    public static TheoryData<string> Roots => [StagingRoot, MashupRoot];

    [Theory]
    [MemberData(nameof(Roots))]
    public void Reads_every_entry_in_a_real_root(string root)
    {
        Assert.SkipUnless(Directory.Exists(root), $"{root} not present on this machine.");

        var info = new FolderInfoReader().Read(root);

        Assert.NotEmpty(info.Entries);
        Assert.All(info.Entries, e => Assert.NotEmpty(e.RelativePath));
        Assert.All(info.Entries, e => Assert.NotEmpty(e.Keywords));
    }

    [Fact]
    public void A_name_under_two_roots_still_means_its_own_roots_file()
    {
        Assert.SkipUnless(
            Directory.Exists(StagingRoot) && Directory.Exists(MashupRoot),
            "Both Ableton roots must be present."
        );

        // The two roots' stores have different UUIDs; the reader keys on the containing root.
        var reader = new FolderInfoReader();
        var staging = reader.Read(StagingRoot);
        var mashup = reader.Read(MashupRoot);

        // A name can be under both roots on purpose; it must mean its own root's file.
        foreach (
            var shared in staging
                .Entries.Select(e => e.RelativePath)
                .Intersect(
                    mashup.Entries.Select(e => e.RelativePath),
                    StringComparer.OrdinalIgnoreCase
                )
        )
        {
            Assert.True(staging.TryGet(Path.Combine(StagingRoot, shared), out _));
            Assert.False(staging.TryGet(Path.Combine(MashupRoot, shared), out _));
            Assert.False(mashup.TryGet(Path.Combine(StagingRoot, shared), out _));
        }
    }
}
