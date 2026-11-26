import Foundation

public enum IcyParserEvent: Equatable, Sendable {
    case audio(Data)
    case metadata(StreamMetadata)
}

public struct IcyMetadataParser: Sendable {
    private enum State: Sendable {
        case audio
        case metadataLength
        case metadata
    }

    public let metaInt: Int
    private var audioRemaining: Int
    private var state: State = .audio
    private var metadataBytesRemaining = 0
    private var metadataBuffer = Data()

    public init(metaInt: Int) {
        self.metaInt = max(0, metaInt)
        audioRemaining = max(0, metaInt)
        if self.metaInt == 0 { state = .audio }
    }

    public mutating func reset() {
        audioRemaining = metaInt
        state = .audio
        metadataBytesRemaining = 0
        metadataBuffer.removeAll(keepingCapacity: true)
    }

    public mutating func append(_ data: Data) -> [IcyParserEvent] {
        guard metaInt > 0 else {
            return data.isEmpty ? [] : [.audio(data)]
        }
        var events: [IcyParserEvent] = []
        var offset = data.startIndex

        while offset < data.endIndex {
            switch state {
            case .audio:
                let amount = min(audioRemaining, data.endIndex - offset)
                if amount > 0 {
                    events.append(.audio(Data(data[offset..<(offset + amount)])))
                    offset += amount
                    audioRemaining -= amount
                }
                if audioRemaining == 0 {
                    state = .metadataLength
                }
            case .metadataLength:
                let lengthByte = data[offset]
                offset += 1
                metadataBytesRemaining = Int(lengthByte) * 16
                metadataBuffer.removeAll(keepingCapacity: true)
                if metadataBytesRemaining == 0 {
                    audioRemaining = metaInt
                    state = .audio
                } else {
                    state = .metadata
                }
            case .metadata:
                let amount = min(metadataBytesRemaining, data.endIndex - offset)
                metadataBuffer.append(data[offset..<(offset + amount)])
                offset += amount
                metadataBytesRemaining -= amount
                if metadataBytesRemaining == 0 {
                    let metadata = Self.parseBlock(metadataBuffer)
                    metadataBuffer.removeAll(keepingCapacity: true)
                    audioRemaining = metaInt
                    state = .audio
                    if !metadata.isEmpty {
                        events.append(.metadata(metadata))
                    }
                }
            }
        }
        return events
    }

    public static func parseBlock(_ data: Data) -> StreamMetadata {
        var trimmed = data
        while trimmed.last == 0 { trimmed.removeLast() }
        let text = String(data: trimmed, encoding: .utf8)
            ?? String(data: trimmed, encoding: .isoLatin1)
            ?? ""
        return StreamMetadata.fromIcy(text)
    }
}

public struct IcyStreamInfo: Equatable, Sendable {
    public var streamName = ""
    public var contentType = ""
    public var bitrateKbps = 0
    public var metaInt = 0
    public var bytesReceived: Int64 = 0
    public var lastDataAt = Date.distantPast

    public init() {}

    public init(response: HTTPURLResponse) {
        self.init()
        for (key, value) in response.allHeaderFields {
            guard let name = (key as? String)?.lowercased() else { continue }
            let text = String(describing: value)
            switch name {
            case "icy-name": streamName = TextUtilities.clean(text, maxLength: 120)
            case "content-type": contentType = text.lowercased()
            case "icy-br", "ice-bitrate": bitrateKbps = Int(text) ?? 0
            case "icy-metaint": metaInt = Int(text) ?? 0
            default: break
            }
        }
    }
}

public enum IcyConnectionEvent {
    case started(IcyStreamInfo)
    case audio(Data)
    case metadata(StreamMetadata)
    case finished(Error?)
}

public struct IcyConnectionError: Error, CustomStringConvertible {
    public let message: String
    public var description: String { message }

    public static func httpStatus(_ status: Int) -> IcyConnectionError {
        IcyConnectionError(message: "HTTP status \(status)")
    }
}

