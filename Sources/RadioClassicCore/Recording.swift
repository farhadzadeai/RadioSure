import Foundation

public enum RecordingState: String, Equatable, Sendable {
    case connecting
    case recording
    case reconnecting
    case ended
}

public struct RecordingOptions: Equatable, Sendable {
    public var format: RecordingFormat
    public var mp3Bitrate: Int
    public var splitTracks: Bool
    public var trackTemplate: String
    public var continuousTemplate: String
    public var directory: URL
    public var stationSubfolders: Bool
    public var retryAttempts: Int
    public var proxyMode: ProxyMode
    public var proxyHost: String
    public var proxyPort: Int

    public init(
        format: RecordingFormat = .original,
        mp3Bitrate: Int = 192,
        splitTracks: Bool = true,
        trackTemplate: String = "{station}_{artist}_{title}_{date}",
        continuousTemplate: String = "{station}_{date}_{time}",
        directory: URL,
        stationSubfolders: Bool = true,
        retryAttempts: Int = 3,
        proxyMode: ProxyMode = .system,
        proxyHost: String = "",
        proxyPort: Int = 8080
    ) {
        self.format = format
        self.mp3Bitrate = mp3Bitrate
        self.splitTracks = splitTracks
        self.trackTemplate = trackTemplate
        self.continuousTemplate = continuousTemplate
        self.directory = directory
        self.stationSubfolders = stationSubfolders
        self.retryAttempts = retryAttempts
        self.proxyMode = proxyMode
        self.proxyHost = proxyHost
        self.proxyPort = proxyPort
    }
}

public final class RecordingSession {
    public let id = UUID()
    public let stationID: Int64
    public let stationName: String
    public let stationURL: String
    public let endAt: Date?
    public let scheduled: Bool

    private let options: RecordingOptions
    private let repository: RecordingRepository
    private let lock = NSRecursiveLock()
    private var connection: IcyStreamConnection?
    private var state: RecordingState = .connecting
    private var stopRequested = false
    private var generation = 0
    private var failures = 0
    private var output: FileHandle?
    private var writeBuffer = Data()
    private var rowID: Int64 = 0
    private var currentFile = ""
    private var currentTitle = ""
    private var currentFileTitle = ""
    private var currentExtension = ".mp3"
    private var lastFlush = Date.distantPast
    private var endTimer: DispatchSourceTimer?

    public private(set) var startedAt = Date()
    public private(set) var errorMessage = ""
    public private(set) var endedByError = false
    public private(set) var currentTrack = ""
    public private(set) var codec = ""
    public private(set) var totalBytes: Int64 = 0
    public private(set) var filesWritten = 0
    public private(set) var effectiveFormat = ""

    public var onChange: ((RecordingSession) -> Void)?
    public var onEnded: ((RecordingSession) -> Void)?

    public init(
        stationID: Int64,
        stationName: String,
        stationURL: String,
        options: RecordingOptions,
        repository: RecordingRepository,
        endAt: Date? = nil,
        scheduled: Bool = false
    ) {
        self.stationID = stationID
        self.stationName = stationName
        self.stationURL = stationURL
        self.options = options
        self.repository = repository
        self.endAt = endAt
        self.scheduled = scheduled
    }

    public var currentState: RecordingState {
        lock.lock()
        defer { lock.unlock() }
        return state
    }

    public var isRecording: Bool {
        currentState != .ended
    }

    public var currentFilePath: String {
        lock.lock()
        defer { lock.unlock() }
        return currentFile
    }

    public var currentFileBytes: Int64 {
        lock.lock()
        defer { lock.unlock() }
        var written: UInt64 = 0
        if let output, let offset = try? output.offset() { written = offset }
        return Int64(writeBuffer.count) + Int64(written)
    }

    public func start() {
        startedAt = Date()
        do {
            try FileManager.default.createDirectory(at: options.directory, withIntermediateDirectories: true)
        } catch {
            fail(key: "Err.RecDir", error: error)
            return
        }
        startEndTimer()
        connect(reconnect: false)
    }

    public func stop() {
        lock.lock()
        stopRequested = true
        let connection = self.connection
        self.connection = nil
        lock.unlock()
        connection?.cancel()
        finish(success: true, errorKey: "")
    }

    private func startEndTimer() {
        let timer = DispatchSource.makeTimerSource(queue: DispatchQueue.global(qos: .utility))
        timer.schedule(deadline: .now() + 1, repeating: 1)
        timer.setEventHandler { [weak self] in
            guard let self else { return }
            if let endAt = self.endAt, Date() >= endAt {
                self.stop()
                return
            }
            self.flushIfDue(force: false)
        }
        endTimer = timer
        timer.resume()
    }

