import XCTest
@testable import RadioClassicCore

final class CoreTests: XCTestCase {
    func testMetadataSplitsArtistAndTitle() {
        let cases: [(String, String, String)] = [
            ("Artist - Title", "Artist", "Title"),
            ("Just A Title", "", "Just A Title"),
            ("A - B - C", "A", "B - C"),
            ("", "", "")
        ]
        for (input, artist, title) in cases {
            let metadata = StreamMetadata.fromCombined(input)
            XCTAssertEqual(artist, metadata.artist)
            XCTAssertEqual(title, metadata.title)
        }
    }

    func testMetadataParsesIcyBlock() {
        let metadata = StreamMetadata.fromIcy("StreamTitle='Queen - Bohemian Rhapsody';StreamUrl='';")
        XCTAssertEqual("Queen", metadata.artist)
        XCTAssertEqual("Bohemian Rhapsody", metadata.title)
    }

    func testMetadataIcyHandlesApostrophes() {
        let metadata = StreamMetadata.fromIcy("StreamTitle='It's My Life - Bon Jovi';")
        XCTAssertFalse(metadata.isEmpty)
    }

    func testMetadataOggComments() {
        let metadata = StreamMetadata.fromComments(["ARTIST=Pink Floyd", "TITLE=Time"])
        XCTAssertEqual("Pink Floyd", metadata.artist)
        XCTAssertEqual("Time", metadata.title)
    }

