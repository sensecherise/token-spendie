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

/// Timing for the refresh icon's minimum-visible spin. A usage fetch can finish
/// in ~100 ms — far too fast for a 1 s/turn spin to read as a spin — so once the
/// spin starts it is held for at least one full turn.
enum RefreshSpin {
    /// One full turn of the icon at the spin animation's 1 s/turn rate.
    static let minDuration: TimeInterval = 1.0

    /// Seconds the spin must still run, given it began at `start`, measured at
    /// `now`. Zero once the minimum has elapsed, or when there is no `start`.
    static func remaining(start: Date?, now: Date) -> TimeInterval {
        guard let start else { return 0 }
        return max(0, minDuration - now.timeIntervalSince(start))
    }
}

/// The header refresh control: spins while a refresh runs — held to at least one
/// full turn so a fast fetch is still visible — is disabled while spinning, and
/// shows a subtle rounded background on hover.
private struct RefreshButton: View {
    @ObservedObject var store: UsageStore
    var onRefresh: () -> Void
    @State private var hovering = false
    /// Drives the spin. Set true when a refresh starts; cleared only after the
    /// fetch finishes AND at least `RefreshSpin.minDuration` has been shown.
    @State private var spinning = false
    /// When the current spin began — used to compute the minimum hold.
    @State private var spinStart: Date?

    var body: some View {
        Button(action: tapped) {
            Image(systemName: "arrow.clockwise")
                .font(.system(size: 11, weight: .semibold))
                .rotationEffect(.degrees(spinning ? 360 : 0))
                .animation(spinAnimation, value: spinning)
                .frame(width: 20, height: 20)
                .background(
                    RoundedRectangle(cornerRadius: 6, style: .continuous)
                        .fill(Color.primary.opacity(hovering ? 0.12 : 0))
                )
        }
        .buttonStyle(.plain)
        .disabled(spinning)
        .onHover { hovering = $0 }
        .animation(.easeOut(duration: 0.12), value: hovering)
        .onAppear { if store.isRefreshing { startSpin() } }
        .onChange(of: store.isRefreshing) { refreshing in
            if refreshing { startSpin() } else { stopSpinAfterMinimum() }
        }
    }

    /// Continuous rotation while spinning; a quick settle to rest when it ends.
    private var spinAnimation: Animation {
        spinning
            ? .linear(duration: 1).repeatForever(autoreverses: false)
            : .linear(duration: 0.2)
    }

    /// Handles a click: spins immediately to acknowledge it, then triggers the
    /// refresh. The spin is feedback for the *click*, so it runs even when the
    /// refresh is throttled or in 429 backoff — cases where `store.isRefreshing`
    /// never flips. A real fetch, if one runs, extends the spin via `onChange`.
    private func tapped() {
        onRefresh()
        startSpin()
        stopSpinAfterMinimum()
    }

    private func startSpin() {
        spinStart = Date()
        spinning = true
    }

    /// Stops the spin, but never before one full turn has been shown.
    private func stopSpinAfterMinimum() {
        let pending = spinStart
        let remaining = RefreshSpin.remaining(start: pending, now: Date())
        guard remaining > 0 else { spinning = false; return }
        DispatchQueue.main.asyncAfter(deadline: .now() + remaining) {
            // Skip if a newer spin began, or another refresh is now in flight.
            if spinStart == pending, !store.isRefreshing { spinning = false }
        }
    }
}

/// A full-width action row in the dropdown's actions section: leading icon,
/// label, and a subtle rounded highlight on hover.
private struct MenuActionRow: View {
    let systemImage: String
    let title: String
    var action: () -> Void
    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            HStack(spacing: 8) {
                Image(systemName: systemImage)
                    .font(.system(size: 11))
                    .frame(width: 14)
                Text(title).font(.system(size: 12))
                Spacer()
            }
            .padding(.horizontal, 8)
            .padding(.vertical, 6)
            .frame(maxWidth: .infinity, alignment: .leading)
            .contentShape(Rectangle())
            .background(
                RoundedRectangle(cornerRadius: 6, style: .continuous)
                    .fill(Color.primary.opacity(hovering ? 0.09 : 0))
            )
        }
        .buttonStyle(.plain)
        .onHover { hovering = $0 }
        .animation(.easeOut(duration: 0.12), value: hovering)
    }
}

/// The full detail panel: header, usage rows, actions section, status strip.
struct DetailPanelView: View {
    @ObservedObject var store: UsageStore
    @ObservedObject var preferences: Preferences
    var onRefresh: () -> Void
    var onOpenSettings: () -> Void
    var onQuit: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            header
            Divider()
            content.padding(13)
            Divider()
            actions
            Divider()
            statusStrip
        }
        .frame(width: 260)
    }

    private var header: some View {
        HStack {
            Text("TOKEN SPENDIE")
                .font(.system(size: 10, weight: .heavy)).kerning(0.5)
            Spacer()
            RefreshButton(store: store, onRefresh: onRefresh)
        }
        .padding(.horizontal, 10).padding(.vertical, 6)
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
            }
        }()
        return HStack(alignment: .top, spacing: 8) {
            Text(icon).font(.system(size: 15))
            Text(text).font(.system(size: 11)).foregroundStyle(.primary.opacity(0.85))
        }
    }

    private var actions: some View {
        VStack(spacing: 0) {
            MenuActionRow(systemImage: "gearshape", title: "Settings…", action: onOpenSettings)
            MenuActionRow(systemImage: "power", title: "Quit", action: onQuit)
        }
        .padding(.horizontal, 5)
        .padding(.vertical, 4)
    }

    private var statusStrip: some View {
        Text(statusText)
            .font(.system(size: 9))
            .foregroundStyle(.secondary)
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.horizontal, 13)
            .padding(.vertical, 7)
    }

    private var statusText: String {
        guard let snapshot = store.snapshot else { return " " }
        let ago = Formatting.updatedAgo(snapshot.fetchedAt, now: Date())
        if let until = store.rateLimitedUntil {
            let mins = max(1, Int((until.timeIntervalSinceNow / 60).rounded(.up)))
            return "rate limited — retry in \(mins)m"
        }
        return store.state == .stale ? "offline — \(ago)" : ago
    }
}
