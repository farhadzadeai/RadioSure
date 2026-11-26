import Combine
import RadioClassicCore
import SwiftUI

struct ContentView: View {
    @StateObject private var model = MainViewModel()
    private let timer = Timer.publish(every: 0.25, on: .main, in: .common).autoconnect()

    var body: some View {
        content
            .background(model.themeService.color("WindowBack"))
            .background(WindowAccessor { window in
                window.title = model.windowTitle
                window.titleVisibility = .hidden
                window.titlebarAppearsTransparent = true
                window.isMovableByWindowBackground = true
                window.styleMask.insert(.fullSizeContentView)
                window.delegate = WindowCloseDelegate.shared
                MainWindowRegistry.shared.window = window
                window.level = model.compactMode && model.settingsStore.current.compactAlwaysOnTop ? .floating : .normal
            })
            .frame(
                minWidth: model.compactMode ? 340 : 500,
                idealWidth: CGFloat(model.settingsStore.current.windowWidth),
                minHeight: model.compactMode ? 142 : 420,
                idealHeight: CGFloat(model.settingsStore.current.windowHeight)
            )
            .environmentObject(model)
            .environment(\.layoutDirection, model.localization.isRTL ? .rightToLeft : .leftToRight)
            .onReceive(timer) { _ in
                model.tick()
                if let window = MainWindowRegistry.shared.window, window.title != model.windowTitle {
                    window.title = model.windowTitle
                }
                MenuBarController.shared.update()
            }
            .onAppear {
                AppActionBridge.shared.model = model
                MenuBarController.shared.configure(model: model)
            }
            .onReceive(model.settingsStore.objectWillChange) { _ in model.objectWillChange.send() }
            .onReceive(model.recordingManager.objectWillChange) { _ in model.refreshRecordingState() }
            .sheet(item: $model.activeSheet) { sheet in
                SheetHost(sheet: sheet)
                    .environmentObject(model)
                    .environment(\.layoutDirection, model.localization.isRTL ? .rightToLeft : .leftToRight)
            }
    }

    @ViewBuilder
    private var content: some View {
        if model.compactMode {
            compactContent
        } else {
            regularContent
        }
    }

    private var regularContent: some View {
        VStack(spacing: 4) {
            searchRow
            if model.filtersVisible { filterRow }
            StationListView()
                .frame(maxWidth: .infinity, maxHeight: .infinity)
            displayPanel
            transportBar
            statusBar
        }
        .padding(6)
    }

    private var compactContent: some View {
        VStack(spacing: 4) {
            compactTopLine
            displayPanel
            transportBar
        }
        .padding(6)
    }

    private var searchRow: some View {
        HStack(spacing: 5) {
            Image(systemName: "magnifyingglass")
                .font(.system(size: 11))
                .foregroundStyle(model.themeService.color("TextDim"))
            searchField
            viewMenu
            Button {
                model.setFiltersVisible(!model.filtersVisible)
            } label: {
                Image(systemName: model.filtersVisible ? "line.3.horizontal.decrease.circle.fill" : "line.3.horizontal.decrease.circle")
                    .font(.system(size: 15))
                    .foregroundStyle(model.filtersVisible ? model.themeService.color("AccentTextBrush") : model.themeService.color("TextDim"))
            }
            .buttonStyle(.plain)
            .help(model.localization.t("Tip.Filters"))
            Button { model.refreshDatabase() } label: {
                Image(systemName: "arrow.clockwise")
                    .font(.system(size: 12, weight: .semibold))
                    .foregroundStyle(model.databaseBusy ? model.themeService.color("AccentTextBrush") : model.themeService.color("TextDim"))
            }
            .buttonStyle(.plain)
            .disabled(model.databaseBusy)
            .help(model.localization.t("Tip.RefreshDb"))
        }
    }

    private var searchField: some View {
        HStack(spacing: 2) {
            TextField(model.localization.t("Search"), text: $model.searchText)
                .textFieldStyle(.plain)
                .disableAutocorrection(true)
                .font(.system(size: 11))
                .foregroundStyle(model.themeService.color("Text"))
            if !model.settingsStore.current.searchHistory.isEmpty {
                Menu {
                    ForEach(model.settingsStore.current.searchHistory, id: \.self) { term in
                        Button(term) { model.searchText = term }
                    }
                } label: {
                    Image(systemName: "clock.arrow.circlepath")
                        .font(.system(size: 9))
                        .foregroundStyle(model.themeService.color("TextDim"))
                }
                .menuStyle(.borderlessButton)
                .frame(width: 16)
            }
        }
        .padding(.horizontal, 6)
        .frame(height: 22)
        .background(RoundedRectangle(cornerRadius: 3).fill(model.themeService.color("InputBack")))
        .overlay(RoundedRectangle(cornerRadius: 3).stroke(model.themeService.color("BorderBrush"), lineWidth: 1))
    }

