import SwiftUI

/// Skeleton only - implement against docs/TECH-SPEC.md.
/// Shared assets ready in Shared/: strings.en.json, strings.fa.json, themes.json
struct ContentView: View {
    var body: some View {
        VStack(spacing: 0) {
            // TODO: search bar + filter dropdowns row
            // TODO: sortable station Table (53k rows - use lazy loading)
            // TODO: spectrum visualizer + status bar
            // TODO: transport bar with record button
            Text("RadioClassic for macOS")
                .font(.title2)
                .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
        .frame(minWidth: 560, minHeight: 500)
    }
}
