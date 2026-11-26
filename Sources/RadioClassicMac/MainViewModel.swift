import AppKit
import Foundation
import RadioClassicCore
import SwiftUI
import UniformTypeIdentifiers
@preconcurrency import UserNotifications

@MainActor
final class MainViewModel: ObservableObject {
    let settingsStore: SettingsStore
    let localization: LocalizationService
    let themeService: ThemeService
    let player: RadioPlayer
    let stationRepository: StationRepository
    let recordingRepository: RecordingRepository
    let recordingManager: RecordingManager
    let scheduler: RecordingScheduler
    private let database: SQLiteDatabase
    private let syncService = StationSyncService()

    @Published var stations: [Station] = []
    @Published var selectedStationID: Int64?
    @Published var searchText = "" {
        didSet { scheduleSearchReload() }
    }
    @Published var view: StationView = .all {
        didSet { reloadStations() }
    }
    @Published var filterCountry = "" {
        didSet { reloadStations() }
    }
    @Published var filterLanguage = "" {
        didSet { reloadStations() }
    }
    @Published var filterGenre = "" {
        didSet { scheduleSearchReload() }
    }
    @Published var filterCodec = "" {
        didSet { reloadStations() }
    }
    @Published var filterMinBitrate = 0 {
        didSet { reloadStations() }
    }
    @Published var favoriteFolder = "" {
        didSet { reloadStations() }
    }
    @Published var filtersVisible = false

    @Published var countries: [String] = []
    @Published var languages: [String] = []
    @Published var codecs: [String] = []
    @Published var favoriteFolders: [String] = []

    @Published var stationCountText = ""
    @Published var databaseStatus = ""
    @Published var databaseBusy = false
    @Published var stateText = ""
    @Published var stationLine = ""
    @Published var trackLine = ""
    @Published var qualityText = ""
    @Published var errorText = ""
    @Published var bufferText = ""
    @Published var elapsedText = ""
    @Published var recordingText = ""
    @Published var isRecordingActive = false
    @Published var targetStationRecording = false
    @Published var isFavorite = false
    @Published var spectrum: [Float] = Array(repeating: 0, count: 28)
    @Published var compactMode = false
    @Published var startupError = ""
    @Published var activeSheet: AppSheet?

    private var queryGeneration = 0
    private var searchTask: Task<Void, Never>?
    private var lastVolumeSaved = -1
    private var lastMutedSaved: Bool?
    private var lastEqualizer = EqSettings()
    private var regularWindowSize: NSSize?

    var selectedStation: Station? {
        stations.first { $0.id == selectedStationID }
    }

    var windowTitle: String {
        let app = localization.t("AppTitle")
        let track = trackLine.trimmingCharacters(in: .whitespacesAndNewlines)
        return track.isEmpty ? app : "\(track) - \(app)"
    }

    init() {
        let settings = SettingsStore()
        settings.load()
        settingsStore = settings

        let localization = LocalizationService.shared
        localization.setLanguage(settings.current.language)
        self.localization = localization

        let themes = ThemeService.shared
        themes.setTheme(settings.current.theme)
        themes.customAccentHex = settings.current.accentColor
        themeService = themes

        let openedDatabase: SQLiteDatabase
        do {
            openedDatabase = try SQLiteDatabase()
        } catch {
            let fallback = FileManager.default.temporaryDirectory.appendingPathComponent("radioclassic-recovery-" + UUID().uuidString + ".db")
            openedDatabase = (try? SQLiteDatabase(file: fallback)) ?? {
                preconditionFailure("SQLite could not be initialized")
            }()
            startupError = error.localizedDescription
        }
        database = openedDatabase
        stationRepository = StationRepository(db: openedDatabase)
        recordingRepository = RecordingRepository(db: openedDatabase)
        recordingManager = RecordingManager(repository: recordingRepository, settings: settings)
        scheduler = RecordingScheduler(repository: recordingRepository, manager: recordingManager, stations: stationRepository)
        player = RadioPlayer(settings: settings)
        lastEqualizer = settings.current.equalizer

        filtersVisible = settings.current.showFilters
        compactMode = settings.current.compactMode
        _ = try? recordingRepository.markInterrupted()

        player.onStateChanged = { [weak self] in
            self?.refreshPlayerState()
        }
        player.onMetadataChanged = { [weak self] metadata in
            self?.handleMetadata(metadata)
        }
        scheduler.onScheduledRecordingStarted = { [weak self] station in
            guard let self else { return }
            self.databaseStatus = self.localization.t("Sch.Started", station)
            self.postScheduledNotification(station: station)
        }

        refreshPlayerState()
        loadFilterLists()
        reloadStations()
        scheduler.start()
        resumeLastStationIfNeeded()

        Task { [weak self] in
            try? await Task.sleep(nanoseconds: 700_000_000)
            self?.refreshDatabaseIfNeeded()
        }
    }

