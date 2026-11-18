using AbleKit.Tags;

namespace AbleKit.Tests.Tags;

public sealed class FolderInfoTests
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "abletests-library");

    private static FileTags Tags(string relativePath, params string[] keywords) =>
        new() { RelativePath = relativePath, Keywords = keywords };

    [Fact]
    public void An_empty_folder_info_has_no_entries_and_finds_nothing()
    {
        var info = FolderInfo.Empty(Folder);

        Assert.Equal(Folder, info.Folder);
        Assert.Empty(info.Entries);
        Assert.False(info.TryGet("a.wav", out var tags));
        Assert.Null(tags);
    }

    [Fact]
    public void A_relative_path_finds_its_entry()
    {
        var entry = Tags("a.wav", "Key|C");
        var info = new FolderInfo(Folder, [entry]);

        Assert.True(info.TryGet("a.wav", out var tags));
        Assert.Same(entry, tags);
    }

    [Fact]
    public void An_absolute_path_under_the_folder_finds_the_entry_by_its_relative_part()
    {
        var entry = Tags("sub/a.wav", "Key|C");
        var info = new FolderInfo(Folder, [entry]);

        Assert.True(info.TryGet(Path.Combine(Folder, "sub", "a.wav"), out var tags));
        Assert.Same(entry, tags);
    }

    [Fact]
    public void Separators_and_case_do_not_matter_when_looking_up()
    {
        var entry = Tags("Sub/A.wav");
        var info = new FolderInfo(Folder, [entry]);

        Assert.True(info.TryGet(@"sub\a.WAV", out var backslashed));
        Assert.Same(entry, backslashed);
        Assert.True(info.TryGet("sub/a.wav/", out var trailing));
        Assert.Same(entry, trailing);
    }

    [Fact]
    public void An_entry_spelled_with_backslashes_is_found_by_a_forward_slash_path()
    {
        var entry = Tags(@"sub\a.wav");
        var info = new FolderInfo(Folder, [entry]);

        Assert.True(info.TryGet("sub/a.wav", out var tags));
        Assert.Same(entry, tags);
    }

    [Fact]
    public void A_file_with_no_entry_is_not_found()
    {
        var info = new FolderInfo(Folder, [Tags("a.wav")]);

        Assert.False(info.TryGet("b.wav", out var tags));
        Assert.Null(tags);
    }

    [Fact]
    public void When_two_entries_name_the_same_file_the_later_one_wins_and_both_stay_listed()
    {
        var first = Tags("a.wav", "Key|C");
        var second = Tags("A.WAV", "Key|D");
        var info = new FolderInfo(Folder, [first, second]);

        Assert.True(info.TryGet("a.wav", out var tags));
        Assert.Same(second, tags);
        Assert.Equal([first, second], info.Entries);
    }

    [Fact]
    public void A_file_tags_record_holds_no_hidden_keywords_or_collections_unless_given()
    {
        var entry = Tags("a.wav", "Key|C");

        Assert.Empty(entry.HiddenKeywords);
        Assert.Empty(entry.Collections);
    }
}
