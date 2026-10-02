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

    [Theory]
    [InlineData("sidecar-clicks.asd", false)]
    [InlineData("sidecar-mono.asd", true)]
    public void A_file_rebuilt_from_its_own_audio_and_transients_is_Lives_with_its_user_onsets_cleared(
        string sidecar,
        bool mono
    )
    {
        var track = mono ? ClickTrack.Mono : ClickTrack.Stereo;
        var sibling = ClickTrack.Fixture(sidecar);
        var live = File.ReadAllBytes(sibling);
        Assert.True(AnalysisFile.TryParse(live, out var analysis));
        Assert.True(AnalysisFileParser.TryScan(live, out var scan));

        var written = Write(sibling, track, analysis.Audio.Transients);

        // Live's file, with the user onsets emptied and switched off.
        var onsets = scan.Layout.Arrays["UserOnsets.UserOnsets"];
        var switchOffset = scan.Layout.Values["UserOnsets.HasUserOnsets.Value"];
        Assert.True(switchOffset >= onsets.End);

        byte[] expected =
        [
            .. live[..onsets.Start],
            0,
            0,
            0,
            0,
            .. live[(onsets.Start + sizeof(int))..onsets.Offset],
            .. live[onsets.End..],
        ];
        expected[switchOffset - (onsets.End - onsets.Offset)] = 0;

        Assert.Equal(expected, written);
    }

    [Fact]
    public void The_file_keeps_the_siblings_warp_and_takes_the_audio_and_transients_given()
    {
        var sibling = Read(ClickTrack.Fixture("sidecar-clicks.asd"));
        var quiet = ClickTrack.Stereo with
        {
            Samples = [.. ClickTrack.Stereo.Samples.Select(sample => sample / 4)],
        };
        Transient[] transients = [new(0, 1), new(22050, 0.5f), new(22050, 0.25f), new(705599, 0)];

        Assert.True(
            AnalysisFile.TryParse(
                Write(ClickTrack.Fixture("sidecar-clicks.asd"), quiet, transients),
                out var written
            )
        );

        Assert.Equal(sibling.Warp.Markers, written.Warp.Markers);
        Assert.Equal(sibling.DefaultClip, written.DefaultClip);
        Assert.Equal(sibling.Warp.Mode, written.Warp.Mode);
        Assert.Equal(transients, written.Audio.Transients);
        Assert.Equal(
            sibling.Audio.Overview.PeakBetween(0, long.MaxValue) / 4,
            written.Audio.Overview.PeakBetween(0, long.MaxValue),
            3
        );
    }

    [Fact]
    public void An_empty_transient_list_is_written_as_none()
    {
        var written = Write(ClickTrack.Fixture("sidecar-clicks.asd"), ClickTrack.Stereo, []);

        Assert.True(AnalysisFile.TryParse(written, out var analysis));
        Assert.Empty(analysis.Audio.Transients);
    }

    [Fact]
    public void The_file_records_the_audios_size()
    {
        var track = ClickTrack.Stereo with { Wav = [.. ClickTrack.Stereo.Wav, .. new byte[100]] };

        var written = Write(ClickTrack.Fixture("sidecar-clicks.asd"), track, []);

        Assert.True(AnalysisFileParser.TryScan(written, out var scan));
        Assert.Equal(
            track.Wav.Length,
            BitConverter.ToInt32(written, scan.Layout.Values["OriginalFileSize.Value"])
        );
    }

    [Fact]
    public void An_existing_file_is_replaced_and_nothing_is_left_beside_it()
    {
        var audio = Audio(ClickTrack.Stereo);
        File.WriteAllBytes($"{audio}.asd", [1, 2, 3]);

        var outcome = WriteOutcome(
            ClickTrack.Fixture("sidecar-clicks.asd"),
            audio,
            ClickTrack.Stereo,
            []
        );

        Assert.Equal(new AnalysisWriteOutcome.Written($"{audio}.asd"), outcome);
        Assert.True(AnalysisFile.TryRead($"{audio}.asd", out _));
        Assert.Equal(
            ["clicks.wav", "clicks.wav.asd"],
            Directory.GetFiles(_root).Select(Path.GetFileName).Order()
        );
    }

    [Fact]
    public void A_sibling_can_be_the_audios_own_file()
    {
        var audio = Audio(ClickTrack.Stereo);
        File.Copy(ClickTrack.Fixture("sidecar-clicks.asd"), $"{audio}.asd");

        var outcome = WriteOutcome($"{audio}.asd", audio, ClickTrack.Stereo, [new(100, 1)]);

        Assert.IsType<AnalysisWriteOutcome.Written>(outcome);
        Assert.Equal([new(100, 1)], Read($"{audio}.asd").Audio.Transients);
    }

    [Fact]
    public void A_sibling_without_a_saved_default_clip_is_rejected()
    {
        AssertRejected(ClickTrack.Fixture("sidecar-clicks-unsaved.asd"), ClickTrack.Stereo);
    }

    [Fact]
    public void A_sibling_of_another_length_is_rejected()
    {
        var shorter = ClickTrack.Stereo with { Samples = ClickTrack.Stereo.Samples[..^2] };

        AssertRejected(ClickTrack.Fixture("sidecar-clicks.asd"), shorter);
    }

    [Fact]
    public void A_sibling_with_other_channels_is_rejected()
    {
        // The mono track is as long as the stereo one, so only the channels differ.
        AssertRejected(ClickTrack.Fixture("sidecar-clicks.asd"), ClickTrack.Mono);
    }

    [Fact]
    public void A_sibling_that_is_not_an_analysis_file_is_rejected()
    {
        var sibling = Path.Combine(_root, "other.asd");
        File.WriteAllBytes(sibling, [.. "not an analysis file"u8]);

        AssertRejected(sibling, ClickTrack.Stereo);
    }

    [Fact]
    public void A_missing_sibling_fails_without_writing()
    {
        var audio = Audio(ClickTrack.Stereo);

        var outcome = WriteOutcome(
            Path.Combine(_root, "missing.asd"),
            audio,
            ClickTrack.Stereo,
            []
        );

        Assert.IsType<AnalysisWriteOutcome.Failed>(outcome);
        Assert.False(File.Exists($"{audio}.asd"));
    }

    [Theory]
    [InlineData(-1, 1f)]
    [InlineData(705600, 1f)]
    [InlineData(100, 1.5f)]
    [InlineData(100, -0.1f)]
    [InlineData(100, float.NaN)]
    public void A_transient_outside_the_audio_or_the_energy_range_throws(int position, float energy)
    {
        Assert.Throws<ArgumentException>(
            "transients",
            () =>
                Write(
                    ClickTrack.Fixture("sidecar-clicks.asd"),
                    ClickTrack.Stereo,
                    [new(position, energy)]
                )
        );
    }

    [Fact]
    public void Transients_out_of_order_throw()
    {
        Assert.Throws<ArgumentException>(
            "transients",
            () =>
                Write(
                    ClickTrack.Fixture("sidecar-clicks.asd"),
                    ClickTrack.Stereo,
                    [new(200, 1), new(100, 1)]
                )
        );
    }

    [Fact]
    public void Samples_that_are_not_whole_frames_throw()
    {
        var odd = ClickTrack.Stereo with { Samples = ClickTrack.Stereo.Samples[..^1] };

        Assert.Throws<ArgumentException>(
            "samples",
            () => Write(ClickTrack.Fixture("sidecar-clicks.asd"), odd, [])
        );
    }

    private byte[] Write(string sibling, ClickTrack track, IReadOnlyList<Transient> transients)
    {
        var audio = Audio(track);
        var outcome = WriteOutcome(sibling, audio, track, transients);

        Assert.Equal(new AnalysisWriteOutcome.Written($"{audio}.asd"), outcome);

        return File.ReadAllBytes($"{audio}.asd");
    }

    private void AssertRejected(string sibling, ClickTrack track)
    {
        var audio = Audio(track);

        var outcome = WriteOutcome(sibling, audio, track, []);

        Assert.IsType<AnalysisWriteOutcome.Rejected>(outcome);
        Assert.False(File.Exists($"{audio}.asd"));
    }

    private AnalysisWriteOutcome WriteOutcome(
        string sibling,
        string audio,
        ClickTrack track,
        IReadOnlyList<Transient> transients
    ) => _writer.WriteFromSibling(sibling, audio, track.Samples, track.ChannelCount, transients);

    private string Audio(ClickTrack track)
    {
        var audio = Path.Combine(_root, "clicks.wav");
        File.WriteAllBytes(audio, track.Wav);

        return audio;
    }

    private static AnalysisFile Read(string path)
    {
        Assert.True(AnalysisFile.TryRead(path, out var analysis), path);

        return analysis;
    }
}
