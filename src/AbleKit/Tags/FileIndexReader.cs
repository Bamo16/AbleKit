using Microsoft.Data.Sqlite;

namespace AbleKit.Tags;

/// <summary>
/// Reads Live's file index, the SQLite database behind the browser, as a <see cref="FolderInfo"/>
/// holding every file under a folder Live watches, at any depth. Read-only.
/// </summary>
public sealed class FileIndexReader(string indexFolder)
{
    private const string DatabasePattern = "Live-files-*.db";

    /// <summary>Every place Ableton knows, against the absolute path it stands for.</summary>
    private const string PlacePathsSql = """
        WITH RECURSIVE up(place_id, parent_id, path) AS (
            SELECT p.file_id, f.parent_id, f.name
            FROM places p
            JOIN files f ON f.file_id = p.file_id
            UNION ALL
            SELECT up.place_id, f.parent_id,
                   f.name || CASE WHEN f.name LIKE '%\' THEN '' ELSE '\' END || up.path
            FROM files f
            JOIN up ON f.file_id = up.parent_id
        )
        SELECT place_id, path FROM up
        """;

    /// <summary>
    /// One row per keyword, so a file with three tags arrives as three rows.
    /// </summary>
    private const string TagsSql = """
        WITH RECURSIVE tree(file_id, path) AS (
            SELECT file_id, '' FROM files WHERE file_id = $place
            UNION ALL
            SELECT f.file_id,
                   CASE WHEN tree.path = '' THEN f.name ELSE tree.path || '/' || f.name END
            FROM files f
            JOIN tree ON f.parent_id = tree.file_id
        )
        SELECT tree.path, mv.value
        FROM tree
        JOIN metadata m ON m.file_id = tree.file_id AND m.key = $keyword
        JOIN metadata_values mv ON mv.id = m.value_id
        ORDER BY tree.path
        """;

    /// <summary>Every keyword on any file, the user's and Live's own.</summary>
    private const string KnownKeywordsSql = """
        SELECT DISTINCT mv.value
        FROM metadata m
        JOIN metadata_values mv ON mv.id = m.value_id
        WHERE m.key IN ($keyword, $derived)
        """;

    private static readonly long UserKeyword = FourCc("UKey");
    private static readonly long DerivedKeyword = FourCc("Keyw");

    /// <summary>Where Live keeps its index unless told otherwise.</summary>
    public static string DefaultFolder { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Ableton",
            "Live Database"
        );

    /// <summary>
    /// Reads what the index holds for <paramref name="folder"/>. With no index, as before Live first
    /// runs, or for a folder Live was never shown, there is nothing to read, and it reads as empty.
    /// </summary>
    /// <exception cref="IOException">The index could not be read: locked, damaged, or laid out differently.</exception>
    /// <exception cref="UnauthorizedAccessException">Reading the index is not permitted.</exception>
    public FolderInfo Read(string folder)
    {
        if (FindDatabase() is not { } database)
            return FolderInfo.Empty(folder);

        try
        {
            using var connection = Open(database);

            return PlaceFor(connection, folder) is { } place
                ? new FolderInfo(folder, TagsUnder(connection, place))
                : FolderInfo.Empty(folder);
        }
        catch (SqliteException ex)
        {
            throw Unreadable(ex);
        }
    }

    /// <summary>
    /// Every keyword Live knows from some file it has indexed, built-in and the user's own,
    /// compared exactly. A keyword missing from it would become a new tag. A tag the user made but
    /// put on no file is missing too, and the index can trail Live by a few seconds. With no index,
    /// it is empty.
    /// </summary>
    /// <exception cref="IOException">The index could not be read: locked, damaged, or laid out differently.</exception>
    /// <exception cref="UnauthorizedAccessException">Reading the index is not permitted.</exception>
    public IReadOnlySet<string> ReadKnownKeywords()
    {
        if (FindDatabase() is not { } database)
            return new HashSet<string>();

        try
        {
            using var connection = Open(database);
            using var command = connection.CreateCommand();
            command.CommandText = KnownKeywordsSql;
            command.Parameters.AddWithValue("$keyword", UserKeyword);
            command.Parameters.AddWithValue("$derived", DerivedKeyword);

            using var reader = command.ExecuteReader();

            var keywords = new HashSet<string>(StringComparer.Ordinal);

            while (reader.Read())
                keywords.Add(reader.GetString(0));

            return keywords;
        }
        catch (SqliteException ex)
        {
            throw Unreadable(ex);
        }
    }

    /// <summary>
    /// The database Live is currently using: the one being written, not the highest-numbered,
    /// since a stale schema version may sit beside it.
    /// </summary>
    private string? FindDatabase() =>
        Directory.Exists(indexFolder)
            ? Directory.EnumerateFiles(indexFolder, DatabasePattern).MaxBy(File.GetLastWriteTimeUtc)
            : null;

    /// <summary>Callers catch an <see cref="IOException"/> without depending on SQLite.</summary>
    private static IOException Unreadable(SqliteException ex) =>
        new($"Live's file index could not be read: {ex.Message}", ex);

    /// <summary>Unpooled, since a pooled connection holds Ableton's file open after closing.</summary>
    private static SqliteConnection Open(string database)
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = database,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString()
        );

        connection.Open();

        return connection;
    }

    private static long? PlaceFor(SqliteConnection connection, string folder)
    {
        using var command = connection.CreateCommand();
        command.CommandText = PlacePathsSql;

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            if (SamePath(reader.GetString(1), folder))
                return reader.GetInt64(0);
        }

        return null;
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase
        );

    private static IReadOnlyList<FileTags> TagsUnder(SqliteConnection connection, long place)
    {
        using var command = connection.CreateCommand();
        command.CommandText = TagsSql;
        command.Parameters.AddWithValue("$place", place);
        command.Parameters.AddWithValue("$keyword", UserKeyword);

        using var reader = command.ExecuteReader();

        var rows = new List<(string Path, string Value)>();

        while (reader.Read())
            rows.Add((reader.GetString(0), reader.GetString(1)));

        return
        [
            .. rows.GroupBy(r => r.Path, StringComparer.Ordinal)
                .Select(g => new FileTags
                {
                    RelativePath = g.Key,
                    Keywords = [.. g.Select(r => r.Value)],
                }),
        ];
    }

    /// <summary>
    /// Ableton stores its integer keys as four-character codes — <c>UKey</c> for a user-applied
    /// keyword, beside <c>Keyw</c> for the ones it derives itself.
    /// </summary>
    private static long FourCc(string code) => code.Aggregate(0L, (value, c) => (value << 8) | c);
}
