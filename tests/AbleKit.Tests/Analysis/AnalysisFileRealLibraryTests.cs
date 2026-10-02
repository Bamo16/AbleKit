using AbleKit.Analysis;

namespace AbleKit.Tests.Analysis;

/// <summary>The reader against every <c>.asd</c> in a real library (see <see cref="RealLibrary"/>).</summary>
public sealed class AnalysisFileRealLibraryTests
{
    private static readonly Lazy<IReadOnlyList<Sidecar>> Library = new(() =>
        [
            .. RealLibrary
                .SampleFolders()
                .SelectMany(folder =>
                    Directory.EnumerateFiles(folder, "*.asd", SearchOption.AllDirectories)
                )
                .Select(file => new Sidecar(
                    Path.GetFileName(file),
                    AnalysisFile.TryRead(file, out var analysis) ? analysis : null
                )),
        ]
    );

    [Fact]
    public void Every_sidecar_in_the_library_parses()
    {
        var unreadable = Sidecars().Where(s => s.Analysis is null).Select(s => s.File).ToList();

        Assert.Empty(unreadable);
    }

    [Fact]
    public void A_saved_default_clip_and_warp_markers_come_together()
    {
        var disagreeing = Warps()
            .Where(s => s.Analysis.DefaultClip is null == s.Analysis.Warp.Markers.Count > 0)
            .Select(s => s.File)
            .ToList();

        Assert.Empty(disagreeing);
    }

    [Fact]
    public void A_saved_default_clip_ends_after_it_starts()
    {
        var backwards = Warps()
            .Where(s => s is { Analysis.DefaultClip: { } clip } && clip.End <= clip.Start)
            .Select(s => s.File)
            .ToList();

        Assert.Empty(backwards);
    }

    [Fact]
    public void Every_sidecar_in_the_library_reaches_its_overview()
    {
        var without = Warps()
            .Where(s => s.Analysis.Audio.Overview is not { BinCount: > 0 })
            .Select(s => s.File)
            .ToList();

        Assert.Empty(without);
    }

    [Fact]
    public void Transients_never_go_backwards_and_their_energies_are_at_most_one()
    {
        // A position repeats now and then in Live's own lists, so equal neighbours are allowed.
        var disordered = Warps()
            .Where(s =>
                s.Analysis.Audio.Transients is not { } transients
                || transients.Any(transient => transient.Energy is not (> 0 and <= 1))
                || transients
                    .Zip(transients.Skip(1))
                    .Any(pair => pair.Second.Position < pair.First.Position)
            )
            .Select(s => s.File)
            .ToList();

        Assert.Empty(disordered);
    }

    [Fact]
    public void Markers_advance_in_both_seconds_and_beats()
    {
        var outOfOrder = Warps()
            .Where(s =>
                s.Analysis.Warp.Markers.Zip(s.Analysis.Warp.Markers.Skip(1))
                    .Any(p => !Advances(p.First, p.Second))
            )
            .Select(s => s.File)
            .ToList();

        Assert.Empty(outOfOrder);
    }

    [Fact]
    public void A_sample_with_markers_has_a_positive_tempo_at_each_of_them()
    {
        var nonsense = Warps()
            .Where(s => s.Analysis.Warp.Markers.Count > 1)
            .Where(s =>
                s.Analysis.Warp.Markers.Any(m => s.Analysis.Warp.TempoAt(m.Seconds) is not > 0)
            )
            .Select(s => s.File)
            .ToList();

        Assert.Empty(nonsense);
    }

    [Fact]
    public void The_time_signature_reads_as_a_usable_meter()
    {
        var unusable = Warps()
            .Where(s =>
                s.Analysis.Warp.TimeSignature.Numerator is < 1 or > 32
                || s.Analysis.Warp.TimeSignature.Denominator is < 1
            )
            .Select(s => s.File)
            .ToList();

        Assert.Empty(unusable);
    }

    [Fact]
    public void Every_sidecar_in_the_library_writes_back_as_Live_wrote_it()
    {
        // Live's list ids record the file's editing history; the writer numbers them afresh.
        var changed = RealLibrary
            .SampleFolders()
            .SelectMany(folder =>
                Directory.EnumerateFiles(folder, "*.asd", SearchOption.AllDirectories)
            )
            .Where(file =>
                File.ReadAllBytes(file) is var asd
                && !RoundTrip.Rewrite(asd).AsSpan().SequenceEqual(RoundTrip.WithFreshIds(asd))
            )
            .Select(Path.GetFileName)
            .ToList();

        Assert.Empty(changed);
    }

    private static bool Advances(WarpMarker first, WarpMarker second) =>
        second.Seconds >= first.Seconds && second.Beat > first.Beat;

    private static IReadOnlyList<Sidecar> Sidecars()
    {
        // Skips when no library is configured.
        RealLibrary.SampleFolders();

        return Library.Value;
    }

    private static IEnumerable<(string File, AnalysisFile Analysis)> Warps() =>
        Sidecars()
            .Select(s =>
                (
                    s.File,
                    s.Analysis ?? throw new InvalidOperationException($"{s.File} did not parse.")
                )
            );

    private sealed record Sidecar(string File, AnalysisFile? Analysis);
}
