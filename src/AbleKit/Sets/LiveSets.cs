using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace AbleKit.Sets;

/// <summary>
/// The Live sets (<c>.als</c>) under a projects folder, seen through the samples they use: which sets
/// use a file, and pointing them at a file that has moved. Live's own backups are left out.
/// </summary>
/// <param name="projectsRoot">The folder to search, at any depth, for sets.</param>
/// <param name="time">The clock that stamps backups; the system clock when null.</param>
public sealed partial class LiveSets(string projectsRoot, TimeProvider? time = null)
{
    private const string BackupFolder = "Backup";

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, Referenced> _cache = new(
        StringComparer.OrdinalIgnoreCase
    );

    [GeneratedRegex("""<Path Value="(?<Path>[^"]*)""")]
    private static partial Regex FilePath { get; }

    /// <summary>A sample reference: the name its relative path ends in, then its full path.</summary>
    [GeneratedRegex(
        """
            <FileRef(\s[^>]*)?>
            .*?
            <RelativePath\sValue="([^"]*/)?(?<Name>[^"/]*)"
            .*?
            <Path\sValue="(?<Path>[^"]*)"
            """,
        RegexOptions.Singleline | RegexOptions.IgnorePatternWhitespace
    )]
    private static partial Regex FileRef { get; }

    /// <summary>
    /// The sets that use any of <paramref name="samples"/>, by full path, in name order. A set that
    /// cannot be read is passed over.
    /// </summary>
    /// <param name="samples">Full paths of audio files, with either kind of slash.</param>
    public IReadOnlyList<string> Using(IReadOnlyCollection<string> samples) =>
        [.. SetsUsing(samples.Select(Normalize)).Order(StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// Points every set that uses a moved sample at its new path, changing nothing else, after copying
    /// the set into its project's <c>Backup</c> folder. Live does not lock a set it has open and
    /// overwrites it on its next save, so relink only sets that are closed.
    /// </summary>
    /// <param name="moves">The samples moved on disk, each from and to its full path.</param>
    /// <param name="backupLabel">
    /// Who is relinking, put in each backup's name: <c>Song [label 2026-10-01 093000].als</c>.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="backupLabel"/> is empty or cannot be in a file name.</exception>
    public SetRelinkResult Relink(IReadOnlyList<SampleMove> moves, string backupLabel)
    {
        if (
            string.IsNullOrWhiteSpace(backupLabel)
            || backupLabel.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
        )
            throw new ArgumentException(
                $"'{backupLabel}' cannot go in a file name",
                nameof(backupLabel)
            );

        var renamed = moves
            .DistinctBy(move => Normalize(move.From), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                move => Normalize(move.From),
                move => Normalize(move.To),
                StringComparer.OrdinalIgnoreCase
            );

        List<string> relinked = [];
        List<SetRelinkFailure> failed = [];

        foreach (var set in SetsUsing(renamed.Keys))
        {
            try
            {
                var xml = Read(set) ?? throw new InvalidDataException("the set could not be read");

                Backup(set, backupLabel);
                Write(set, Relinked(xml, renamed));
                relinked.Add(set);
            }
            catch (Exception ex)
                when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                failed.Add(new SetRelinkFailure(set, ex.Message));
            }
        }

        return new SetRelinkResult(relinked, failed);
    }

    private List<string> SetsUsing(IEnumerable<string> samples)
    {
        var wanted = samples.ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (wanted.Count is 0 || !Directory.Exists(projectsRoot))
            return [];

        return
        [
            .. Directory
                .EnumerateFiles(projectsRoot, "*.als", SearchOption.AllDirectories)
                .Where(set => !IsBackup(set))
                .Where(set => PathsIn(set).Overlaps(wanted)),
        ];
    }

    /// <summary>Replaces the name in the relative path and the whole full path, and nothing else.</summary>
    private static string Relinked(string xml, Dictionary<string, string> renamed) =>
        FileRef.Replace(
            xml,
            match =>
            {
                var from = Normalize(WebUtility.HtmlDecode(match.Groups["Path"].Value));

                if (!renamed.TryGetValue(from, out var to))
                    return match.Value;

                var (name, path) = (match.Groups["Name"], match.Groups["Path"]);
                var nameAt = name.Index - match.Index;
                var pathAt = path.Index - match.Index;
                var value = match.Value;

                return $"{value[..nameAt]}{Escape(Path.GetFileName(to))}"
                    + $"{value[(nameAt + name.Length)..pathAt]}{Escape(to)}"
                    + value[(pathAt + path.Length)..];
            }
        );

    private void Backup(string set, string label)
    {
        var folder = Path.Combine(Path.GetDirectoryName(set)!, BackupFolder);
        Directory.CreateDirectory(folder);

        File.Copy(
            set,
            Path.Combine(
                folder,
                $"{Path.GetFileNameWithoutExtension(set)} [{label} {_time.GetLocalNow():yyyy-MM-dd HHmmss}].als"
            )
        );
    }

    private static string? Read(string set)
    {
        try
        {
            using var file = File.OpenRead(set);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);

            return reader.ReadToEnd();
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>Writes beside the set and swaps it in, so a failure leaves the set as it was.</summary>
    private static void Write(string set, string xml)
    {
        var temporary = $"{set}.tmp";

        using (var file = File.Create(temporary))
        using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
            gzip.Write(new UTF8Encoding(false).GetBytes(xml));

        File.Move(temporary, set, overwrite: true);
    }

    /// <summary>Every sample path a set references, read again only when the set's stamp moves.</summary>
    private HashSet<string> PathsIn(string set)
    {
        if (new FileInfo(set) is not { Exists: true } file)
            return [];

        var stamp = (file.Length, file.LastWriteTimeUtc);

        if (_cache.TryGetValue(set, out var held) && held.Stamp == stamp)
            return held.Paths;

        if (Read(set) is not { } xml)
            return [];

        var paths = FilePath
            .Matches(xml)
            .Select(match => Normalize(WebUtility.HtmlDecode(match.Groups["Path"].Value)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _cache[set] = new Referenced(stamp, paths);

        return paths;
    }

    private bool IsBackup(string set) =>
        Path.GetRelativePath(projectsRoot, set)
            .Split(Path.DirectorySeparatorChar)
            .Contains(BackupFolder, StringComparer.OrdinalIgnoreCase);

    // Live escapes only the ampersand, the one XML special character a file name can hold.
    private static string Escape(string value) => value.Replace("&", "&amp;");

    private static string Normalize(string path) => path.Replace('\\', '/');

    private sealed record Referenced((long Length, DateTime Written) Stamp, HashSet<string> Paths);
}

/// <summary>A sample moved or renamed on disk.</summary>
/// <param name="From">Its full path before.</param>
/// <param name="To">Its full path after.</param>
public sealed record SampleMove(string From, string To);

/// <summary>Which sets now point at the moved samples, and which could not be changed.</summary>
/// <param name="Relinked">The sets rewritten, by full path.</param>
/// <param name="Failed">The sets that use a moved sample but could not be rewritten; each is as it was.</param>
public sealed record SetRelinkResult(
    IReadOnlyList<string> Relinked,
    IReadOnlyList<SetRelinkFailure> Failed
);

/// <summary>A set that could not be relinked, to relink by hand.</summary>
/// <param name="Set">The set's full path.</param>
/// <param name="Error">Why it could not be rewritten.</param>
public sealed record SetRelinkFailure(string Set, string Error);
