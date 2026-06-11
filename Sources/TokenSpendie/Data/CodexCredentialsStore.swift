import Foundation

/// Codex CLI's OAuth token set, as stored in `auth.json`.
struct CodexCredentials: Equatable {
    let accessToken: String
    let refreshToken: String
    let idToken: String?
    let accountId: String?
    let lastRefresh: Date?

    /// Codex CLI's own policy: refresh when `last_refresh` is older than
    /// 8 days (or missing).
    func needsRefresh(now: Date) -> Bool {
        guard let lastRefresh else { return true }
        return now.timeIntervalSince(lastRefresh) > 8 * 86400
    }
}

/// Reads and (after a token refresh) rewrites Codex CLI's `auth.json`.
/// Write-back preserves every field this app does not own — the file belongs
/// to Codex CLI; we only rotate the token set, exactly like the CLI itself.
struct CodexCredentialsStore {
    let fileURL: URL

    /// `$CODEX_HOME/auth.json` when set, else `~/.codex/auth.json`.
    static func defaultURL(
        environment: [String: String] = ProcessInfo.processInfo.environment,
        home: URL = FileManager.default.homeDirectoryForCurrentUser) -> URL {
        if let codexHome = environment["CODEX_HOME"], !codexHome.isEmpty {
            return URL(fileURLWithPath: codexHome).appendingPathComponent("auth.json")
        }
        return home.appendingPathComponent(".codex", isDirectory: true)
            .appendingPathComponent("auth.json")
    }

    /// True when `auth.json` holds a non-empty OAuth access token. API-key-only
    /// files are not detected — the usage endpoint needs the ChatGPT-plan OAuth
    /// token. Cheap (one small file read), never prompts.
    func detect() -> Bool {
        guard let creds = try? load() else { return false }
        return !creds.accessToken.isEmpty
    }

    /// Loads the token set. Any missing/malformed state maps to
    /// `ProviderError.reauthRequired` — by the time `load()` runs the provider
    /// was detected, so a broken file means "log in again".
    func load() throws -> CodexCredentials {
        guard let data = try? Data(contentsOf: fileURL),
              let root = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any],
              let tokens = root["tokens"] as? [String: Any],
              let accessToken = tokens["access_token"] as? String,
              let refreshToken = tokens["refresh_token"] as? String else {
            throw ProviderError.reauthRequired
        }
        let lastRefresh = (root["last_refresh"] as? String)
            .flatMap(UsageDecoder.parseDate)
        return CodexCredentials(accessToken: accessToken,
                                refreshToken: refreshToken,
                                idToken: tokens["id_token"] as? String,
                                accountId: tokens["account_id"] as? String,
                                lastRefresh: lastRefresh)
    }

    /// Atomically rewrites `auth.json` with the refreshed token set, keeping
    /// all fields we do not own (`OPENAI_API_KEY`, unknown future fields).
    func save(_ creds: CodexCredentials, refreshedAt: Date) throws {
        var root: [String: Any] = [:]
        if let data = try? Data(contentsOf: fileURL),
           let existing = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] {
            root = existing
        }
        var tokens = (root["tokens"] as? [String: Any]) ?? [:]
        tokens["access_token"] = creds.accessToken
        tokens["refresh_token"] = creds.refreshToken
        if let idToken = creds.idToken { tokens["id_token"] = idToken }
        if let accountId = creds.accountId { tokens["account_id"] = accountId }
        root["tokens"] = tokens
        root["last_refresh"] = Self.isoFormatter.string(from: refreshedAt)

        let data = try JSONSerialization.data(
            withJSONObject: root, options: [.prettyPrinted, .sortedKeys])
        try data.write(to: fileURL, options: .atomic)
    }

    private static let isoFormatter: ISO8601DateFormatter = {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime]
        return formatter
    }()
}
