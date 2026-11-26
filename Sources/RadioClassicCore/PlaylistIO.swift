import Foundation

public enum PlaylistIOError: Error, CustomStringConvertible {
    case unsupportedFormat
    case empty
    case malformed(String)

    public var description: String {
        switch self {
        case .unsupportedFormat: return "Unsupported playlist format"
        case .empty: return "The playlist contains no stations"
        case .malformed(let message): return message
        }
    }
}

public struct ExportedStation: Codable, Equatable, Sendable {
    public var name: String
    public var url: String
    public var homepage: String
    public var country: String
    public var language: String
    public var tags: String
    public var codec: String
    public var bitrate: Int
    public var favoriteFolder: String
    public var uuid: String?

    enum CodingKeys: String, CodingKey {
        case name
        case url
        case homepage
        case country
        case language
        case tags
        case genre
        case codec
        case bitrate
        case favoriteFolder
        case folder
        case uuid
    }

    public init(station: Station) {
        name = station.name
        url = station.url
        homepage = station.homepage
        country = station.country
        language = station.language
        tags = station.tags
        codec = station.codec
        bitrate = station.bitrate
        favoriteFolder = station.favoriteFolder
        uuid = station.uuid
    }

    public init(name: String, url: String) {
        self.name = name
        self.url = url
        homepage = ""
        country = ""
        language = ""
        tags = ""
        codec = ""
        bitrate = 0
        favoriteFolder = ""
        uuid = nil
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        name = (try container.decodeIfPresent(String.self, forKey: .name)) ?? ""
        url = (try container.decodeIfPresent(String.self, forKey: .url)) ?? ""
        homepage = (try container.decodeIfPresent(String.self, forKey: .homepage)) ?? ""
        country = (try container.decodeIfPresent(String.self, forKey: .country)) ?? ""
        language = (try container.decodeIfPresent(String.self, forKey: .language)) ?? ""
        if let value = try container.decodeIfPresent(String.self, forKey: .tags) {
            tags = value
        } else {
            tags = (try container.decodeIfPresent(String.self, forKey: .genre)) ?? ""
        }
        codec = (try container.decodeIfPresent(String.self, forKey: .codec)) ?? ""
        bitrate = (try container.decodeIfPresent(Int.self, forKey: .bitrate)) ?? 0
        if let value = try container.decodeIfPresent(String.self, forKey: .favoriteFolder) {
            favoriteFolder = value
        } else {
            favoriteFolder = (try container.decodeIfPresent(String.self, forKey: .folder)) ?? ""
        }
        uuid = try container.decodeIfPresent(String.self, forKey: .uuid)
    }

    public func encode(to encoder: Encoder) throws {
        var container = encoder.container(keyedBy: CodingKeys.self)
        try container.encode(name, forKey: .name)
        try container.encode(url, forKey: .url)
        try container.encode(homepage, forKey: .homepage)
        try container.encode(country, forKey: .country)
        try container.encode(language, forKey: .language)
        try container.encode(tags, forKey: .genre)
        try container.encode(codec, forKey: .codec)
        try container.encode(bitrate, forKey: .bitrate)
        try container.encode(favoriteFolder, forKey: .folder)
        try container.encodeIfPresent(uuid, forKey: .uuid)
    }

    public var station: Station {
        Station(
            uuid: uuid.flatMap { UUID(uuidString: $0) != nil ? $0 : nil },
            name: TextUtilities.clean(name, maxLength: 200),
            url: url.trimmingCharacters(in: .whitespacesAndNewlines),
            homepage: TextUtilities.clean(homepage, maxLength: 500),
            country: TextUtilities.clean(country, maxLength: 80),
            language: TextUtilities.clean(language, maxLength: 120),
            tags: TextUtilities.clean(tags, maxLength: 200),
            codec: codec,
            bitrate: bitrate,
            isCustom: true,
            favoriteFolder: TextUtilities.clean(favoriteFolder, maxLength: 80)
        )
    }
}

