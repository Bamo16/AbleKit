using AbleKit.Analysis;

namespace AbleKit.Tests.Analysis;

public sealed class AnalysisFileWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "AbleKit.Tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly AnalysisFileWriter _writer = new();

    public AnalysisFileWriterTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void The_file_written_is_the_model_as_bytes()
    {
        var analysis = Clicks();
        var path = Path.Combine(_root, "Break (copy).wav.asd");

        var outcome = _writer.Write(path, analysis);

        Assert.Equal(new AnalysisWriteOutcome.Written(path), outcome);
        Assert.Equal(analysis.ToBytes(), File.ReadAllBytes(path));
    }

    [Fact]
    public void An_existing_file_is_replaced_and_nothing_is_left_beside_it()
    {
        var path = Path.Combine(_root, "clicks.wav.asd");
        File.WriteAllBytes(path, [1, 2, 3]);

        _writer.Write(path, Clicks());

        Assert.True(AnalysisFile.TryRead(path, out _));
        Assert.Equal(["clicks.wav.asd"], Directory.GetFiles(_root).Select(Path.GetFileName));
    }

    [Fact]
    public void A_folder_that_is_not_there_fails_without_writing()
    {
        var path = Path.Combine(_root, "missing", "clicks.wav.asd");

        var outcome = _writer.Write(path, Clicks());

        Assert.IsType<AnalysisWriteOutcome.Failed>(outcome);
        Assert.False(Directory.Exists(Path.Combine(_root, "missing")));
    }

    private static AnalysisFile Clicks()
    {
        Assert.True(
            AnalysisFile.TryRead(ClickTrack.Fixture("sidecar-clicks.asd"), out var analysis)
        );

        return analysis;
    }
}