    func testFileNamerSanitizesInvalidCharacters() {
        let name = RecordingFileNamer.sanitize("AC/DC: Back <in> Black? |*\"")
        for bad in ["/", ":", "<", ">", "?", "|", "*", "\""] {
            XCTAssertFalse(name.contains(bad))
        }
        XCTAssertFalse(name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
    }

    func testFileNamerTemplateExpansionAndEmptyTokens() {
        let when = Date(timeIntervalSince1970: 1_741_110_600)
        let expanded = RecordingFileNamer.expand(
            template: "{station}_{artist}_{title}",
            station: "Radio X",
            artist: "Queen",
            title: "Bohemian Rhapsody",
            when: when
        )
        XCTAssertTrue(expanded.contains("Radio X"))
        XCTAssertTrue(expanded.contains("Queen"))
        XCTAssertTrue(expanded.contains("Bohemian Rhapsody"))

        let sparse = RecordingFileNamer.expand(
            template: "{station}_{artist}_{title}",
            station: "Radio X",
            artist: "",
            title: "",
            when: when
        )
        XCTAssertFalse(sparse.contains("__"))
    }

    func testFileNamerDuplicatesGetUniqueSuffix() throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("rc-test-" + UUID().uuidString, isDirectory: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        let first = try RecordingFileNamer.uniquePath(directory: directory, baseName: "song", extension: ".mp3")
        try Data([1]).write(to: first)
        let second = try RecordingFileNamer.uniquePath(directory: directory, baseName: "song", extension: ".mp3")
        XCTAssertNotEqual(first, second)
        try Data([1]).write(to: second)
        let third = try RecordingFileNamer.uniquePath(directory: directory, baseName: "song", extension: ".mp3")
        XCTAssertNotEqual(first, third)
        XCTAssertNotEqual(second, third)
    }

    func testFormatting() {
        XCTAssertEqual("512 B", Formatting.size(512))
        XCTAssertEqual("1.0 MB", Formatting.size(1024 * 1024))
        XCTAssertEqual("02:03", Formatting.duration(123))
        XCTAssertEqual("1:00:01", Formatting.duration(3601))
    }

    func testPlaylistParsers() throws {
        let m3u = "#EXTM3U\n#EXTINF:-1,Test Radio\nhttps://example.com/stream.mp3\n"
        let m3uEntries = PlaylistIO.parseM3U(m3u)
        XCTAssertEqual(1, m3uEntries.count)
        XCTAssertEqual("Test Radio", m3uEntries[0].name)

        let pls = "[playlist]\nNumberOfEntries=1\nFile1=https://example.com/aac\nTitle1=PLS Radio\n"
        let plsEntries = PlaylistIO.parsePLS(pls)
        XCTAssertEqual(1, plsEntries.count)
        XCTAssertEqual("PLS Radio", plsEntries[0].name)

        let xspf = """
        <?xml version="1.0"?><playlist version="1"><trackList><track>
        <title>XSPF Radio</title><location>https://example.com/xspf</location>
        </track></trackList></playlist>
        """.data(using: .utf8)!
        let xspfEntries = PlaylistIO.parseXSPF(xspf)
        XCTAssertEqual(1, xspfEntries.count)
        XCTAssertEqual("XSPF Radio", xspfEntries[0].name)

        XCTAssertFalse(PlaylistIO.isValidStreamURL("javascript:alert(1)"))
        XCTAssertTrue(PlaylistIO.isValidStreamURL("https://example.com/stream"))
    }


    func testPlaylistExportersAndWindowsCompatibleJSON() throws {
        let station = Station(
            uuid: UUID().uuidString,
            name: "Test & Radio",
            url: "https://example.com/stream?a=1&b=2",
            homepage: "https://example.com",
            country: "Germany",
            language: "english",
            tags: "rock",
            codec: "MP3",
            bitrate: 128,
            isCustom: true,
            favoriteFolder: "Rock"
        )

        let m3u = PlaylistIO.exportM3U([station])
        XCTAssertTrue(m3u.contains("#EXTINF:-1,Test & Radio"))
        XCTAssertEqual(1, PlaylistIO.parseM3U(m3u).count)

        let pls = PlaylistIO.exportPLS([station])
        XCTAssertTrue(pls.contains("NumberOfEntries=1"))
        XCTAssertEqual(1, PlaylistIO.parsePLS(pls).count)

        let xspfText = PlaylistIO.exportXSPF([station])
        XCTAssertTrue(xspfText.contains("a=1&amp;b=2"))
        XCTAssertEqual(1, PlaylistIO.parseXSPF(Data(xspfText.utf8)).count)

        let json = try PlaylistIO.exportJSON([station])
        let decoded = try PlaylistIO.parseJSON(json)
        XCTAssertEqual(1, decoded.count)
        XCTAssertEqual("rock", decoded[0].tags)
        XCTAssertEqual("Rock", decoded[0].favoriteFolder)
        XCTAssertEqual(station.uuid, decoded[0].uuid)

        let windowsJSON = """
        [{"name":"Legacy Radio","url":"https://example.com/legacy","genre":"jazz","folder":"Jazz","uuid":"\(station.uuid ?? "")"}]
        """.data(using: .utf8)!
        let legacy = try PlaylistIO.parseJSON(windowsJSON)
        XCTAssertEqual("jazz", legacy[0].tags)
        XCTAssertEqual("Jazz", legacy[0].favoriteFolder)
    }

    func testSettingsRoundTripAndCorruptFallback() throws {
        let file = FileManager.default.temporaryDirectory.appendingPathComponent("rc-settings-" + UUID().uuidString + ".json")
        defer { try? FileManager.default.removeItem(at: file) }
        let store = SettingsStore(file: file)
        store.load()
        store.current.bufferMilliseconds = 4321
        store.current.theme = "DarkClassic"
        store.current.lastDatabaseSync = Date(timeIntervalSince1970: 1_700_000_000)
        store.save()

        let again = SettingsStore(file: file)
        again.load()
        XCTAssertEqual(4321, again.current.bufferMilliseconds)
        XCTAssertEqual("DarkClassic", again.current.theme)
        XCTAssertNotNil(again.current.lastDatabaseSync)

        try "{ this is not json !!!".write(to: file, atomically: true, encoding: .utf8)
        let corrupt = SettingsStore(file: file)
        corrupt.load()
        XCTAssertEqual(AppSettings().bufferMilliseconds, corrupt.current.bufferMilliseconds)
    }

    func testAudioTapCanBeCreated() {
        let monitor = AudioTapMonitor()
        XCTAssertEqual(noErr, monitor.creationStatus)
        XCTAssertNotNil(monitor.makeAudioMix())
    }
}
