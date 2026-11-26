import Foundation
import SQLite3

public final class StationRepository {
    private let db: SQLiteDatabase

    private static let columns = "s.id,s.uuid,s.name,s.url,s.homepage,s.favicon,s.country,s.countrycode,s.language,s.tags,s.codec,s.bitrate,s.votes,s.clicks,s.lastcheckok,s.hls,s.custom"

    public init(db: SQLiteDatabase) {
        self.db = db
    }

    private static func mapFull(_ row: SQLiteRow) -> Station {
        Station(
            id: row.int64(0),
            uuid: row.optionalString(1),
            name: row.string(2),
            url: row.string(3),
            homepage: row.string(4),
            favicon: row.string(5),
            country: row.string(6),
            countryCode: row.string(7),
            language: row.string(8),
            tags: row.string(9),
            codec: row.string(10),
            bitrate: row.int(11),
            votes: row.int(12),
            clicks: row.int(13),
            lastCheckOK: row.bool(14),
            isHLS: row.bool(15),
            isCustom: row.bool(16)
        )
    }

    public func count() throws -> Int {
        Int(try db.scalarInt("SELECT COUNT(*) FROM stations"))
    }

    public func get(_ id: Int64) throws -> Station? {
        try db.query("SELECT \(Self.columns) FROM stations s WHERE s.id=?", [id], map: Self.mapFull).first
    }

    public func search(_ query: StationQuery) throws -> [Station] {
        var sql = "SELECT s.id,s.name,s.country,s.tags,s.language,s.codec,s.bitrate,s.votes,s.clicks FROM stations s"
        var bindings: [Any?] = []
        var whereClauses: [String] = []

        switch query.view {
        case .favorites:
            sql += " JOIN favorites f ON f.station_id=s.id"
            if !query.favoriteFolder.isEmpty {
                whereClauses.append("f.folder=?")
                bindings.append(query.favoriteFolder)
            }
        case .recent:
            sql += " JOIN (SELECT station_id, MAX(played_at) AS last FROM history GROUP BY station_id) h ON h.station_id=s.id"
        case .custom:
            whereClauses.append("s.custom=1")
        case .all:
            break
        }

        let words = query.search.split(whereSeparator: { $0 == " " || $0 == "\t" || $0 == "\n" }).map(String.init)
        for word in words.prefix(8) {
            whereClauses.append("(s.name LIKE ? ESCAPE '\\' OR s.country LIKE ? ESCAPE '\\' OR s.tags LIKE ? ESCAPE '\\' OR s.language LIKE ? ESCAPE '\\')")
            let pattern = likePattern(word)
            bindings.append(contentsOf: [pattern, pattern, pattern, pattern])
        }
        if !query.country.isEmpty {
            whereClauses.append("s.country=?")
            bindings.append(query.country)
        }
        if !query.language.isEmpty {
            whereClauses.append("s.language LIKE ? ESCAPE '\\'")
            bindings.append(likePattern(query.language))
        }
        if !query.genre.isEmpty {
            whereClauses.append("s.tags LIKE ? ESCAPE '\\'")
            bindings.append(likePattern(query.genre))
        }
        if !query.codec.isEmpty {
            whereClauses.append("s.codec=? COLLATE NOCASE")
            bindings.append(query.codec)
        }
        if query.minBitrate > 0 {
            whereClauses.append("s.bitrate>=?")
            bindings.append(query.minBitrate)
        }
        if !whereClauses.isEmpty {
            sql += " WHERE " + whereClauses.joined(separator: " AND ")
        }

        let direction = query.descending ? "DESC" : "ASC"
        sql += " ORDER BY "
        if query.view == .recent && query.sort == .name && !query.descending {
            sql += "h.last DESC"
        } else {
            switch query.sort {
            case .country:
                sql += "s.country COLLATE NOCASE \(direction), s.name COLLATE NOCASE"
            case .genre:
                sql += "s.tags COLLATE NOCASE \(direction), s.name COLLATE NOCASE"
            case .language:
                sql += "s.language COLLATE NOCASE \(direction), s.name COLLATE NOCASE"
            case .codec:
                sql += "s.codec COLLATE NOCASE \(direction), s.name COLLATE NOCASE"
            case .bitrate:
                sql += "s.bitrate \(direction), s.name COLLATE NOCASE"
            case .votes:
                sql += "s.votes \(direction), s.name COLLATE NOCASE"
            case .clicks:
                sql += "s.clicks \(direction), s.name COLLATE NOCASE"
            case .name:
                sql += "s.name COLLATE NOCASE \(direction)"
            }
        }

        return try db.query(sql, bindings) { row in
            Station(
                id: row.int64(0),
                name: row.string(1),
                country: row.string(2),
                language: row.string(4),
                tags: row.string(3),
                codec: row.string(5),
                bitrate: row.int(6),
                votes: row.int(7),
                clicks: row.int(8)
            )
        }
    }