    private func connect(reconnect: Bool) {
        guard StreamURLValidator.isValidStreamURL(stationURL), let url = URL(string: stationURL) else {
            fail(key: "Err.InvalidUrl", error: nil)
            return
        }
        lock.lock()
        generation += 1
        let currentGeneration = generation
        state = reconnect ? .reconnecting : .connecting
        lock.unlock()
        notifyChange()

        let connection = IcyStreamConnection(
            url: url,
            proxyMode: options.proxyMode,
            proxyHost: options.proxyHost,
            proxyPort: options.proxyPort
        )
        connection.onEvent = { [weak self] event in
            self?.handle(event, generation: currentGeneration)
        }
        lock.lock()
        if stopRequested {
            lock.unlock()
            connection.cancel()
            return
        }
        self.connection = connection
        lock.unlock()
        connection.start()
    }

    private func handle(_ event: IcyConnectionEvent, generation currentGeneration: Int) {
        lock.lock()
        let valid = generation == currentGeneration && !stopRequested && state != .ended
        lock.unlock()
        guard valid else { return }

        switch event {
        case .started(let info):
            do {
                try configureStream(info)
                lock.lock()
                failures = 0
                state = .recording
                lock.unlock()
                notifyChange()
            } catch {
                fail(key: "Err.Unsupported", error: error)
            }
        case .audio(let data):
            appendAudio(data)
        case .metadata(let metadata):
            handleMetadata(metadata)
        case .finished(let error):
            connectionFinished(error: error)
        }
    }

    private func configureStream(_ info: IcyStreamInfo) throws {
        let contentType = info.contentType.lowercased()
        let pathExtension = URL(string: stationURL)?.pathExtension.lowercased() ?? ""
        var ext = ".mp3"
        var format = "MP3"

        if contentType.contains("mpegurl") || contentType.contains("x-mpegurl") || pathExtension == "m3u8" || pathExtension == "m3u" {
            throw IcyConnectionError(message: "HLS streams cannot be recorded as a continuous compressed file")
        } else if contentType.contains("aac") || pathExtension == "aac" || pathExtension == "aacp" {
            ext = ".aac"
            format = "AAC"
        } else if contentType.contains("ogg") || pathExtension == "ogg" {
            ext = ".ogg"
            format = "OGG"
        } else if contentType.contains("opus") || pathExtension == "opus" {
            ext = ".opus"
            format = "OPUS"
        } else if contentType.contains("flac") || pathExtension == "flac" {
            ext = ".flac"
            format = "FLAC"
        }

        lock.lock()
        currentExtension = ext
        codec = format
        effectiveFormat = info.bitrateKbps > 0 ? "\(format) \(info.bitrateKbps)k" : format
        lock.unlock()
    }

    private func appendAudio(_ data: Data) {
        lock.lock()
        guard state == .recording || state == .connecting || state == .reconnecting else {
            lock.unlock()
            return
        }
        do {
            if output == nil { try openFileLocked() }
            writeBuffer.append(data)
            totalBytes += Int64(data.count)
            if writeBuffer.count >= 65536 || Date().timeIntervalSince(lastFlush) >= 3 {
                try flushBufferLocked()
                updateOpenRowLocked()
                if !hasEnoughDiskSpace() {
                    throw CocoaError(.fileWriteOutOfSpace)
                }
            }
            lock.unlock()
        } catch {
            lock.unlock()
            fail(key: "Err.RecWrite", error: error)
        }
    }

    private func handleMetadata(_ metadata: StreamMetadata) {
        let display = metadata.display
        guard !display.isEmpty else { return }
        lock.lock()
        if display == currentTitle {
            lock.unlock()
            return
        }
        let hadTitle = !currentTitle.isEmpty
        currentTitle = display
        currentTrack = display
        if hadTitle && options.splitTracks {
            closeFileLocked(status: RecordingStatus.completed)
        }
        lock.unlock()
        notifyChange()
    }

    private func connectionFinished(error: Error?) {
        lock.lock()
        let wasStopped = stopRequested || state == .ended
        if connection != nil { connection = nil }
        lock.unlock()
        if wasStopped { return }

        if let endAt, Date() >= endAt {
            finish(success: true, errorKey: "")
            return
        }

        lock.lock()
        failures += 1
        let attempt = failures
        let retryLimit = max(0, options.retryAttempts)
        lock.unlock()

        if attempt > retryLimit {
            fail(key: error == nil ? "Err.ConnectionLost" : "Err.ConnectionLost", error: error)
            return
        }

        let delay = min(2.0 + Double(attempt) * 1.5, 8.0)
        lock.lock()
        state = .reconnecting
        let currentGeneration = generation
        lock.unlock()
        notifyChange()
        DispatchQueue.global(qos: .utility).asyncAfter(deadline: .now() + delay) { [weak self] in
            guard let self else { return }
            self.lock.lock()
            let valid = self.generation == currentGeneration && !self.stopRequested && self.state != .ended
            self.lock.unlock()
            if valid { self.connect(reconnect: true) }
        }
    }

