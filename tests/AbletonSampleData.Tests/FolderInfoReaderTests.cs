namespace AbletonSampleData.Tests;

public sealed class FolderInfoReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "AbletonSampleData.Tests",
        Guid.NewGuid().ToString("n")
    );

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    private FolderInfo ReadXmp(string itemsXml)
    {
        var folderInfo = Path.Combine(_root, "Ableton Folder Info");
        Directory.CreateDirectory(folderInfo);

        File.WriteAllText(
            Path.Combine(folderInfo, "dc66a3fa-0fe1-5352-91cf-3ec237e9ee90.xmp"),
            $"""
            <x:xmpmeta xmlns:x="adobe:ns:meta/" x:xmptk="XMP Core 6.0.0">
               <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
                  <rdf:Description rdf:about=""
                        xmlns:dc="http://purl.org/dc/elements/1.1/"
                        xmlns:ablFR="https://ns.ableton.com/xmp/fs-resources/1.0/">
                     <dc:format>application/vnd.ableton.folder</dc:format>
                     <ablFR:resource>folder</ablFR:resource>
                     <ablFR:platform>win</ablFR:platform>
                     <ablFR:items>
                        <rdf:Bag>
            {itemsXml}
                        </rdf:Bag>
                     </ablFR:items>
                  </rdf:Description>
               </rdf:RDF>
            </x:xmpmeta>
            """
        );

        return new FolderInfoReader().Read(_root);
    }

    private static string Entry(string filePath, params string[] keywords) =>
        $"""
                         <rdf:li rdf:parseType="Resource">
                            <ablFR:filePath>{filePath}</ablFR:filePath>
                            <ablFR:keywords>
                               <rdf:Bag>
            {string.Join("\n", keywords.Select(k => $"                     <rdf:li>{k}</rdf:li>"))}
                               </rdf:Bag>
                            </ablFR:keywords>
                         </rdf:li>
            """;

    [Fact]
    public void Reads_each_entrys_keywords_as_live_wrote_them()
    {
        var info = ReadXmp(
            Entry(
                "Jackson 5 - I Want You Back (Instrumental - Full).flac",
                "Key|G♯",
                "Key|Major",
                "Type|Instrumental"
            )
        );

        Assert.True(
            info.TryGet("Jackson 5 - I Want You Back (Instrumental - Full).flac", out var tags)
        );
        Assert.Equal(["Key|G♯", "Key|Major", "Type|Instrumental"], tags.Keywords);
        Assert.Empty(tags.HiddenKeywords);
    }

    [Fact]
    public void Resolves_by_absolute_path_under_the_root()
    {
        var info = ReadXmp(
            Entry("Yes - Owner of a Lonely Heart (Drums).flac", "Key|C", "Key|Minor")
        );

        Assert.True(
            info.TryGet(
                Path.Combine(_root, "Yes - Owner of a Lonely Heart (Drums).flac"),
                out var tags
            )
        );
        Assert.Equal(["Key|C", "Key|Minor"], tags.Keywords);
    }

    [Fact]
    public void A_file_with_no_entry_is_simply_absent()
    {
        var info = ReadXmp(Entry("Tagged.flac", "Key|A", "Key|Minor"));

        Assert.False(info.TryGet("Never Tagged.flac", out _));
    }

    [Fact]
    public void A_root_with_no_folder_info_reads_empty_rather_than_throwing()
    {
        Directory.CreateDirectory(_root);

        var info = new FolderInfoReader().Read(_root);

        Assert.Empty(info.Entries);
        Assert.False(info.TryGet("anything.flac", out _));
    }

    [Fact]
    public void A_malformed_xmp_reads_empty_rather_than_throwing()
    {
        var folderInfo = Path.Combine(_root, "Ableton Folder Info");
        Directory.CreateDirectory(folderInfo);
        File.WriteAllText(Path.Combine(folderInfo, "half-written.xmp"), "<x:xmpmeta><rdf:RDF");

        Assert.Empty(new FolderInfoReader().Read(_root).Entries);
    }

    [Fact]
    public void Reads_while_the_file_is_held_open_for_writing()
    {
        ReadXmp(Entry("Held.flac", "Key|F", "Key|Major"));
        var xmp = Directory.GetFiles(Path.Combine(_root, "Ableton Folder Info"), "*.xmp")[0];

        using var holder = new FileStream(
            xmp,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.ReadWrite
        );

        Assert.Single(new FolderInfoReader().Read(_root).Entries);
    }
}
