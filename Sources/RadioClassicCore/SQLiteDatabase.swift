import Foundation
import SQLite3

public struct SQLiteError: Error, CustomStringConvertible {
    public let message: String
    public let code: Int32

    public var description: String { "SQLite \(code): \(message)" }
}

public final class SQLiteDatabase {
    private let lock = NSRecursiveLock()
    private var db: OpaquePointer?

    private static let sqliteTransient: sqlite3_destructor_type = unsafeBitCast(-1, to: sqlite3_destructor_type.self)

    private static let migrations: [String] = [
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
        CREATE INDEX ix_stations_gen ON stations(gen);
        CREATE INDEX ix_stations_custom ON stations(custom);

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
        """
    ]

    public init(file: URL? = nil) throws {
        let path = file?.path ?? AppPaths.databaseFile.path
        if path != ":memory:" {
            try FileManager.default.createDirectory(atPath: URL(fileURLWithPath: path).deletingLastPathComponent().path, withIntermediateDirectories: true)
        }
        var handle: OpaquePointer?
        let flags = SQLITE_OPEN_CREATE | SQLITE_OPEN_READWRITE | SQLITE_OPEN_FULLMUTEX
        guard sqlite3_open_v2(path, &handle, flags, nil) == SQLITE_OK, let opened = handle else {
            let message = handle.flatMap { sqlite3_errmsg($0) }.map { String(cString: $0) } ?? "open failed"
            if let handle { sqlite3_close(handle) }
            throw SQLiteError(message: message, code: SQLITE_CANTOPEN)
        }
        db = opened
        try executeDirect("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON;")
        try migrate()
    }

    deinit {
        if let db { sqlite3_close_v2(db) }
    }

    private func migrate() throws {
        let version = Int(try scalarInt("PRAGMA user_version"))
        guard version < Self.migrations.count else { return }
        for index in version..<Self.migrations.count {
            try transaction {
                try executeDirect(Self.migrations[index])
                try executeDirect("PRAGMA user_version=\(index + 1)")
            }
        }
    }

    private func executeDirect(_ sql: String) throws {
        lock.lock()
        defer { lock.unlock() }
        var errorPointer: UnsafeMutablePointer<CChar>?
        let result = sqlite3_exec(db, sql, nil, nil, &errorPointer)
        if result != SQLITE_OK {
            let message = errorPointer.map { String(cString: $0) } ?? lastErrorMessage()
            if let errorPointer { sqlite3_free(errorPointer) }
            throw SQLiteError(message: message, code: result)
        }
    }

    private func lastErrorMessage() -> String {
        guard let db, let message = sqlite3_errmsg(db) else { return "unknown error" }
        return String(cString: message)
    }

    @discardableResult
    public func execute(_ sql: String, _ bindings: [Any?] = []) throws -> Int {
        try withStatement(sql) { statement in
            try bind(bindings, to: statement)
            let result = sqlite3_step(statement)
            guard result == SQLITE_DONE else { throw SQLiteError(message: lastErrorMessage(), code: result) }
            return Int(sqlite3_changes(db))
        }
    }

    public func scalar(_ sql: String, _ bindings: [Any?] = []) throws -> Any? {
        try withStatement(sql) { statement in
            try bind(bindings, to: statement)
            let result = sqlite3_step(statement)
            if result == SQLITE_DONE { return nil }
            guard result == SQLITE_ROW else { throw SQLiteError(message: lastErrorMessage(), code: result) }
            return columnValue(statement, 0)
        }
    }

    public func scalarInt(_ sql: String, _ bindings: [Any?] = []) throws -> Int64 {
        if let value = try scalar(sql, bindings) as? Int64 { return value }
        if let value = try scalar(sql, bindings) as? Int { return Int64(value) }
        return 0
    }

    public func query<T>(_ sql: String, _ bindings: [Any?] = [], map: (SQLiteRow) throws -> T) throws -> [T] {
        try withStatement(sql) { statement in
            try bind(bindings, to: statement)
            var rows: [T] = []
            while true {
                let result = sqlite3_step(statement)
                if result == SQLITE_DONE { break }
                guard result == SQLITE_ROW else { throw SQLiteError(message: lastErrorMessage(), code: result) }
                rows.append(try map(SQLiteRow(statement: statement)))
            }
            return rows
        }
    }

    public func transaction<T>(_ block: () throws -> T) throws -> T {
        lock.lock()
        defer { lock.unlock() }
        try executeDirect("BEGIN IMMEDIATE TRANSACTION")
        do {
            let value = try block()
            try executeDirect("COMMIT")
            return value
        } catch {
            try? executeDirect("ROLLBACK")
            throw error
        }
    }

    public func withStatement<T>(_ sql: String, _ body: (OpaquePointer) throws -> T) throws -> T {
        lock.lock()
        defer { lock.unlock() }
        var statement: OpaquePointer?
        let result = sqlite3_prepare_v2(db, sql, -1, &statement, nil)
        guard result == SQLITE_OK, let statement else {
            if let statement { sqlite3_finalize(statement) }
            throw SQLiteError(message: lastErrorMessage(), code: result)
        }
        defer { sqlite3_finalize(statement) }
        return try body(statement)
    }

    public func bind(_ values: [Any?], to statement: OpaquePointer) throws {
        for (offset, value) in values.enumerated() {
            try bindValue(value, index: Int32(offset + 1), to: statement)
        }
    }

    public func bindValue(_ value: Any?, index: Int32, to statement: OpaquePointer) throws {
        let result: Int32
        switch value {
        case nil:
            result = sqlite3_bind_null(statement, index)
        case let string as String:
            result = sqlite3_bind_text(statement, index, string, -1, Self.sqliteTransient)
        case let int as Int:
            result = sqlite3_bind_int64(statement, index, Int64(int))
        case let int as Int64:
            result = sqlite3_bind_int64(statement, index, int)
        case let int as Int32:
            result = sqlite3_bind_int(statement, index, int)
        case let double as Double:
            result = sqlite3_bind_double(statement, index, double)
        case let bool as Bool:
            result = sqlite3_bind_int(statement, index, bool ? 1 : 0)
        case let date as Date:
            let string = DatabaseDate.string(date)
            result = sqlite3_bind_text(statement, index, string, -1, Self.sqliteTransient)
        default:
            let string = String(describing: value!)
            result = sqlite3_bind_text(statement, index, string, -1, Self.sqliteTransient)
        }
        guard result == SQLITE_OK else { throw SQLiteError(message: lastErrorMessage(), code: result) }
    }

    public func bindText(_ value: String?, index: Int32, to statement: OpaquePointer) throws {
        if let value {
            try bindValue(value, index: index, to: statement)
        } else {
            try bindValue(nil, index: index, to: statement)
        }
    }

    private func columnValue(_ statement: OpaquePointer, _ index: Int32) -> Any? {
        switch sqlite3_column_type(statement, index) {
        case SQLITE_NULL:
            return nil
        case SQLITE_INTEGER:
            return sqlite3_column_int64(statement, index)
        case SQLITE_FLOAT:
            return sqlite3_column_double(statement, index)
        case SQLITE_TEXT:
            guard let pointer = sqlite3_column_text(statement, index) else { return "" }
            return String(cString: pointer)
        case SQLITE_BLOB:
            guard let pointer = sqlite3_column_blob(statement, index) else { return Data() }
            return Data(bytes: pointer, count: Int(sqlite3_column_bytes(statement, index)))
        default:
            return nil
        }
    }

    public var lastInsertRowID: Int64 {
        sqlite3_last_insert_rowid(db)
    }
}

public struct SQLiteRow {
    let statement: OpaquePointer

    public func isNull(_ index: Int) -> Bool {
        sqlite3_column_type(statement, Int32(index)) == SQLITE_NULL
    }

    public func string(_ index: Int) -> String {
        guard let pointer = sqlite3_column_text(statement, Int32(index)) else { return "" }
        return String(cString: pointer)
    }

    public func optionalString(_ index: Int) -> String? {
        isNull(index) ? nil : string(index)
    }

    public func int(_ index: Int) -> Int {
        Int(sqlite3_column_int(statement, Int32(index)))
    }

    public func int64(_ index: Int) -> Int64 {
        sqlite3_column_int64(statement, Int32(index))
    }

    public func double(_ index: Int) -> Double {
        sqlite3_column_double(statement, Int32(index))
    }

    public func bool(_ index: Int) -> Bool {
        int(index) != 0
    }

    public func date(_ index: Int) -> Date {
        DatabaseDate.parse(string(index)) ?? .distantPast
    }

    public func optionalDate(_ index: Int) -> Date? {
        isNull(index) ? nil : date(index)
    }
}