public enum PlaylistIO {
    public static let maxFileBytes = 5 * 1024 * 1024
    public static let maxEntries = 5000

    public static func isValidStreamURL(_ value: String) -> Bool {
        StreamURLValidator.isValidStreamURL(value)
    }

    public static func importPlaylist(from file: URL) throws -> [Station] {
        let attributes = try FileManager.default.attributesOfItem(atPath: file.path)
        if (attributes[.size] as? NSNumber)?.intValue ?? 0 > maxFileBytes {
            throw PlaylistIOError.malformed("The playlist file is too large.")
        }
        let data = try Data(contentsOf: file)
        guard data.count <= maxFileBytes else {
            throw PlaylistIOError.malformed("The playlist file is too large.")
        }
        let text = String(decoding: data, as: UTF8.self)
        let head = text.drop(while: { $0.isWhitespace })
        let ext = file.pathExtension.lowercased()
        let entries: [ExportedStation]
        switch ext {
        case "json":
            entries = try parseJSON(data)
        case "m3u", "m3u8":
            entries = parseM3U(text)
        case "pls":
            entries = parsePLS(text)
        case "xspf":
            entries = parseXSPF(data)
        default:
            if head.hasPrefix("[playlist]") {
                entries = parsePLS(text)
            } else if head.hasPrefix("<") {
                entries = parseXSPF(data)
            } else if head.hasPrefix("[") || head.hasPrefix("{") {
                entries = try parseJSON(data)
            } else {
                entries = parseM3U(text)
            }
        }
        let stations = entries
            .filter { !$0.url.isEmpty && isValidStreamURL($0.url) }
            .prefix(maxEntries)
            .map(\.station)
        if stations.isEmpty { throw PlaylistIOError.empty }
        return stations
    }

    public static func exportData(for fileExtension: String, stations: [Station]) throws -> Data {
        switch fileExtension.lowercased() {
        case "pls": return Data(exportPLS(stations).utf8)
        case "xspf": return Data(exportXSPF(stations).utf8)
        case "json": return try exportJSON(stations)
        default: return Data(exportM3U(stations).utf8)
        }
    }

    public static func exportJSON(_ stations: [Station]) throws -> Data {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        return try encoder.encode(stations.map(ExportedStation.init(station:)))
    }

    public static func exportM3U(_ stations: [Station]) -> String {
        var text = "#EXTM3U\r\n"
        for station in stations {
            text += "#EXTINF:-1,\(oneLine(station.name))\r\n\(station.url)\r\n"
        }
        return text
    }

    public static func exportPLS(_ stations: [Station]) -> String {
        var text = "[playlist]\r\n"
        for (index, station) in stations.enumerated() {
            let number = index + 1
            text += "File\(number)=\(station.url)\r\n"
            text += "Title\(number)=\(oneLine(station.name))\r\n"
            text += "Length\(number)=-1\r\n"
        }
        text += "NumberOfEntries=\(stations.count)\r\nVersion=2\r\n"
        return text
    }

    public static func exportXSPF(_ stations: [Station]) -> String {
        var text = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
        text += "<playlist xmlns=\"http://xspf.org/ns/0/\" version=\"1\">\n  <trackList>\n"
        for station in stations {
            text += "    <track>\n"
            text += "      <location>\(xmlEscape(station.url))</location>\n"
            text += "      <title>\(xmlEscape(station.name))</title>\n"
            text += "    </track>\n"
        }
        text += "  </trackList>\n</playlist>\n"
        return text
    }

    public static func parseJSON(_ data: Data) throws -> [ExportedStation] {
        try JSONDecoder().decode([ExportedStation].self, from: data)
    }

