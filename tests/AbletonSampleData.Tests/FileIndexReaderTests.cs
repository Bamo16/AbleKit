using Microsoft.Data.Sqlite;

namespace AbletonSampleData.Tests;

/// <summary>The reader against a database laid out like Live 12's, holding only the tables it reads.</summary>
public class FileIndexReaderTests : IDisposable
{
    private const string Schema = """
        CREATE TABLE files (file_id INTEGER PRIMARY KEY AUTOINCREMENT, parent_id INTEGER, name TEXT);
        CREATE TABLE places (file_id INTEGER, folder_kind INTEGER, level INTEGER NOT NULL DEFAULT 0, name TEXT);
        CREATE TABLE metadata (file_id INTEGER, key INTEGER, value_id INTEGER);
        CREATE TABLE metadata_values (id INTEGER PRIMARY KEY AUTOINCREMENT, value TEXT);
        """;

    private static readonly long UserKeyword = FourCc("UKey");
    private static readonly long DerivedKeyword = FourCc("Keyw");

    private readonly string _index = Path.Combine(
        Path.GetTempPath(),
        "AbletonSampleData.Tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly string _root = Path.Combine(Path.GetTempPath(), "Samples");

    public FileIndexReaderTests() => Directory.CreateDirectory(_index);

    public void Dispose()
    {
        if (Directory.Exists(_index))
            Directory.Delete(_index, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Reads_each_files_user_keywords_under_the_root()
    {
        using (var db = Create("Live-files-12300.db"))
        {
            var root = Place(db, _root);
            var a = File(db, root, "a.flac");
            var folder = File(db, root, "Loops");
            var b = File(db, folder, "b.wav");

            Tag(db, a, UserKeyword, "Key|A");
            Tag(db, a, UserKeyword, "Key|Minor");
            Tag(db, b, UserKeyword, "Type|Loop");
        }

        var info = new FileIndexReader(_index).Read(_root);

        Assert.True(info.TryGet("a.flac", out var a1));
        Assert.Equal(["Key|A", "Key|Minor"], a1.Keywords.Order());
        Assert.True(info.TryGet(Path.Combine(_root, "Loops", "b.wav"), out var b1));
        Assert.Equal(["Type|Loop"], b1.Keywords);
    }

    [Fact]
    public void Keywords_Live_derives_itself_are_not_read()
    {
        using (var db = Create("Live-files-12300.db"))
        {
            var a = File(db, Place(db, _root), "a.flac");

            Tag(db, a, DerivedKeyword, "Drums");
        }

        Assert.Empty(new FileIndexReader(_index).Read(_root).Entries);
    }

    [Fact]
    public void A_folder_Live_was_never_shown_reads_empty()
    {
        using (var db = Create("Live-files-12300.db"))
            File(db, Place(db, _root), "a.flac");

        Assert.Empty(new FileIndexReader(_index).Read(Path.GetTempPath()).Entries);
    }

    [Fact]
    public void The_database_last_written_is_the_one_read()
    {
        using (var old = Create("Live-files-12300.db"))
            Tag(old, File(old, Place(old, _root), "a.flac"), UserKeyword, "Key|C");

        System.IO.File.SetLastWriteTimeUtc(
            Path.Combine(_index, "Live-files-12300.db"),
            DateTime.UtcNow.AddDays(-1)
        );

        using (var current = Create("Live-files-11000.db"))
            Tag(current, File(current, Place(current, _root), "a.flac"), UserKeyword, "Key|D");

        Assert.True(new FileIndexReader(_index).Read(_root).TryGet("a.flac", out var tags));
        Assert.Equal(["Key|D"], tags.Keywords);
    }

    private SqliteConnection Create(string name)
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(_index, name),
                Pooling = false,
            }.ToString()
        );

        connection.Open();
        Execute(connection, Schema);

        return connection;
    }

    /// <summary>A root as Live records it: one row per path segment, the drive's name ending in a separator.</summary>
    private static long Place(SqliteConnection db, string root)
    {
        var drive = Path.GetPathRoot(root)!;
        var parent = File(db, 0, drive);

        foreach (
            var segment in root[drive.Length..]
                .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
        )
            parent = File(db, parent, segment);

        Execute(
            db,
            $"INSERT INTO places (file_id, folder_kind, name) VALUES ({parent}, 2, 'root')"
        );

        return parent;
    }

    private static long File(SqliteConnection db, long parent, string name)
    {
        using var command = db.CreateCommand();
        command.CommandText =
            "INSERT INTO files (parent_id, name) VALUES ($parent, $name); SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$parent", parent);
        command.Parameters.AddWithValue("$name", name);

        return (long)command.ExecuteScalar()!;
    }

    private static void Tag(SqliteConnection db, long file, long key, string value)
    {
        using var command = db.CreateCommand();
        command.CommandText = """
            INSERT INTO metadata_values (value) VALUES ($value);
            INSERT INTO metadata (file_id, key, value_id) VALUES ($file, $key, last_insert_rowid());
            """;
        command.Parameters.AddWithValue("$value", value);
        command.Parameters.AddWithValue("$file", file);
        command.Parameters.AddWithValue("$key", key);
        command.ExecuteNonQuery();
    }

    private static void Execute(SqliteConnection db, string sql)
    {
        using var command = db.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static long FourCc(string code) => code.Aggregate(0L, (value, c) => (value << 8) | c);
}