public final class IcyStreamConnection: NSObject, URLSessionDataDelegate {
    private let url: URL
    private let timeout: TimeInterval
    private let userAgent: String
    private let proxyMode: ProxyMode
    private let proxyHost: String
    private let proxyPort: Int
    private let lock = NSLock()
    private var session: URLSession?
    private var task: URLSessionDataTask?
    private var parser: IcyMetadataParser?
    private var info = IcyStreamInfo()
    private var cancelled = false
    private var started = false
    private var finished = false

    public var onEvent: ((IcyConnectionEvent) -> Void)?

    public init(
        url: URL,
        timeout: TimeInterval = 10,
        userAgent: String = RadioBrowserClient.userAgent,
        proxyMode: ProxyMode = .system,
        proxyHost: String = "",
        proxyPort: Int = 8080
    ) {
        self.url = url
        self.timeout = timeout
        self.userAgent = userAgent
        self.proxyMode = proxyMode
        self.proxyHost = proxyHost
        self.proxyPort = proxyPort
    }

    public var currentInfo: IcyStreamInfo {
        lock.lock()
        defer { lock.unlock() }
        return info
    }

    public var isActive: Bool {
        lock.lock()
        defer { lock.unlock() }
        return started && !finished
    }

    public func start() {
        lock.lock()
        if started && !finished {
            lock.unlock()
            return
        }
        cancelled = false
        finished = false
        started = true
        info = IcyStreamInfo()
        lock.unlock()

        let configuration = URLSessionConfiguration.default
        configuration.timeoutIntervalForRequest = timeout
        configuration.timeoutIntervalForResource = 0
        configuration.requestCachePolicy = .reloadIgnoringLocalCacheData
        configuration.urlCache = nil
        configuration.httpAdditionalHeaders = [
            "Icy-MetaData": "1",
            "User-Agent": userAgent,
            "Accept": "*/*"
        ]
        RadioBrowserClient.applyProxy(configuration: configuration, mode: proxyMode, host: proxyHost, port: proxyPort)
        let queue = OperationQueue()
        queue.maxConcurrentOperationCount = 1
        let session = URLSession(configuration: configuration, delegate: self, delegateQueue: queue)
        var request = URLRequest(url: url)
        request.timeoutInterval = timeout
        let task = session.dataTask(with: request)
        lock.lock()
        self.session = session
        self.task = task
        lock.unlock()
        task.resume()
    }

    public func cancel() {
        lock.lock()
        cancelled = true
        let task = self.task
        let session = self.session
        self.task = nil
        self.session = nil
        lock.unlock()
        task?.cancel()
        session?.invalidateAndCancel()
    }

    public func urlSession(_ session: URLSession, dataTask: URLSessionDataTask, didReceive response: URLResponse, completionHandler: @escaping (URLSession.ResponseDisposition) -> Void) {
        guard let http = response as? HTTPURLResponse, (200..<400).contains(http.statusCode) else {
            completionHandler(.cancel)
            finish(with: IcyConnectionError.httpStatus((response as? HTTPURLResponse)?.statusCode ?? -1))
            return
        }
        let responseInfo = IcyStreamInfo(response: http)
        lock.lock()
        info = responseInfo
        parser = IcyMetadataParser(metaInt: responseInfo.metaInt)
        lock.unlock()
        onEvent?(.started(responseInfo))
        completionHandler(.allow)
    }

    public func urlSession(_ session: URLSession, dataTask: URLSessionDataTask, didReceive data: Data) {
        lock.lock()
        cancelled = cancelled || task == nil
        info.bytesReceived += Int64(data.count)
        info.lastDataAt = Date()
        let parserCopy = parser
        lock.unlock()
        guard var parser = parserCopy else { return }
        let events = parser.append(data)
        lock.lock()
        self.parser = parser
        lock.unlock()
        for event in events {
            switch event {
            case .audio(let audio): onEvent?(.audio(audio))
            case .metadata(let metadata): onEvent?(.metadata(metadata))
            }
        }
    }

    public func urlSession(_ session: URLSession, task: URLSessionTask, didCompleteWithError error: Error?) {
        finish(with: error)
    }

    private func finish(with error: Error?) {
        lock.lock()
        if finished || cancelled {
            finished = true
            lock.unlock()
            return
        }
        finished = true
        let session = self.session
        self.session = nil
        self.task = nil
        lock.unlock()
        session?.finishTasksAndInvalidate()
        onEvent?(.finished(error))
    }
}