    private var viewMenu: some View {
        Menu {
            ForEach(StationView.allCases, id: \.self) { view in
                Button {
                    model.view = view
                } label: {
                    HStack {
                        Text(viewTitle(view))
                        if model.view == view { Image(systemName: "checkmark") }
                    }
                }
            }
        } label: {
            HStack(spacing: 3) {
                Text(viewTitle(model.view))
                    .font(.system(size: 10))
                    .lineLimit(1)
                Image(systemName: "chevron.down")
                    .font(.system(size: 7, weight: .bold))
            }
            .foregroundStyle(model.themeService.color("Text"))
            .padding(.horizontal, 7)
            .frame(height: 22)
            .background(RoundedRectangle(cornerRadius: 3).fill(model.themeService.color("PanelBack")))
            .overlay(RoundedRectangle(cornerRadius: 3).stroke(model.themeService.color("BorderBrush"), lineWidth: 1))
        }
        .menuStyle(.borderlessButton)
        .help(model.localization.t("Tip.View"))
    }

    private var filterRow: some View {
        HStack(spacing: 5) {
            ComboBox(placeholder: model.localization.t("Col.Country"), text: $model.filterCountry, suggestions: model.countries)
                .frame(width: 104)
            ComboBox(placeholder: model.localization.t("Col.Genre"), text: $model.filterGenre, suggestions: genreSuggestions)
                .frame(width: 94)
            ComboBox(placeholder: model.localization.t("Col.Language"), text: $model.filterLanguage, suggestions: model.languages)
                .frame(width: 94)
            ComboBox(placeholder: model.localization.t("Col.Codec"), text: $model.filterCodec, suggestions: model.codecs)
                .frame(width: 76)
            bitrateMenu
            if model.view == .favorites {
                ComboBox(placeholder: model.localization.t("Fav.ChooseFolder"), text: $model.favoriteFolder, suggestions: model.favoriteFolders)
                    .frame(width: 100)
            }
            Spacer(minLength: 0)
        }
    }

    private var genreSuggestions: [String] {
        ["Rock", "Pop", "Jazz", "Classical", "News", "Talk", "Dance", "Electronic", "Folk", "Persian"]
    }

    private var bitrateMenu: some View {
        Menu {
            Button(model.localization.t("All")) { model.filterMinBitrate = 0 }
            ForEach([64, 96, 128, 192, 256, 320], id: \.self) { value in
                Button("\(value)+ kbps") { model.filterMinBitrate = value }
            }
        } label: {
            HStack(spacing: 3) {
                Text(model.filterMinBitrate == 0 ? model.localization.t("Filter.MinBitrate") : "\(model.filterMinBitrate)+ kbps")
                    .font(.system(size: 10))
                    .lineLimit(1)
                Image(systemName: "chevron.down")
                    .font(.system(size: 7, weight: .bold))
            }
            .foregroundStyle(model.themeService.color("Text"))
            .padding(.horizontal, 6)
            .frame(height: 22)
            .background(RoundedRectangle(cornerRadius: 3).fill(model.themeService.color("PanelBack")))
            .overlay(RoundedRectangle(cornerRadius: 3).stroke(model.themeService.color("BorderBrush"), lineWidth: 1))
        }
        .menuStyle(.borderlessButton)
    }

    private var compactTopLine: some View {
        HStack(spacing: 5) {
            Text(model.stationLine.isEmpty ? model.localization.t("AppTitle") : model.stationLine)
                .font(.system(size: 11, weight: .semibold))
                .foregroundStyle(model.themeService.color("Text"))
                .lineLimit(1)
            Spacer()
            Button { model.toggleCompactMode() } label: {
                Image(systemName: "arrow.up.left.and.arrow.down.right")
                    .font(.system(size: 10, weight: .bold))
                    .foregroundStyle(model.themeService.color("TextDim"))
            }
            .buttonStyle(.plain)
            .help(model.localization.t("Tip.Expand"))
        }
    }

