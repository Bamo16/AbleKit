using AbleKit.Analysis;

namespace AbleKit.Tests.Analysis;

/// <summary>The reader against every <c>.asd</c> the real library holds.</summary>
public sealed class AnalysisFileRealLibraryTests
{
    private static readonly Lazy<IReadOnlyList<Sidecar>> Library = new(() =>
        [
            .. Directory
                .EnumerateFiles(RealLibrary.MashupRoot, "*.asd", SearchOption.AllDirectories)
                .Select(file => new Sidecar(
                    Path.GetFileName(file),
                    AnalysisFile.TryRead(file, out var warp) ? warp : null
                )),
        ]
    );

    [Fact]
    public void Every_sidecar_in_the_library_parses()
    {
        var unreadable = Sidecars().Where(s => s.Warp is null).Select(s => s.File).ToList();

        Assert.Empty(unreadable);
    }

    [Fact]
    public void A_saved_default_clip_and_warp_markers_come_together()
    {
        var disagreeing = Warps()
            .Where(s => s.Warp.DefaultClip is null == s.Warp.Markers.Count > 0)
            .Select(s => s.File)
            .ToList();

        Assert.Empty(disagreeing);
    }

    [Fact]
    public void A_saved_default_clip_ends_after_it_starts()
    {
        var backwards = Warps()
            .Where(s => s is { Warp.DefaultClip: { } clip } && clip.End <= clip.Start)
            .Select(s => s.File)
            .ToList();

        Assert.Empty(backwards);
    }

    [Fact]
    public void Every_sidecar_in_the_library_reaches_its_overview()
    {
        var without = Warps()
            .Where(s => s.Warp.Overview is not { Peaks.Count: > 0 })
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
                s.Warp.Transients is not { } transients
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
                s.Warp.Markers.Zip(s.Warp.Markers.Skip(1)).Any(p => !Advances(p.First, p.Second))
            )
            .Select(s => s.File)
            .ToList();

        Assert.Empty(outOfOrder);
    }

    [Fact]
    public void A_sample_with_markers_has_a_positive_tempo_at_each_of_them()
    {
        var nonsense = Warps()
            .Where(s => s.Warp.Markers.Count > 1)
            .Where(s => s.Warp.Markers.Any(m => s.Warp.TempoAt(m.Seconds) is not > 0))
            .Select(s => s.File)
            .ToList();

        Assert.Empty(nonsense);
    }

    [Fact]
    public void The_time_signature_reads_as_a_usable_meter()
    {
        var unusable = Warps()
            .Where(s =>
                s.Warp.TimeSignatureNumerator is < 1 or > 32
                || s.Warp.TimeSignatureDenominator is < 1
            )
            .Select(s => s.File)
            .ToList();

        Assert.Empty(unusable);
    }

    private static bool Advances(WarpMarker first, WarpMarker second) =>
        second.Seconds >= first.Seconds && second.Beat > first.Beat;

    private static IReadOnlyList<Sidecar> Sidecars()
    {
        Assert.SkipUnless(
            Directory.Exists(RealLibrary.MashupRoot),
            $"{RealLibrary.MashupRoot} not present on this machine."
        );

        return Library.Value;
    }

    private static IEnumerable<(string File, AnalysisFile Warp)> Warps() =>
        Sidecars()
            .Select(s =>
                (s.File, s.Warp ?? throw new InvalidOperationException($"{s.File} did not parse."))
            );

    private sealed record Sidecar(string File, AnalysisFile? Warp);
}
