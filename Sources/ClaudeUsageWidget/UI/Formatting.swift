import Foundation
import SwiftUI

/// Color tier for a usage percentage.
enum UsageLevel: Equatable {
    case calm   // < 70%
    case warn   // 70% to < 90%
    case hot    // >= 90%

    static func forPercent(_ percent: Double) -> UsageLevel {
        if percent >= 90 { return .hot }
        if percent >= 70 { return .warn }
        return .calm
    }

    var color: Color {
        switch self {
        case .calm: return Color(red: 0.37, green: 0.72, blue: 0.47)
        case .warn: return Color(red: 0.88, green: 0.64, blue: 0.25)
        case .hot:  return Color(red: 0.85, green: 0.33, blue: 0.31)
        }
    }
}

/// Pure string formatting for the UI.
enum Formatting {
    /// Cached formatter — `DateFormatter` is costly to allocate. Used only from
    /// the main thread (SwiftUI rendering), so a single shared instance is safe.
    private static let weekdayDateFormatter: DateFormatter = {
        let formatter = DateFormatter()
        formatter.dateFormat = "EEE, MMM d"
        return formatter
    }()

    /// "resets in 2h 47m" / "resets in 40m" / "resetting now" / "" when unknown.
    static func resetCountdown(to date: Date?, now: Date) -> String {
        guard let date else { return "" }
        let remaining = Int(date.timeIntervalSince(now))
        if remaining <= 0 { return "resetting now" }
        let hours = remaining / 3600
        let minutes = (remaining % 3600) / 60
        if hours > 0 { return "resets in \(hours)h \(minutes)m" }
        return "resets in \(minutes)m"
    }

    /// "resets Mon, May 25" — absolute date, used for weekly windows.
    static func resetDate(_ date: Date?) -> String {
        guard let date else { return "" }
        return "resets \(weekdayDateFormatter.string(from: date))"
    }

    /// "updated just now" / "updated 10s ago" / "updated 5m ago" / "updated 2h ago".
    static func updatedAgo(_ date: Date, now: Date) -> String {
        let elapsed = Int(now.timeIntervalSince(date))
        if elapsed < 3 { return "updated just now" }
        if elapsed < 60 { return "updated \(elapsed)s ago" }
        if elapsed < 3600 { return "updated \(elapsed / 60)m ago" }
        return "updated \(elapsed / 3600)h ago"
    }
}
