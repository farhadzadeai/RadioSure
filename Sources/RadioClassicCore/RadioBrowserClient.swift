import Foundation

public struct RadioBrowserError: Error, CustomStringConvertible {
    public let message: String
    public var description: String { message }

    public init(_ message: String) { self.message = message }
}

private struct RadioBrowserServer: Decodable {
    let name: String
}

private struct RadioBrowserStats: Decodable {
    let stations: Int
    let stationsBroken: Int

    enum CodingKeys: String, CodingKey {
        case stations
        case stationsBroken = "stations_broken"
    }
}

public struct StationPage: Sendable {
    public let stations: [Station]
    public let rawCount: Int
}

private struct RadioBrowserStationDTO: Decodable {
    enum CodingKeys: String, CodingKey {
        case stationuuid
        case name
        case url
        case urlResolved = "url_resolved"
        case homepage
        case favicon
        case country
        case countrycode
        case language
        case tags
        case codec
        case bitrate
        case votes
        case clickcount
        case lastcheckok
        case hls
    }

    let uuid: String
    let name: String
    let url: String
    let homepage: String
    let favicon: String
    let country: String
    let countryCode: String
    let language: String
    let tags: String
    let codec: String
    let bitrate: Int
    let votes: Int
    let clicks: Int
    let lastCheckOK: Bool
    let isHLS: Bool

    private static func string(_ container: KeyedDecodingContainer<CodingKeys>, _ key: CodingKeys) -> String {
        if let value = try? container.decode(String.self, forKey: key) { return value }
        if let value = try? container.decode(Int.self, forKey: key) { return String(value) }
        return ""
    }

    private static func integer(_ container: KeyedDecodingContainer<CodingKeys>, _ key: CodingKeys) -> Int {
        if let value = try? container.decode(Int.self, forKey: key) { return value }
        if let value = try? container.decode(String.self, forKey: key), let intValue = Int(value) { return intValue }
        if let value = try? container.decode(Bool.self, forKey: key) { return value ? 1 : 0 }
        return 0
    }

    init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        uuid = Self.string(container, .stationuuid)
        name = TextUtilities.clean(Self.string(container, .name), maxLength: 180)
        url = Self.string(container, .urlResolved).isEmpty ? Self.string(container, .url) : Self.string(container, .urlResolved)
        homepage = Self.string(container, .homepage)
        favicon = Self.string(container, .favicon)
        country = Self.string(container, .country)
        countryCode = Self.string(container, .countrycode)
        language = Self.string(container, .language)
        tags = Self.string(container, .tags)
        codec = Self.string(container, .codec).uppercased()
        bitrate = Self.integer(container, .bitrate)
        votes = Self.integer(container, .votes)
        clicks = Self.integer(container, .clickcount)
        lastCheckOK = Self.integer(container, .lastcheckok) != 0
        isHLS = Self.integer(container, .hls) != 0
    }

    var station: Station? {
        guard !uuid.isEmpty, !name.isEmpty, StreamURLValidator.isValidStreamURL(url) else { return nil }
        return Station(
            uuid: uuid,
            name: name,
            url: url,
            homepage: homepage,
            favicon: favicon,
            country: country,
            countryCode: countryCode,
            language: language,
            tags: tags,
            codec: codec,
            bitrate: bitrate,
            votes: votes,
            clicks: clicks,
            lastCheckOK: lastCheckOK,
            isHLS: isHLS
        )
    }
}

public final class RadioBrowserClient {
    private static let fallbackServers = ["de1.api.radio-browser.info", "de2.api.radio-browser.info", "fi1.api.radio-browser.info"]
    public static let userAgent = "RadioClassic/1.0"

    private let session: URLSession
    private let timeout: TimeInterval
    private var selectedServer: String?
    public private(set) var expectedWorkingStationCount: Int?

    public init(
        timeout: TimeInterval = 10,
        session: URLSession? = nil,
        proxyMode: ProxyMode = .system,
        proxyHost: String = "",
        proxyPort: Int = 8080
    ) {
        self.timeout = timeout
        if let session {
            self.session = session
        } else {
            let configuration = URLSessionConfiguration.default
            configuration.timeoutIntervalForRequest = timeout
            configuration.timeoutIntervalForResource = 120
            configuration.httpAdditionalHeaders = ["User-Agent": Self.userAgent]
            Self.applyProxy(configuration: configuration, mode: proxyMode, host: proxyHost, port: proxyPort)
            self.session = URLSession(configuration: configuration)
        }
    }

    public static func applyProxy(configuration: URLSessionConfiguration, mode: ProxyMode, host: String, port: Int) {
        switch mode {
        case .system:
            configuration.connectionProxyDictionary = nil
        case .none:
            configuration.connectionProxyDictionary = [:]
        case .manual:
            let cleanHost = host.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !cleanHost.isEmpty, (1...65535).contains(port) else {
                configuration.connectionProxyDictionary = [:]
                return
            }
            configuration.connectionProxyDictionary = [
                kCFNetworkProxiesHTTPEnable as String: true,
                kCFNetworkProxiesHTTPProxy as String: cleanHost,
                kCFNetworkProxiesHTTPPort as String: port,
                kCFNetworkProxiesHTTPSEnable as String: true,
                kCFNetworkProxiesHTTPSProxy as String: cleanHost,
                kCFNetworkProxiesHTTPSPort as String: port
            ]
        }
    }

