using System.Text;
using Microsoft.Data.Sqlite;
using RadioClassic.Models;

namespace RadioClassic.Data;

/// <summary>Stations, favorites and playback history.</summary>
public sealed class StationRepository(Database db)
{
    private const string Columns =
        "s.id,s.uuid,s.name,s.url,s.homepage,s.favicon,s.country,s.countrycode,s.language,s.tags,s.codec,s.bitrate,s.votes,s.clicks,s.lastcheckok,s.hls,s.custom";

    private static Station Map(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        Uuid = r.IsDBNull(1) ? null : r.GetString(1),
        Name = r.GetString(2),
        Url = r.GetString(3),
        Homepage = r.GetString(4),
        Favicon = r.GetString(5),
        Country = r.GetString(6),
        CountryCode = r.GetString(7),
        Language = r.GetString(8),
        Tags = r.GetString(9),
        Codec = r.GetString(10),
        Bitrate = r.GetInt32(11),
        Votes = r.GetInt32(12),
        Clicks = r.GetInt32(13),
        LastCheckOk = r.GetInt32(14) != 0,
        IsHls = r.GetInt32(15) != 0,
        IsCustom = r.GetInt32(16) != 0,
    };

    // ---------------------------------------------------------------- queries

    public int Count() => Convert.ToInt32(db.Scalar("SELECT COUNT(*) FROM stations"));

    public Station? Get(long id) =>
        db.Query($"SELECT {Columns} FROM stations s WHERE s.id=@id", Map, ("@id", id)).FirstOrDefault();

    /// <summary>
    /// Returns lightweight rows for the station list. Only the columns shown in the list are
    /// loaded, and repeated strings (country, language, codec) are shared to keep memory low.
    /// </summary>
    public List<Station> Search(StationQuery q)
    {
        var sql = new StringBuilder("SELECT s.id,s.name,s.country,s.tags,s.language,s.codec,s.bitrate FROM stations s");
        var args = new List<(string, object?)>();
        var where = new List<string>();

        switch (q.View)
        {
            case StationView.Favorites:
                sql.Append(" JOIN favorites f ON f.station_id=s.id");
                if (q.FavoriteFolder.Length > 0)
                {
                    where.Add("f.folder=@folder");
                    args.Add(("@folder", q.FavoriteFolder));
                }
                break;
            case StationView.Recent:
                sql.Append(" JOIN (SELECT station_id, MAX(played_at) AS last FROM history GROUP BY station_id) h ON h.station_id=s.id");
                break;
            case StationView.Custom:
                where.Add("s.custom=1");
                break;
        }

        var words = q.Search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i < words.Length && i < 8; i++)
        {
            where.Add($"(s.name LIKE @w{i} ESCAPE '\\' OR s.country LIKE @w{i} ESCAPE '\\' OR s.tags LIKE @w{i} ESCAPE '\\' OR s.language LIKE @w{i} ESCAPE '\\')");
            args.Add(($"@w{i}", Like(words[i])));
        }
        if (q.Country.Length > 0) { where.Add("s.country=@country"); args.Add(("@country", q.Country)); }
        if (q.Language.Length > 0) { where.Add("s.language LIKE @lang ESCAPE '\\'"); args.Add(("@lang", Like(q.Language))); }
        if (q.Genre.Length > 0) { where.Add("s.tags LIKE @genre ESCAPE '\\'"); args.Add(("@genre", Like(q.Genre))); }
        if (q.Codec.Length > 0) { where.Add("s.codec=@codec COLLATE NOCASE"); args.Add(("@codec", q.Codec)); }
        if (q.MinBitrate > 0) { where.Add("s.bitrate>=@br"); args.Add(("@br", q.MinBitrate)); }

        if (where.Count > 0) sql.Append(" WHERE ").Append(string.Join(" AND ", where));

        var dir = q.Descending ? "DESC" : "ASC";
        sql.Append(" ORDER BY ");
        if (q.View == StationView.Recent && q.Sort == StationSort.Name && !q.Descending)
            sql.Append("h.last DESC");
        else
            sql.Append(q.Sort switch
            {
                StationSort.Country => $"s.country COLLATE NOCASE {dir}, s.name COLLATE NOCASE",
                StationSort.Genre => $"s.tags COLLATE NOCASE {dir}, s.name COLLATE NOCASE",
                StationSort.Language => $"s.language COLLATE NOCASE {dir}, s.name COLLATE NOCASE",
                StationSort.Bitrate => $"s.bitrate {dir}, s.name COLLATE NOCASE",
                StationSort.Votes => $"s.votes {dir}, s.name COLLATE NOCASE",
                StationSort.Clicks => $"s.clicks {dir}, s.name COLLATE NOCASE",
                _ => $"s.name COLLATE NOCASE {dir}"
            });

