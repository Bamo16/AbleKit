using AbleKit.Analysis;

namespace AbleKit.Tests.Analysis;

public sealed class AnalysisFileReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "AbleKit.Tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly AnalysisFileReader _reader = new();

    public AnalysisFileReaderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_file_Live_wrote_is_read()
    {
        var outcome = _reader.Read(ClickTrack.Fixture("sidecar-clicks.asd"));

        var read = Assert.IsType<AnalysisReadOutcome.Read>(outcome);
        Assert.Equal(new DefaultClip(0, 32), read.Analysis.DefaultClip);
    }

    [Fact]
    public void No_file_at_the_path_is_missing()
    {
        Assert.IsType<AnalysisReadOutcome.Missing>(
            _reader.Read(Path.Combine(_root, "none.wav.asd"))
        );
    }

    [Fact]
    public void No_folder_at_the_path_is_missing_too()
    {
        Assert.IsType<AnalysisReadOutcome.Missing>(
            _reader.Read(Path.Combine(_root, "gone", "none.wav.asd"))
        );
    }

    [Fact]
    public void A_file_held_open_by_another_program_fails_with_its_reason()
    {
        var path = Copy("sidecar-clicks.asd");
        using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        var failed = Assert.IsType<AnalysisReadOutcome.Failed>(_reader.Read(path));
        Assert.NotEmpty(failed.Error);
    }

    [Fact]
    public void A_file_cut_short_is_unrecognised_and_says_what_it_did_not_find()
    {
        var path = Path.Combine(_root, "short.wav.asd");
        File.WriteAllBytes(
            path,
            File.ReadAllBytes(ClickTrack.Fixture("sidecar-clicks.asd"))[..30000]
        );

        var unrecognised = Assert.IsType<AnalysisReadOutcome.Unrecognised>(_reader.Read(path));
        Assert.Contains("OverView", unrecognised.Reason);
    }

    [Fact]
    public void A_file_that_is_not_an_analysis_file_is_unrecognised()
    {
        var path = Path.Combine(_root, "notes.wav.asd");
        File.WriteAllText(path, "not an analysis file");

        var unrecognised = Assert.IsType<AnalysisReadOutcome.Unrecognised>(_reader.Read(path));
        Assert.Contains("SampleData", unrecognised.Reason);
    }

    [Fact]
    public void A_head_that_does_not_lead_to_the_clip_is_unrecognised()
    {
        var path = Copy("sidecar-clicks.asd");
        var asd = File.ReadAllBytes(path);
        asd[2]++;
        File.WriteAllBytes(path, asd);

        var unrecognised = Assert.IsType<AnalysisReadOutcome.Unrecognised>(_reader.Read(path));
        Assert.Contains("head", unrecognised.Reason);
    }

    private string Copy(string fixture)
    {
        var path = Path.Combine(_root, fixture);
        File.Copy(ClickTrack.Fixture(fixture), path);

        return path;
    }
}
