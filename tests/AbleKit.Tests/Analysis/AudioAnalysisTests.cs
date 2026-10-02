using AbleKit.Analysis;

namespace AbleKit.Tests.Analysis;

public sealed class AudioAnalysisTests
{
    [Theory]
    [InlineData("sidecar-clicks.asd", false)]
    [InlineData("sidecar-mono.asd", true)]
    public void An_analysis_of_the_audio_Live_analysed_is_Lives_own(string sidecar, bool mono)
    {
        // Live's head table is kept: what it holds is not known, so it cannot be computed.
        var track = mono ? ClickTrack.Mono : ClickTrack.Stereo;
        var live = Read(sidecar);

        var rebuilt = AudioAnalysis.FromSamples(
            track.Samples,
            track.ChannelCount,
            live.Audio.Transients,
            track.Wav.Length
        ) with
        {
            HeadTable = live.Audio.HeadTable,
        };

        Assert.Equal(live.ToBytes(), (live with { Audio = rebuilt }).ToBytes());
    }

    [Fact]
    public void The_head_table_holds_only_the_start_and_the_length()
    {
        var audio = AudioAnalysis.FromSamples(ClickTrack.Stereo.Samples, 2, [], 1000);

        Assert.Equal([0, ClickTrack.Stereo.Frames], audio.HeadTable);
    }

    [Fact]
    public void A_siblings_warp_carries_over_to_new_audio_without_its_transient_edits()
    {
        // A sibling whose transients were edited on its own audio, which the new stem must not carry.
        var sibling = Read("sidecar-clicks.asd");
        var edited = sibling with
        {
            Audio = sibling.Audio with { HasUserOnsets = true, UserOnsets = [new(0.5, 1, false)] },
        };
        var quiet = ClickTrack.Stereo.Samples.Select(sample => sample / 4).ToArray();
        Transient[] transients = [new(0, 1), new(22050, 0.5f)];

        var stem = edited with { Audio = AudioAnalysis.FromSamples(quiet, 2, transients, 1234) };

        Assert.True(AnalysisFile.TryParse(stem.ToBytes(), out var reread));
        Assert.Equal(sibling.Warp.Markers, reread.Warp.Markers);
        Assert.Equal(sibling.DefaultClip, reread.DefaultClip);
        Assert.Equal(transients, reread.Audio.Transients);
        Assert.False(reread.Audio.HasUserOnsets);
        Assert.Empty(reread.Audio.UserOnsets);
        Assert.Equal(1234, reread.Audio.OriginalFileSize);
        Assert.Equal(
            sibling.Audio.Overview.PeakBetween(0, long.MaxValue) / 4,
            reread.Audio.Overview.PeakBetween(0, long.MaxValue),
            3
        );
    }

    [Fact]
    public void Audio_of_any_length_gets_an_analysis_Live_reads()
    {
        // Three seconds of a quiet tone: no sibling, every setting at Live's default.
        var samples = Enumerable
            .Range(0, 3 * 44100)
            .Select(i => 0.25f * MathF.Sin(i * 2 * MathF.PI * 440 / 44100))
            .ToArray();

        var analysis = new AnalysisFile { Audio = AudioAnalysis.FromSamples(samples, 1, [], 0) };

        Assert.True(AnalysisFile.TryParse(analysis.ToBytes(), out var reread));
        Assert.Equal(3 * 44100, reread.Audio.HeadTable[^1]);
        Assert.Equal(0.25f, reread.Audio.Overview.PeakBetween(0, long.MaxValue), 2);
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
                AudioAnalysis.FromSamples(ClickTrack.Stereo.Samples, 2, [new(position, energy)], 0)
        );
    }

    [Fact]
    public void Transients_out_of_order_throw()
    {
        Assert.Throws<ArgumentException>(
            "transients",
            () =>
                AudioAnalysis.FromSamples(
                    ClickTrack.Stereo.Samples,
                    2,
                    [new(200, 1), new(100, 1)],
                    0
                )
        );
    }

    [Fact]
    public void Samples_that_are_not_whole_frames_throw()
    {
        Assert.Throws<ArgumentException>(
            "samples",
            () => AudioAnalysis.FromSamples(ClickTrack.Stereo.Samples.AsSpan(..^1), 2, [], 0)
        );
    }

    [Fact]
    public void A_file_size_past_what_the_file_records_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            "fileSize",
            () => AudioAnalysis.FromSamples(ClickTrack.Stereo.Samples, 2, [], 1L << 31)
        );
    }

    private static AnalysisFile Read(string fixture)
    {
        Assert.True(
            AnalysisFile.TryParse(File.ReadAllBytes(ClickTrack.Fixture(fixture)), out var analysis)
        );

        return analysis;
    }
}