    public static func parseM3U(_ text: String) -> [ExportedStation] {
        var entries: [ExportedStation] = []
        var pendingTitle = ""
        for rawLine in text.components(separatedBy: .newlines) {
            let line = rawLine.trimmingCharacters(in: .whitespacesAndNewlines)
            if line.isEmpty { continue }
            if line.uppercased().hasPrefix("#EXTINF") {
                if let comma = line.firstIndex(of: ",") {
                    pendingTitle = String(line[line.index(after: comma)...]).trimmingCharacters(in: .whitespacesAndNewlines)
                }
            } else if !line.hasPrefix("#") {
                guard isValidStreamURL(line) else { continue }
                let name = pendingTitle.isEmpty ? stationNameFromURL(line) : pendingTitle
                entries.append(ExportedStation(name: name, url: line))
                pendingTitle = ""
            }
        }
        return entries
    }

    public static func parsePLS(_ text: String) -> [ExportedStation] {
        var files: [Int: String] = [:]
        var titles: [Int: String] = [:]
        for rawLine in text.components(separatedBy: .newlines) {
            let line = rawLine.trimmingCharacters(in: .whitespacesAndNewlines)
            guard let equal = line.firstIndex(of: "=") else { continue }
            let key = line[..<equal].trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
            let value = String(line[line.index(after: equal)...]).trimmingCharacters(in: .whitespacesAndNewlines)
            if key.hasPrefix("file"), let index = Int(key.dropFirst(4)) {
                files[index] = value
            } else if key.hasPrefix("title"), let index = Int(key.dropFirst(5)) {
                titles[index] = value
            }
        }
        return files.keys.sorted().compactMap { index in
            guard let url = files[index], isValidStreamURL(url) else { return nil }
            return ExportedStation(name: titles[index]?.isEmpty == false ? titles[index]! : stationNameFromURL(url), url: url)
        }
    }

    private final class XSPFParser: NSObject, XMLParserDelegate {
        var entries: [ExportedStation] = []
        var currentLocation = ""
        var currentTitle = ""
        var currentElement = ""
        var inTrack = false

        func parser(_ parser: XMLParser, didStartElement elementName: String, namespaceURI: String?, qualifiedName qName: String?, attributes attributeDict: [String: String] = [:]) {
            currentElement = elementName.lowercased()
            if currentElement == "track" {
                inTrack = true
                currentLocation = ""
                currentTitle = ""
            }
        }

        func parser(_ parser: XMLParser, foundCharacters string: String) {
            guard inTrack else { return }
            if currentElement == "location" { currentLocation += string }
            if currentElement == "title" { currentTitle += string }
        }

        func parser(_ parser: XMLParser, didEndElement elementName: String, namespaceURI: String?, qualifiedName qName: String?) {
            if elementName.lowercased() == "track" {
                let url = currentLocation.trimmingCharacters(in: .whitespacesAndNewlines)
                if isValidStreamURL(url) {
                    let title = currentTitle.trimmingCharacters(in: .whitespacesAndNewlines)
                    entries.append(ExportedStation(name: title.isEmpty ? stationNameFromURL(url) : title, url: url))
                }
                inTrack = false
            }
            currentElement = ""
        }
    }

    public static func parseXSPF(_ data: Data) -> [ExportedStation] {
        let delegate = XSPFParser()
        let parser = XMLParser(data: data)
        parser.delegate = delegate
        parser.shouldResolveExternalEntities = false
        parser.parse()
        return delegate.entries
    }

    private static func stationNameFromURL(_ value: String) -> String {
        guard let url = URL(string: value) else { return "Station" }
        let last = url.lastPathComponent.replacingOccurrences(of: ".mp3", with: "").replacingOccurrences(of: ".aac", with: "")
        return last.isEmpty ? (url.host ?? "Station") : last
    }

    private static func oneLine(_ value: String) -> String {
        value.replacingOccurrences(of: "\r", with: " ").replacingOccurrences(of: "\n", with: " ")
    }

    private static func xmlEscape(_ value: String) -> String {
        value
            .replacingOccurrences(of: "&", with: "&amp;")
            .replacingOccurrences(of: "<", with: "&lt;")
            .replacingOccurrences(of: ">", with: "&gt;")
            .replacingOccurrences(of: "\"", with: "&quot;")
    }
}
