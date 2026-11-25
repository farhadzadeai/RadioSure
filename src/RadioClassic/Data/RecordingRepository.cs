using Microsoft.Data.Sqlite;
using RadioClassic.Models;

namespace RadioClassic.Data;

/// <summary>Recording history and recording schedules.</summary>
public sealed class RecordingRepository(Database db)
{
    // ---------------------------------------------------------------- history

    private static RecordingEntry MapEntry(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        StationName = r.GetString(1),
        StationUrl = r.GetString(2),
        Title = r.GetString(3),
        File = r.GetString(4),
        Format = r.GetString(5),
        Started = Database.ParseIso(r.GetString(6)),
        Ended = r.IsDBNull(7) ? null : Database.ParseIso(r.GetString(7)),
        Bytes = r.GetInt64(8),
        Status = r.GetString(9),
        Error = r.GetString(10),
        Scheduled = r.GetInt32(11) != 0
    };

    public long Insert(RecordingEntry e)
    {
        lock (db.Sync)
        {
            db.Execute("""
                INSERT INTO recordings(station_name,station_url,title,file,format,started,ended,bytes,status,error,scheduled)
                VALUES(@sn,@su,@title,@file,@format,@started,@ended,@bytes,@status,@error,@scheduled)
                """,
                ("@sn", e.StationName), ("@su", e.StationUrl), ("@title", e.Title), ("@file", e.File),
                ("@format", e.Format), ("@started", Database.Iso(e.Started)),
                ("@ended", e.Ended is { } end ? Database.Iso(end) : null), ("@bytes", e.Bytes),
                ("@status", e.Status), ("@error", e.Error), ("@scheduled", e.Scheduled ? 1 : 0));
            e.Id = Convert.ToInt64(db.Scalar("SELECT last_insert_rowid()"));
            return e.Id;
        }
    }

    public void Update(RecordingEntry e) =>
        db.Execute("""
            UPDATE recordings SET title=@title,file=@file,ended=@ended,bytes=@bytes,status=@status,error=@error WHERE id=@id
            """,
            ("@title", e.Title), ("@file", e.File), ("@ended", e.Ended is { } end ? Database.Iso(end) : null),
            ("@bytes", e.Bytes), ("@status", e.Status), ("@error", e.Error), ("@id", e.Id));

    public void Delete(long id) => db.Execute("DELETE FROM recordings WHERE id=@id", ("@id", id));

    public List<RecordingEntry> List(string search = "")
    {
        const string sql = """
            SELECT id,station_name,station_url,title,file,format,started,ended,bytes,status,error,scheduled
            FROM recordings
            WHERE @q='' OR station_name LIKE @like OR title LIKE @like OR file LIKE @like
            ORDER BY started DESC, id DESC
            """;
        return db.Query(sql, MapEntry, ("@q", search), ("@like", "%" + search + "%"));
    }

    /// <summary>
    /// Called at startup: entries still marked "recording" belong to a session that did not end
    /// cleanly (crash, power loss). They are kept and flagged instead of being discarded.
    /// </summary>
    public int MarkInterrupted()
    {
        var open = db.Query("SELECT id,file FROM recordings WHERE status=@s", r => (Id: r.GetInt64(0), File: r.GetString(1)),
            ("@s", RecordingStatus.Recording));
        foreach (var (id, file) in open)
        {
            long bytes = 0;
            DateTime? ended = null;
            try
            {
                var info = new System.IO.FileInfo(file);
                if (info.Exists) { bytes = info.Length; ended = info.LastWriteTime; }
            }
            catch { }
            db.Execute("UPDATE recordings SET status=@s, bytes=@b, ended=@e WHERE id=@id",
                ("@s", RecordingStatus.Interrupted), ("@b", bytes),
                ("@e", ended is { } e ? Database.Iso(e) : null), ("@id", id));
        }
        return open.Count;
    }

    // ---------------------------------------------------------------- schedules

    private static RecordingSchedule MapSchedule(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        StationId = r.GetInt64(1),
        StationName = r.GetString(2),
        StationUrl = r.GetString(3),
        Start = Database.ParseIso(r.GetString(4)),
        DurationMinutes = r.GetInt32(5),
        Repeat = (ScheduleRepeat)r.GetInt32(6),
        Days = r.GetInt32(7),
        Enabled = r.GetInt32(8) != 0,
        LastRun = r.IsDBNull(9) ? null : Database.ParseIso(r.GetString(9))
    };

    public List<RecordingSchedule> Schedules() =>
        db.Query("""
                 SELECT id,station_id,station_name,station_url,start,duration_min,repeat,days,enabled,last_run
                 FROM schedules ORDER BY start
                 """, MapSchedule);

    public void SaveSchedule(RecordingSchedule s)
    {
        lock (db.Sync)
        {
            var args = new (string, object?)[]
            {
                ("@sid", s.StationId), ("@sn", s.StationName), ("@su", s.StationUrl), ("@start", Database.Iso(s.Start)),
                ("@dur", s.DurationMinutes), ("@repeat", (int)s.Repeat), ("@days", s.Days), ("@enabled", s.Enabled ? 1 : 0),
                ("@last", s.LastRun is { } l ? Database.Iso(l) : null), ("@id", s.Id)
            };
            if (s.Id == 0)
            {
                db.Execute("""
                    INSERT INTO schedules(station_id,station_name,station_url,start,duration_min,repeat,days,enabled,last_run)
                    VALUES(@sid,@sn,@su,@start,@dur,@repeat,@days,@enabled,@last)
                    """, args[..^1]);
                s.Id = Convert.ToInt64(db.Scalar("SELECT last_insert_rowid()"));
            }
            else
            {
                db.Execute("""
                    UPDATE schedules SET station_id=@sid,station_name=@sn,station_url=@su,start=@start,duration_min=@dur,
                        repeat=@repeat,days=@days,enabled=@enabled,last_run=@last WHERE id=@id
                    """, args);
            }
        }
    }

    public void DeleteSchedule(long id) => db.Execute("DELETE FROM schedules WHERE id=@id", ("@id", id));
}
