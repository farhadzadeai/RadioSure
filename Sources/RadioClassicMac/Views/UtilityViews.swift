import AppKit
import RadioClassicCore
import SwiftUI

struct ComboBox: View {
    @EnvironmentObject private var model: MainViewModel
    let placeholder: String
    @Binding var text: String
    var suggestions: [String]

    var body: some View {
        HStack(spacing: 2) {
            TextField(placeholder, text: $text)
                .textFieldStyle(.plain)
                .disableAutocorrection(true)
                .font(.system(size: 11))
                .foregroundStyle(model.themeService.color("Text"))
            Menu {
                Button(model.localization.t("All")) { text = "" }
                ForEach(suggestions, id: \.self) { item in
                    Button(item) { text = item }
                }
            } label: {
                Image(systemName: "chevron.down")
                    .font(.system(size: 8, weight: .bold))
                    .foregroundStyle(model.themeService.color("TextDim"))
            }
            .menuStyle(.borderlessButton)
            .frame(width: 18)
        }
        .padding(.horizontal, 6)
        .frame(height: 22)
        .background(
            RoundedRectangle(cornerRadius: 3)
                .fill(model.themeService.color("InputBack"))
        )
        .overlay(
            RoundedRectangle(cornerRadius: 3)
                .stroke(model.themeService.color("BorderBrush"), lineWidth: 1)
        )
    }
}

struct SpectrumView: View {
    @EnvironmentObject private var model: MainViewModel

    var body: some View {
        Canvas { context, size in
            let background = Path(roundedRect: CGRect(origin: .zero, size: size), cornerRadius: 3)
            context.fill(background, with: .color(model.themeService.color("DisplayBack")))
            guard !model.spectrum.isEmpty, model.settingsStore.current.showSpectrum else { return }
            let count = model.spectrum.count
            let gap = 1.0
            let width = max(1, (size.width - gap * Double(count - 1)) / Double(count))
            for index in 0..<count {
                let value = min(1, max(0, Double(model.spectrum[index])))
                let height = max(1.5, value * (size.height - 3))
                let x = Double(index) * (width + gap)
                let rect = CGRect(x: x, y: size.height - height - 1, width: width, height: height)
                let color = value > 0.78 ? model.themeService.color("DisplayPeak") : model.themeService.color("DisplayBar")
                context.fill(Path(rect), with: .color(color.opacity(0.25 + value * 0.75)))
            }
        }
        .frame(height: 28)
        .overlay(
            RoundedRectangle(cornerRadius: 3)
                .stroke(model.themeService.color("BorderBrush"), lineWidth: 1)
        )
    }
}

struct WindowAccessor: NSViewRepresentable {
    let onResolve: (NSWindow) -> Void

    func makeNSView(context: Context) -> NSView {
        let view = NSView()
        DispatchQueue.main.async {
            if let window = view.window { onResolve(window) }
        }
        return view
    }

    func updateNSView(_ nsView: NSView, context: Context) {
        DispatchQueue.main.async {
            if let window = nsView.window { onResolve(window) }
        }
    }
}

struct PlayerButton: View {
    @EnvironmentObject private var model: MainViewModel
    let systemName: String
    let help: String
    var active = false
    var tint: Color?
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Image(systemName: systemName)
                .font(.system(size: 14, weight: .semibold))
                .foregroundStyle(tint ?? (active ? model.themeService.color("AccentTextBrush") : model.themeService.color("Text")))
                .frame(width: 30, height: 28)
                .background(
                    RoundedRectangle(cornerRadius: 4)
                        .fill(active ? model.themeService.color("HoverBack") : model.themeService.color("PanelBack"))
                )
                .overlay(
                    RoundedRectangle(cornerRadius: 4)
                        .stroke(model.themeService.color("BtnBorder"), lineWidth: 1)
                )
        }
        .buttonStyle(.plain)
        .help(help)
    }
}
