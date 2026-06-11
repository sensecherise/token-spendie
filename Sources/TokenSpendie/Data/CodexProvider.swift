import Foundation

/// The `UsageProvider` for OpenAI Codex. Reads the CLI's OAuth tokens from
/// `auth.json`, refreshes them when stale (Codex CLI's own 8-day policy) or on
/// a 401, writes refreshed tokens back (refresh tokens rotate — discarding the
/// new one would invalidate the CLI's login), and calls the `wham/usage`
/// endpoint Codex itself uses.
struct CodexProvider: UsageProvider {
    let id: ProviderID = .codex
    let displayName: String = "Codex"

    private static let usageURL = URL(string: "https://chatgpt.com/backend-api/wham/usage")!
    private static let refreshURL = URL(string: "https://auth.openai.com/oauth/token")!
    /// Codex CLI's public OAuth client id (from codex-rs source).
    private static let clientID = "app_EMoamEEZ73f0CkXaXp7hrann"

    private let store: CodexCredentialsStore
    private let transport: HTTPTransport
    private let now: () -> Date

    init(store: CodexCredentialsStore = CodexCredentialsStore(fileURL: CodexCredentialsStore.defaultURL()),
         transport: @escaping HTTPTransport = DefaultTransport.shared,
         now: @escaping () -> Date = Date.init) {
        self.store = store
        self.transport = transport
        self.now = now
    }

    func detectCredentials() -> Bool {
        store.detect()
    }

    func fetchUsage() async throws -> ProviderSnapshot {
        var creds = try store.load()
        if creds.needsRefresh(now: now()) {
            creds = try await refreshAndSave(creds)
        }
        do {
            return try await fetchSnapshot(with: creds)
        } catch ProviderError.unauthorized {
            creds = try await refreshAndSave(creds)
            do {
                return try await fetchSnapshot(with: creds)
            } catch ProviderError.unauthorized {
                throw ProviderError.reauthRequired
            }
        }
    }

    // MARK: - Usage endpoint

    private func fetchSnapshot(with creds: CodexCredentials) async throws -> ProviderSnapshot {
        var request = URLRequest(url: Self.usageURL)
        request.httpMethod = "GET"
        request.setValue("Bearer \(creds.accessToken)", forHTTPHeaderField: "Authorization")
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        request.setValue("TokenSpendie/1.0", forHTTPHeaderField: "User-Agent")
        if let accountId = creds.accountId {
            request.setValue(accountId, forHTTPHeaderField: "ChatGPT-Account-Id")
        }

        let (data, response) = try await transport(request)
        switch response.statusCode {
        case 200:
            return try Self.decode(data, fetchedAt: now())
        // 403 is treated like 401 (refresh + retry): wham/usage answers 403
        // for some expired-auth states — deliberate deviation from the Claude
        // endpoint, which only refreshes on 401.
        case 401, 403:
            throw ProviderError.unauthorized
        case 429:
            let retryAfter = response.value(forHTTPHeaderField: "Retry-After")
                .flatMap(TimeInterval.init)
            throw ProviderError.rateLimited(retryAfter: retryAfter)
        default:
            throw ProviderError.badResponse
        }
    }

    // MARK: - Token refresh

    /// One refresh round-trip + write-back. Any rejection means the refresh
    /// token is dead → `reauthRequired`.
    private func refreshAndSave(_ creds: CodexCredentials) async throws -> CodexCredentials {
        var request = URLRequest(url: Self.refreshURL)
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = try JSONSerialization.data(withJSONObject: [
            "client_id": Self.clientID,
            "grant_type": "refresh_token",
            "refresh_token": creds.refreshToken,
            "scope": "openid profile email",
        ])

        let (data, response) = try await transport(request)
        guard response.statusCode == 200,
              let json = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] else {
            // 400/401/anything-not-200: the stored refresh token is unusable.
            throw ProviderError.reauthRequired
        }
        // Fields absent from the response keep their stored values.
        let refreshed = CodexCredentials(
            accessToken: json["access_token"] as? String ?? creds.accessToken,
            refreshToken: json["refresh_token"] as? String ?? creds.refreshToken,
            idToken: json["id_token"] as? String ?? creds.idToken,
            accountId: creds.accountId,
            lastRefresh: now())
        try store.save(refreshed, refreshedAt: now())
        return refreshed
    }

    // MARK: - Decoding / mapping

    /// Decodes the `wham/usage` payload straight into a `ProviderSnapshot`.
    /// `primary_window` (≈5 h) is the headline; either window may be absent;
    /// neither present → `badResponse`. `credits` is ignored.
    static func decode(_ data: Data, fetchedAt: Date) throws -> ProviderSnapshot {
        guard let root = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] else {
            throw ProviderError.badResponse
        }
        let rateLimit = root["rate_limit"] as? [String: Any] ?? [:]

        func window(_ key: String) -> (window: UsageWindow, seconds: Int?)? {
            guard let raw = rateLimit[key] as? [String: Any],
                  let used = (raw["used_percent"] as? NSNumber)?.doubleValue else {
                return nil
            }
            let resetsAt = (raw["reset_at"] as? NSNumber)
                .map { Date(timeIntervalSince1970: $0.doubleValue) }
            let seconds = (raw["limit_window_seconds"] as? NSNumber)?.intValue
            return (UsageWindow(percent: used, resetsAt: resetsAt), seconds)
        }

        var windows: [LabeledWindow] = []
        if let primary = window("primary_window") {
            let hours = max(1, ((primary.seconds ?? 18000) + 1800) / 3600)
            windows.append(LabeledWindow(label: "Session · \(hours)h",
                                         detail: "\(hours)-hour window",
                                         resetStyle: .countdown,
                                         window: primary.window))
        }
        if let secondary = window("secondary_window") {
            let days = max(1, ((secondary.seconds ?? 604800) + 43200) / 86400)
            windows.append(LabeledWindow(label: days == 7 ? "Weekly" : "\(days)-day",
                                         detail: "\(days)-day window",
                                         resetStyle: .date,
                                         window: secondary.window))
        }
        guard let headline = windows.first else { throw ProviderError.badResponse }

        let plan = (root["plan_type"] as? String).flatMap { raw -> String? in
            guard !raw.isEmpty else { return nil }
            return raw.split(separator: "_").map(\.capitalized).joined(separator: " ")
        }
        return ProviderSnapshot(id: .codex, plan: plan, headline: headline,
                                windows: windows, fetchedAt: fetchedAt)
    }
}
