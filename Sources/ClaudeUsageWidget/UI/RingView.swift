import SwiftUI

/// A circular progress ring filled to `percent` (0–100), colored by usage level.
struct RingView: View {
    let percent: Double
    var lineWidth: CGFloat = 4
    var dimmed: Bool = false

    private var fraction: CGFloat { min(max(percent / 100, 0), 1) }
    private var color: Color {
        dimmed ? Color.secondary : UsageLevel.forPercent(percent).color
    }

    var body: some View {
        ZStack {
            Circle()
                .stroke(Color.primary.opacity(0.18), lineWidth: lineWidth)
            Circle()
                .trim(from: 0, to: fraction)
                .stroke(color, style: StrokeStyle(lineWidth: lineWidth, lineCap: .round))
                .rotationEffect(.degrees(-90))
        }
    }
}

/// The compact menu bar item: session ring + percentage, or a status glyph.
struct MenuBarLabel: View {
    @ObservedObject var store: UsageStore

    var body: some View {
        HStack(spacing: 4) {
            switch store.state {
            case .error(.claudeCodeNotFound):
                Text("✳ –").font(.system(size: 12, weight: .semibold))
            case .error:
                Text("✳ !").font(.system(size: 12, weight: .semibold))
                    .foregroundStyle(UsageLevel.hot.color)
            case .loading where store.snapshot == nil:
                Text("✳ …").font(.system(size: 12, weight: .semibold))
            default:
                let percent = store.snapshot?.session.percent ?? 0
                RingView(percent: percent, lineWidth: 3, dimmed: store.state == .stale)
                    .frame(width: 14, height: 14)
                Text("\(Int(percent.rounded()))%")
                    .font(.system(size: 12, weight: .semibold))
                    .foregroundStyle(store.state == .stale ? Color.secondary : Color.primary)
            }
        }
        .padding(.horizontal, 2)
    }
}
