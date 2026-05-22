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
