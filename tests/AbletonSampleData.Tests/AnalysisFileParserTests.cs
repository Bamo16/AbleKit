namespace AbletonSampleData.Tests;

/// <summary>
/// One stem's sidecar before and after <em>Save Default Clip</em>, cut short after the warp
/// section, a Live 12 sidecar with Loop on from DBraun/AbletonParsing, and click tracks' sidecars.
/// </summary>
public sealed class AnalysisFileParserTests
{
    [Fact]
    public void An_analysed_sidecar_has_no_default_clip_and_no_markers()
    {
        var warp = Read("sidecar-analysed.asd");

        Assert.Null(warp.DefaultClip);
        Assert.Empty(warp.Markers);
    }

    [Fact]
    public void Save_default_clip_writes_the_clip_and_the_markers()
    {
        var warp = Read("sidecar-default-clip-saved.asd");

        Assert.Equal(new DefaultClip(32, 478.26300236222113), warp.DefaultClip);
        Assert.Equal([157, 157.03125], warp.Markers.Select(marker => marker.Beat));
        Assert.Equal(175.0007, warp.TempoAt(0)!.Value, 4);
    }

    [Fact]
    public void With_loop_on_the_clip_starts_at_the_offset_from_the_loop_and_ends_at_its_end_marker()
    {
        Assert.Equal(new DefaultClip(0, 5), Read("sidecar-loop-on.asd").DefaultClip);
    }

    [Fact]
    public void A_mono_sidecar_reads_like_a_stereo_one()
    {
        // Its head ends in 2 bytes where a stereo file's ends in 4, so the saved byte sits earlier.
        var warp = Read("sidecar-mono.asd");

        Assert.Equal(new DefaultClip(0, 32), warp.DefaultClip);
        Assert.Equal([0, 32, 32.03125], warp.Markers.Select(marker => marker.Beat));
        Assert.Equal(5513, warp.Overview?.Peaks.Count);
    }

    [Fact]
    public void The_hidden_marker_sets_the_tempo_past_the_last_visible_one()
    {
        // A click track that speeds from 120 to 140 BPM at 8 s, with visible markers at 0 s and
        // 8 s only. The 140 was set by dragging a later click onto the grid.
        var warp = Read("sidecar-tail-tempo.asd");

        Assert.Equal(120, warp.TempoAt(4)!.Value, 3);
        Assert.Equal(140, warp.TempoAt(12)!.Value, 1);
    }

    [Fact]
    public void The_overview_holds_a_peak_for_every_128_samples()
    {
        // The DBraun sidecar is 181,675 samples long, whole to the end of the file.
        var overview = Read("sidecar-loop-on.asd").Overview;

        Assert.NotNull(overview);
        Assert.Equal(128, overview.SamplesPerBin);
        Assert.Equal(1420, overview.Peaks.Count);
        Assert.InRange(overview.Peaks.Max(), 0.1f, 1.5f);
    }

    [Fact]
    public void A_sidecar_cut_short_before_its_overview_still_reads_without_one()
    {
        var warp = Read("sidecar-default-clip-saved.asd");

        Assert.Null(warp.Overview);
        Assert.NotNull(warp.DefaultClip);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(32000)]
    public void A_sidecar_cut_short_is_unreadable(int length)
    {
        var asd = File.ReadAllBytes(Fixture("sidecar-default-clip-saved.asd"));

        Assert.False(AnalysisFile.TryParse(asd.AsSpan(0, length), out _));
    }

    [Fact]
    public void A_head_that_does_not_lead_to_the_clip_is_unreadable()
    {
        var asd = File.ReadAllBytes(Fixture("sidecar-default-clip-saved.asd"));
        asd[2]++;

        Assert.False(AnalysisFile.TryParse(asd, out _));
    }

    private static AnalysisFile Read(string name)
    {
        Assert.True(AnalysisFile.TryRead(Fixture(name), out var warp), name);

        return warp;
    }

    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
}
