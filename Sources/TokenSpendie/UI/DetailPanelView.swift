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

/// Drives the "fetching…" ellipsis: a dot count that cycles 1 → 2 → 3 as the
/// `TimelineView` ticks, so the text reads "fetching." → "fetching.." →
/// "fetching..." while a refresh runs.
enum FetchingEllipsis {
    /// Seconds between dot-count changes — the `TimelineView` tick interval.
    static let period: TimeInterval = 0.4

    /// Dot count (1...3) for a given timeline tick.
    static func dotCount(at date: Date) -> Int {
        let ticks = Int(date.timeIntervalSinceReferenceDate / period)
        return ticks % 3 + 1
    }
}

/// The resolved status shown in the popover header, left of the refresh button.
enum RefreshStatus: Equatable {
    /// No snapshot yet and not fetching — the header shows nothing.
    case idle
    /// A refresh is running — the header shows "fetching" + an animated ellipsis.
    case fetching
    /// A ready-to-display string: "updated 5m ago" / "offline — …" / "rate limited — …".
    case text(String)
}

/// Resolves the header status from store state. Pure, so it is unit-tested
/// directly; the view passes live store values and `Date()`.
enum RefreshStatusResolver {
    static func resolve(isFetching: Bool,
                        snapshotFetchedAt: Date?,
                        rateLimitedUntil: Date?,
                        isStale: Bool,
                        now: Date) -> RefreshStatus {
        if isFetching { return .fetching }
        guard let fetchedAt = snapshotFetchedAt else { return .idle }
        if let until = rateLimitedUntil {
            let mins = max(1, Int((until.timeIntervalSince(now) / 60).rounded(.up)))
            return .text("rate limited — retry in \(mins)m")
        }
        let ago = Formatting.updatedAgo(fetchedAt, now: now)
        return .text(isStale ? "offline — \(ago)" : ago)
    }
}

/// A headless circular arc — `arrow.clockwise` with the head removed — that
/// spins continuously. Shown in place of the refresh glyph while a fetch runs.
private struct SpinningArc: View {
    @State private var angle: Double = 0

    var body: some View {
        Circle()
            .trim(from: 0, to: 0.85)
            .stroke(style: StrokeStyle(lineWidth: 1.6, lineCap: .round))
            .frame(width: 11, height: 11)
            .rotationEffect(.degrees(angle))
            .onAppear {
                withAnimation(.linear(duration: 1).repeatForever(autoreverses: false)) {
                    angle = 360
                }
            }
    }
}

/// The popover header's status + refresh control. Owns the spin state so the
/// status text and the icon flip together: while spinning the text reads
/// "fetching…" and the glyph cross-fades from `arrow.clockwise` to a headless
/// spinning arc. The spin is held to at least one full turn via `RefreshSpin`
/// so a fast fetch is still readable; the "fetching…" text honours the same
/// minimum because both are driven by `spinning`.
private struct RefreshIndicator: View {
    @ObservedObject var store: UsageStore
    var onRefresh: () -> Void

    @State private var hovering = false
    /// Drives the "fetching…" text and the icon morph. Set true when a refresh
    /// starts; cleared only after the fetch finishes AND `RefreshSpin.minDuration`
    /// has elapsed.
    @State private var spinning = false
    /// When the current spin began — used to compute the minimum hold.
    @State private var spinStart: Date?

    var body: some View {
        HStack(spacing: 6) {
            statusText
            refreshButton
        }
        .onAppear { if store.isRefreshing { startSpin() } }
        .onChange(of: store.isRefreshing) { refreshing in
            if refreshing { startSpin() } else { stopSpinAfterMinimum() }
        }
    }

    // MARK: Status text

    @ViewBuilder
    private var statusText: some View {
        switch RefreshStatusResolver.resolve(
            isFetching: spinning,
            snapshotFetchedAt: store.snapshot?.fetchedAt,
            rateLimitedUntil: store.rateLimitedUntil,
            isStale: store.state == .stale,
            now: Date()
        ) {
        case .idle:
            EmptyView()
        case .fetching:
            fetchingText
        case .text(let value):
            Text(value)
                .font(.system(size: 9))
                .foregroundStyle(.secondary)
        }
    }

    /// "fetching" followed by an ellipsis that cycles 1 → 2 → 3 dots. The dots
    /// sit in a fixed-width slot so the row width does not jump as they change.
    private var fetchingText: some View {
        TimelineView(.periodic(from: .now, by: FetchingEllipsis.period)) { context in
            HStack(spacing: 0) {
                Text("fetching")
                Text(String(repeating: ".", count: FetchingEllipsis.dotCount(at: context.date)))
                    .frame(width: 12, alignment: .leading)
            }
            .font(.system(size: 9))
            .foregroundStyle(.secondary)
        }
    }

    // MARK: Refresh button

    private var refreshButton: some View {
        Button(action: tapped) {
            ZStack {
                Image(systemName: "arrow.clockwise")
                    .font(.system(size: 11, weight: .semibold))
                    .opacity(spinning ? 0 : 1)
                if spinning {
                    SpinningArc().transition(.opacity)
                }
            }
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
        .animation(.easeInOut(duration: 0.15), value: spinning)
    }

    // MARK: Spin state

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
        }
        .frame(width: 260)
    }

    private var header: some View {
        HStack {
            Text("TOKEN SPENDIE")
                .font(.system(size: 10, weight: .heavy)).kerning(0.5)
            Spacer()
            RefreshIndicator(store: store, onRefresh: onRefresh)
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

}