    private var displayPanel: some View {
        VStack(spacing: 3) {
            HStack(alignment: .center, spacing: 6) {
                VStack(alignment: .leading, spacing: 1) {
                    Text(model.stationLine.isEmpty ? model.localization.t("AppTitle") : model.stationLine)
                        .font(.system(size: 11, weight: .semibold))
                        .foregroundStyle(model.themeService.color("Glyph"))
                        .lineLimit(1)
                    Text(displayTrackText)
                        .font(.system(size: 10))
                        .foregroundStyle(model.errorText.isEmpty ? model.themeService.color("DisplayPeak") : model.themeService.color("RecBrush"))
                        .lineLimit(1)
                }
                Spacer(minLength: 4)
                Text(model.qualityText)
                    .font(.system(size: 9, design: .monospaced))
                    .foregroundStyle(model.themeService.color("TextDim"))
                    .lineLimit(1)
            }
            .padding(.horizontal, 6)
            .frame(height: 33)
            .background(RoundedRectangle(cornerRadius: 3).fill(model.themeService.color("DisplayBack")))
            .overlay(RoundedRectangle(cornerRadius: 3).stroke(model.themeService.color("BorderBrush"), lineWidth: 1))
            if model.settingsStore.current.showSpectrum {
                SpectrumView()
            }
        }
    }

    private var displayTrackText: String {
        if !model.errorText.isEmpty { return model.errorText }
        if !model.trackLine.isEmpty { return model.trackLine }
        if !model.player.streamName.isEmpty { return model.player.streamName }
        return model.stateText
    }

    private var transportBar: some View {
        HStack(spacing: 5) {
            PlayerButton(systemName: "backward.fill", help: model.localization.t("Tip.Previous")) { model.step(-1) }
            PlayerButton(systemName: playPauseSymbol, help: model.localization.t(playPauseHelpKey)) { model.playPause() }
            PlayerButton(systemName: "stop.fill", help: model.localization.t("Tip.Stop")) { model.stop() }
            PlayerButton(systemName: "forward.fill", help: model.localization.t("Tip.Next")) { model.step(1) }
            PlayerButton(
                systemName: "record.circle",
                help: model.localization.t("Tip.Record"),
                active: model.targetStationRecording,
                tint: model.targetStationRecording || model.isRecordingActive ? model.themeService.color("RecBrush") : model.themeService.color("Text")
            ) { model.toggleRecord() }
            PlayerButton(
                systemName: model.isFavorite ? "star.fill" : "star",
                help: model.localization.t("Tip.Favorites"),
                active: model.isFavorite,
                tint: model.isFavorite ? .yellow : nil
            ) { model.toggleFavorite() }

            HStack(spacing: 4) {
                Button {
                    model.player.muted.toggle()
                } label: {
                    Image(systemName: model.player.muted || model.player.volume == 0 ? "speaker.slash.fill" : "speaker.wave.2.fill")
                        .font(.system(size: 11))
                        .foregroundStyle(model.themeService.color("TextDim"))
                }
                .buttonStyle(.plain)
                .help(model.localization.t("Tip.Mute"))
                Slider(value: volumeBinding, in: 0...100)
                    .frame(width: 82)
                    .help(model.localization.t("Tip.Volume"))
            }

            Spacer(minLength: 2)
            Button { model.activeSheet = .settings } label: {
                Image(systemName: "slider.horizontal.3")
                    .font(.system(size: 12))
                    .foregroundStyle(model.themeService.color("TextDim"))
            }
            .buttonStyle(.plain)
            .help(model.localization.t("Tip.Equalizer"))
            menuButton
            Button { model.toggleCompactMode() } label: {
                Image(systemName: model.compactMode ? "arrow.up.left.and.arrow.down.right" : "arrow.down.right.and.arrow.up.left")
                    .font(.system(size: 10, weight: .bold))
                    .foregroundStyle(model.themeService.color("TextDim"))
            }
            .buttonStyle(.plain)
            .help(model.localization.t(model.compactMode ? "Tip.Expand" : "Tip.Compact"))
        }
        .frame(height: 31)
    }

    private var volumeBinding: Binding<Double> {
        Binding(
            get: { Double(model.player.volume) },
            set: { model.player.volume = Int($0.rounded()) }
        )
    }

    private var playPauseSymbol: String {
        switch model.player.state {
        case .playing: return "pause.fill"
        case .paused: return "play.fill"
        case .connecting, .reconnecting: return "stop.circle.fill"
        case .stopped, .error: return "play.fill"
        }
    }

