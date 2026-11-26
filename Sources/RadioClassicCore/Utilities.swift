import Foundation

public enum TextUtilities {
    public static func clean(_ value: String?, maxLength: Int = 300) -> String {
        guard let value, !value.isEmpty else { return "" }
        var result = ""
        result.reserveCapacity(min(value.count, maxLength))
        for scalar in value.trimmingCharacters(in: .whitespacesAndNewlines).unicodeScalars {
            if result.count >= maxLength { break }
            result.append(CharacterSet.controlCharacters.contains(scalar) ? " " : Character(scalar))
        }
        return result.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    public static func titleCaseList(_ list: String?, maxItems: Int) -> String {
        guard let list, !list.isEmpty else { return "" }
        return list.split(separator: ",", omittingEmptySubsequences: true)
            .map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
            .filter { !$0.isEmpty }
            .prefix(maxItems)
            .map(titleCase)
            .joined(separator: ", ")
    }

    public static func titleCase(_ value: String) -> String {
        guard let first = value.first else { return value }
        return String(first).uppercased() + value.dropFirst()
    }
}

public enum Formatting {
    public static func size(_ bytes: Int64) -> String {
        switch bytes {
        case ..<1024:
            return "\(bytes) B"
        case ..<(1024 * 1024):
            return String(format: "%.1f KB", Double(bytes) / 1024).replacingOccurrences(of: ".0 ", with: " ")
        case ..<(1024 * 1024 * 1024):
            return String(format: "%.1f MB", Double(bytes) / (1024 * 1024))
        default:
            return String(format: "%.2f GB", Double(bytes) / (1024 * 1024 * 1024))
        }
    }

    public static func duration(_ interval: TimeInterval) -> String {
        let total = max(0, Int(interval.rounded(.down)))
        let hours = total / 3600
        let minutes = (total % 3600) / 60
        let seconds = total % 60
        if hours > 0 { return String(format: "%d:%02d:%02d", hours, minutes, seconds) }
        return String(format: "%02d:%02d", minutes, seconds)
    }
}

public enum DatabaseDate {
    private static let lock = NSLock()
    private static let formatter: DateFormatter = {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.dateFormat = "yyyy-MM-dd HH:mm:ss"
        return formatter
    }()

    public static func string(_ date: Date) -> String {
        lock.lock()
        defer { lock.unlock() }
        return formatter.string(from: date)
    }

    public static func parse(_ value: String) -> Date? {
        lock.lock()
        defer { lock.unlock() }
        return formatter.date(from: value)
    }
}

public enum RecordingFileNamer {
    private static let invalidCharacters = CharacterSet(charactersIn: "/\\:?<>|*\"").union(.controlCharacters)
    private static let maxNameLength = 120

    public static func sanitize(_ value: String?, fallback: String = "recording") -> String {
        guard let value, !value.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return fallback }
        var result = ""
        for scalar in value.trimmingCharacters(in: .whitespacesAndNewlines).unicodeScalars {
            if result.count >= maxNameLength + 20 { break }
            result.append(invalidCharacters.contains(scalar) ? "-" : Character(scalar))
        }
        result = result.trimmingCharacters(in: CharacterSet(charactersIn: " ."))
        return result.isEmpty ? fallback : result
    }

    public static func expand(template: String, station: String, artist: String, title: String, when: Date) -> String {
        let date = DateFormatter.cached(format: "yyyy-MM-dd").string(from: when)
        let time = DateFormatter.cached(format: "HHmmss").string(from: when)
        let dateTime = DateFormatter.cached(format: "yyyy-MM-dd_HHmmss").string(from: when)
        var result = template
            .replacingOccurrences(of: "{station}", with: sanitize(station, fallback: "station"))
            .replacingOccurrences(of: "{artist}", with: sanitize(artist, fallback: ""))
            .replacingOccurrences(of: "{title}", with: sanitize(title, fallback: ""))
            .replacingOccurrences(of: "{datetime}", with: dateTime)
            .replacingOccurrences(of: "{date}", with: date)
            .replacingOccurrences(of: "{time}", with: time)
        while result.contains("__") { result = result.replacingOccurrences(of: "__", with: "_") }
        result = result.trimmingCharacters(in: CharacterSet(charactersIn: "_ "))
        return sanitize(result)
    }

    public static func uniquePath(directory: URL, baseName: String, extension ext: String) throws -> URL {
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let trimmed = String(baseName.prefix(maxNameLength))
        for index in 1...999 {
            let name = index == 1 ? trimmed : "\(trimmed) (\(index))"
            let url = directory.appendingPathComponent(name + ext)
            if !FileManager.default.fileExists(atPath: url.path) { return url }
        }
        throw CocoaError(.fileWriteFileExists)
    }

    public static func stationDirectory(root: URL, station: String, subfolders: Bool) -> URL {
        subfolders ? root.appendingPathComponent(sanitize(station, fallback: "station"), isDirectory: true) : root
    }
}

private extension DateFormatter {
    static let cacheLock = NSLock()
    static var cache: [String: DateFormatter] = [:]

    static func cached(format: String) -> DateFormatter {
        cacheLock.lock()
        defer { cacheLock.unlock() }
        if let formatter = cache[format] { return formatter }
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.dateFormat = format
        cache[format] = formatter
        return formatter
    }
}

public enum StreamURLValidator {
    public static func isValidStreamURL(_ value: String) -> Bool {
        guard let url = URL(string: value.trimmingCharacters(in: .whitespacesAndNewlines)),
              let scheme = url.scheme?.lowercased(),
              scheme == "http" || scheme == "https",
              url.host?.isEmpty == false else { return false }
        return true
    }
}
