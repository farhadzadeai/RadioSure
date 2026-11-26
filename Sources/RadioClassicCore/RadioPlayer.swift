import AVFoundation
import Foundation

@MainActor
public final class RadioPlayer: NSObject, ObservableObject {
    @Published public private(set) var state: PlayerState = .stopped
    @Published public private(set) var station: Station?
    @Published public private(set) var metadata: StreamMetadata = .empty
    @Published public private(set) var streamName = ""
    @Published public private(set) var codec = ""
    @Published public private(set) var bitrateKbps = 0
    @Published public private(set) var bufferPercent = 0
    @Published public private(set) var elapsed: TimeInterval = 0
    @Published public private(set) var lastErrorKey = ""
    @Published public private(set) var reconnectAttempt = 0
    @Published public private(set) var isStalled = false
    @Published public var volume = 70 {
        didSet { applyVolume() }
    }
    @Published public var muted = false {
        didSet { applyVolume() }
    }

    public var onStateChanged: (() -> Void)?
    public var onMetadataChanged: ((StreamMetadata) -> Void)?

    private let settings: SettingsStore
    private var player: AVPlayer?
    private var item: AVPlayerItem?
    private var observations: [NSKeyValueObservation] = []
    private var notificationObservers: [NSObjectProtocol] = []
    private var generation = 0
    private var metadataConnection: IcyStreamConnection?
    private var tapMonitor = AudioTapMonitor()
    private var watchdogTimer: Timer?
    private var playingSince = Date()
    private var silenceRetried = false
    private var waitingSince: Date?
    private var lastStallNotification = Date.distantPast
    private var monitorHadData = false

    public init(settings: SettingsStore) {
        self.settings = settings
        super.init()
        volume = settings.current.volume
        muted = settings.current.muted
        startWatchdog()
    }

    public func shutdown() {
        watchdogTimer?.invalidate()
        watchdogTimer = nil
        cleanupPlaybackItem()
        metadataConnection?.cancel()
        metadataConnection = nil
    }

    // MARK: - Transport

    public func play(_ station: Station) {
        guard StreamURLValidator.isValidStreamURL(station.url), let url = URL(string: station.url) else {
            self.station = station
            fail(with: "Err.InvalidUrl")
            return
        }
        let codecHint = station.codec.uppercased()
        let pathType = url.pathExtension.lowercased()
        if codecHint.contains("OGG") || codecHint.contains("OPUS") || codecHint.contains("FLAC") ||
            ["ogg", "opus", "flac"].contains(pathType) {
            self.station = station
            fail(with: "Err.Unsupported")
            return
        }
        generation += 1
        self.station = station
        metadata = .empty
        streamName = ""
        codec = station.codec.uppercased()
        bitrateKbps = station.bitrate
        reconnectAttempt = 0
        silenceRetried = false
        waitingSince = nil
        monitorHadData = false
        setState(.connecting)
        onMetadataChanged?(metadata)
        connect(url: url, generation: generation, reconnect: false)
    }

    public func pause() {
        guard state == .playing else { return }
        player?.pause()
        setState(.paused)
    }

    public func resume() {
        guard let station else { return }
        if state == .paused, item != nil {
            player?.play()
            playingSince = Date().addingTimeInterval(-elapsed)
            setState(.connecting)
        } else {
            play(station)
        }
    }

    public func stop() {
        generation += 1
        cleanupPlaybackItem()
        metadataConnection?.cancel()
        metadataConnection = nil
        metadata = .empty
        elapsed = 0
        bufferPercent = 0
        isStalled = false
        setState(.stopped)
        onMetadataChanged?(metadata)
    }

    // MARK: - Connection