    private var playPauseHelpKey: String {
        model.player.state == .playing ? "Menu.Pause" : "Tip.Play"
    }

    private var menuButton: some View {
        Menu {
            Button(model.localization.t("Menu.AddStation")) { model.activeSheet = .addStation }
            Divider()
            Button(model.localization.t("Menu.Favorites")) { model.view = .favorites }
            Button(model.localization.t("Menu.History")) { model.activeSheet = .history }
            Button(model.localization.t("Menu.Recordings")) { model.activeSheet = .recordings }
            Divider()
            Button(model.localization.t("Menu.ImportPlaylist")) { model.importStations() }
            Button(model.localization.t("Menu.ExportFavorites")) { model.exportFavorites() }
            Divider()
            Menu(model.localization.t("Menu.Language")) {
                ForEach(LocalizationService.languages, id: \.code) { language in
                    Button {
                        model.settingsStore.current.language = language.code
                        model.applyPreferences()
                    } label: {
                        HStack {
                            Text(language.name)
                            if model.settingsStore.current.language == language.code { Image(systemName: "checkmark") }
                        }
                    }
                }
            }
            Button(model.localization.t("Menu.Options")) { model.activeSheet = .settings }
            Button(model.localization.t("Menu.About")) { model.activeSheet = .about }
        } label: {
            Image(systemName: "line.3.horizontal")
                .font(.system(size: 12, weight: .semibold))
                .foregroundStyle(model.themeService.color("TextDim"))
                .frame(width: 26, height: 24)
        }
        .menuStyle(.borderlessButton)
        .help(model.localization.t("Tip.Menu"))
    }

    private var statusBar: some View {
        HStack(spacing: 8) {
            Text(model.stationCountText)
                .lineLimit(1)
            Rectangle()
                .fill(model.themeService.color("BorderBrush"))
                .frame(width: 1, height: 11)
            HStack(spacing: 3) {
                Rectangle()
                    .fill(model.themeService.color("InputBack"))
                    .frame(width: 52, height: 5)
                    .overlay(alignment: .leading) {
                        Rectangle()
                            .fill(model.themeService.color("BufferFill"))
                            .frame(width: 52 * CGFloat(model.player.bufferPercent) / 100, height: 5)
                    }
                    .overlay(Rectangle().stroke(model.themeService.color("BorderBrush"), lineWidth: 0.5))
                Text(model.bufferText)
            }
            Text("\(model.stateText) \(model.elapsedText)")
                .lineLimit(1)
            if !model.recordingText.isEmpty {
                Text(model.recordingText)
                    .foregroundStyle(model.themeService.color("RecBrush"))
                    .font(.system(size: 10, weight: .bold))
            }
            Spacer(minLength: 4)
            Text(model.databaseStatus)
                .lineLimit(1)
                .truncationMode(.tail)
            if !model.qualityText.isEmpty {
                Text(model.qualityText)
                    .font(.system(size: 9, weight: .semibold, design: .monospaced))
                    .foregroundStyle(model.themeService.color("Text"))
            }
        }
        .font(.system(size: 9))
        .foregroundStyle(model.themeService.color("TextDim"))
        .padding(.horizontal, 5)
        .frame(height: 20)
        .background(model.themeService.color("StatusBack"))
        .overlay(Rectangle().stroke(model.themeService.color("BorderBrush"), lineWidth: 0.5))
    }

    private func viewTitle(_ view: StationView) -> String {
        switch view {
        case .all: return model.localization.t("View.All")
        case .favorites: return model.localization.t("View.Favorites")
        case .recent: return model.localization.t("View.Recent")
        case .custom: return model.localization.t("View.Custom")
        }
    }
}

private struct SheetHost: View {
    @EnvironmentObject private var model: MainViewModel
    let sheet: AppSheet

    var body: some View {
        switch sheet {
        case .settings:
            SettingsView()
        case .recordings:
            RecordingsView()
        case .history:
            HistoryView()
        case .about:
            AboutView()
        case .addStation:
            StationEditorView(station: Station(isCustom: true), isNew: true)
        case .editStation(let station):
            StationEditorView(station: station, isNew: false)
        case .stationDetails(let station):
            StationDetailsView(station: station)
        case .schedule(let station):
            ScheduleView(station: station)
        }
    }
}
