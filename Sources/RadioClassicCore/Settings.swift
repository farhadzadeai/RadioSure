import Foundation

public enum RecordingFormat: String, Codable, CaseIterable, Sendable {
    case original
    case mp3
    case wav
}

public enum ProxyMode: String, Codable, CaseIterable, Sendable {
    case system
    case none
    case manual
}

public struct EqSettings: Codable, Equatable, Sendable {
    public var enabled = false
    public var preset = "Flat"
    public var bands = Array(repeating: 0.0, count: 10)
    public var bass = 0.0
    public var treble = 0.0
    public var preamp = 0.0
    public var balance = 0.0

    public init() {}
}

public struct EqPreset: Codable, Equatable, Sendable {
    public var name = ""
    public var bands = Array(repeating: 0.0, count: 10)
    public var bass = 0.0
    public var treble = 0.0
    public var preamp = 0.0

    public init() {}

    public init(name: String, bands: [Double], bass: Double = 0, treble: Double = 0, preamp: Double = 0) {
        self.name = name
        self.bands = bands
        self.bass = bass
        self.treble = treble
        self.preamp = preamp
    }

    public static let builtIn: [EqPreset] = [
        EqPreset(name: "Flat", bands: Array(repeating: 0, count: 10)),
        EqPreset(name: "Rock", bands: [5, 4, 3, 1, -1, -1, 1, 3, 4, 5]),
        EqPreset(name: "Pop", bands: [-1, 1, 3, 4, 4, 3, 1, 0, -1, -1]),
        EqPreset(name: "Jazz", bands: [3, 2, 1, 2, -1, -1, 0, 1, 2, 3]),
        EqPreset(name: "Classical", bands: [4, 3, 2, 1, -1, -1, 0, 2, 3, 4]),
        EqPreset(name: "Vocal", bands: [-3, -2, -1, 1, 4, 4, 3, 1, 0, -2]),
        EqPreset(name: "Bass Boost", bands: [7, 6, 5, 3, 1, 0, 0, 0, 0, 0])
    ]
}

public struct AppSettings: Codable, Equatable, Sendable {
    public var startAtLogin = false
    public var startMinimized = false
    public var minimizeToMenuBar = true
    public var closeToMenuBar = true
    public var rememberLastStation = true
    public var resumeOnStart = false
    public var volume = 70
    public var muted = false
    public var language = "en"
    public var showNotifications = true

    public var bufferMilliseconds = 5000
    public var autoReconnect = true
    public var retryAttempts = 5

    public var recordingDirectory = AppPaths.defaultRecordingDirectory.path
    public var recordingFormat: RecordingFormat = .original
    public var mp3Bitrate = 192
    public var splitTracks = true
    public var trackFileTemplate = "{station}_{artist}_{title}_{date}"
    public var continuousFileTemplate = "{station}_{date}_{time}"
    public var stationSubfolders = true
    public var maxConcurrentRecordings = 4
    public var notifyScheduledRecordings = true

    public var theme = "Classic"
    public var accentColor: String?
    public var compactMode = false
    public var compactAlwaysOnTop = false
    public var showSpectrum = true
    public var showFilters = false

    public var timeoutSeconds = 10
    public var proxyMode: ProxyMode = .system
    public var proxyHost = ""
    public var proxyPort = 8080
    public var databaseRefreshDays = 7
    public var reportClicks = false

    public var globalHotkeys = true
    public var mediaKeys = true

    public var equalizer = EqSettings()
    public var customPresets: [EqPreset] = []

    public var lastStationID: Int64 = 0
    public var lastDatabaseSync: Date?
    public var windowWidth = 560.0
    public var windowHeight = 500.0
    public var sort: StationSort = .name
    public var sortDescending = false
    public var searchHistory: [String] = []

    public init() {}

    public mutating func sanitize() {
        volume = min(max(volume, 0), 100)
        bufferMilliseconds = min(max(bufferMilliseconds, 1000), 30000)
        retryAttempts = min(max(retryAttempts, 0), 50)
        mp3Bitrate = min(max(mp3Bitrate, 64), 320)
        maxConcurrentRecordings = min(max(maxConcurrentRecordings, 1), 10)
        timeoutSeconds = min(max(timeoutSeconds, 3), 60)
        databaseRefreshDays = min(max(databaseRefreshDays, 1), 90)
        proxyPort = min(max(proxyPort, 1), 65535)
        if !["en", "fa"].contains(language) { language = "en" }
        if recordingDirectory.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            recordingDirectory = AppPaths.defaultRecordingDirectory.path
        }
        if trackFileTemplate.isEmpty { trackFileTemplate = "{station}_{artist}_{title}_{date}" }
        if continuousFileTemplate.isEmpty { continuousFileTemplate = "{station}_{date}_{time}" }
        if equalizer.bands.count != 10 { equalizer.bands = Array(repeating: 0.0, count: 10) }
        if windowWidth < 480 || windowWidth.isNaN { windowWidth = 560 }
        if windowHeight < 300 || windowHeight.isNaN { windowHeight = 500 }
    }
}

public enum AppPaths {
    public static var applicationSupportDirectory: URL {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
            ?? FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/Application Support", isDirectory: true)
        return base.appendingPathComponent("RadioClassic", isDirectory: true)
    }

    public static var databaseFile: URL {
        applicationSupportDirectory.appendingPathComponent("stations.db")
    }

    public static var settingsFile: URL {
        applicationSupportDirectory.appendingPathComponent("settings.json")
    }

    public static var defaultRecordingDirectory: URL {
        FileManager.default.urls(for: .musicDirectory, in: .userDomainMask).first?
            .appendingPathComponent("RadioClassic/Recordings", isDirectory: true)
            ?? FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Music/RadioClassic/Recordings", isDirectory: true)
    }
}

public final class SettingsStore: ObservableObject {
    @Published public var current = AppSettings() {
        didSet {
            guard !isSanitizing else { return }
            isSanitizing = true
            current.sanitize()
            isSanitizing = false
            if isLoaded { save() }
        }
    }

    private let file: URL
    private var isLoaded = false
    private var isSanitizing = false

    public init(file: URL = AppPaths.settingsFile) {
        self.file = file
    }

    public func load() {
        do {
            let data = try Data(contentsOf: file)
            let decoder = JSONDecoder()
            decoder.dateDecodingStrategy = .iso8601
            var decoded = try decoder.decode(AppSettings.self, from: data)
            decoded.sanitize()
            isLoaded = false
            current = decoded
            isLoaded = true
        } catch {
            if FileManager.default.fileExists(atPath: file.path) {
                try? FileManager.default.copyItem(at: file, to: file.appendingPathExtension("bad"))
            }
            isLoaded = true
            current = AppSettings()
        }
    }

    public func save() {
        do {
            try FileManager.default.createDirectory(at: file.deletingLastPathComponent(), withIntermediateDirectories: true)
            var copy = current
            copy.sanitize()
            let encoder = JSONEncoder()
            encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
            encoder.dateEncodingStrategy = .iso8601
            let data = try encoder.encode(copy)
            try data.write(to: file, options: .atomic)
        } catch {
            NSLog("RadioClassic: settings could not be saved: \(error.localizedDescription)")
        }
    }

    public func restoreDefaults() {
        let sync = current.lastDatabaseSync
        current = AppSettings()
        current.lastDatabaseSync = sync
        save()
    }
}
