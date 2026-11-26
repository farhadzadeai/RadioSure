import Foundation

public struct Station: Codable, Identifiable, Hashable, Sendable {
    public var id: Int64
    public var uuid: String?
    public var name: String
    public var url: String
    public var homepage: String
    public var favicon: String
    public var country: String
    public var countryCode: String
    public var language: String
    public var tags: String
    public var codec: String
    public var bitrate: Int
    public var votes: Int
    public var clicks: Int
    public var lastCheckOK: Bool
    public var isHLS: Bool
    public var isCustom: Bool
    public var isFavorite: Bool
    public var favoriteFolder: String

    public init(
        id: Int64 = 0,
        uuid: String? = nil,
        name: String = "",
        url: String = "",
        homepage: String = "",
        favicon: String = "",
        country: String = "",
        countryCode: String = "",
        language: String = "",
        tags: String = "",
        codec: String = "",
        bitrate: Int = 0,
        votes: Int = 0,
        clicks: Int = 0,
        lastCheckOK: Bool = true,
        isHLS: Bool = false,
        isCustom: Bool = false,
        isFavorite: Bool = false,
        favoriteFolder: String = ""
    ) {
        self.id = id
        self.uuid = uuid
        self.name = name
        self.url = url
        self.homepage = homepage
        self.favicon = favicon
        self.country = country
        self.countryCode = countryCode
        self.language = language
        self.tags = tags
        self.codec = codec
        self.bitrate = bitrate
        self.votes = votes
        self.clicks = clicks
        self.lastCheckOK = lastCheckOK
        self.isHLS = isHLS
        self.isCustom = isCustom
        self.isFavorite = isFavorite
        self.favoriteFolder = favoriteFolder
    }

    public var genreDisplay: String { TextUtilities.titleCaseList(tags, maxItems: 2) }
    public var languageDisplay: String { TextUtilities.titleCaseList(language, maxItems: 2) }
    public var bitrateDisplay: String { bitrate > 0 ? String(bitrate) : "" }
}

public enum StationView: String, Codable, CaseIterable, Sendable {
    case all
    case favorites
    case recent
    case custom
}

public enum StationSort: String, Codable, CaseIterable, Sendable {
    case name
    case country
    case genre
    case language
    case codec
    case bitrate
    case votes
    case clicks
}

public struct StationQuery: Equatable, Sendable {
    public var view: StationView = .all
    public var search = ""
    public var country = ""
    public var language = ""
    public var genre = ""
    public var codec = ""
    public var minBitrate = 0
    public var favoriteFolder = ""
    public var sort: StationSort = .name
    public var descending = false

    public init() {}
}

public enum PlayerState: String, Equatable, Sendable {
    case stopped
    case connecting
    case playing
    case paused
    case reconnecting
    case error
}

public struct StreamMetadata: Equatable, Sendable {
    public var artist: String
    public var title: String

    public static let empty = StreamMetadata(artist: "", title: "")

    public init(artist: String, title: String) {
        self.artist = artist
        self.title = title
    }

    public var isEmpty: Bool { artist.isEmpty && title.isEmpty }
    public var display: String {
        if !artist.isEmpty && !title.isEmpty { return "\(artist) - \(title)" }
        return !artist.isEmpty ? artist : title
    }

    public static func fromIcy(_ meta: String?) -> StreamMetadata {
        guard let meta, !meta.isEmpty else { return .empty }
        let key = "StreamTitle='"
        guard let start = meta.range(of: key, options: .caseInsensitive)?.upperBound else { return .empty }
        let remainder = meta[start...]
        var end = remainder.range(of: "';")?.lowerBound
        if end == nil, let last = remainder.lastIndex(of: "'") { end = last }
        guard let endIndex = end, endIndex >= start else { return .empty }
        return fromCombined(String(meta[start..<endIndex]))
    }

    public static func fromCombined(_ combined: String?) -> StreamMetadata {
        let text = TextUtilities.clean(combined)
        if text.isEmpty || text == "-" { return .empty }
        if let range = text.range(of: " - "), range.lowerBound > text.startIndex {
            return StreamMetadata(
                artist: String(text[..<range.lowerBound]).trimmingCharacters(in: .whitespacesAndNewlines),
                title: String(text[range.upperBound...]).trimmingCharacters(in: .whitespacesAndNewlines)
            )
        }
        return StreamMetadata(artist: "", title: text)
    }

    public static func fromComments(_ comments: [String]) -> StreamMetadata {
        var artist = ""
        var title = ""
        for comment in comments {
            if comment.count >= 7 && comment.prefix(7).lowercased() == "artist=" {
                artist = TextUtilities.clean(String(comment.dropFirst(7)))
            } else if comment.count >= 6 && comment.prefix(6).lowercased() == "title=" {
                title = TextUtilities.clean(String(comment.dropFirst(6)))
            }
        }
        if artist.isEmpty && title.contains(" - ") { return fromCombined(title) }
        return artist.isEmpty && title.isEmpty ? .empty : StreamMetadata(artist: artist, title: title)
    }
}

