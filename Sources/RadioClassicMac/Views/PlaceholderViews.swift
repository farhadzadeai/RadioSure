import RadioClassicCore
import SwiftUI

struct AboutView: View {
    @EnvironmentObject private var model: MainViewModel

    var body: some View {
        VStack(spacing: 12) {
            Image(systemName: "dot.radiowaves.left.and.right")
                .font(.system(size: 42))
                .foregroundStyle(model.themeService.color("AccentTextBrush"))
            Text(model.localization.t("AppTitle"))
                .font(.title3.weight(.semibold))
            Text(model.localization.t("About.Version", "1.0"))
                .foregroundStyle(.secondary)
            Text(model.localization.t("About.Text"))
                .multilineTextAlignment(.center)
                .font(.system(size: 11))
                .foregroundStyle(.secondary)
                .frame(maxWidth: 360)
        }
        .padding(28)
        .frame(width: 430, height: 260)
    }
}
