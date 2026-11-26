import RadioClassicCore
import SwiftUI

@main
struct RadioClassicMacApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate
    @ObservedObject private var localization = LocalizationService.shared

    var body: some Scene {
        WindowGroup(id: "main") {
            ContentView()
        }
        .windowStyle(.hiddenTitleBar)
        .defaultSize(width: 560, height: 500)
        .commands {
            CommandMenu(localization.t("Menu.Play")) {
                Button(localization.t("Menu.Play")) { AppActionBridge.shared.model?.playPause() }
                    .keyboardShortcut(.space, modifiers: [])
                Button(localization.t("Menu.Stop")) { AppActionBridge.shared.model?.stop() }
                    .keyboardShortcut(".", modifiers: [])
                Divider()
                Button(localization.t("Tip.Previous")) { AppActionBridge.shared.model?.step(-1) }
                    .keyboardShortcut(.leftArrow, modifiers: [.command])
                Button(localization.t("Tip.Next")) { AppActionBridge.shared.model?.step(1) }
                    .keyboardShortcut(.rightArrow, modifiers: [.command])
                Divider()
                Button(localization.t("Menu.Record")) { AppActionBridge.shared.model?.toggleRecord() }
                    .keyboardShortcut("r", modifiers: [.command])
                Button(localization.t("Menu.Mute")) { AppActionBridge.shared.model?.player.muted.toggle() }
                    .keyboardShortcut("m", modifiers: [.command])
                Button(localization.t("Tip.Volume") + " +") { AppActionBridge.shared.model?.adjustVolume(by: 5) }
                    .keyboardShortcut(.upArrow, modifiers: [.command])
                Button(localization.t("Tip.Volume") + " −") { AppActionBridge.shared.model?.adjustVolume(by: -5) }
                    .keyboardShortcut(.downArrow, modifiers: [.command])
            }
            CommandMenu(localization.t("Menu.Options")) {
                Button(localization.t("Menu.CompactMode")) { AppActionBridge.shared.model?.toggleCompactMode() }
                    .keyboardShortcut("c", modifiers: [.command, .shift])
                Button(localization.t("Menu.Favorites")) { AppActionBridge.shared.model?.toggleFavorite() }
                    .keyboardShortcut("f", modifiers: [.command])
                Button(localization.t("Menu.History")) { AppActionBridge.shared.model?.activeSheet = .history }
                    .keyboardShortcut("h", modifiers: [.command])
                Button(localization.t("Menu.Recordings")) { AppActionBridge.shared.model?.activeSheet = .recordings }
                    .keyboardShortcut("r", modifiers: [.command, .shift])
            }
        }
    }
}
