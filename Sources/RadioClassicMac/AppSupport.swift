import AppKit
import MediaPlayer
import ServiceManagement
import SwiftUI


enum LoginItemService {
    static func setEnabled(_ enabled: Bool) {
        guard Bundle.main.bundleIdentifier != nil else { return }
        do {
            if enabled {
                if SMAppService.mainApp.status != .enabled { try SMAppService.mainApp.register() }
            } else if SMAppService.mainApp.status == .enabled {
                try SMAppService.mainApp.unregister()
            }
        } catch {
            NSLog("RadioClassic: login item could not be updated: \(error.localizedDescription)")
        }
    }
}

@MainActor
final class AppActionBridge {
    static let shared = AppActionBridge()
    weak var model: MainViewModel?

    private init() {}
}

@MainActor
final class MainWindowRegistry {
    static let shared = MainWindowRegistry()
    weak var window: NSWindow?

    private init() {}
}

@MainActor
final class WindowCloseDelegate: NSObject, NSWindowDelegate {
    static let shared = WindowCloseDelegate()
    weak var model: MainViewModel?

    func windowShouldClose(_ sender: NSWindow) -> Bool {
        guard model?.settingsStore.current.closeToMenuBar != false else { return true }
        sender.orderOut(nil)
        return false
    }

    func windowDidEndLiveResize(_ notification: Notification) {
        guard let window = notification.object as? NSWindow,
              let model,
              !model.compactMode else { return }
        let size = window.frame.size
        if size.width >= 300, size.height >= 130 {
            model.settingsStore.current.windowWidth = Double(size.width)
            model.settingsStore.current.windowHeight = Double(size.height)
        }
    }

    func windowWillMiniaturize(_ notification: Notification) {
        guard let window = notification.object as? NSWindow,
              model?.settingsStore.current.minimizeToMenuBar == true else { return }
        window.orderOut(nil)
    }
}

@MainActor
final class MenuBarController: NSObject {
    static let shared = MenuBarController()

    private var statusItem: NSStatusItem?
    private weak var model: MainViewModel?
    private var remoteTargets: [Any] = []
    private var appliedInitialVisibility = false
    private var menuSignature = ""
    private var nowPlayingSignature = ""
    private var globalKeyMonitor: Any?
    private var wavePhase: CGFloat = 0