    // MARK: - Station queries

    private func scheduleSearchReload() {
        searchTask?.cancel()
        searchTask = Task { [weak self] in
            try? await Task.sleep(nanoseconds: 200_000_000)
            guard !Task.isCancelled else { return }
            self?.reloadStations()
        }
    }

    private func buildQuery() -> StationQuery {
        var query = StationQuery()
        query.view = view
        query.search = searchText.trimmingCharacters(in: .whitespacesAndNewlines)
        query.country = filterCountry
        query.language = filterLanguage
        query.genre = filterGenre
        query.codec = filterCodec
        query.minBitrate = filterMinBitrate
        query.favoriteFolder = favoriteFolder
        query.sort = settingsStore.current.sort
        query.descending = settingsStore.current.sortDescending
        return query
    }

    func reloadStations() {
        queryGeneration += 1
        let generation = queryGeneration
        let query = buildQuery()
        let repository = stationRepository
        Task.detached(priority: .userInitiated) { [weak self] in
            let result = (try? repository.search(query)) ?? []
            await MainActor.run { [weak self] in
                guard let self, self.queryGeneration == generation else { return }
                self.stations = result
                self.stationCountText = self.localization.t("StationsFound", result.count)
                self.refreshRecordingState()
            }
        }
    }

    func loadFilterLists() {
        let repository = stationRepository
        Task.detached(priority: .utility) { [weak self] in
            let countries = (try? repository.countries()) ?? []
            let languages = (try? repository.languages()) ?? []
            let codecs = (try? repository.codecs()) ?? []
            let folders = (try? repository.favoriteFolders()) ?? []
            await MainActor.run { [weak self] in
                self?.countries = countries
                self?.languages = languages
                self?.codecs = codecs
                self?.favoriteFolders = folders
            }
        }
    }

    func sortBy(_ sort: StationSort) {
        if settingsStore.current.sort == sort {
            settingsStore.current.sortDescending.toggle()
        } else {
            settingsStore.current.sort = sort
            settingsStore.current.sortDescending = sort == .bitrate || sort == .votes || sort == .clicks
        }
        settingsStore.save()
        reloadStations()
    }

    func select(_ station: Station) {
        selectedStationID = station.id
        refreshFavoriteState()
        refreshRecordingState()
    }

    func fullStation(_ station: Station) -> Station {
        guard station.url.isEmpty, let full = try? stationRepository.get(station.id) else { return station }
        return full
    }

    // MARK: - Database sync

    func refreshDatabaseIfNeeded() {
        guard syncService.isRefreshDue(repository: stationRepository, settings: settingsStore.current) else { return }
        refreshDatabase()
    }

    func refreshDatabase() {
        guard !databaseBusy else { return }
        databaseBusy = true
        databaseStatus = localization.t("UpdatingDb")
        let network = settingsStore.current
        let client = RadioBrowserClient(
            timeout: TimeInterval(network.timeoutSeconds),
            proxyMode: network.proxyMode,
            proxyHost: network.proxyHost,
            proxyPort: network.proxyPort
        )
        let repository = stationRepository
        Task { [weak self] in
            guard let self else { return }
            do {
                let total = try await self.syncService.refresh(client: client, repository: repository) { count in
                    Task { @MainActor [weak self] in
                        self?.databaseStatus = self?.localization.t("UpdatingDbProgress", count) ?? ""
                    }
                }
                self.settingsStore.current.lastDatabaseSync = Date()
                self.settingsStore.save()
                self.databaseBusy = false
                self.databaseStatus = self.localization.t("DbUpdated", total)
                self.loadFilterLists()
                self.reloadStations()
            } catch {
                self.databaseBusy = false
                self.databaseStatus = self.localization.t("DbUpdateFailed", error.localizedDescription)
            }
        }
    }

