import Foundation

/// Counts Gemini CLI usage from its local log files. Gemini exposes no usage
/// API, so this scans `~/.gemini/tmp/<project>/logs.json` — the CLI's own
/// per-project prompt log. Best-effort: any unreadable file or malformed
/// record is skipped, never thrown.
struct GeminiUsageReader {
    private let geminiHome: URL
    /// Clock — injected so tests can pin "today".
    let now: () -> Date
    /// Calendar deciding the local-midnight day boundary.
    private let calendar: Calendar

    init(geminiHome: URL = FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent(".gemini", isDirectory: true),
         now: @escaping () -> Date = Date.init,
         calendar: Calendar = .current) {
        self.geminiHome = geminiHome
        self.now = now
        self.calendar = calendar
    }

    /// True if Gemini CLI OAuth credentials are present. A cheap file-existence
    /// check — never reads the secret, never prompts.
    func detectCredentials() -> Bool {
        FileManager.default.fileExists(
            atPath: geminiHome.appendingPathComponent("oauth_creds.json").path)
    }

    /// The next local midnight after `now()` — when the daily count resets.
    func nextLocalMidnight() -> Date {
        let startOfToday = calendar.startOfDay(for: now())
        return calendar.date(byAdding: .day, value: 1, to: startOfToday)
            ?? startOfToday
    }

    /// Today's prompt count across every project's `logs.json`. A prompt is a
    /// `type: "user"` record whose message is not a slash command. Best-effort:
    /// missing, unreadable, or malformed files contribute 0.
    func requestsToday() -> Int {
        let startOfToday = calendar.startOfDay(for: now())
        let tmpDir = geminiHome.appendingPathComponent("tmp", isDirectory: true)
        guard let projects = try? FileManager.default.contentsOfDirectory(
            at: tmpDir, includingPropertiesForKeys: nil) else {
            return 0
        }
        return projects.reduce(into: 0) { total, project in
            let logs = project.appendingPathComponent("logs.json")
            total += Self.countPrompts(inLogFileAt: logs, since: startOfToday)
        }
    }

    /// Counts `type: "user"`, non-slash-command records dated `>= since` in one
    /// `logs.json`. Returns 0 for a missing, unreadable, or malformed file.
    private static func countPrompts(inLogFileAt url: URL, since: Date) -> Int {
        guard
            let data = try? Data(contentsOf: url),
            let records = (try? JSONSerialization.jsonObject(with: data))
                as? [[String: Any]]
        else { return 0 }
        return records.reduce(into: 0) { count, record in
            guard (record["type"] as? String) == "user" else { return }
            if let message = record["message"] as? String,
               message.hasPrefix("/") { return }   // slash command — no API call
            guard
                let raw = record["timestamp"] as? String,
                let stamp = Self.parseTimestamp(raw),
                stamp >= since
            else { return }
            count += 1
        }
    }

    /// Parses an ISO-8601 timestamp, accepting it with or without fractional
    /// seconds (`logs.json` uses fractional seconds; tolerate either).
    static func parseTimestamp(_ string: String) -> Date? {
        if let date = isoFractional.date(from: string) { return date }
        return isoPlain.date(from: string)
    }

    private static let isoFractional: ISO8601DateFormatter = {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter
    }()

    private static let isoPlain: ISO8601DateFormatter = {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime]
        return formatter
    }()
}
