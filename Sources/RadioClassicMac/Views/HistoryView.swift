import RadioClassicCore
import SwiftUI

struct HistoryView: View {
    @EnvironmentObject private var model: MainViewModel
    @Environment(\.dismiss) private var dismiss
    @State private var selectedTab = 0
    @State private var stations: [HistoryEntry] = []
    @State private var songs: [HistoryEntry] = []
    @State private var showClearConfirmation = false

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                Text(model.localization.t("Hist.Title"))
                    .font(.system(size: 14, weight: .semibold))
                Spacer()
                Picker("", selection: $selectedTab) {
                    Text(model.localization.t("Hist.Stations")).tag(0)
                    Text(model.localization.t("Hist.Songs")).tag(1)
                }
                .pickerStyle(.segmented)
                .frame(width: 190)
            }
            .padding(.horizontal, 14)
            .padding(.vertical, 10)

            List {
                if selectedTab == 0 {
                    ForEach(stations) { entry in
                        row(title: entry.stationName, subtitle: dateText(entry.playedAt)) {
                            playHistoryEntry(entry)
                        }
                    }
                } else {
                    ForEach(songs) { entry in
                        row(title: entry.title, subtitle: "\(entry.stationName)  ·  \(dateText(entry.playedAt))") {}
                    }
                }
                if (selectedTab == 0 ? stations : songs).isEmpty {
                    Text(model.localization.t("Hist.Empty"))
                        .foregroundStyle(.secondary)
                }
            }
            .listStyle(.plain)

            HStack {
                Button(model.localization.t("Hist.Clear")) { showClearConfirmation = true }
                    .disabled(stations.isEmpty && songs.isEmpty)
                Spacer()
                Button(model.localization.t("Close")) { dismiss() }
                    .keyboardShortcut(.defaultAction)
            }
            .padding(12)
        }
        .frame(width: 560, height: 430)
        .onAppear(perform: reload)
        .confirmationDialog(model.localization.t("Hist.ClearConfirm"), isPresented: $showClearConfirmation) {
            Button(model.localization.t("Yes"), role: .destructive) {
                try? model.stationRepository.clearHistory()
                reload()
            }
            Button(model.localization.t("No"), role: .cancel) {}
        }
    }

    private func row(title: String, subtitle: String, action: @escaping () -> Void) -> some View {
        HStack {
            VStack(alignment: .leading, spacing: 2) {
                Text(title.isEmpty ? "—" : title)
                    .font(.system(size: 11, weight: .medium))
                    .lineLimit(1)
                Text(subtitle)
                    .font(.system(size: 9))
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }
            Spacer()
        }
        .contentShape(Rectangle())
        .onTapGesture(count: 2, perform: action)
    }

    private func reload() {
        stations = (try? model.stationRepository.history()) ?? []
        songs = (try? model.stationRepository.songs()) ?? []
    }

    private func playHistoryEntry(_ entry: HistoryEntry) {
        if let station = try? model.stationRepository.get(entry.stationID) {
            model.play(station)
            dismiss()
        }
    }

    private func dateText(_ date: Date) -> String {
        date.formatted(date: .abbreviated, time: .shortened)
    }
}
