using System.Xml;
using AbleKit.Tags;

namespace AbleKit.Tests.Tags;

public sealed class FolderInfoFileTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "AbleKit.Tests",
        "abletests-" + Guid.NewGuid().ToString("n")
    );

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    private string Store(string name, string content, DateTime writtenUtc)
    {
        var directory = Path.Combine(_root, FolderInfoFile.Directory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, writtenUtc);

        return path;
    }

    [Fact]
    public void A_folder_with_no_store_directory_has_no_stores()
    {
        Directory.CreateDirectory(_root);

        Assert.Empty(FolderInfoFile.All(_root));
        Assert.Null(FolderInfoFile.In(_root));
    }

    [Fact]
    public void Stores_are_listed_newest_first_and_the_newest_is_the_live_one()
    {
        var old = Store("old.xmp", "<a/>", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var recent = Store(
            "recent.xmp",
            "<a/>",
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)
        );

        Assert.Equal([recent, old], FolderInfoFile.All(_root));
        Assert.Equal(recent, FolderInfoFile.In(_root));
    }

    [Fact]
    public void Only_xmp_files_count_as_stores()
    {
        var store = Store("a.xmp", "<a/>", DateTime.UtcNow);
        Store("a.txt", "<a/>", DateTime.UtcNow);

        Assert.Equal([store], FolderInfoFile.All(_root));
    }

    [Fact]
    public void A_well_formed_store_loads()
    {
        var path = Store("a.xmp", "<root><child/></root>", DateTime.UtcNow);

        var document = FolderInfoFile.Load(path);

        Assert.Equal("root", document.Root?.Name.LocalName);
        Assert.Single(document.Root!.Elements());
    }

    [Fact]
    public void A_store_cut_off_mid_write_throws_that_it_changed_and_keeps_the_XML_error()
    {
        var path = Store("a.xmp", "<root><child>", DateTime.UtcNow);

        var ex = Assert.Throws<TagStoreChangedException>(() => FolderInfoFile.Load(path));

        Assert.IsType<XmlException>(ex.InnerException);
    }

    [Fact]
    public void A_store_that_went_away_throws_that_it_changed_and_keeps_the_missing_file_error()
    {
        var path = Path.Combine(_root, FolderInfoFile.Directory, "gone.xmp");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var ex = Assert.Throws<TagStoreChangedException>(() => FolderInfoFile.Load(path));

        Assert.IsType<FileNotFoundException>(ex.InnerException);
    }

    [Fact]
    public void A_store_held_open_for_writing_by_another_program_still_loads()
    {
        var path = Store("a.xmp", "<root/>", DateTime.UtcNow);
        using var writer = new FileStream(
            path,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.ReadWrite
        );

        Assert.Equal("root", FolderInfoFile.Load(path).Root?.Name.LocalName);
    }
}