    private func resumeLastStationIfNeeded() {
        guard settingsStore.current.resumeOnStart,
              settingsStore.current.rememberLastStation,
              settingsStore.current.lastStationID > 0,
              let station = try? stationRepository.get(settingsStore.current.lastStationID) else { return }
        play(station)
    }

    // MARK: - Playback

    func play(_ station: Station) {
        let full = fullStation(station)
        guard !full.url.isEmpty else { return }
        player.play(full)
        selectedStationID = full.id
        let repository = stationRepository
        let shouldReportClick = settingsStore.current.reportClicks
        let network = settingsStore.current
        Task.detached(priority: .utility) {
            try? repository.addHistory(full)
            if shouldReportClick, let uuid = full.uuid {
                let client = RadioBrowserClient(
                    timeout: TimeInterval(network.timeoutSeconds),
                    proxyMode: network.proxyMode,
                    proxyHost: network.proxyHost,
                    proxyPort: network.proxyPort
                )
                await client.reportClick(uuid: uuid)
            }
        }
        if settingsStore.current.rememberLastStation {
            settingsStore.current.lastStationID = full.id
        }
        addSearchHistory()
        refreshFavoriteState()
    }

    func playSelected() {
        if let selectedStation { play(selectedStation) }
    }

    func playPause() {
        switch player.state {
        case .playing:
            player.pause()
        case .paused:
            player.resume()
        case .connecting, .reconnecting:
            player.stop()
        case .stopped, .error:
            if let selectedStation {
                play(selectedStation)
            } else if let first = stations.first {
                play(first)
            }
        }
    }

    func adjustVolume(by delta: Int) {
        player.volume = min(100, max(0, player.volume + delta))
    }

    func stop() {
        player.stop()
    }

    func step(_ direction: Int) {
        guard !stations.isEmpty else { return }
        let referenceID = selectedStationID ?? player.station?.id
        var index = referenceID.flatMap { id in stations.firstIndex { $0.id == id } } ?? -1
        index += direction
        if index < 0 { index = stations.count - 1 }
        if index >= stations.count { index = 0 }
        play(stations[index])
    }

    func refreshPlayerState() {
        stateText = switch player.state {
        case .connecting: localization.t("State.Connecting")
        case .reconnecting: localization.t("State.Reconnecting", player.reconnectAttempt)
        case .playing: localization.t("State.Playing")
        case .paused: localization.t("State.Paused")
        case .error: localization.t("State.Error")
        case .stopped: localization.t("State.Stopped")
        }
        errorText = player.state == .error && !player.lastErrorKey.isEmpty ? localization.t(player.lastErrorKey) : ""
        stationLine = player.station?.name ?? ""
        if player.state == .playing {
            qualityText = player.bitrateKbps > 0
                ? localization.t("QualityFmt", player.codec.isEmpty ? "MP3" : player.codec, player.bitrateKbps)
                : player.codec
        } else if player.state == .stopped || player.state == .error {
            qualityText = ""
            if player.state == .stopped { trackLine = "" }
        }
        objectWillChange.send()
    }

    private func handleMetadata(_ metadata: StreamMetadata) {
        trackLine = metadata.display
        if !metadata.display.isEmpty, let station = player.station {
            let repository = stationRepository
            let stationName = station.name
            let display = metadata.display
            Task.detached(priority: .utility) {
                try? repository.addSong(stationName: stationName, title: display)
            }
        }
        objectWillChange.send()
    }

    func tick() {
        if player.volume != lastVolumeSaved {
            lastVolumeSaved = player.volume
            settingsStore.current.volume = player.volume
        }
        if player.muted != lastMutedSaved {
            lastMutedSaved = player.muted
            settingsStore.current.muted = player.muted
        }

        if player.state == .playing {
            bufferText = localization.t("BufferFmt", player.bufferPercent)
            elapsedText = "(\(Formatting.duration(player.elapsed)))"
            refreshPlayerState()
        } else if player.state == .connecting || player.state == .reconnecting {
            bufferText = localization.t("BufferFmt", player.bufferPercent)
            elapsedText = ""
        } else {
            bufferText = localization.t("BufferFmt", 0)
            elapsedText = ""
        }
        if lastEqualizer != settingsStore.current.equalizer {
            lastEqualizer = settingsStore.current.equalizer
            player.updateEqualizer(lastEqualizer)
        }
        spectrum = player.spectrum(bins: 28)
        refreshRecordingState()
    }

