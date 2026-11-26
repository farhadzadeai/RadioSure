import Foundation

public final class RecordingRepository {
    private let db: SQLiteDatabase

    public init(db: SQLiteDatabase) {
        self.db = db
    }

    private static func mapEntry(_ row: SQLiteRow) -> RecordingEntry {
        RecordingEntry(
            id: row.int64(0),
            stationName: row.string(1),
            stationURL: row.string(2),
            title: row.string(3),
            file: row.string(4),
            format: row.string(5),
            started: row.date(6),
            ended: row.optionalDate(7),
            bytes: row.int64(8),
            status: row.string(9),
            error: row.string(10),
            scheduled: row.bool(11)
        )
    }

    @discardableResult
    public func insert(_ entry: RecordingEntry) throws -> Int64 {
        try db.execute("""
            INSERT INTO recordings(station_name,station_url,title,file,format,started,ended,bytes,status,error,scheduled)
            VALUES(?,?,?,?,?,?,?,?,?,?,?)
            """, [
                entry.stationName, entry.stationURL, entry.title, entry.file, entry.format,
                DatabaseDate.string(entry.started), entry.ended.map(DatabaseDate.string), entry.bytes,
                entry.status, entry.error, entry.scheduled
            ])
        return db.lastInsertRowID
    }

    public func update(_ entry: RecordingEntry) throws {
        try db.execute("""
            UPDATE recordings SET title=?,file=?,ended=?,bytes=?,status=?,error=? WHERE id=?
            """, [
                entry.title, entry.file, entry.ended.map(DatabaseDate.string), entry.bytes,
                entry.status, entry.error, entry.id
            ])
    }

    public func delete(_ id: Int64) throws {
        try db.execute("DELETE FROM recordings WHERE id=?", [id])
    }

    public func list(search: String = "") throws -> [RecordingEntry] {
        let like = "%\(search)%"
        return try db.query("""
            SELECT id,station_name,station_url,title,file,format,started,ended,bytes,status,error,scheduled
            FROM recordings
            WHERE ?='' OR station_name LIKE ? OR title LIKE ? OR file LIKE ?
            ORDER BY started DESC, id DESC
            """, [search, like, like, like], map: Self.mapEntry)
    }

    @discardableResult
    public func markInterrupted() throws -> Int {
        let open = try db.query("SELECT id,file FROM recordings WHERE status=?", [RecordingStatus.recording]) {
            (id: $0.int64(0), file: $0.string(1))
        }
        for item in open {
            var bytes: Int64 = 0
            var ended: Date?
            if let attributes = try? FileManager.default.attributesOfItem(atPath: item.file) {
                bytes = (attributes[.size] as? NSNumber)?.int64Value ?? 0
                ended = attributes[.modificationDate] as? Date
            }
            try db.execute("UPDATE recordings SET status=?, bytes=?, ended=? WHERE id=?",
                           [RecordingStatus.interrupted, bytes, ended.map(DatabaseDate.string), item.id])
        }
        return open.count
    }

    private static func mapSchedule(_ row: SQLiteRow) -> RecordingSchedule {
        RecordingSchedule(
            id: row.int64(0),
            stationID: row.int64(1),
            stationName: row.string(2),
            stationURL: row.string(3),
            start: row.date(4),
            durationMinutes: row.int(5),
            repeatMode: ScheduleRepeat(rawValue: row.int(6)) ?? .once,
            days: row.int(7),
            enabled: row.bool(8),
            lastRun: row.optionalDate(9)
        )
    }

    public func schedules() throws -> [RecordingSchedule] {
        try db.query("""
            SELECT id,station_id,station_name,station_url,start,duration_min,repeat,days,enabled,last_run
            FROM schedules ORDER BY start
            """, map: Self.mapSchedule)
    }

    public func saveSchedule(_ schedule: inout RecordingSchedule) throws {
        if schedule.id == 0 {
            try db.execute("""
                INSERT INTO schedules(station_id,station_name,station_url,start,duration_min,repeat,days,enabled,last_run)
                VALUES(?,?,?,?,?,?,?,?,?)
                """, [
                    schedule.stationID, schedule.stationName, schedule.stationURL, DatabaseDate.string(schedule.start),
                    schedule.durationMinutes, schedule.repeatMode.rawValue, schedule.days, schedule.enabled,
                    schedule.lastRun.map(DatabaseDate.string)
                ])
            schedule.id = db.lastInsertRowID
        } else {
            try db.execute("""
                UPDATE schedules SET station_id=?,station_name=?,station_url=?,start=?,duration_min=?,repeat=?,days=?,enabled=?,last_run=? WHERE id=?
                """, [
                    schedule.stationID, schedule.stationName, schedule.stationURL, DatabaseDate.string(schedule.start),
                    schedule.durationMinutes, schedule.repeatMode.rawValue, schedule.days, schedule.enabled,
                    schedule.lastRun.map(DatabaseDate.string), schedule.id
                ])
        }
    }

    public func deleteSchedule(_ id: Int64) throws {
        try db.execute("DELETE FROM schedules WHERE id=?", [id])
    }
}
