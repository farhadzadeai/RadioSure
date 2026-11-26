import RadioClassicCore
import SwiftUI

struct SettingsView: View {
    @EnvironmentObject private var model: MainViewModel
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                Text(model.localization.t("Opt.Title"))
                    .font(.system(size: 14, weight: .semibold))
                Spacer()
            }
            .padding(.horizontal, 14)
            .padding(.vertical, 10)

            TabView {
                generalTab.tabItem { Text(model.localization.t("Opt.General")) }
                playbackTab.tabItem { Text(model.localization.t("Opt.Playback")) }
                recordingTab.tabItem { Text(model.localization.t("Opt.Recording")) }
                appearanceTab.tabItem { Text(model.localization.t("Opt.Appearance")) }
                networkTab.tabItem { Text(model.localization.t("Opt.Network")) }
                equalizerTab.tabItem { Text(model.localization.t("Eq.Title")) }
            }
            .padding(.horizontal, 12)

            HStack {
                Button(model.localization.t("Opt.RestoreDefaults")) { restoreDefaults() }
                    .buttonStyle(.borderless)
                Spacer()
                Button(model.localization.t("Close")) {
                    model.applyPreferences()
                    dismiss()
                }
                .keyboardShortcut(.defaultAction)
            }
            .padding(12)
        }
        .frame(width: 560, height: 480)
        .background(model.themeService.color("WindowBack"))
    }

    private func binding<T>(_ keyPath: WritableKeyPath<AppSettings, T>) -> Binding<T> {
        Binding(
            get: { model.settingsStore.current[keyPath: keyPath] },
            set: { model.settingsStore.current[keyPath: keyPath] = $0 }
        )
    }

    private var generalTab: some View {
        Form {
            Section {
                Toggle(model.localization.t("Gen.StartWithWindows"), isOn: binding(\.startAtLogin))
                Toggle(model.localization.t("Gen.StartMinimized"), isOn: binding(\.startMinimized))
                Toggle(model.localization.t("Gen.MinimizeToTray"), isOn: binding(\.minimizeToMenuBar))
                Toggle(model.localization.t("Gen.CloseToTray"), isOn: binding(\.closeToMenuBar))
                Toggle(model.localization.t("Gen.Notifications"), isOn: binding(\.showNotifications))
            }
            Section {
                Toggle(model.localization.t("Gen.RememberStation"), isOn: binding(\.rememberLastStation))
                Toggle(model.localization.t("Gen.ResumeOnStart"), isOn: binding(\.resumeOnStart))
                HStack {
                    Text(model.localization.t("Gen.DefaultVolume"))
                    Slider(value: intDoubleBinding(\.volume), in: 0...100)
                    Text("\(model.settingsStore.current.volume)%")
                        .frame(width: 42, alignment: .trailing)
                }
            }
            Section {
                Picker(model.localization.t("Gen.Language"), selection: binding(\.language)) {
                    ForEach(LocalizationService.languages, id: \.code) { language in
                        Text(language.name).tag(language.code)
                    }
                }
                .onChange(of: model.settingsStore.current.language) { _ in model.applyPreferences() }
                Text(model.localization.t("Opt.RestartHint"))
                    .font(.system(size: 10))
                    .foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
    }

    private var playbackTab: some View {
        Form {
            Section {
                HStack {
                    Text(model.localization.t("Pb.Buffer"))
                    Slider(value: intDoubleBinding(\.bufferMilliseconds), in: 1000...15000, step: 500)
                    Text("\(model.settingsStore.current.bufferMilliseconds) ms")
                        .frame(width: 70, alignment: .trailing)
                }
                Toggle(model.localization.t("Pb.AutoReconnect"), isOn: binding(\.autoReconnect))
                Stepper(value: binding(\.retryAttempts), in: 0...10) {
                    HStack {
                        Text(model.localization.t("Pb.RetryAttempts"))
                        Spacer()
                        Text("\(model.settingsStore.current.retryAttempts)")
                    }
                }
            }
            Section {
                Toggle(model.localization.t("Pb.Hotkeys"), isOn: binding(\.globalHotkeys))
                Toggle(model.localization.t("Pb.MediaKeys"), isOn: binding(\.mediaKeys))
                Picker(model.localization.t("Pb.Output"), selection: .constant("default")) {
                    Text(model.localization.t("Pb.OutputDefault")).tag("default")
                }
                .disabled(true)
            }
        }
        .formStyle(.grouped)
    }

    private var recordingTab: some View {
        Form {
            Section {
                HStack {
                    Text(model.localization.t("Rc.Folder"))
                    TextField("", text: binding(\.recordingDirectory))
                        .textFieldStyle(.roundedBorder)
                    Button(model.localization.t("Browse")) { chooseRecordingDirectory() }
                    Button(model.localization.t("Rc.OpenFolder")) { openRecordingDirectory() }
                }
                Picker(model.localization.t("Rc.Format"), selection: binding(\.recordingFormat)) {
                    Text(model.localization.t("Rc.FormatOriginal")).tag(RecordingFormat.original)
                }
                Toggle(model.localization.t("Rc.Split"), isOn: binding(\.splitTracks))
                Toggle(model.localization.t("Rc.StationFolders"), isOn: binding(\.stationSubfolders))
                Stepper(value: binding(\.maxConcurrentRecordings), in: 1...10) {
                    HStack {
                        Text(model.localization.t("Rc.MaxConcurrent"))
                        Spacer()
                        Text("\(model.settingsStore.current.maxConcurrentRecordings)")
                    }
                }
                Toggle(model.localization.t("Rc.NotifyScheduled"), isOn: binding(\.notifyScheduledRecordings))
            }
            Section {
                TextField(model.localization.t("Rc.TrackTemplate"), text: binding(\.trackFileTemplate))
                    .textFieldStyle(.roundedBorder)
                TextField(model.localization.t("Rc.ContinuousTemplate"), text: binding(\.continuousFileTemplate))
                    .textFieldStyle(.roundedBorder)
                Text(model.localization.t("Rc.TemplateHint"))
                    .font(.system(size: 10))
                    .foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
    }

    private var appearanceTab: some View {
        Form {
            Section {
                Picker(model.localization.t("Ap.Theme"), selection: binding(\.theme)) {
                    ForEach(model.themeService.names, id: \.self) { theme in
                        Text(theme.replacingOccurrences(of: "Classic", with: " Classic")).tag(theme)
                    }
                }
                .onChange(of: model.settingsStore.current.theme) { _ in model.applyPreferences() }
                Toggle(model.localization.t("Ap.Spectrum"), isOn: binding(\.showSpectrum))
                Toggle(model.localization.t("Ap.ShowFilters"), isOn: binding(\.showFilters))
                    .onChange(of: model.settingsStore.current.showFilters) { value in model.setFiltersVisible(value) }
                Toggle(model.localization.t("Ap.CompactOnTop"), isOn: binding(\.compactAlwaysOnTop))
            }
            Section {
                HStack {
                    Text(model.localization.t("Ap.Accent"))
                    TextField("#2F6DB3", text: accentBinding)
                        .textFieldStyle(.roundedBorder)
                        .frame(width: 100)
                    Circle()
                        .fill(model.themeService.color("AccentColor"))
                        .frame(width: 18, height: 18)
                    Button(model.localization.t("Ap.AccentDefault")) {
                        model.settingsStore.current.accentColor = nil
                        model.applyPreferences()
                    }
                }
            }
        }
        .formStyle(.grouped)
    }

    private var networkTab: some View {
        Form {
            Section {
                Stepper(value: binding(\.timeoutSeconds), in: 3...60) {
                    HStack {
                        Text(model.localization.t("Net.Timeout"))
                        Spacer()
                        Text("\(model.settingsStore.current.timeoutSeconds)s")
                    }
                }
                Stepper(value: binding(\.databaseRefreshDays), in: 1...90) {
                    HStack {
                        Text(model.localization.t("Net.Refresh"))
                        Spacer()
                        Text("\(model.settingsStore.current.databaseRefreshDays)d")
                    }
                }
                Toggle(model.localization.t("Net.ReportClicks"), isOn: binding(\.reportClicks))
            }
            Section {
                Picker(model.localization.t("Net.Proxy"), selection: binding(\.proxyMode)) {
                    Text(model.localization.t("Net.ProxySystem")).tag(ProxyMode.system)
                    Text(model.localization.t("Net.ProxyNone")).tag(ProxyMode.none)
                    Text(model.localization.t("Net.ProxyManual")).tag(ProxyMode.manual)
                }
                HStack {
                    Text(model.localization.t("Net.ProxyHost"))
                    TextField("", text: binding(\.proxyHost))
                        .textFieldStyle(.roundedBorder)
                        .disabled(model.settingsStore.current.proxyMode != .manual)
                    Text(model.localization.t("Net.ProxyPort"))
                    TextField("", value: binding(\.proxyPort), format: .number)
                        .textFieldStyle(.roundedBorder)
                        .frame(width: 70)
                        .disabled(model.settingsStore.current.proxyMode != .manual)
                }
            }
        }
        .formStyle(.grouped)
    }

    private var equalizerTab: some View {
        Form {
            Section {
                Toggle(model.localization.t("Eq.Enabled"), isOn: eqBinding(\.enabled))
                Picker(model.localization.t("Eq.Preset"), selection: eqBinding(\.preset)) {
                    ForEach(equalizerPresetNames, id: \.self) { name in Text(name).tag(name) }
                }
                .onChange(of: model.settingsStore.current.equalizer.preset) { preset in applyEqualizerPreset(preset) }
                HStack {
                    Button(model.localization.t("Eq.Reset")) { resetEqualizer() }
                    Button(model.localization.t("Eq.SavePreset")) { savePreset() }
                }
            }
            Section {
                sliderRow(model.localization.t("Eq.Preamp"), value: eqBinding(\.preamp), range: -12...12)
                sliderRow(model.localization.t("Eq.Bass"), value: eqBinding(\.bass), range: -12...12)
                sliderRow(model.localization.t("Eq.Treble"), value: eqBinding(\.treble), range: -12...12)
                sliderRow(model.localization.t("Eq.Balance"), value: eqBinding(\.balance), range: -1...1)
            }
            Section {
                ForEach(0..<10, id: \.self) { index in
                    sliderRow(bandName(index), value: bandBinding(index), range: -12...12)
                }
            }
        }
        .formStyle(.grouped)
    }

    private func eqBinding<T>(_ keyPath: WritableKeyPath<EqSettings, T>) -> Binding<T> {
        Binding(
            get: { model.settingsStore.current.equalizer[keyPath: keyPath] },
            set: { model.settingsStore.current.equalizer[keyPath: keyPath] = $0 }
        )
    }

    private func bandBinding(_ index: Int) -> Binding<Double> {
        Binding(
            get: { model.settingsStore.current.equalizer.bands.indices.contains(index) ? model.settingsStore.current.equalizer.bands[index] : 0 },
            set: { value in
                if model.settingsStore.current.equalizer.bands.indices.contains(index) {
                    model.settingsStore.current.equalizer.bands[index] = value
                }
            }
        )
    }

    private func sliderRow(_ label: String, value: Binding<Double>, range: ClosedRange<Double>) -> some View {
        HStack {
            Text(label)
                .frame(width: 82, alignment: .leading)
            Slider(value: value, in: range)
            Text(String(format: "%.1f", value.wrappedValue))
                .frame(width: 42, alignment: .trailing)
                .font(.system(size: 10, design: .monospaced))
        }
    }

    private var equalizerPresetNames: [String] {
        EqPreset.builtIn.map(\.name) + model.settingsStore.current.customPresets.map(\.name)
    }

    private func applyEqualizerPreset(_ name: String) {
        guard let preset = EqPreset.builtIn.first(where: { $0.name == name })
            ?? model.settingsStore.current.customPresets.first(where: { $0.name == name }) else { return }
        model.settingsStore.current.equalizer.bands = preset.bands
        model.settingsStore.current.equalizer.bass = preset.bass
        model.settingsStore.current.equalizer.treble = preset.treble
        model.settingsStore.current.equalizer.preamp = preset.preamp
    }

    private func bandName(_ index: Int) -> String {
        ["31", "62", "125", "250", "500", "1k", "2k", "4k", "8k", "16k"][index] + " Hz"
    }

    private func intDoubleBinding(_ keyPath: WritableKeyPath<AppSettings, Int>) -> Binding<Double> {
        Binding(
            get: { Double(model.settingsStore.current[keyPath: keyPath]) },
            set: { model.settingsStore.current[keyPath: keyPath] = Int($0.rounded()) }
        )
    }

    private var accentBinding: Binding<String> {
        Binding(
            get: { model.settingsStore.current.accentColor ?? "" },
            set: { model.settingsStore.current.accentColor = $0.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? nil : $0 }
        )
    }

    private func chooseRecordingDirectory() {
        let panel = NSOpenPanel()
        panel.canChooseFiles = false
        panel.canChooseDirectories = true
        panel.allowsMultipleSelection = false
        if panel.runModal() == .OK, let url = panel.url {
            model.settingsStore.current.recordingDirectory = url.path
        }
    }

    private func openRecordingDirectory() {
        let url = URL(fileURLWithPath: model.settingsStore.current.recordingDirectory, isDirectory: true)
        try? FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        NSWorkspace.shared.open(url)
    }

    private func resetEqualizer() {
        model.settingsStore.current.equalizer = EqSettings()
        model.settingsStore.save()
    }

    private func savePreset() {
        let settings = model.settingsStore.current.equalizer
        var preset = EqPreset()
        preset.name = "Custom \(model.settingsStore.current.customPresets.count + 1)"
        preset.bands = settings.bands
        preset.bass = settings.bass
        preset.treble = settings.treble
        preset.preamp = settings.preamp
        model.settingsStore.current.customPresets.append(preset)
        model.settingsStore.current.equalizer.preset = preset.name
        model.settingsStore.save()
    }

    private func restoreDefaults() {
        model.settingsStore.current = AppSettings()
        model.applyPreferences()
    }
}