    private func openFileLocked() throws {
        let directory = RecordingFileNamer.stationDirectory(root: options.directory, station: stationName, subfolders: options.stationSubfolders)
        let now = Date()
        let baseName: String
        if !currentTitle.isEmpty {
            let metadata = StreamMetadata.fromCombined(currentTitle)
            baseName = RecordingFileNamer.expand(template: options.trackTemplate, station: stationName, artist: metadata.artist, title: metadata.title, when: now)
        } else {
            baseName = RecordingFileNamer.expand(template: options.continuousTemplate, station: stationName, artist: "", title: "", when: now)
        }
        let url = try RecordingFileNamer.uniquePath(directory: directory, baseName: baseName, extension: currentExtension)
        guard FileManager.default.createFile(atPath: url.path, contents: nil) else {
            throw CocoaError(.fileWriteUnknown)
        }
        output = try FileHandle(forWritingTo: url)
        currentFile = url.path
        currentFileTitle = currentTitle
        filesWritten += 1

        let entry = RecordingEntry(
            stationName: stationName,
            stationURL: stationURL,
            title: currentFileTitle,
            file: currentFile,
            format: effectiveFormat,
            started: now,
            status: RecordingStatus.recording,
            scheduled: scheduled
        )
        rowID = try repository.insert(entry)
    }

    private func flushBufferLocked() throws {
        guard !writeBuffer.isEmpty else { return }
        try output?.write(contentsOf: writeBuffer)
        writeBuffer.removeAll(keepingCapacity: true)
        lastFlush = Date()
    }

    private func updateOpenRowLocked() {
        guard rowID != 0 else { return }
        let bytes = fileLengthLocked()
        let entry = RecordingEntry(
            id: rowID,
            title: currentFileTitle,
            file: currentFile,
            bytes: bytes,
            status: RecordingStatus.recording
        )
        try? repository.update(entry)
    }

    private func fileLengthLocked() -> Int64 {
        let written = (try? output?.offset()) ?? 0
        return Int64(written)
    }

    private func closeFileLocked(status: String) {
        if rowID == 0 && output == nil { return }
        var bytes: Int64 = 0
        do {
            try flushBufferLocked()
            try? output?.synchronize()
            bytes = fileLengthLocked()
            try? output?.close()
        } catch {
            bytes = fileLengthLocked()
        }
        output = nil
        writeBuffer.removeAll(keepingCapacity: false)

        if rowID != 0 {
            let entry = RecordingEntry(
                id: rowID,
                title: currentFileTitle,
                file: currentFile,
                ended: Date(),
                bytes: bytes,
                status: bytes == 0 ? RecordingStatus.failed : status,
                error: bytes == 0 ? "The file is empty." : ""
            )
            try? repository.update(entry)
        }
        rowID = 0
        currentFileTitle = ""
    }

    private func flushIfDue(force: Bool) {
        lock.lock()
        defer { lock.unlock() }
        guard output != nil else { return }
        if force || Date().timeIntervalSince(lastFlush) >= 3 {
            try? flushBufferLocked()
            try? output?.synchronize()
            updateOpenRowLocked()
        }
    }

    private func hasEnoughDiskSpace() -> Bool {
        guard let attributes = try? FileManager.default.attributesOfFileSystem(forPath: options.directory.path),
              let free = attributes[.systemFreeSize] as? NSNumber else { return true }
        return free.int64Value > 30 * 1024 * 1024
    }

    private func notifyChange() {
        onChange?(self)
    }

    private func fail(key: String, error: Error?) {
        lock.lock()
        if state == .ended {
            lock.unlock()
            return
        }
        errorMessage = key
        endedByError = true
        closeFileLocked(status: RecordingStatus.failed)
        lock.unlock()
        finish(success: false, errorKey: key)
    }

    private func finish(success: Bool, errorKey: String) {
        lock.lock()
        if state == .ended {
            lock.unlock()
            return
        }
        state = .ended
        errorMessage = errorKey
        endedByError = !success
        stopRequested = true
        endTimer?.cancel()
        endTimer = nil
        let connection = self.connection
        self.connection = nil
        closeFileLocked(status: success ? RecordingStatus.completed : RecordingStatus.failed)
        lock.unlock()
        connection?.cancel()
        notifyChange()
        onEnded?(self)
    }
}

public final class RecordingManager: ObservableObject {
    @Published public private(set) var sessions: [RecordingSession] = []

    private let repository: RecordingRepository
    private let settings: SettingsStore
    private let lock = NSRecursiveLock()

