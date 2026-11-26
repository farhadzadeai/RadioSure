import XCTest
@testable import RadioClassicCore

final class DatabaseTests: XCTestCase {
    private func temporaryDatabase() throws -> (SQLiteDatabase, URL) {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("rc-db-" + UUID().uuidString + ".db")
        return (try SQLiteDatabase(file: url), url)
    }

    override func tearDown() {
        super.tearDown()
    }

    private func cleanup(_ url: URL) {
        try? FileManager.default.removeItem(at: url)
        try? FileManager.default.removeItem(at: url.appendingPathExtension("wal"))
        try? FileManager.default.removeItem(at: url.appendingPathExtension("shm"))
    }

    func testStationCrudFavoritesAndHistory() throws {
        let (db, file) = try temporaryDatabase()
        defer { cleanup(file) }
        let repository = StationRepository(db: db)
        let id = try repository.addCustom(Station(name: "Test FM", url: "https://example.com/x", country: "Germany"))
        XCTAssertGreaterThan(id, 0)
        var station = try XCTUnwrap(repository.get(id))
        XCTAssertEqual("Test FM", station.name)

        try repository.setFavorite(id)
        XCTAssertTrue(try repository.isFavorite(id))
        XCTAssertEqual(1, try repository.favorites().count)
        try repository.removeFavorite(id)
        XCTAssertFalse(try repository.isFavorite(id))

        station.name = "Renamed"
        try repository.update(station)
        XCTAssertEqual("Renamed", try XCTUnwrap(repository.get(id)).name)

        try repository.addHistory(station)
        XCTAssertEqual(1, try repository.history().count)
        try repository.clearHistory()
        XCTAssertTrue(try repository.history().isEmpty)
    }

    func testSearchFindsByNameAndFilters() throws {
        let (db, file) = try temporaryDatabase()
        defer { cleanup(file) }
        let repository = StationRepository(db: db)
        _ = try repository.addCustom(Station(name: "Jazz Lounge", url: "https://example.com/1", country: "France", language: "english", tags: "jazz", codec: "MP3", bitrate: 128))
        _ = try repository.addCustom(Station(name: "Rock Antenne", url: "https://example.com/2", country: "Germany", language: "german", tags: "rock", codec: "AAC", bitrate: 64))

        var query = StationQuery()
        query.search = "jazz"
        let jazz = try repository.search(query)
        XCTAssertEqual(1, jazz.count)
        XCTAssertEqual("Jazz Lounge", jazz[0].name)

        query = StationQuery()
        query.country = "Germany"
        let germany = try repository.search(query)
        XCTAssertEqual(1, germany.count)
        XCTAssertEqual("Rock Antenne", germany[0].name)

        query = StationQuery()
        query.minBitrate = 100
        XCTAssertEqual(1, try repository.search(query).count)
    }


    func testSearchSupportsMultipleWordsAndSorting() throws {
        let (db, file) = try temporaryDatabase()
        defer { cleanup(file) }
        let repository = StationRepository(db: db)
        try repository.upsertRemote([
            Station(uuid: "a", name: "Alpha Jazz", url: "https://example.com/a", country: "Germany", language: "english", tags: "jazz,smooth", codec: "MP3", bitrate: 128, votes: 4),
            Station(uuid: "b", name: "Beta Rock", url: "https://example.com/b", country: "France", language: "french", tags: "rock", codec: "AAC", bitrate: 192, votes: 9),
            Station(uuid: "c", name: "Gamma Jazz Club", url: "https://example.com/c", country: "Germany", language: "english", tags: "jazz,club", codec: "MP3", bitrate: 320, votes: 7)
        ], generation: 1)

        var query = StationQuery()
        query.search = "jazz germany"
        let results = try repository.search(query)
        XCTAssertEqual(2, results.count)
        XCTAssertTrue(results.allSatisfy { $0.tags.contains("jazz") && $0.country == "Germany" })

        query.search = ""
        query.sort = .bitrate
        query.descending = true
        XCTAssertEqual([320, 192, 128], try repository.search(query).map(\.bitrate))
    }

    func testRemoteUpsertAndPrune() throws {
        let (db, file) = try temporaryDatabase()
        defer { cleanup(file) }
        let repository = StationRepository(db: db)
        let generation: Int64 = 100
        try repository.upsertRemote([
            Station(uuid: "one", name: "One", url: "https://example.com/1", votes: 10),
            Station(uuid: "two", name: "Two", url: "https://example.com/2", votes: 20)
        ], generation: generation)
        XCTAssertEqual(2, try repository.count())

        try repository.upsertRemote([
            Station(uuid: "one", name: "One Updated", url: "https://example.com/1", votes: 11)
        ], generation: generation + 1)
        try repository.finishSync(generation: generation + 1)

        XCTAssertEqual(1, try repository.count())
        var query = StationQuery()
        query.search = "updated"
        XCTAssertEqual("One Updated", try repository.search(query).first?.name)
    }

    func testRecordingRepositoryAndSchedules() throws {
        let (db, file) = try temporaryDatabase()
        defer { cleanup(file) }
        let repository = RecordingRepository(db: db)
        let id = try repository.insert(RecordingEntry(stationName: "Radio", stationURL: "https://example.com", file: "/tmp/a.mp3", status: RecordingStatus.recording))
        XCTAssertGreaterThan(id, 0)
        XCTAssertEqual(1, try repository.markInterrupted())
        let entry = try XCTUnwrap(repository.list().first)
        XCTAssertEqual(RecordingStatus.interrupted, entry.status)

        var schedule = RecordingSchedule(stationID: 1, stationName: "Radio", stationURL: "https://example.com", durationMinutes: 30)
        try repository.saveSchedule(&schedule)
        XCTAssertGreaterThan(schedule.id, 0)
        XCTAssertEqual(1, try repository.schedules().count)
        schedule.enabled = false
        try repository.saveSchedule(&schedule)
        XCTAssertFalse(try XCTUnwrap(repository.schedules().first).enabled)
    }
}