    private func postScheduledNotification(station: String) {
        guard settingsStore.current.showNotifications,
              settingsStore.current.notifyScheduledRecordings else { return }
        let content = UNMutableNotificationContent()
        content.title = localization.t("Rec.Title")
        content.body = localization.t("Sch.Started", station)
        content.sound = .default
        let request = UNNotificationRequest(identifier: UUID().uuidString, content: content, trigger: nil)
        UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound]) { granted, _ in
            guard granted else { return }
            UNUserNotificationCenter.current().add(request)
        }
    }

    // MARK: - Favorites and station management

    func refreshFavoriteState() {
        guard let station = player.station ?? selectedStation else {
            isFavorite = false
            return
        }
        let repository = stationRepository
        let id = station.id
        Task.detached(priority: .utility) { [weak self] in
            let favorite = (try? repository.isFavorite(id)) ?? false
            await MainActor.run { [weak self] in
                guard let self, (self.player.station ?? self.selectedStation)?.id == id else { return }
                self.isFavorite = favorite
            }
        }
    }

    func setFavorite(_ station: Station, folder: String) {
        let full = fullStation(station)
        do {
            try stationRepository.setFavorite(full.id, folder: folder.trimmingCharacters(in: .whitespacesAndNewlines))
            selectedStationID = full.id
            isFavorite = true
            databaseStatus = localization.t("Fav.Added")
            loadFilterLists()
            if view == .favorites { reloadStations() }
        } catch {
            databaseStatus = error.localizedDescription
        }
    }

    func toggleFavorite() {
        guard let station = player.station ?? selectedStation else { return }
        do {
            if try stationRepository.isFavorite(station.id) {
                try stationRepository.removeFavorite(station.id)
                isFavorite = false
            } else {
                try stationRepository.setFavorite(station.id)
                isFavorite = true
                databaseStatus = localization.t("Fav.Added")
            }
            loadFilterLists()
            if view == .favorites { reloadStations() }
        } catch {
            databaseStatus = error.localizedDescription
        }
    }

    func saveStation(_ station: Station, isNew: Bool) throws {
        var copy = station
        copy.name = TextUtilities.clean(copy.name, maxLength: 180)
        copy.url = copy.url.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !copy.name.isEmpty else { throw PlaylistIOError.malformed(localization.t("St.NameRequired")) }
        guard StreamURLValidator.isValidStreamURL(copy.url) else { throw PlaylistIOError.malformed(localization.t("St.UrlInvalid")) }
        if isNew {
            if try stationRepository.findByURL(copy.url) == nil {
                _ = try stationRepository.addCustom(copy)
            }
        } else {
            try stationRepository.update(copy)
        }
        loadFilterLists()
        reloadStations()
    }

    func deleteStation(_ station: Station) throws {
        try stationRepository.delete(station.id)
        if selectedStationID == station.id { selectedStationID = nil }
        reloadStations()
    }

    // MARK: - Recording

    func toggleRecord() {
        guard let target = player.station ?? selectedStation else { return }
        let station = fullStation(target)
        if recordingManager.stopStation(station.id, url: station.url) {
            refreshRecordingState()
            return
        }
        guard !recordingManager.isRecordingStation(station.id, url: station.url) else { return }
        if recordingManager.start(station) == nil {
            errorText = localization.t("Rec.LimitReached")
        }
        refreshRecordingState()
    }

    func refreshRecordingState() {
        isRecordingActive = recordingManager.activeCount > 0
        if isRecordingActive, let first = recordingManager.sessions.first(where: \.isRecording) {
            let duration = Formatting.duration(Date().timeIntervalSince(first.startedAt))
            let count = recordingManager.activeCount
            recordingText = count > 1 ? "REC \(duration) (+\(count - 1))" : "REC \(duration)"
        } else {
            recordingText = ""
        }
        if let target = player.station ?? selectedStation {
            targetStationRecording = recordingManager.isRecordingStation(target.id, url: target.url)
        } else {
            targetStationRecording = false
        }
    }

    // MARK: - Import/export and preferences

    func importStations() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = ["json", "m3u", "m3u8", "pls", "xspf"].compactMap { UTType(filenameExtension: $0) }
        panel.allowsMultipleSelection = false
        panel.canChooseDirectories = false
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do {
            let imported = try PlaylistIO.importPlaylist(from: url)
            var inserted = 0
            for station in imported {
                if try stationRepository.findByURL(station.url) == nil {
                    _ = try stationRepository.addCustom(station)
                    inserted += 1
                }
            }
            databaseStatus = localization.t("Io.Imported", inserted)
            loadFilterLists()
            reloadStations()
        } catch {
            databaseStatus = localization.t("Io.ImportFailed", error.localizedDescription)
        }
    }

    func exportFavorites() {
        do {
            let favorites = try stationRepository.favorites()
            guard !favorites.isEmpty else { return }
            let panel = NSSavePanel()
            panel.allowedContentTypes = ["m3u", "pls", "xspf", "json"].compactMap { UTType(filenameExtension: $0) }
            panel.nameFieldStringValue = "favorites.m3u"
            guard panel.runModal() == .OK, let url = panel.url else { return }
            try PlaylistIO.exportData(for: url.pathExtension, stations: favorites).write(to: url, options: .atomic)
            databaseStatus = localization.t("Io.Exported")
        } catch {
            databaseStatus = localization.t("Io.ExportFailed", error.localizedDescription)
        }
    }

    func applyPreferences() {
        localization.setLanguage(settingsStore.current.language)
        themeService.setTheme(settingsStore.current.theme)
        themeService.customAccentHex = settingsStore.current.accentColor
        filtersVisible = settingsStore.current.showFilters
        compactMode = settingsStore.current.compactMode
        LoginItemService.setEnabled(settingsStore.current.startAtLogin)
        settingsStore.save()
        refreshPlayerState()
        objectWillChange.send()
    }

    func toggleCompactMode() {
        compactMode.toggle()
        settingsStore.current.compactMode = compactMode
        applyWindowMode()
        objectWillChange.send()
    }

    func applyWindowMode() {
        guard let window = MainWindowRegistry.shared.window else { return }
        if compactMode {
            if regularWindowSize == nil, window.frame.size.height > 200 {
                regularWindowSize = window.frame.size
            }
            if abs(window.frame.size.width - 380) > 2 || window.frame.size.height > 160 {
                window.setContentSize(NSSize(width: 380, height: 126))
            }
        } else {
            let fallback = NSSize(width: settingsStore.current.windowWidth, height: settingsStore.current.windowHeight)
            window.setContentSize(regularWindowSize ?? fallback)
            regularWindowSize = nil
        }
    }

    func setFiltersVisible(_ visible: Bool) {
        filtersVisible = visible
        settingsStore.current.showFilters = visible
    }

    private func addSearchHistory() {
        let term = searchText.trimmingCharacters(in: .whitespacesAndNewlines)
        guard term.count > 1 else { return }
        settingsStore.current.searchHistory.removeAll { $0.caseInsensitiveCompare(term) == .orderedSame }
        settingsStore.current.searchHistory.insert(term, at: 0)
        if settingsStore.current.searchHistory.count > 10 {
            settingsStore.current.searchHistory.removeLast(settingsStore.current.searchHistory.count - 10)
        }
    }

    func copyStationURL(_ station: Station) {
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(fullStation(station).url, forType: .string)
    }

    func openHomepage(_ station: Station) {
        let homepage = fullStation(station).homepage
        guard let url = URL(string: homepage), url.scheme?.hasPrefix("http") == true else { return }
        NSWorkspace.shared.open(url)
    }

    func shutdown() {
        scheduler.stop()
        recordingManager.stopAll()
        player.shutdown()
    }
}

enum AppSheet: Identifiable {
    case settings
    case recordings
    case history
    case about
    case addStation
    case editStation(Station)
    case stationDetails(Station)
    case schedule(Station)

    var id: String {
        switch self {
        case .settings: return "settings"
        case .recordings: return "recordings"
        case .history: return "history"
        case .about: return "about"
        case .addStation: return "addStation"
        case .editStation(let station): return "edit-\(station.id)"
        case .stationDetails(let station): return "details-\(station.id)"
        case .schedule(let station): return "schedule-\(station.id)"
        }
    }
}
