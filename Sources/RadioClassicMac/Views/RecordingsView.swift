import RadioClassicCore
import SwiftUI

struct RecordingsView: View {
    @EnvironmentObject private var model: MainViewModel
    @Environment(\.dismiss) private var dismiss
    @State private var entries: [RecordingEntry] = []
    @State private var schedules: [RecordingSchedule] = []
    @State private var search = ""
    @State private var selectedEntryID: Int64?
    @State private var entryPendingDeletion: RecordingEntry?
    @State private var schedulePendingDeletion: RecordingSchedule?
    @State private var tab = 0

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                Text(model.localization.t("Rec.Title"))
                    .font(.system(size: 14, weight: .semibold))
                Spacer()
                Picker("", selection: $tab) {
                    Text(model.localization.t("Rec.History")).tag(0)
                    Text(model.localization.t("Rec.Schedules")).tag(1)
                }
                .pickerStyle(.segmented)
                .frame(width: 190)
            }
            .padding(.horizontal, 14)
            .padding(.vertical, 10)

            if tab == 0 { recordingsTab } else { schedulesTab }

            HStack {
                Button(model.localization.t("Rec.OpenFolder")) { openRecordingFolder() }
                Button(model.localization.t("Rec.ExportList")) { exportList() }
                Spacer()
                Button(model.localization.t("Close")) { dismiss() }
                    .keyboardShortcut(.defaultAction)
            }
            .padding(12)
        }
        .frame(width: 720, height: 500)
        .onAppear(perform: reload)
        .onReceive(Timer.publish(every: 2, on: .main, in: .common).autoconnect()) { _ in reload() }
        .alert(item: $entryPendingDeletion) { entry in
            Alert(
                title: Text(model.localization.t("Rec.DeleteConfirm")),
                primaryButton: .destructive(Text(model.localization.t("Yes"))) { delete(entry) },
                secondaryButton: .cancel(Text(model.localization.t("No")))
            )
        }
        .alert(item: $schedulePendingDeletion) { schedule in
            Alert(
                title: Text(model.localization.t("Sch.DeleteConfirm")),
                primaryButton: .destructive(Text(model.localization.t("Yes"))) {
                    try? model.recordingRepository.deleteSchedule(schedule.id)
                    reload()
                },
                secondaryButton: .cancel(Text(model.localization.t("No")))
            )
        }
    }

    private var recordingsTab: some View {
        VStack(spacing: 6) {
            if !model.recordingManager.sessions.isEmpty {
                VStack(spacing: 3) {
                    ForEach(model.recordingManager.sessions, id: \.id) { session in
                        HStack {
                            Circle()
                                .fill(Color.red)
                                .frame(width: 7, height: 7)
                            Text(session.stationName)
                                .font(.system(size: 10, weight: .semibold))
                                .lineLimit(1)
                            Text(session.currentTrack)
                                .font(.system(size: 10))
                                .foregroundStyle(.secondary)
                                .lineLimit(1)
                            Spacer()
                            Text(Formatting.duration(Date().timeIntervalSince(session.startedAt)))
                                .font(.system(size: 10, design: .monospaced))
                            Button(model.localization.t("Rec.Stop")) { session.stop(); reload() }
                                .controlSize(.small)
                        }
                        .padding(.horizontal, 10)
                    }
                }
                .padding(.vertical, 5)
                .background(RoundedRectangle(cornerRadius: 4).fill(Color.red.opacity(0.08)))
                .padding(.horizontal, 12)
            }

            HStack {
                Image(systemName: "magnifyingglass")
                    .font(.system(size: 10))
                    .foregroundStyle(.secondary)
                TextField(model.localization.t("Rec.SearchPrompt"), text: $search)
                    .textFieldStyle(.roundedBorder)
                    .onSubmit(reload)
                    .onChange(of: search) { _ in reload() }
            }
            .padding(.horizontal, 12)

            List(selection: $selectedEntryID) {
                ForEach(entries) { entry in
                    VStack(alignment: .leading, spacing: 3) {
                        HStack {
                            Text(entry.title.isEmpty ? entry.fileName : entry.title)
                                .font(.system(size: 11, weight: .medium))
                                .lineLimit(1)
                            Spacer()
                            statusBadge(entry.status)
                        }
                        HStack(spacing: 8) {
                            Text(entry.stationName)
                            Text(entry.started.formatted(date: .abbreviated, time: .shortened))
                            Text(entry.durationDisplay)
                            Text(entry.sizeDisplay)
                            Text(entry.format)
                        }
                        .font(.system(size: 9))
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                        Text(entry.file)
                            .font(.system(size: 8, design: .monospaced))
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                            .truncationMode(.middle)
                    }
                    .tag(entry.id)
                    .contextMenu { entryMenu(entry) }
                }
            }
            .listStyle(.plain)
            .overlay {
                if entries.isEmpty {
                    Text(model.localization.t("Rec.NoneYet"))
                        .foregroundStyle(.secondary)
                }
            }
        }
    }

    private var schedulesTab: some View {
        List {
            ForEach(schedules) { schedule in
                HStack {
                    VStack(alignment: .leading, spacing: 2) {
                        Text(schedule.stationName)
                            .font(.system(size: 11, weight: .medium))
                        Text(scheduleSummary(schedule))
                            .font(.system(size: 9))
                            .foregroundStyle(.secondary)
                    }
                    Spacer()
                    Toggle("", isOn: scheduleEnabledBinding(schedule))
                        .labelsHidden()
                    Button(model.localization.t("Sch.Delete"), role: .destructive) {
                        schedulePendingDeletion = schedule
                    }
                    .controlSize(.small)
                }
            }
        }
        .listStyle(.plain)
        .overlay {
            if schedules.isEmpty {
                Text(model.localization.t("Rec.NoneScheduled"))
                    .foregroundStyle(.secondary)
            }
        }
    }

    @ViewBuilder
    private func entryMenu(_ entry: RecordingEntry) -> some View {
        Button(model.localization.t("Rec.PlayFile")) { openFile(entry.file) }
        Button(model.localization.t("Rec.OpenFolder")) { revealFile(entry.file) }
        Button(model.localization.t("Rec.Rename")) { rename(entry) }
        Divider()
        Button(model.localization.t("Rec.DeleteFile"), role: .destructive) { entryPendingDeletion = entry }
    }

    private func statusBadge(_ status: String) -> some View {
        let key = switch status {
        case RecordingStatus.completed: "Rec.StatusCompleted"
        case RecordingStatus.interrupted: "Rec.StatusInterrupted"
        case RecordingStatus.failed: "Rec.StatusFailed"
        default: "Rec.StatusRecording"
        }
        return Text(model.localization.t(key))
            .font(.system(size: 8, weight: .semibold))
            .padding(.horizontal, 5)
            .padding(.vertical, 2)
            .background(Capsule().fill(status == RecordingStatus.failed ? Color.red.opacity(0.16) : Color.green.opacity(0.14)))
    }

    private func scheduleEnabledBinding(_ schedule: RecordingSchedule) -> Binding<Bool> {
        Binding(
            get: { schedule.enabled },
            set: { enabled in
                var copy = schedule
                copy.enabled = enabled
                try? model.recordingRepository.saveSchedule(&copy)
                reload()
            }
        )
    }

    private func reload() {
        entries = (try? model.recordingRepository.list(search: search.trimmingCharacters(in: .whitespacesAndNewlines))) ?? []
        schedules = (try? model.recordingRepository.schedules()) ?? []
    }

    private func scheduleSummary(_ schedule: RecordingSchedule) -> String {
        let start = schedule.start.formatted(date: .abbreviated, time: .shortened)
        let repeatText = switch schedule.repeatMode {
        case .once: model.localization.t("Sch.Once")
        case .daily: model.localization.t("Sch.Daily")
        case .weekly: model.localization.t("Sch.Weekly")
        }
        let next = schedule.nextStart(after: Date())?.formatted(date: .abbreviated, time: .shortened) ?? "—"
        return "\(start) · \(schedule.durationMinutes)m · \(repeatText) · \(model.localization.t("Sch.NextRun")) \(next)"
    }

    private func openRecordingFolder() {
        let url = URL(fileURLWithPath: model.settingsStore.current.recordingDirectory, isDirectory: true)
        try? FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        NSWorkspace.shared.open(url)
    }

    private func openFile(_ path: String) {
        let url = URL(fileURLWithPath: path)
        if FileManager.default.fileExists(atPath: path) { NSWorkspace.shared.open(url) }
    }

    private func revealFile(_ path: String) {
        let url = URL(fileURLWithPath: path)
        if FileManager.default.fileExists(atPath: path) {
            NSWorkspace.shared.activateFileViewerSelecting([url])
        } else {
            openRecordingFolder()
        }
    }

    private func rename(_ entry: RecordingEntry) {
        let panel = NSSavePanel()
        panel.directoryURL = URL(fileURLWithPath: entry.file).deletingLastPathComponent()
        panel.nameFieldStringValue = URL(fileURLWithPath: entry.file).lastPathComponent
        guard panel.runModal() == .OK, let destination = panel.url else { return }
        do {
            try FileManager.default.moveItem(at: URL(fileURLWithPath: entry.file), to: destination)
            var copy = entry
            copy.file = destination.path
            try model.recordingRepository.update(copy)
            reload()
        } catch {
            model.databaseStatus = error.localizedDescription
        }
    }

    private func delete(_ entry: RecordingEntry) {
        try? FileManager.default.removeItem(at: URL(fileURLWithPath: entry.file))
        try? model.recordingRepository.delete(entry.id)
        reload()
    }

    private func exportList() {
        let panel = NSSavePanel()
        panel.nameFieldStringValue = "RadioClassic-Recordings.txt"
        guard panel.runModal() == .OK, let url = panel.url else { return }
        let text = entries.map {
            "\($0.started.formatted(date: .numeric, time: .shortened))\t\($0.stationName)\t\($0.title)\t\($0.file)\t\($0.sizeDisplay)"
        }.joined(separator: "\n")
        try? text.write(to: url, atomically: true, encoding: .utf8)
    }
}