    private func likePattern(_ term: String) -> String {
        let escaped = term
            .replacingOccurrences(of: "\\", with: "\\\\")
            .replacingOccurrences(of: "%", with: "\\%")
            .replacingOccurrences(of: "_", with: "\\_")
        return "%\(escaped)%"
    }

    public func countries() throws -> [String] {
        try db.query("SELECT DISTINCT country FROM stations WHERE country<>'' ORDER BY country COLLATE NOCASE") { $0.string(0) }
    }

    public func languages() throws -> [String] {
        try db.query("""
            SELECT language FROM stations WHERE language<>'' AND language NOT LIKE '%,%'
            GROUP BY language HAVING COUNT(*)>=1 ORDER BY language COLLATE NOCASE
            """) { TextUtilities.titleCase($0.string(0)) }
    }

    public func codecs() throws -> [String] {
        try db.query("SELECT DISTINCT codec FROM stations WHERE codec<>'' ORDER BY codec COLLATE NOCASE") { $0.string(0).uppercased() }
    }

    public func upsertRemote(_ batch: [Station], generation: Int64) throws {
        guard !batch.isEmpty else { return }
        let sql = """
            INSERT INTO stations(uuid,name,url,homepage,favicon,country,countrycode,language,tags,codec,bitrate,votes,clicks,lastcheckok,hls,custom,gen)
            VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,0,?)
            ON CONFLICT(uuid) DO UPDATE SET
                name=excluded.name, url=excluded.url, homepage=excluded.homepage, favicon=excluded.favicon,
                country=excluded.country, countrycode=excluded.countrycode, language=excluded.language,
                tags=excluded.tags, codec=excluded.codec, bitrate=excluded.bitrate, votes=excluded.votes,
                clicks=excluded.clicks, lastcheckok=excluded.lastcheckok, hls=excluded.hls, gen=excluded.gen
            """
        try db.transaction {
            try db.withStatement(sql) { statement in
                for station in batch {
                    sqlite3_reset(statement)
                    sqlite3_clear_bindings(statement)
                    try db.bindText(station.uuid, index: 1, to: statement)
                    try db.bindValue(station.name, index: 2, to: statement)
                    try db.bindValue(station.url, index: 3, to: statement)
                    try db.bindValue(station.homepage, index: 4, to: statement)
                    try db.bindValue(station.favicon, index: 5, to: statement)
                    try db.bindValue(station.country, index: 6, to: statement)
                    try db.bindValue(station.countryCode, index: 7, to: statement)
                    try db.bindValue(station.language, index: 8, to: statement)
                    try db.bindValue(station.tags, index: 9, to: statement)
                    try db.bindValue(station.codec, index: 10, to: statement)
                    try db.bindValue(station.bitrate, index: 11, to: statement)
                    try db.bindValue(station.votes, index: 12, to: statement)
                    try db.bindValue(station.clicks, index: 13, to: statement)
                    try db.bindValue(station.lastCheckOK, index: 14, to: statement)
                    try db.bindValue(station.isHLS, index: 15, to: statement)
                    try db.bindValue(generation, index: 16, to: statement)
                    let result = sqlite3_step(statement)
                    if result != SQLITE_DONE {
                        throw SQLiteError(message: "station upsert failed", code: result)
                    }
                }
            }
        }
    }

    @discardableResult
    public func finishSync(generation: Int64) throws -> Int {
        try db.execute("""
            DELETE FROM stations WHERE custom=0 AND gen<>?
            AND id NOT IN (SELECT station_id FROM favorites)
            """, [generation])
    }