    private func validHost(_ host: String) -> Bool {
        host.lowercased().hasSuffix(".radio-browser.info") && !host.contains("/") && !host.contains(" ")
    }

    public func discoverServers() async throws -> [String] {
        var hosts: [String] = []
        if let url = URL(string: "https://all.api.radio-browser.info/json/servers") {
            do {
                let (data, response) = try await session.data(from: url)
                if (response as? HTTPURLResponse)?.statusCode == 200 {
                    let decoded = try JSONDecoder().decode([RadioBrowserServer].self, from: data)
                    hosts.append(contentsOf: decoded.map(\.name).filter(validHost))
                }
            } catch {
                if hosts.isEmpty {
                    NSLog("RadioClassic: Radio-Browser server discovery failed: \(error.localizedDescription)")
                }
            }
        }
        hosts = Array(Set(hosts)).shuffled()
        if let selectedServer {
            hosts.removeAll { $0 == selectedServer }
            hosts.insert(selectedServer, at: 0)
        }
        for fallback in Self.fallbackServers where !hosts.contains(fallback) {
            hosts.append(fallback)
        }
        return hosts
    }

    private func probe(server: String) async throws -> Bool {
        guard let url = URL(string: "https://\(server)/json/stats") else { return false }
        var request = URLRequest(url: url)
        request.timeoutInterval = timeout
        let (data, response) = try await session.data(for: request)
        guard (response as? HTTPURLResponse)?.statusCode == 200 else { return false }
        if let stats = try? JSONDecoder().decode(RadioBrowserStats.self, from: data), stats.stations > 0 {
            expectedWorkingStationCount = max(1, stats.stations - max(0, stats.stationsBroken))
        }
        return true
    }

    public func selectServer() async throws -> String {
        var lastError: Error?
        for server in try await discoverServers() {
            do {
                if try await probe(server: server) {
                    selectedServer = server
                    return server
                }
            } catch {
                lastError = error
            }
        }
        throw RadioBrowserError("No Radio-Browser server could be reached. \(lastError?.localizedDescription ?? "")")
    }

    public func stationsPage(server: String, limit: Int, offset: Int) async throws -> [Station] {
        try await stationsPageDetailed(server: server, limit: limit, offset: offset).stations
    }

    public func stationsPageDetailed(server: String, limit: Int, offset: Int) async throws -> StationPage {
        guard var components = URLComponents(string: "https://\(server)/json/stations") else {
            throw RadioBrowserError("Invalid Radio-Browser server")
        }
        components.queryItems = [
            URLQueryItem(name: "hidebroken", value: "true"),
            URLQueryItem(name: "order", value: "votes"),
            URLQueryItem(name: "reverse", value: "true"),
            URLQueryItem(name: "limit", value: String(limit)),
            URLQueryItem(name: "offset", value: String(offset))
        ]
        guard let url = components.url else { throw RadioBrowserError("Invalid station list URL") }
        var request = URLRequest(url: url)
        request.timeoutInterval = max(timeout, 30)
        let (data, response) = try await session.data(for: request)
        guard let status = (response as? HTTPURLResponse)?.statusCode, status == 200 else {
            throw RadioBrowserError("Station list download failed at offset \(offset)")
        }
        let decoded = try JSONDecoder().decode([RadioBrowserStationDTO].self, from: data)
        return StationPage(stations: decoded.compactMap(\.station), rawCount: decoded.count)
    }

    public func allStations(pageSize: Int = 5000) -> AsyncThrowingStream<Station, Error> {
        AsyncThrowingStream { continuation in
            let task = Task {
                do {
                    let server = try await selectServer()
                    var offset = 0
                    while true {
                        try Task.checkCancellation()
                        let page = try await stationsPageDetailed(server: server, limit: pageSize, offset: offset)
                        for station in page.stations {
                            continuation.yield(station)
                        }
                        if page.rawCount < pageSize { break }
                        offset += pageSize
                    }
                    continuation.finish()
                } catch {
                    continuation.finish(throwing: error)
                }
            }
            continuation.onTermination = { _ in task.cancel() }
        }
    }

    public func reportClick(uuid: String) async {
        guard !uuid.isEmpty else { return }
        do {
            let server = try await selectServer()
            guard let url = URL(string: "https://\(server)/json/url/\(uuid)") else { return }
            var request = URLRequest(url: url)
            request.timeoutInterval = timeout
            _ = try await session.data(for: request)
        } catch {
            NSLog("RadioClassic: click report failed: \(error.localizedDescription)")
        }
    }
}