    private func connect(url: URL, generation currentGeneration: Int, reconnect: Bool) {
        cleanupPlaybackItem()
        metadataConnection?.cancel()

        let item = AVPlayerItem(url: url)
        item.preferredForwardBufferDuration = TimeInterval(settings.current.bufferMilliseconds) / 1000
        let tap = AudioTapMonitor(equalizer: settings.current.equalizer)
        tapMonitor.invalidate()
        tapMonitor = tap
        if let mix = tap.makeAudioMix() {
            item.audioMix = mix
        }
        self.item = item

        if player == nil {
            player = AVPlayer(playerItem: item)
        } else {
            player?.replaceCurrentItem(with: item)
        }
        player?.automaticallyWaitsToMinimizeStalling = true
        applyVolume()
        attachObservers(to: item, generation: currentGeneration)
        reconnectAttempt = reconnect ? reconnectAttempt + 1 : 0
        setState(reconnect ? .reconnecting : .connecting)
        player?.play()
        startMetadataMonitor(url: url, generation: currentGeneration)
    }

    private func cleanupPlaybackItem() {
        tapMonitor.invalidate()
        observations.forEach { $0.invalidate() }
        observations.removeAll()
        notificationObservers.forEach { NotificationCenter.default.removeObserver($0) }
        notificationObservers.removeAll()
        player?.replaceCurrentItem(with: nil)
        item = nil
    }

    private func attachObservers(to item: AVPlayerItem, generation currentGeneration: Int) {
        observations.append(item.observe(\.status, options: [.new], changeHandler: { [weak self] _, _ in
            Task { @MainActor in
                self?.handlePlaybackUpdate(generation: currentGeneration)
            }
        }))
        observations.append(item.observe(\.isPlaybackBufferEmpty, options: [.new], changeHandler: { [weak self] _, _ in
            Task { @MainActor in
                self?.handlePlaybackUpdate(generation: currentGeneration)
            }
        }))
        observations.append(item.observe(\.isPlaybackLikelyToKeepUp, options: [.new], changeHandler: { [weak self] _, _ in
            Task { @MainActor in
                self?.handlePlaybackUpdate(generation: currentGeneration)
            }
        }))
        if let player {
            observations.append(player.observe(\.timeControlStatus, options: [.new], changeHandler: { [weak self] _, _ in
                Task { @MainActor in
                    self?.handlePlaybackUpdate(generation: currentGeneration)
                }
            }))
            observations.append(player.observe(\.rate, options: [.new], changeHandler: { [weak self] _, _ in
                Task { @MainActor in
                    self?.handlePlaybackUpdate(generation: currentGeneration)
                }
            }))
        }

        let center = NotificationCenter.default
        notificationObservers.append(center.addObserver(forName: .AVPlayerItemFailedToPlayToEndTime, object: item, queue: .main) { [weak self] notification in
            Task { @MainActor in
                self?.handleItemFailure(notification.userInfo?[AVPlayerItemFailedToPlayToEndTimeErrorKey] as? Error, generation: currentGeneration)
            }
        })
        notificationObservers.append(center.addObserver(forName: .AVPlayerItemPlaybackStalled, object: item, queue: .main) { [weak self] _ in
            Task { @MainActor in
                self?.lastStallNotification = Date()
                self?.handlePlaybackUpdate(generation: currentGeneration)
            }
        })
        notificationObservers.append(center.addObserver(forName: .AVPlayerItemDidPlayToEndTime, object: item, queue: .main) { [weak self] _ in
            Task { @MainActor in
                self?.handleStreamEnded(generation: currentGeneration)
            }
        })
    }

    private func handlePlaybackUpdate(generation currentGeneration: Int) {
        guard generation == currentGeneration, state != .stopped, state != .error else { return }
        if let error = item?.error {
            handleItemFailure(error, generation: currentGeneration)
            return
        }
        if item?.status == .failed {
            handleItemFailure(item?.error, generation: currentGeneration)
            return
        }
        guard let player else { return }
        if player.timeControlStatus == .playing || player.rate > 0.05 {
            waitingSince = nil
            if state != .playing {
                playingSince = Date()
                elapsed = 0
                tapMonitor.resetSilenceMeasurement()
                setState(.playing)
            }
        } else if player.timeControlStatus == .waitingToPlayAtSpecifiedRate {
            if waitingSince == nil { waitingSince = Date() }
        }
    }

