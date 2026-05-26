import SwiftUI

struct AboutView: View {
    private let version: String = Bundle.main
        .infoDictionary?["CFBundleShortVersionString"] as? String ?? "–"

    var body: some View {
        VStack(spacing: 12) {
            Image(nsImage: NSApp.applicationIconImage)
                .resizable()
                .frame(width: 64, height: 64)

            Text("Token Spendie")
                .font(.system(size: 16, weight: .bold))

            Text("Version \(version)")
                .font(.system(size: 12))
                .foregroundStyle(.secondary)

            Divider()

            Text("Created by **nong.seng**")
                .font(.system(size: 12))

            Text("Claude Code usage menu bar widget")
                .font(.system(size: 11))
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
        }
        .padding(24)
        .frame(width: 260)
    }
}
