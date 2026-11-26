import AppKit
import RadioClassicCore
import SwiftUI

private enum StationColumn {
    case name, country, genre, language, codec, bitrate

    var sort: StationSort {
        switch self {
        case .name: return .name
        case .country: return .country
        case .genre: return .genre
        case .language: return .language
        case .codec: return .codec
        case .bitrate: return .bitrate
        }
    }
}

struct StationListView: View {
    @EnvironmentObject private var model: MainViewModel
    @State private var stationPendingDelete: Station?

    private let fixedWidth: CGFloat = 78 + 88 + 80 + 54 + 46
    private let spacing: CGFloat = 4

    var body: some View {
        GeometryReader { geometry in
            let titleWidth = max(135, geometry.size.width - fixedWidth - spacing * 5)
            VStack(spacing: 0) {
                header(titleWidth: titleWidth)
                Divider().overlay(model.themeService.color("BorderBrush"))
                ZStack {
                    ScrollView {
                        LazyVStack(spacing: 0) {
                            ForEach(model.stations) { station in
                                row(station, titleWidth: titleWidth)
                            }
                        }
                    }
                    if model.stations.isEmpty {
                        Text(model.databaseBusy ? model.localization.t("UpdatingDb") : model.localization.t("NoStationsYet"))
                            .font(.system(size: 11))
                            .foregroundStyle(model.themeService.color("TextDim"))
                            .multilineTextAlignment(.center)
                            .padding(24)
                    }
                }
            }
            .background(model.themeService.color("ListBack"))
            .overlay(
                Rectangle()
                    .stroke(model.themeService.color("BorderBrush"), lineWidth: 1)
            )
        }
        .alert(item: $stationPendingDelete) { station in
            Alert(
                title: Text(model.localization.t("St.DeleteConfirm", station.name)),
                primaryButton: .destructive(Text(model.localization.t("Yes"))) {
                    try? model.deleteStation(station)
                },
                secondaryButton: .cancel(Text(model.localization.t("No")))
            )
        }
    }

    @ViewBuilder
    private func header(titleWidth: CGFloat) -> some View {
        HStack(spacing: spacing) {
            headerButton(.name, title: model.localization.t("Col.Title"), width: titleWidth)
            headerButton(.country, title: model.localization.t("Col.Country"), width: 78)
            headerButton(.genre, title: model.localization.t("Col.Genre"), width: 88)
            headerButton(.language, title: model.localization.t("Col.Language"), width: 80)
            headerButton(.codec, title: model.localization.t("Col.Codec"), width: 54)
            headerButton(.bitrate, title: model.localization.t("Col.Bitrate"), width: 46, sort: .bitrate)
        }
        .padding(.horizontal, 4)
        .frame(height: 23)
        .background(model.themeService.color("HeaderBack"))
    }

    private func headerButton(_ column: StationColumn, title: String, width: CGFloat, sort: StationSort? = nil) -> some View {
        let actualSort: StationSort = sort ?? column.sort
        return Button {
            model.sortBy(actualSort)
        } label: {
            HStack(spacing: 2) {
                Text(title)
                    .font(.system(size: 10, weight: .semibold))
                    .foregroundStyle(model.themeService.color("Text"))
                    .lineLimit(1)
                if model.settingsStore.current.sort == actualSort {
                    Image(systemName: model.settingsStore.current.sortDescending ? "arrow.down" : "arrow.up")
                        .font(.system(size: 7, weight: .bold))
                        .foregroundStyle(model.themeService.color("AccentTextBrush"))
                }
            }
            .frame(width: width, alignment: .leading)
        }
        .buttonStyle(.plain)
    }

    @ViewBuilder
    private func row(_ station: Station, titleWidth: CGFloat) -> some View {
        let selected = model.selectedStationID == station.id
        HStack(spacing: spacing) {
            cell(station.name, width: titleWidth, bold: selected, selected: selected)
            cell(station.country, width: 78, selected: selected)
            cell(station.genreDisplay, width: 88, selected: selected)
            cell(station.languageDisplay, width: 80, selected: selected)
            cell(station.codec, width: 54, selected: selected)
            cell(station.bitrateDisplay, width: 46, selected: selected)
        }
        .padding(.horizontal, 4)
        .frame(height: 22)
        .background(selected ? model.themeService.color("SelectionBack") : Color.clear)
        .contentShape(Rectangle())
        .onTapGesture(count: 2) {
            model.select(station)
            model.play(station)
        }
        .onTapGesture(count: 1) {
            model.select(station)
        }
        .contextMenu {
            Button(model.localization.t("Ctx.Play")) { model.play(station) }
            Button(model.localization.t(targetRecordingKey(station))) {
                model.select(station)
                model.toggleRecord()
            }
            Button(model.localization.t("Ctx.Schedule")) {
                model.select(station)
                model.activeSheet = .schedule(model.fullStation(station))
            }
            Divider()
            if model.fullStation(station).isFavorite || (try? model.stationRepository.isFavorite(station.id)) == true {
                Button(model.localization.t("Ctx.RemoveFromFavorites")) {
                    model.select(station)
                    model.toggleFavorite()
                }
            } else {
                Menu(model.localization.t("Ctx.AddToFavorites")) {
                    Button(model.localization.t("Fav.Default")) { model.setFavorite(station, folder: "") }
                    ForEach(model.favoriteFolders, id: \.self) { folder in
                        Button(folder) { model.setFavorite(station, folder: folder) }
                    }
                    Divider()
                    Button(model.localization.t("Fav.NewFolder")) {
                        if let folder = promptForFavoriteFolder() {
                            model.setFavorite(station, folder: folder)
                        }
                    }
                }
            }
            Button(model.localization.t("Menu.Details")) { model.activeSheet = .stationDetails(model.fullStation(station)) }
            if model.fullStation(station).isCustom {
                Button(model.localization.t("Ctx.EditStation")) { model.activeSheet = .editStation(model.fullStation(station)) }
                Button(model.localization.t("Ctx.DeleteStation"), role: .destructive) {
                    stationPendingDelete = model.fullStation(station)
                }
            }
            Divider()
            Button(model.localization.t("Ctx.CopyUrl")) { model.copyStationURL(station) }
            Button(model.localization.t("Ctx.Homepage")) { model.openHomepage(station) }
        }
    }

    private func targetRecordingKey(_ station: Station) -> String {
        let full = model.fullStation(station)
        return model.recordingManager.isRecordingStation(full.id, url: full.url) ? "Ctx.StopRecording" : "Ctx.Record"
    }

    private func promptForFavoriteFolder() -> String? {
        let alert = NSAlert()
        alert.messageText = model.localization.t("Fav.NewFolder")
        alert.informativeText = model.localization.t("Fav.NewFolderPrompt")
        let input = NSTextField(frame: NSRect(x: 0, y: 0, width: 240, height: 24))
        alert.accessoryView = input
        alert.addButton(withTitle: model.localization.t("OK"))
        alert.addButton(withTitle: model.localization.t("Cancel"))
        guard alert.runModal() == .alertFirstButtonReturn else { return nil }
        let value = input.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        return value.isEmpty ? nil : value
    }

    private func cell(_ value: String, width: CGFloat, bold: Bool = false, selected: Bool = false) -> some View {
        Text(value)
            .font(.system(size: 11, weight: bold ? .semibold : .regular))
            .foregroundStyle(selected ? model.themeService.color("SelectionText") : model.themeService.color("Text"))
            .lineLimit(1)
            .truncationMode(.tail)
            .frame(width: width, alignment: .leading)
    }
}