    private func handleItemFailure(_ error: Error?, generation currentGeneration: Int) {
        guard generation == currentGeneration, state != .stopped, state != .error else { return }
        retryOrFail(error, stalled: false)
    }

    private func handleStreamEnded(generation currentGeneration: Int) {
        guard generation == currentGeneration, state == .playing || state == .paused else { return }
        retryOrFail(nil, stalled: false)
    }

    private func retryOrFail(_ error: Error?, stalled: Bool) {
        let maximum = settings.current.autoReconnect ? max(0, settings.current.retryAttempts) : 0
        if reconnectAttempt < maximum, let station, let url = URL(string: station.url) {
            let delay = min(2.0 + Double(reconnectAttempt) * 1.5, 10.0)
            setState(.reconnecting)
            let currentGeneration = generation
            Task { [weak self] in
                try? await Task.sleep(nanoseconds: UInt64(delay * 1_000_000_000))
                guard let self else { return }
                await MainActor.run {
                    guard self.generation == currentGeneration, self.state != .stopped, self.state != .error else { return }
                    self.generation += 1
                    self.connect(url: url, generation: self.generation, reconnect: true)
                }
            }
        } else {
            fail(with: stalled ? "Err.StreamStalled" : errorKey(for: error))
        }
    }

    private func reconnectAfterBadAudio(stalled: Bool) {
        guard let station, let url = URL(string: station.url) else { return }
        generation += 1
        connect(url: url, generation: generation, reconnect: true)
    }

    // MARK: - Metadata monitor

    private func startMetadataMonitor(url: URL, generation currentGeneration: Int) {
        let current = settings.current
        let connection = IcyStreamConnection(
            url: url,
            timeout: TimeInterval(current.timeoutSeconds),
            proxyMode: current.proxyMode,
            proxyHost: current.proxyHost,
            proxyPort: current.proxyPort
        )
        connection.onEvent = { [weak self, weak connection] event in
            Task { @MainActor in
                guard let self, self.generation == currentGeneration else { return }
                if let connection, self.metadataConnection !== connection { return }
                self.handleMetadataEvent(event)
            }
        }
        metadataConnection = connection
        connection.start()
    }

    private func handleMetadataEvent(_ event: IcyConnectionEvent) {
        switch event {
        case .started(let info):
            if !info.streamName.isEmpty { streamName = info.streamName }
            if info.bitrateKbps > 0 { bitrateKbps = info.bitrateKbps }
            if codec.isEmpty { codec = codecFromContentType(info.contentType) }
        case .metadata(let metadata):
            guard metadata != self.metadata else { return }
            self.metadata = metadata
            onMetadataChanged?(metadata)
        case .audio:
            monitorHadData = true
        case .finished:
            break
        }
    }

    private func codecFromContentType(_ contentType: String) -> String {
        let type = contentType.lowercased()
        if type.contains("aac") { return "AAC" }
        if type.contains("ogg") { return "OGG" }
        if type.contains("opus") { return "OPUS" }
        if type.contains("flac") { return "FLAC" }
        if type.contains("mpegurl") { return "HLS" }
        if type.contains("mpeg") || type.contains("mp3") { return "MP3" }
        return codec
    }

    // MARK: - Watchdog and status

    private func startWatchdog() {
        watchdogTimer?.invalidate()
        watchdogTimer = Timer.scheduledTimer(withTimeInterval: 0.5, repeats: true) { [weak self] _ in
            Task { @MainActor in
                self?.tick()
            }
        }
    }

