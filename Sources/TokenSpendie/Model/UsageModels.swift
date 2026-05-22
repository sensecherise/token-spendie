import Foundation

/// A single rate-limit window (session or weekly).
struct UsageWindow: Codable, Equatable {
    /// Percentage used, normalized to 0–100 (may exceed 100 if over cap).
    let percent: Double
    /// When this window's usage resets, if known.
    let resetsAt: Date?
}

/// A model-specific weekly cap (e.g. Opus, Sonnet).
struct ModelWeekly: Codable, Equatable {
    let model: String
    let window: UsageWindow
}

/// One complete reading of usage at a point in time.
struct UsageSnapshot: Codable, Equatable {
    let session: UsageWindow          // five_hour
    let weekly: UsageWindow           // seven_day
    let modelWeeklies: [ModelWeekly]  // seven_day_opus / seven_day_sonnet, if present
    let fetchedAt: Date
}

/// A user-facing failure. Each maps to a distinct widget state.
enum UsageError: Error, Equatable {
    case claudeCodeNotFound     // no Keychain item / Claude Code not logged in
    case keychainAccessDenied   // user denied the Keychain access prompt
    case loginExpired           // 401 even after re-reading the Keychain
    case network                // offline / unreachable
    case badResponse            // non-200 or unparseable payload
}

/// What the data layer's provider/decoder can throw.
enum ProviderError: Error, Equatable {
    case unauthorized           // HTTP 401
    case network                // transport failure
    case badResponse            // non-200, or payload could not be decoded
    case rateLimited(retryAfter: TimeInterval?)  // HTTP 429
}

/// The store's published display state.
enum LoadState: Equatable {
    case loading                // first load, no snapshot yet
    case ok                     // showing a fresh snapshot
    case stale                  // showing a cached snapshot, last refresh failed
    case error(UsageError)      // no usable snapshot to show
}

/// Identifies an AI CLI the widget can track.
enum ProviderID: String, Codable, CaseIterable, Equatable {
    case claude
    case gemini
}

/// How a window's `resetsAt` is rendered in the panel.
enum ResetStyle: String, Codable, Equatable {
    case countdown   // "resets in 2h 10m" — short rolling windows
    case date        // "resets Mon, May 25" — weekly windows
}

/// One usage window plus the text the panel needs to render it. `label` is the
/// row title; `detail` is the static prefix of the reset line (the live
/// countdown/date is appended at render time so it stays current).
struct LabeledWindow: Codable, Equatable {
    let label: String
    let detail: String
    let resetStyle: ResetStyle
    let window: UsageWindow
}

/// One complete reading of a provider's usage, normalized for the panel.
/// `windows` is the full native list and should include the `headline` window.
struct ProviderSnapshot: Codable, Equatable {
    let id: ProviderID
    let plan: String?               // best-effort; nil hides the plan pill
    let headline: LabeledWindow     // drives the ring + collapsed-row %
    let windows: [LabeledWindow]
    let fetchedAt: Date
    /// A short data-quality note shown under the section; nil hides it. Used
    /// by providers whose numbers are estimates rather than exact readings.
    var note: String? = nil
}

/// One panel row's complete state. Reuses `LoadState` as the per-provider
/// state. `displayName` is held here so an errored/loading row can render
/// before any snapshot exists.
struct ProviderUsage: Equatable {
    let id: ProviderID
    let displayName: String
    var state: LoadState
    var snapshot: ProviderSnapshot?
}
