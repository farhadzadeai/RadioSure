import Foundation
import SwiftUI

public struct ThemePalette: Decodable, Sendable {
    public let colors: [String: String]

    public init(from decoder: Decoder) throws {
        let container = try decoder.singleValueContainer()
        colors = (try? container.decode([String: String].self)) ?? [:]
    }

    public init(colors: [String: String]) {
        self.colors = colors
    }

    public func hex(_ key: String) -> String {
        colors[key] ?? "#000000"
    }
}

public final class ThemeService: ObservableObject {
    public static let shared = ThemeService()
    public static let order = ["Classic", "BlueClassic", "DarkClassic", "Graphite", "Minimal", "Silver"]

    @Published public var currentName = "Classic"
    @Published public var customAccentHex: String?
    public private(set) var themes: [String: ThemePalette] = [:]

    public init(bundle: Bundle? = nil) {
        let bundle = bundle ?? .module
        guard let url = bundle.url(forResource: "themes", withExtension: "json"),
              let data = try? Data(contentsOf: url),
              let decoded = try? JSONDecoder().decode([String: ThemePalette].self, from: data) else { return }
        themes = decoded
    }

    public var names: [String] {
        Self.order.filter { themes[$0] != nil } + themes.keys.sorted().filter { !Self.order.contains($0) }
    }

    public var current: ThemePalette {
        themes[currentName] ?? themes["Classic"] ?? ThemePalette(colors: [:])
    }

    public func setTheme(_ name: String) {
        if themes[name] != nil { currentName = name }
    }

    public func color(_ key: String) -> Color {
        var value = current.hex(key)
        if key == "AccentColor", let customAccentHex, !customAccentHex.isEmpty { value = customAccentHex }
        if value.hasPrefix("{"), key != "AccentColor" { value = current.hex("AccentColor") }
        if key == "AccentBrush", let customAccentHex, !customAccentHex.isEmpty { value = customAccentHex }
        return Color(hex: value)
    }
}

public extension Color {
    init(hex: String) {
        var value = hex.trimmingCharacters(in: .whitespacesAndNewlines)
        if value.hasPrefix("#") { value.removeFirst() }
        var rgb: UInt64 = 0
        Scanner(string: value).scanHexInt64(&rgb)
        if value.count == 6 {
            self.init(
                .sRGB,
                red: Double((rgb >> 16) & 0xFF) / 255,
                green: Double((rgb >> 8) & 0xFF) / 255,
                blue: Double(rgb & 0xFF) / 255,
                opacity: 1
            )
        } else if value.count == 8 {
            self.init(
                .sRGB,
                red: Double((rgb >> 24) & 0xFF) / 255,
                green: Double((rgb >> 16) & 0xFF) / 255,
                blue: Double((rgb >> 8) & 0xFF) / 255,
                opacity: Double(rgb & 0xFF) / 255
            )
        } else {
            self.init(.sRGB, red: 0, green: 0, blue: 0, opacity: 1)
        }
    }
}