    public func tick() {
        updateBufferStatus()
        guard state == .playing else { return }
        elapsed = Date().timeIntervalSince(playingSince)

        var stalled = false
        if let item, item.isPlaybackBufferEmpty, player?.timeControlStatus != .playing {
            if waitingSince == nil { waitingSince = Date() }
        } else if Date().timeIntervalSince(lastStallNotification) < 4 {
            if waitingSince == nil { waitingSince = lastStallNotification }
        } else if player?.timeControlStatus == .waitingToPlayAtSpecifiedRate {
            if waitingSince == nil { waitingSince = Date() }
        } else {
            waitingSince = nil
        }
        if let waitingSince, Date().timeIntervalSince(waitingSince) >= 8 {
            stalled = true
        }
        if isStalled != stalled {
            isStalled = stalled
            onStateChanged?()
        }

        let silenceDuration = tapMonitor.continuousSilenceDuration
        let silent = tapMonitor.isProducingAudio && silenceDuration >= 8
        if !silent && !stalled {
            if silenceDuration < 1 { silenceRetried = false }
            return
        }

        if !silenceRetried && settings.current.autoReconnect {
            silenceRetried = true
            reconnectAfterBadAudio(stalled: stalled)
        } else {
            fail(with: stalled ? "Err.StreamStalled" : "Err.NoAudio")
        }
    }

    private func updateBufferStatus() {
        guard state == .playing || state == .connecting || state == .reconnecting || state == .paused,
              let item else {
            if bufferPercent != 0 { bufferPercent = 0 }
            return
        }
        if item.isPlaybackLikelyToKeepUp {
            bufferPercent = 100
            return
        }
        guard let range = item.loadedTimeRanges.first?.timeRangeValue else {
            bufferPercent = state == .playing ? 50 : 0
            return
        }
        let end = CMTimeGetSeconds(range.start + range.duration)
        let current = CMTimeGetSeconds(item.currentTime())
        guard end.isFinite, current.isFinite else {
            bufferPercent = state == .playing ? 100 : 0
            return
        }
        let target = max(1, TimeInterval(settings.current.bufferMilliseconds) / 1000)
        let ahead = max(0, end - current)
        bufferPercent = min(100, max(0, Int((ahead / target * 100).rounded())))
    }

    // MARK: - Volume, analysis and errors

    private func effectiveVolume() -> Float {
        let linear = Float(max(0, min(100, volume))) / 100
        return muted ? 0 : linear * linear
    }

    private func applyVolume() {
        player?.volume = effectiveVolume()
    }

    public func spectrum(bins: Int) -> [Float] {
        tapMonitor.spectrum(bins: bins)
    }

    public func updateEqualizer(_ settings: EqSettings) {
        tapMonitor.updateEqualizer(settings)
    }

    public var audioLevel: Float {
        tapMonitor.audioLevel
    }

    public var silenceMeasurementAvailable: Bool {
        tapMonitor.isProducingAudio
    }

    private func fail(with key: String) {
        lastErrorKey = key
        cleanupPlaybackItem()
        metadataConnection?.cancel()
        metadataConnection = nil
        isStalled = false
        setState(.error)
    }

    private func errorKey(for error: Error?) -> String {
        guard let error else { return "Err.CannotOpen" }
        let nsError = error as NSError
        if nsError.domain == NSURLErrorDomain {
            switch nsError.code {
            case NSURLErrorNotConnectedToInternet: return "Err.NoInternet"
            case NSURLErrorTimedOut: return "Err.Timeout"
            case NSURLErrorSecureConnectionFailed, NSURLErrorServerCertificateHasBadDate,
                 NSURLErrorServerCertificateUntrusted, NSURLErrorServerCertificateHasUnknownRoot,
                 NSURLErrorServerCertificateNotYetValid, NSURLErrorClientCertificateRejected,
                 NSURLErrorClientCertificateRequired: return "Err.Ssl"
            case NSURLErrorCannotFindHost, NSURLErrorCannotConnectToHost, NSURLErrorNetworkConnectionLost:
                return "Err.CannotOpen"
            default: break
            }
        }
        return "Err.CannotOpen"
    }

    private func setState(_ newState: PlayerState) {
        guard state != newState else { return }
        state = newState
        onStateChanged?()
    }
}