    func configure(model: MainViewModel) {
        self.model = model
        WindowCloseDelegate.shared.model = model
        if statusItem == nil {
            let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
            if let button = item.button {
                button.image = NSImage(systemSymbolName: "dot.radiowaves.left.and.right", accessibilityDescription: "RadioClassic")
                button.action = #selector(showMainWindow)
                button.target = self
                button.sendAction(on: [.leftMouseUp, .rightMouseUp])
            }
            statusItem = item
        }
        rebuildMenu()
        configureRemoteCommands()
        configureGlobalHotkeys()
        if !appliedInitialVisibility {
            appliedInitialVisibility = true
            if model.settingsStore.current.startMinimized {
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.4) {
                    MainWindowRegistry.shared.window?.orderOut(nil)
                }
            }
        }
    }

    func update() {
        guard let model else { return }
        let signature = [
            model.trackLine,
            model.stationLine,
            model.player.state.rawValue,
            String(model.targetStationRecording),
            model.settingsStore.current.language
        ].joined(separator: "\u{1f}")
        if signature != menuSignature {
            menuSignature = signature
            rebuildMenu()
        }
        if signature != nowPlayingSignature {
            nowPlayingSignature = signature
            updateNowPlaying()
        }
        updateStatusIcon(model)
    }

    private func configureGlobalHotkeys() {
        guard globalKeyMonitor == nil else { return }
        globalKeyMonitor = NSEvent.addGlobalMonitorForEvents(matching: .keyDown) { [weak self] event in
            Task { @MainActor in
                self?.handleGlobalKey(event)
            }
        }
    }

    private func handleGlobalKey(_ event: NSEvent) {
        guard let model, model.settingsStore.current.globalHotkeys else { return }
        let flags = event.modifierFlags.intersection(.deviceIndependentFlagsMask)
        guard flags.contains(.control), flags.contains(.option), !flags.contains(.command) else { return }
        switch event.keyCode {
        case 49:
            model.playPause()
        case 123:
            model.step(-1)
        case 124:
            model.step(1)
        case 126:
            model.adjustVolume(by: 5)
        case 125:
            model.adjustVolume(by: -5)
        default:
            break
        }
    }

    private func configureRemoteCommands() {
        guard remoteTargets.isEmpty else { return }
        let center = MPRemoteCommandCenter.shared()
        remoteTargets.append(center.playCommand.addTarget { [weak self] _ in
            self?.perform { $0.playPause() } ?? .commandFailed
        })
        remoteTargets.append(center.pauseCommand.addTarget { [weak self] _ in
            self?.perform { $0.playPause() } ?? .commandFailed
        })
        remoteTargets.append(center.togglePlayPauseCommand.addTarget { [weak self] _ in
            self?.perform { $0.playPause() } ?? .commandFailed
        })
        remoteTargets.append(center.stopCommand.addTarget { [weak self] _ in
            self?.perform { $0.stop() } ?? .commandFailed
        })
        remoteTargets.append(center.nextTrackCommand.addTarget { [weak self] _ in
            self?.perform { $0.step(1) } ?? .commandFailed
        })
        remoteTargets.append(center.previousTrackCommand.addTarget { [weak self] _ in
            self?.perform { $0.step(-1) } ?? .commandFailed
        })
    }

    private func perform(_ action: (MainViewModel) -> Void) -> MPRemoteCommandHandlerStatus? {
        guard let model, model.settingsStore.current.mediaKeys else { return .commandFailed }
        action(model)
        return .success
    }

    private func rebuildMenu() {
        guard let item = statusItem, let model else { return }
        let t = { model.localization.t($0) }
        let menu = NSMenu()
        let nowPlaying = NSMenuItem(title: model.trackLine.isEmpty ? t("Tray.NowPlaying") + ": —" : model.trackLine, action: nil, keyEquivalent: "")
        nowPlaying.isEnabled = false
        menu.addItem(nowPlaying)
        menu.addItem(.separator())
        menu.addItem(makeItem(t("Tray.Show"), action: #selector(showMainWindow), key: ""))
        menu.addItem(makeItem(t(model.player.state == .playing ? "Menu.Pause" : "Menu.Play"), action: #selector(togglePlay), key: ""))
        menu.addItem(makeItem(t("Menu.Stop"), action: #selector(stopPlayback), key: ""))
        menu.addItem(makeItem(t(model.targetStationRecording ? "Menu.StopRecording" : "Menu.Record"), action: #selector(toggleRecording), key: ""))
        menu.addItem(.separator())
        menu.addItem(makeItem(t("Tray.Exit"), action: #selector(quitApplication), key: "q"))
        item.menu = menu
    }

    private func makeItem(_ title: String, action: Selector, key: String) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: action, keyEquivalent: key)
        item.target = self
        return item
    }


    private func updateStatusIcon(_ model: MainViewModel) {
        guard let button = statusItem?.button else { return }
        guard model.player.state == .playing else {
            wavePhase = 0
            button.image = NSImage(systemSymbolName: "dot.radiowaves.left.and.right", accessibilityDescription: "RadioClassic")
            return
        }
        wavePhase = (wavePhase + 0.18).truncatingRemainder(dividingBy: 1)
        let level = CGFloat(max(0.18, min(1, model.player.audioLevel * 1.35)))
        button.image = makeRadioWavesIcon(phase: wavePhase, level: level)
    }

    private func makeRadioWavesIcon(phase: CGFloat, level: CGFloat) -> NSImage {
        let size = NSSize(width: 18, height: 18)
        let image = NSImage(size: size)
        image.lockFocus()
        let center = NSPoint(x: size.width / 2, y: size.height / 2)
        NSColor(calibratedRed: 0.05, green: 1, blue: 0.25, alpha: 0.55 + 0.45 * level).setFill()
        NSBezierPath(ovalIn: NSRect(x: center.x - 1.45, y: center.y - 1.45, width: 2.9, height: 2.9)).fill()

        for wave in 0..<3 {
            let progress = (phase + CGFloat(wave) / 3).truncatingRemainder(dividingBy: 1)
            let radius = 3.0 + progress * 6.0
            let alpha = (1 - progress) * (0.28 + 0.72 * level)
            NSColor(calibratedRed: 0.05, green: 1, blue: 0.25, alpha: alpha).setStroke()
            for direction in [-1.0, 1.0] as [CGFloat] {
                let path = NSBezierPath()
                path.lineWidth = 1.25
                path.lineCapStyle = .round
                path.move(to: NSPoint(x: center.x + direction * radius * 0.55, y: center.y - radius))
                path.curve(
                    to: NSPoint(x: center.x + direction * radius * 0.55, y: center.y + radius),
                    controlPoint1: NSPoint(x: center.x + direction * radius * 1.15, y: center.y - radius * 0.48),
                    controlPoint2: NSPoint(x: center.x + direction * radius * 1.15, y: center.y + radius * 0.48)
                )
                path.stroke()
            }
        }
        image.unlockFocus()
        image.isTemplate = false
        return image
    }

    private func updateNowPlaying() {
        guard let model else { return }
        var info: [String: Any] = [:]
        if !model.trackLine.isEmpty { info[MPMediaItemPropertyTitle] = model.trackLine }
        if !model.stationLine.isEmpty { info[MPMediaItemPropertyArtist] = model.stationLine }
        info[MPNowPlayingInfoPropertyPlaybackRate] = model.player.state == .playing ? 1.0 : 0.0
        MPNowPlayingInfoCenter.default().nowPlayingInfo = info.isEmpty ? nil : info
    }

    @objc private func showMainWindow() {
        showMainWindowPublic()
    }

    func showMainWindowPublic() {
        if let window = MainWindowRegistry.shared.window {
            window.deminiaturize(nil)
            window.orderFrontRegardless()
            window.makeKeyAndOrderFront(nil)
            NSApp.activate(ignoringOtherApps: true)
        } else {
            NSApp.activate(ignoringOtherApps: true)
        }
    }

    @objc private func togglePlay() { model?.playPause() }
    @objc private func stopPlayback() { model?.stop() }
    @objc private func toggleRecording() { model?.toggleRecord() }

    @objc private func quitApplication() {
        model?.shutdown()
        NSApp.terminate(nil)
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationWillFinishLaunching(_ notification: Notification) {
        guard let identifier = Bundle.main.bundleIdentifier else { return }
        let others = NSRunningApplication.runningApplications(withBundleIdentifier: identifier)
            .filter { $0.processIdentifier != ProcessInfo.processInfo.processIdentifier }
        guard let existing = others.first else { return }
        existing.activate(options: [.activateAllWindows])
        NSApp.terminate(nil)
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        AppActionBridge.shared.model?.settingsStore.current.closeToMenuBar == false
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        if !flag {
            MenuBarController.shared.showMainWindowPublic()
        }
        return true
    }

    @MainActor
    func applicationWillTerminate(_ notification: Notification) {
        AppActionBridge.shared.model?.shutdown()
    }
}
