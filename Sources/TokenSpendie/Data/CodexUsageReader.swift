import Foundation

/// One rate-limit window from a Codex `token_count` event: the percent used and
/// how long until it resets, measured from when the snapshot was captured.
struct CodexRateWindow: Equatable {
    let usedPercent: Double
    let resetsInSeconds: TimeInterval?
}

/// A Codex rate-limit snapshot: the rolling 5-hour (`primary`) and weekly
/// (`secondary`) windows, plus the time the snapshot was captured.
struct CodexRateLimits: Equatable {
    let primary: CodexRateWindow?
    let secondary: CodexRateWindow?
    let capturedAt: Date
}

/// Reads OpenAI Codex CLI usage from its local session rollout files. Codex
/// exposes no usage API, so this scans `~/.codex/sessions/**/rollout-*.jsonl` —
/// the CLI's own per-session event log — for the most recent `token_count`
/// event that carries a `rate_limits` snapshot. Codex fetches those numbers from
/// the server during normal use and records them here, so the reading reflects
/// the last figures Codex itself saw. Best-effort: any unreadable or malformed
/// file or line is skipped, never thrown.
struct CodexUsageReader {
    private let codexHome: URL
    /// Clock — injected so tests can pin "now".
    let now: () -> Date

    init(codexHome: URL = FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent(".codex", isDirectory: true),
         now: @escaping () -> Date = Date.init) {
        self.codexHome = codexHome
        self.now = now
    }

    /// True if Codex CLI credentials are present. A cheap file-existence check —
    /// never reads the secret, never prompts.
    func detectCredentials() -> Bool {
        FileManager.default.fileExists(
            atPath: codexHome.appendingPathComponent("auth.json").path)
    }

    /// The most recent rate-limit snapshot across every session rollout file, or
    /// nil if none has been recorded yet. "Most recent" is by the event's own
    /// timestamp, so a freshly written file that lacks rate-limit data never
    /// shadows an older file that has it.
    func latestRateLimits() -> CodexRateLimits? {
        let sessionsDir = codexHome.appendingPathComponent("sessions", isDirectory: true)
        guard let enumerator = FileManager.default.enumerator(
            at: sessionsDir, includingPropertiesForKeys: nil) else { return nil }
        var latest: CodexRateLimits?
        for case let url as URL in enumerator where url.pathExtension == "jsonl" {
            guard let candidate = Self.latestRateLimits(inFileAt: url) else { continue }
            if latest == nil || candidate.capturedAt > latest!.capturedAt {
                latest = candidate
            }
        }
        return latest
    }

    /// The last (most recent) rate-limit snapshot in one rollout file, scanning
    /// its newline-delimited JSON records. Returns nil for an unreadable file or
    /// one that records no rate limits.
    private static func latestRateLimits(inFileAt url: URL) -> CodexRateLimits? {
        guard let data = try? Data(contentsOf: url),
              let text = String(data: data, encoding: .utf8) else { return nil }
        var latest: CodexRateLimits?
        for line in text.split(whereSeparator: { $0.isNewline }) {
            guard let lineData = String(line).data(using: .utf8),
                  let record = (try? JSONSerialization.jsonObject(with: lineData))
                    as? [String: Any],
                  let limits = parse(record) else { continue }
            if latest == nil || limits.capturedAt >= latest!.capturedAt {
                latest = limits
            }
        }
        return latest
    }

    /// Extracts a `rate_limits` snapshot from one rollout record. Codex nests it
    /// under `payload` (a `token_count` event); older builds placed it at the
    /// top level, so both are accepted. The record's `timestamp` dates the
    /// snapshot; absent that, "now" is used. Returns nil when neither window is
    /// present (e.g. a `rate_limits: null` event from exec mode).
    static func parse(_ record: [String: Any]) -> CodexRateLimits? {
        let payload = record["payload"] as? [String: Any]
        guard let limits = (payload?["rate_limits"] ?? record["rate_limits"])
                as? [String: Any] else { return nil }
        let primary = window(limits["primary"])
        let secondary = window(limits["secondary"])
        guard primary != nil || secondary != nil else { return nil }
        let capturedAt = (record["timestamp"] as? String)
            .flatMap(GeminiUsageReader.parseTimestamp) ?? Date()
        return CodexRateLimits(primary: primary, secondary: secondary,
                               capturedAt: capturedAt)
    }

    /// Maps one `{used_percent, resets_in_seconds}` object to a `CodexRateWindow`.
    /// A window with no `used_percent` is treated as absent.
    private static func window(_ raw: Any?) -> CodexRateWindow? {
        guard let dict = raw as? [String: Any],
              let percent = (dict["used_percent"] as? NSNumber)?.doubleValue
        else { return nil }
        let resets = (dict["resets_in_seconds"] as? NSNumber)?.doubleValue
        return CodexRateWindow(usedPercent: percent, resetsInSeconds: resets)
    }
}