        var pool = new Dictionary<string, string>(StringComparer.Ordinal);
        string Shared(string s) => s.Length == 0 ? "" : pool.TryGetValue(s, out var v) ? v : pool[s] = s;

        return db.Query(sql.ToString(), r => new Station
        {
            Id = r.GetInt64(0),
            Name = r.GetString(1),
            Country = Shared(r.GetString(2)),
            Tags = Shared(r.GetString(3)),
            Language = Shared(r.GetString(4)),
            Codec = Shared(r.GetString(5)),
            Bitrate = r.GetInt32(6)
        }, [.. args]);
    }

    private static string Like(string term) =>
        "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

    public List<string> Countries() =>
        db.Query("SELECT DISTINCT country FROM stations WHERE country<>'' ORDER BY country COLLATE NOCASE", r => r.GetString(0));

    /// <summary>Most common single-language values (the raw field may contain comma separated lists).</summary>
    public List<string> Languages() =>
        db.Query("""
                 SELECT language FROM stations WHERE language<>'' AND language NOT LIKE '%,%'
                 GROUP BY language HAVING COUNT(*)>=5 ORDER BY language COLLATE NOCASE
                 """, r => Text.TitleCase(r.GetString(0)));

    // ---------------------------------------------------------------- remote sync

    /// <summary>Inserts or updates a batch of Radio-Browser stations in one transaction.</summary>
    public void UpsertRemote(IReadOnlyList<Station> batch, long generation)
    {
        lock (db.Sync)
        {
            using var tx = db.Connection.BeginTransaction();
            using var cmd = db.Command("""
                INSERT INTO stations(uuid,name,url,homepage,favicon,country,countrycode,language,tags,codec,bitrate,votes,clicks,lastcheckok,hls,custom,gen)
                VALUES(@uuid,@name,@url,@homepage,@favicon,@country,@cc,@language,@tags,@codec,@bitrate,@votes,@clicks,@ok,@hls,0,@gen)
                ON CONFLICT(uuid) DO UPDATE SET
                    name=excluded.name, url=excluded.url, homepage=excluded.homepage, favicon=excluded.favicon,
                    country=excluded.country, countrycode=excluded.countrycode, language=excluded.language,
                    tags=excluded.tags, codec=excluded.codec, bitrate=excluded.bitrate, votes=excluded.votes,
                    clicks=excluded.clicks, lastcheckok=excluded.lastcheckok, hls=excluded.hls, gen=excluded.gen
                """);
            cmd.Transaction = tx;
            string[] names = ["@uuid", "@name", "@url", "@homepage", "@favicon", "@country", "@cc", "@language", "@tags",
                "@codec", "@bitrate", "@votes", "@clicks", "@ok", "@hls", "@gen"];
            var p = names.Select(n => cmd.Parameters.Add(n, SqliteType.Text)).ToArray();
            cmd.Prepare();
            foreach (var s in batch)
            {
                p[0].Value = s.Uuid; p[1].Value = s.Name; p[2].Value = s.Url; p[3].Value = s.Homepage;
                p[4].Value = s.Favicon; p[5].Value = s.Country; p[6].Value = s.CountryCode; p[7].Value = s.Language;
                p[8].Value = s.Tags; p[9].Value = s.Codec; p[10].Value = s.Bitrate; p[11].Value = s.Votes;
                p[12].Value = s.Clicks; p[13].Value = s.LastCheckOk ? 1 : 0; p[14].Value = s.IsHls ? 1 : 0;
                p[15].Value = generation;
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
    }

    /// <summary>
    /// Removes remote stations that were not part of the latest sync. Favorites and custom
    /// stations are always kept, so user data survives database refreshes.
    /// </summary>
    public int FinishSync(long generation) =>
        db.Execute("""
                   DELETE FROM stations WHERE custom=0 AND gen<>@gen
                   AND id NOT IN (SELECT station_id FROM favorites)
                   """, ("@gen", generation));

    // ---------------------------------------------------------------- custom stations

    public long AddCustom(Station s)
    {
        lock (db.Sync)
        {
            db.Execute("""
                INSERT INTO stations(uuid,name,url,homepage,favicon,country,language,tags,codec,bitrate,custom)
                VALUES(NULL,@name,@url,@homepage,@favicon,@country,@language,@tags,@codec,@bitrate,1)
                """,
                ("@name", s.Name), ("@url", s.Url), ("@homepage", s.Homepage), ("@favicon", s.Favicon),
                ("@country", s.Country), ("@language", s.Language), ("@tags", s.Tags), ("@codec", s.Codec),
                ("@bitrate", s.Bitrate));
            s.Id = Convert.ToInt64(db.Scalar("SELECT last_insert_rowid()"));
            s.IsCustom = true;
            return s.Id;
        }
    }

    /// <summary>
    /// Saves edited station information. Editing a Radio-Browser station turns it into a custom
    /// station so the next database refresh does not overwrite the user's changes.
    /// </summary>
    public void Update(Station s)
    {
        db.Execute("""
            UPDATE stations SET name=@name,url=@url,homepage=@homepage,country=@country,language=@language,
                tags=@tags,custom=1,uuid=NULL WHERE id=@id
            """,
            ("@name", s.Name), ("@url", s.Url), ("@homepage", s.Homepage), ("@country", s.Country),
            ("@language", s.Language), ("@tags", s.Tags), ("@id", s.Id));
        s.IsCustom = true;
        s.Uuid = null;
    }

    public void Delete(long id) => db.Execute("DELETE FROM stations WHERE id=@id", ("@id", id));

    public Station? FindByUrl(string url) =>
        db.Query($"SELECT {Columns} FROM stations s WHERE s.url=@url LIMIT 1", Map, ("@url", url)).FirstOrDefault();

    // ---------------------------------------------------------------- favorites

    public bool IsFavorite(long id) =>
        db.Scalar("SELECT 1 FROM favorites WHERE station_id=@id", ("@id", id)) is not null;

    public void SetFavorite(long id, string folder = "") =>
        db.Execute("""
                   INSERT INTO favorites(station_id,folder,added) VALUES(@id,@folder,@added)
                   ON CONFLICT(station_id) DO UPDATE SET folder=excluded.folder
                   """, ("@id", id), ("@folder", folder), ("@added", Database.Iso(DateTime.Now)));

    public void RemoveFavorite(long id) => db.Execute("DELETE FROM favorites WHERE station_id=@id", ("@id", id));

    public List<Station> Favorites() =>
        db.Query($"""
                  SELECT {Columns},f.folder FROM stations s JOIN favorites f ON f.station_id=s.id
                  ORDER BY f.folder COLLATE NOCASE, s.name COLLATE NOCASE
                  """, r =>
        {
            var s = Map(r);
            s.IsFavorite = true;
            s.FavoriteFolder = r.GetString(17);
            return s;
        });

    public List<string> FavoriteFolders() =>
        db.Query("SELECT DISTINCT folder FROM favorites WHERE folder<>'' ORDER BY folder COLLATE NOCASE", r => r.GetString(0));

    public void RenameFavoriteFolder(string from, string to) =>
        db.Execute("UPDATE favorites SET folder=@to WHERE folder=@from", ("@from", from), ("@to", to));

    // ---------------------------------------------------------------- history

    public void AddHistory(Station s)
    {
        db.Execute("INSERT INTO history(station_id,station_name,played_at) VALUES(@id,@name,@at)",
            ("@id", s.Id), ("@name", s.Name), ("@at", Database.Iso(DateTime.Now)));
        // Keep the history bounded.
        db.Execute("DELETE FROM history WHERE id NOT IN (SELECT id FROM history ORDER BY id DESC LIMIT 1000)");
    }

    public List<HistoryEntry> History(int limit = 500) =>
        db.Query("SELECT station_id,station_name,played_at FROM history ORDER BY id DESC LIMIT @n", r => new HistoryEntry
        {
            StationId = r.GetInt64(0),
            StationName = r.GetString(1),
            PlayedAt = Database.ParseIso(r.GetString(2))
        }, ("@n", limit));

    public List<Station> Recent(int limit = 15) =>
        db.Query($"""
                  SELECT {Columns} FROM stations s
                  JOIN (SELECT station_id, MAX(played_at) AS last FROM history GROUP BY station_id) h ON h.station_id=s.id
                  ORDER BY h.last DESC LIMIT @n
                  """, Map, ("@n", limit));

    public void AddSong(string stationName, string title)
    {
        db.Execute("INSERT INTO songs(station_name,title,played_at) VALUES(@s,@t,@at)",
            ("@s", stationName), ("@t", title), ("@at", Database.Iso(DateTime.Now)));
        db.Execute("DELETE FROM songs WHERE id NOT IN (SELECT id FROM songs ORDER BY id DESC LIMIT 2000)");
    }

    public List<HistoryEntry> Songs(int limit = 500) =>
        db.Query("SELECT station_name,title,played_at FROM songs ORDER BY id DESC LIMIT @n", r => new HistoryEntry
        {
            StationName = r.GetString(0),
            Title = r.GetString(1),
            PlayedAt = Database.ParseIso(r.GetString(2))
        }, ("@n", limit));

    public void ClearHistory()
    {
        db.Execute("DELETE FROM history");
        db.Execute("DELETE FROM songs");
    }
}