public struct RecordingEntry: Identifiable, Equatable, Sendable {
    public var id: Int64
    public var stationName: String
    public var stationURL: String
    public var title: String
    public var file: String
    public var format: String
    public var started: Date
    public var ended: Date?
    public var bytes: Int64
    public var status: String
    public var error: String
    public var scheduled: Bool

    public init(
        id: Int64 = 0,
        stationName: String = "",
        stationURL: String = "",
        title: String = "",
        file: String = "",
        format: String = "",
        started: Date = Date(),
        ended: Date? = nil,
        bytes: Int64 = 0,
        status: String = RecordingStatus.recording,
        error: String = "",
        scheduled: Bool = false
    ) {
        self.id = id
        self.stationName = stationName
        self.stationURL = stationURL
        self.title = title
        self.file = file
        self.format = format
        self.started = started
        self.ended = ended
        self.bytes = bytes
        self.status = status
        self.error = error
        self.scheduled = scheduled
    }

    public var fileName: String { URL(fileURLWithPath: file).lastPathComponent }
    public var sizeDisplay: String { Formatting.size(bytes) }
    public var durationDisplay: String { ended.map { Formatting.duration($0.timeIntervalSince(started)) } ?? "" }
}

public enum RecordingStatus {
    public static let recording = "recording"
    public static let completed = "completed"
    public static let interrupted = "interrupted"
    public static let failed = "failed"
}

public enum ScheduleRepeat: Int, Codable, CaseIterable, Sendable {
    case once = 0
    case daily = 1
    case weekly = 2
}

public struct RecordingSchedule: Identifiable, Equatable, Sendable {
    public var id: Int64
    public var stationID: Int64
    public var stationName: String
    public var stationURL: String
    public var start: Date
    public var durationMinutes: Int
    public var repeatMode: ScheduleRepeat
    public var days: Int
    public var enabled: Bool
    public var lastRun: Date?

    public init(
        id: Int64 = 0,
        stationID: Int64 = 0,
        stationName: String = "",
        stationURL: String = "",
        start: Date = Date(),
        durationMinutes: Int = 60,
        repeatMode: ScheduleRepeat = .once,
        days: Int = 0,
        enabled: Bool = true,
        lastRun: Date? = nil
    ) {
        self.id = id
        self.stationID = stationID
        self.stationName = stationName
        self.stationURL = stationURL
        self.start = start
        self.durationMinutes = durationMinutes
        self.repeatMode = repeatMode
        self.days = days
        self.enabled = enabled
        self.lastRun = lastRun
    }

    public func activeOccurrence(at now: Date, calendar: Calendar = .current) -> (start: Date, end: Date)? {
        let duration = TimeInterval(max(1, durationMinutes) * 60)
        if repeatMode == .once {
            return now >= start && now < start.addingTimeInterval(duration) ? (start, start.addingTimeInterval(duration)) : nil
        }

        for back in 0...1 {
            guard let day = calendar.date(byAdding: .day, value: -back, to: calendar.startOfDay(for: now)) else { continue }
            let time = calendar.dateComponents([.hour, .minute, .second], from: start)
            guard let occurrenceStart = calendar.date(bySettingHour: time.hour ?? 0, minute: time.minute ?? 0, second: time.second ?? 0, of: day) else { continue }
            if occurrenceStart < start { continue }
            if repeatMode == .weekly {
                let weekday = calendar.component(.weekday, from: day) - 1
                if (days & (1 << weekday)) == 0 { continue }
            }
            let end = occurrenceStart.addingTimeInterval(duration)
            if now >= occurrenceStart && now < end { return (occurrenceStart, end) }
        }
        return nil
    }

    public func nextStart(after now: Date, calendar: Calendar = .current) -> Date? {
        guard enabled else { return nil }
        if repeatMode == .once { return start > now ? start : nil }
        if repeatMode == .weekly && (days & 0x7F) == 0 { return nil }

        var day = max(calendar.startOfDay(for: now), calendar.startOfDay(for: start))
        let time = calendar.dateComponents([.hour, .minute, .second], from: start)
        for _ in 0..<9 {
            guard let occurrence = calendar.date(bySettingHour: time.hour ?? 0, minute: time.minute ?? 0, second: time.second ?? 0, of: day) else {
                day = calendar.date(byAdding: .day, value: 1, to: day) ?? day.addingTimeInterval(86400)
                continue
            }
            if occurrence > now && occurrence >= start {
                if repeatMode != .weekly {
                    return occurrence
                }
                let weekday = calendar.component(.weekday, from: day) - 1
                if (days & (1 << weekday)) != 0 { return occurrence }
            }
            day = calendar.date(byAdding: .day, value: 1, to: day) ?? day.addingTimeInterval(86400)
        }
        return nil
    }
}

public struct HistoryEntry: Identifiable, Equatable, Sendable {
    public var id: Int64
    public var stationID: Int64
    public var stationName: String
    public var title: String
    public var playedAt: Date

    public init(id: Int64 = 0, stationID: Int64 = 0, stationName: String = "", title: String = "", playedAt: Date = Date()) {
        self.id = id
        self.stationID = stationID
        self.stationName = stationName
        self.title = title
        self.playedAt = playedAt
    }
}