    @discardableResult
    public func addCustom(_ station: Station) throws -> Int64 {
        try db.execute("""
            INSERT INTO stations(uuid,name,url,homepage,favicon,country,language,tags,codec,bitrate,custom)
            VALUES(NULL,?,?,?,?,?,?,?,?,?,1)
            """, [station.name, station.url, station.homepage, station.favicon, station.country, station.language, station.tags, station.codec, station.bitrate])
        return db.lastInsertRowID
    }

    public func update(_ station: Station) throws {
        try db.execute("""
            UPDATE stations SET name=?,url=?,homepage=?,country=?,language=?,tags=?,custom=1,uuid=NULL WHERE id=?
            """, [station.name, station.url, station.homepage, station.country, station.language, station.tags, station.id])
    }

    public func delete(_ id: Int64) throws {
        try db.execute("DELETE FROM stations WHERE id=?", [id])
    }

    public func findByURL(_ url: String) throws -> Station? {
        try db.query("SELECT \(Self.columns) FROM stations s WHERE s.url=? LIMIT 1", [url], map: Self.mapFull).first
    }

    public func isFavorite(_ id: Int64) throws -> Bool {
        try db.scalar("SELECT 1 FROM favorites WHERE station_id=?", [id]) != nil
    }

    public func setFavorite(_ id: Int64, folder: String = "") throws {
        try db.execute("""
            INSERT INTO favorites(station_id,folder,added) VALUES(?,?,?)
            ON CONFLICT(station_id) DO UPDATE SET folder=excluded.folder
            """, [id, folder, DatabaseDate.string(Date())])
    }

    public func removeFavorite(_ id: Int64) throws {
        try db.execute("DELETE FROM favorites WHERE station_id=?", [id])
    }

    public func favorites() throws -> [Station] {
        try db.query("""
            SELECT \(Self.columns),f.folder FROM stations s JOIN favorites f ON f.station_id=s.id
            ORDER BY f.folder COLLATE NOCASE, s.name COLLATE NOCASE
            """) { row in
            var station = Self.mapFull(row)
            station.isFavorite = true
            station.favoriteFolder = row.string(17)
            return station
        }
    }

    public func favoriteFolders() throws -> [String] {
        try db.query("SELECT DISTINCT folder FROM favorites WHERE folder<>'' ORDER BY folder COLLATE NOCASE") { $0.string(0) }
    }

    public func renameFavoriteFolder(from oldName: String, to newName: String) throws {
        try db.execute("UPDATE favorites SET folder=? WHERE folder=?", [newName, oldName])
    }

    public func addHistory(_ station: Station) throws {
        try db.execute("INSERT INTO history(station_id,station_name,played_at) VALUES(?,?,?)",
                       [station.id, station.name, DatabaseDate.string(Date())])
        try db.execute("DELETE FROM history WHERE id NOT IN (SELECT id FROM history ORDER BY id DESC LIMIT 1000)")
    }

    public func history(limit: Int = 500) throws -> [HistoryEntry] {
        try db.query("SELECT station_id,station_name,played_at FROM history ORDER BY id DESC LIMIT ?", [limit]) {
            HistoryEntry(stationID: $0.int64(0), stationName: $0.string(1), playedAt: $0.date(2))
        }
    }

    public func recent(limit: Int = 15) throws -> [Station] {
        try db.query("""
            SELECT \(Self.columns) FROM stations s
            JOIN (SELECT station_id, MAX(played_at) AS last FROM history GROUP BY station_id) h ON h.station_id=s.id
            ORDER BY h.last DESC LIMIT ?
            """, [limit], map: Self.mapFull)
    }

    public func addSong(stationName: String, title: String) throws {
        try db.execute("INSERT INTO songs(station_name,title,played_at) VALUES(?,?,?)",
                       [stationName, title, DatabaseDate.string(Date())])
        try db.execute("DELETE FROM songs WHERE id NOT IN (SELECT id FROM songs ORDER BY id DESC LIMIT 2000)")
    }

    public func songs(limit: Int = 500) throws -> [HistoryEntry] {
        try db.query("SELECT station_name,title,played_at FROM songs ORDER BY id DESC LIMIT ?", [limit]) {
            HistoryEntry(stationName: $0.string(0), title: $0.string(1), playedAt: $0.date(2))
        }
    }

    public func clearHistory() throws {
        try db.execute("DELETE FROM history")
        try db.execute("DELETE FROM songs")
    }
}
