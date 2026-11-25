using System.IO;
using Microsoft.Data.Sqlite;
using RadioClassic.Core;

namespace RadioClassic.Data;

/// <summary>
/// Owns the single SQLite connection (the SQLite engine that ships with Windows, winsqlite3.dll)
/// and applies schema migrations. All access goes through <see cref="Sync"/> to stay thread-safe.
/// </summary>
public sealed class Database : IDisposable
{
    /// <summary>Ordered schema migrations; index + 1 is the resulting PRAGMA user_version.</summary>
    private static readonly string[] Migrations =
    [
        // v1: initial schema
        """
        CREATE TABLE stations(
            id          INTEGER PRIMARY KEY,
            uuid        TEXT UNIQUE,
            name        TEXT NOT NULL,
            url         TEXT NOT NULL,
            homepage    TEXT NOT NULL DEFAULT '',
            favicon     TEXT NOT NULL DEFAULT '',
            country     TEXT NOT NULL DEFAULT '',
            countrycode TEXT NOT NULL DEFAULT '',
            language    TEXT NOT NULL DEFAULT '',
            tags        TEXT NOT NULL DEFAULT '',
            codec       TEXT NOT NULL DEFAULT '',
            bitrate     INTEGER NOT NULL DEFAULT 0,
            votes       INTEGER NOT NULL DEFAULT 0,
            clicks      INTEGER NOT NULL DEFAULT 0,
            lastcheckok INTEGER NOT NULL DEFAULT 1,
            hls         INTEGER NOT NULL DEFAULT 0,
            custom      INTEGER NOT NULL DEFAULT 0,
            gen         INTEGER NOT NULL DEFAULT 0
        );
        CREATE INDEX ix_stations_name ON stations(name COLLATE NOCASE);
        CREATE INDEX ix_stations_country ON stations(country);

        CREATE TABLE favorites(
            station_id INTEGER PRIMARY KEY REFERENCES stations(id) ON DELETE CASCADE,
            folder     TEXT NOT NULL DEFAULT '',
            added      TEXT NOT NULL
        );

        CREATE TABLE history(
            id           INTEGER PRIMARY KEY,
            station_id   INTEGER NOT NULL,
            station_name TEXT NOT NULL,
            played_at    TEXT NOT NULL
        );
        CREATE INDEX ix_history_played ON history(played_at);

        CREATE TABLE songs(
            id           INTEGER PRIMARY KEY,
            station_name TEXT NOT NULL,
            title        TEXT NOT NULL,
            played_at    TEXT NOT NULL
        );

        CREATE TABLE recordings(
            id           INTEGER PRIMARY KEY,
            station_name TEXT NOT NULL,
            station_url  TEXT NOT NULL,
            title        TEXT NOT NULL DEFAULT '',
            file         TEXT NOT NULL,
            format       TEXT NOT NULL DEFAULT '',
            started      TEXT NOT NULL,
            ended        TEXT,
            bytes        INTEGER NOT NULL DEFAULT 0,
            status       TEXT NOT NULL,
            error        TEXT NOT NULL DEFAULT '',
            scheduled    INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE schedules(
            id           INTEGER PRIMARY KEY,
            station_id   INTEGER NOT NULL,
            station_name TEXT NOT NULL,
            station_url  TEXT NOT NULL,
            start        TEXT NOT NULL,
            duration_min INTEGER NOT NULL,
            repeat       INTEGER NOT NULL DEFAULT 0,
            days         INTEGER NOT NULL DEFAULT 0,
            enabled      INTEGER NOT NULL DEFAULT 1,
            last_run     TEXT
        );
        """,
    ];

    private readonly SqliteConnection _connection;

    /// <summary>Lock that every caller must hold while using <see cref="Connection"/>.</summary>
    public object Sync { get; } = new();
    public SqliteConnection Connection => _connection;

    static Database() => SQLitePCL.Batteries_V2.Init();

    public Database(string? file = null)
    {
        file ??= AppPaths.DatabaseFile;
        if (file != ":memory:") Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = file,
            Pooling = false
        }.ToString());
        _connection.Open();
        Execute("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON;");
        Migrate();
    }

    private void Migrate()
    {
        var version = Convert.ToInt32(Scalar("PRAGMA user_version;"));
        for (var i = version; i < Migrations.Length; i++)
        {
            using var tx = _connection.BeginTransaction();
            using (var cmd = _connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = Migrations[i] + $"\nPRAGMA user_version={i + 1};";
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
            Log.Info($"Database migrated to schema version {i + 1}");
        }
    }

    public int Execute(string sql, params (string Name, object? Value)[] args)
    {
        lock (Sync)
        {
            using var cmd = Command(sql, args);
            return cmd.ExecuteNonQuery();
        }
    }

    public object? Scalar(string sql, params (string Name, object? Value)[] args)
    {
        lock (Sync)
        {
            using var cmd = Command(sql, args);
            var value = cmd.ExecuteScalar();
            return value is DBNull ? null : value;
        }
    }

    /// <summary>Runs a query and maps each row. The whole read happens under the lock.</summary>
    public List<T> Query<T>(string sql, Func<SqliteDataReader, T> map, params (string Name, object? Value)[] args)
    {
        lock (Sync)
        {
            using var cmd = Command(sql, args);
            using var reader = cmd.ExecuteReader();
            var list = new List<T>();
            while (reader.Read()) list.Add(map(reader));
            return list;
        }
    }

    public SqliteCommand Command(string sql, params (string Name, object? Value)[] args)
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    public static string Iso(DateTime value) => value.ToString("yyyy-MM-dd HH:mm:ss");

    public static DateTime ParseIso(string value) =>
        DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d) ? d : DateTime.MinValue;

    public void Dispose()
    {
        lock (Sync)
        {
            try { Execute("PRAGMA optimize;"); } catch { }
            _connection.Dispose();
        }
    }
}