    public init(repository: RecordingRepository, settings: SettingsStore) {
        self.repository = repository
        self.settings = settings
    }

    public var activeCount: Int {
        lock.lock()
        defer { lock.unlock() }
        return sessions.filter(\.isRecording).count
    }

    public func isRecordingStation(_ stationID: Int64, url: String) -> Bool {
        lock.lock()
        defer { lock.unlock() }
        return sessions.contains { $0.isRecording && ((stationID != 0 && $0.stationID == stationID) || $0.stationURL == url) }
    }

    public func findByStation(_ stationID: Int64, url: String) -> RecordingSession? {
        lock.lock()
        defer { lock.unlock() }
        return sessions.first { $0.isRecording && ((stationID != 0 && $0.stationID == stationID) || $0.stationURL == url) }
    }

    @discardableResult
    public func start(_ station: Station, endAt: Date? = nil, scheduled: Bool = false) -> RecordingSession? {
        guard StreamURLValidator.isValidStreamURL(station.url) else { return nil }
        lock.lock()
        sessions.removeAll { !$0.isRecording }
        if endAt == nil && activeCount >= settings.current.maxConcurrentRecordings {
            lock.unlock()
            return nil
        }
        if sessions.contains(where: { $0.isRecording && $0.stationURL == station.url }) {
            lock.unlock()
            return nil
        }
        lock.unlock()

        let current = settings.current
        let options = RecordingOptions(
            format: current.recordingFormat,
            mp3Bitrate: current.mp3Bitrate,
            splitTracks: current.splitTracks,
            trackTemplate: current.trackFileTemplate,
            continuousTemplate: current.continuousFileTemplate,
            directory: URL(fileURLWithPath: current.recordingDirectory),
            stationSubfolders: current.stationSubfolders,
            retryAttempts: max(0, current.retryAttempts),
            proxyMode: current.proxyMode,
            proxyHost: current.proxyHost,
            proxyPort: current.proxyPort
        )
        let session = RecordingSession(
            stationID: station.id,
            stationName: station.name,
            stationURL: station.url,
            options: options,
            repository: repository,
            endAt: endAt,
            scheduled: scheduled
        )
        session.onChange = { [weak self] _ in self?.notifyChanged() }
        session.onEnded = { [weak self] ended in
            guard let self else { return }
            self.lock.lock()
            self.sessions.removeAll { $0.id == ended.id }
            self.lock.unlock()
            self.notifyChanged()
        }
        lock.lock()
        sessions.append(session)
        lock.unlock()
        session.start()
        notifyChanged()
        return session
    }

    @discardableResult
    public func stopStation(_ stationID: Int64, url: String) -> Bool {
        guard let session = findByStation(stationID, url: url) else { return false }
        session.stop()
        return true
    }

    public func stopAll() {
        lock.lock()
        let active = sessions
        lock.unlock()
        active.forEach { $0.stop() }
    }

    private func notifyChanged() {
        DispatchQueue.main.async { [weak self] in
            self?.objectWillChange.send()
        }
    }
}

public final class RecordingScheduler {
    private let repository: RecordingRepository
    private let manager: RecordingManager
    private let stations: StationRepository
    private var timer: Timer?

    public var onScheduledRecordingStarted: ((String) -> Void)?

    public init(repository: RecordingRepository, manager: RecordingManager, stations: StationRepository) {
        self.repository = repository
        self.manager = manager
        self.stations = stations
    }

    public func start() {
        stop()
        timer = Timer.scheduledTimer(withTimeInterval: 15, repeats: true) { [weak self] _ in
            self?.tick()
        }
        timer?.fire()
    }

    public func stop() {
        timer?.invalidate()
        timer = nil
    }

    private func tick() {
        guard let schedules = try? repository.schedules() else { return }
        let now = Date()
        for var schedule in schedules where schedule.enabled {
            guard let occurrence = schedule.activeOccurrence(at: now) else { continue }
            if let lastRun = schedule.lastRun, lastRun >= occurrence.start { continue }
            schedule.lastRun = occurrence.start
            try? repository.saveSchedule(&schedule)
            startOccurrence(schedule, end: occurrence.end)
        }
    }

    private func startOccurrence(_ schedule: RecordingSchedule, end: Date) {
        var station = (try? stations.get(schedule.stationID)) ?? Station(
            id: schedule.stationID,
            name: schedule.stationName,
            url: schedule.stationURL
        )
        if station.name.isEmpty { station.name = schedule.stationName }
        if station.url.isEmpty { station.url = schedule.stationURL }
        guard !manager.isRecordingStation(station.id, url: station.url) else { return }
        if manager.start(station, endAt: end, scheduled: true) != nil {
            onScheduledRecordingStarted?(station.name)
        }
    }
}
