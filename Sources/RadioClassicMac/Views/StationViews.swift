import RadioClassicCore
import SwiftUI

struct StationEditorView: View {
    @EnvironmentObject private var model: MainViewModel
    @Environment(\.dismiss) private var dismiss

    let originalStation: Station
    let isNew: Bool

    @State private var name: String
    @State private var url: String
    @State private var homepage: String
    @State private var country: String
    @State private var language: String
    @State private var tags: String
    @State private var codec: String
    @State private var bitrate: Int
    @State private var errorText = ""

    init(station: Station, isNew: Bool) {
        originalStation = station
        self.isNew = isNew
        _name = State(initialValue: station.name)
        _url = State(initialValue: station.url)
        _homepage = State(initialValue: station.homepage)
        _country = State(initialValue: station.country)
        _language = State(initialValue: station.language)
        _tags = State(initialValue: station.tags)
        _codec = State(initialValue: station.codec)
        _bitrate = State(initialValue: max(0, station.bitrate))
    }

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                Text(model.localization.t(isNew ? "St.AddTitle" : "St.EditTitle"))
                    .font(.system(size: 14, weight: .semibold))
                Spacer()
            }
            .padding(.horizontal, 16)
            .padding(.vertical, 12)

            Form {
                Section {
                    field(model.localization.t("St.Name"), text: $name)
                    field(model.localization.t("St.Url"), text: $url)
                    field(model.localization.t("St.Homepage"), text: $homepage)
                }
                Section {
                    field(model.localization.t("St.Country"), text: $country)
                    field(model.localization.t("St.Language"), text: $language)
                    field(model.localization.t("St.Genre"), text: $tags)
                    HStack {
                        Text(model.localization.t("St.Codec"))
                        TextField("", text: $codec)
                            .textFieldStyle(.roundedBorder)
                            .frame(width: 90)
                        Text(model.localization.t("St.Bitrate"))
                        TextField("", value: $bitrate, format: .number)
                            .textFieldStyle(.roundedBorder)
                            .frame(width: 80)
                    }
                }
            }
            .formStyle(.grouped)
            .frame(maxHeight: .infinity)

            if !errorText.isEmpty {
                Text(errorText)
                    .font(.system(size: 11))
                    .foregroundStyle(.red)
                    .padding(.horizontal, 16)
                    .padding(.bottom, 5)
            }

            HStack {
                Spacer()
                Button(model.localization.t("Cancel")) { dismiss() }
                Button(model.localization.t("Save")) { save() }
                    .keyboardShortcut(.defaultAction)
                    .disabled(name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || url.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
            }
            .padding(14)
        }
        .frame(width: 500, height: 430)
    }

    private func field(_ label: String, text: Binding<String>) -> some View {
        HStack {
            Text(label)
                .frame(width: 92, alignment: .leading)
            TextField("", text: text)
                .textFieldStyle(.roundedBorder)
        }
    }

    private func save() {
        var station = originalStation
        station.name = name
        station.url = url
        station.homepage = homepage
        station.country = country
        station.language = language
        station.tags = tags
        station.codec = codec.uppercased()
        station.bitrate = bitrate
        station.isCustom = true
        do {
            try model.saveStation(station, isNew: isNew)
            dismiss()
        } catch {
            errorText = error.localizedDescription
        }
    }
}

struct StationDetailsView: View {
    @EnvironmentObject private var model: MainViewModel
    @Environment(\.dismiss) private var dismiss
    let station: Station

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                Text(model.localization.t("St.DetailsTitle"))
                    .font(.system(size: 14, weight: .semibold))
                Spacer()
                Button {
                    if let url = URL(string: station.favicon), url.scheme?.hasPrefix("http") == true {
                        NSWorkspace.shared.open(url)
                    }
                } label: {
                    AsyncImage(url: URL(string: station.favicon)) { image in
                        image.resizable().scaledToFit()
                    } placeholder: {
                        Image(systemName: "dot.radiowaves.left.and.right")
                            .foregroundStyle(model.themeService.color("TextDim"))
                    }
                    .frame(width: 34, height: 34)
                }
                .buttonStyle(.plain)
                .disabled(station.favicon.isEmpty)
            }
            .padding(.horizontal, 16)
            .padding(.vertical, 12)

            Form {
                detail(model.localization.t("St.Name"), station.name)
                detail(model.localization.t("St.Url"), station.url)
                detail(model.localization.t("St.Homepage"), station.homepage)
                detail(model.localization.t("St.Country"), [station.country, station.countryCode].filter { !$0.isEmpty }.joined(separator: " / "))
                detail(model.localization.t("St.Language"), station.languageDisplay)
                detail(model.localization.t("St.Genre"), station.tags)
                detail(model.localization.t("St.Codec"), station.codec)
                detail(model.localization.t("St.Bitrate"), station.bitrate > 0 ? "\(station.bitrate) kbps" : "")
                detail(model.localization.t("St.Votes"), "\(station.votes)")
                detail(model.localization.t("St.Source"), model.localization.t(station.isCustom ? "St.SourceCustom" : "St.SourceRadioBrowser"))
            }
            .formStyle(.grouped)

            HStack {
                Button(model.localization.t("Ctx.Play")) {
                    model.play(station)
                    dismiss()
                }
                Button(model.localization.t("Ctx.CopyUrl")) { model.copyStationURL(station) }
                Spacer()
                Button(model.localization.t("Close")) { dismiss() }
                    .keyboardShortcut(.defaultAction)
            }
            .padding(14)
        }
        .frame(width: 540, height: 480)
    }

    private func detail(_ label: String, _ value: String) -> some View {
        HStack(alignment: .top) {
            Text(label)
                .foregroundStyle(.secondary)
                .frame(width: 100, alignment: .leading)
            Text(value.isEmpty ? "—" : value)
                .textSelection(.enabled)
                .frame(maxWidth: .infinity, alignment: .leading)
        }
    }
}
