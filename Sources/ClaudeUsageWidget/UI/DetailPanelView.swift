import SwiftUI

/// One labelled progress bar with its reset line.
struct UsageBarRow: View {
    let title: String
    let subtitle: String
    let window: UsageWindow
    let resetLine: String
    var theme: Theme

    private var level: UsageLevel { UsageLevel.forPercent(window.percent) }
    private var tierColor: Color { theme.color(for: level) }
    private var fraction: CGFloat { min(max(window.percent / 100, 0), 1) }

    var body: some View {
        VStack(alignment: .leading, spacing: 5) {
            HStack {
                Text(title).font(.system(size: 12, weight: .semibold))
                Spacer()
                Text("\(Int(window.percent.rounded()))%")
                    .font(.system(size: 12, weight: .bold))
                    .foregroundStyle(tierColor)
            }
            GeometryReader { geo in
                ZStack(alignment: .leading) {
                    Capsule().fill(Color.primary.opacity(0.12))
                    Capsule()
                        .fill(tierColor)
                        .frame(width: geo.size.width * fraction)
                }
            }
            .frame(height: 7)
            Text(resetLine)
                .font(.system(size: 10))
                .foregroundStyle(Color.secondary)
        }
    }
}

/// The full detail panel: header, session/weekly/model rows, footer.
struct DetailPanelView: View {
    @ObservedObject var store: UsageStore
    @ObservedObject var preferences: Preferences
    var onRefresh: () -> Void
    var onOpenSettings: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            header
            Divider()
            content.padding(13)
            Divider()
            footer
        }
        .frame(width: 260)
    }

    private var header: some View {
        HStack {
            Text("CLAUDE USAGE")
                .font(.system(size: 10, weight: .heavy)).kerning(0.5)
            Spacer()
            Button(action: onRefresh) {
                Image(systemName: "arrow.clockwise").font(.system(size: 11, weight: .semibold))
            }
            .buttonStyle(.plain)
        }
        .padding(.horizontal, 13).padding(.vertical, 10)
    }

    @ViewBuilder
    private var content: some View {
        switch store.state {
        case .error(let kind):
            messageView(for: kind)
        case .loading where store.snapshot == nil:
            Text("Loading usage…").font(.system(size: 12)).foregroundStyle(.secondary)
        default:
            if let snapshot = store.snapshot {
                VStack(alignment: .leading, spacing: 13) {
                    UsageBarRow(title: "Session", subtitle: "5-hour window",
                                window: snapshot.session,
                                resetLine: "5-hour window · " + Formatting.resetCountdown(to: snapshot.session.resetsAt, now: Date()),
                                theme: preferences.theme)
                    UsageBarRow(title: "Weekly", subtitle: "all models",
                                window: snapshot.weekly,
                                resetLine: "all models · " + Formatting.resetDate(snapshot.weekly.resetsAt),
                                theme: preferences.theme)
                    ForEach(snapshot.modelWeeklies, id: \.model) { item in
                        UsageBarRow(title: "Weekly · \(item.model)", subtitle: item.model,
                                    window: item.window,
                                    resetLine: "\(item.model) only · " + Formatting.resetDate(item.window.resetsAt),
                                    theme: preferences.theme)
                    }
                }
            }
        }
    }

    private func messageView(for kind: UsageError) -> some View {
        let (icon, text): (String, String) = {
            switch kind {
            case .claudeCodeNotFound:
                return ("🔌", "Claude Code not found. Install and log in to Claude Code, then this widget picks up your usage automatically.")
            case .keychainAccessDenied:
                return ("🔑", "Keychain access needed. The widget reads your Claude login token from the Keychain — click refresh and choose Allow.")
            case .loginExpired:
                return ("⏱", "Login expired. Run any Claude Code command to refresh your session — the widget recovers on its own after that.")
            case .network:
                return ("📡", "Can't reach the usage service. The widget will keep retrying.")
            case .badResponse:
                return ("⚠️", "Couldn't read usage data. The usage source returned something unexpected — the widget will keep retrying.")
            case .noManualToken:
                return ("🔑", "No token saved. Open Settings, choose Manual, and paste a token from `claude setup-token`.")
            }
        }()
        return HStack(alignment: .top, spacing: 8) {
            Text(icon).font(.system(size: 15))
            Text(text).font(.system(size: 11)).foregroundStyle(.primary.opacity(0.85))
        }
    }

    private var footer: some View {
        HStack {
            Text(footerStatus).font(.system(size: 9)).foregroundStyle(.secondary)
            Spacer()
            Button(action: onOpenSettings) {
                Text("⚙ Settings").font(.system(size: 9))
            }
            .buttonStyle(.plain)
        }
        .padding(.horizontal, 13).padding(.vertical, 8)
    }

    private var footerStatus: String {
        guard let snapshot = store.snapshot else { return " " }
        let ago = Formatting.updatedAgo(snapshot.fetchedAt, now: Date())
        return store.state == .stale ? "offline — \(ago)" : ago
    }
}
