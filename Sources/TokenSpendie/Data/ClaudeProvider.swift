import Foundation

/// The `UsageProvider` for Claude Code. Composes the Keychain credential
/// reader and the `/api/oauth/usage` endpoint, retries once on a 401 by
/// re-reading the Keychain (Claude Code refreshes its token during normal
/// use), and converts the Claude-shaped `UsageSnapshot` into a generic
/// `ProviderSnapshot`.
struct ClaudeProvider: UsageProvider {
    let id: ProviderID = .claude
    let displayName: String = "Claude"

    private let credentials: CredentialStore
    private let endpoint: ClaudeUsageEndpoint

    init(credentials: CredentialStore = KeychainReader(),
         endpoint: ClaudeUsageEndpoint = EndpointUsageProvider()) {
        self.credentials = credentials
        self.endpoint = endpoint
    }

    func detectCredentials() -> Bool {
        credentials.credentialsExist()
    }

    func fetchUsage() async throws -> ProviderSnapshot {
        let creds = try credentials.loadCredentials()
        let usage: UsageSnapshot
        do {
            usage = try await endpoint.fetchUsage(accessToken: creds.accessToken)
        } catch ProviderError.unauthorized {
            // Re-read the Keychain once — Claude Code refreshes the token
            // during normal use — then retry.
            let refreshed = try credentials.loadCredentials()
            usage = try await endpoint.fetchUsage(accessToken: refreshed.accessToken)
        }
        return Self.convert(usage)
    }

    /// Pure `UsageSnapshot` → `ProviderSnapshot` mapping. The session window is
    /// the headline; `windows` is `[session, weekly, model-weeklies…]`.
    static func convert(_ usage: UsageSnapshot) -> ProviderSnapshot {
        let session = LabeledWindow(label: "Session", detail: "5-hour window",
                                    resetStyle: .countdown, window: usage.session)
        var windows = [session]
        windows.append(LabeledWindow(label: "Weekly", detail: "all models",
                                     resetStyle: .date, window: usage.weekly))
        for model in usage.modelWeeklies {
            windows.append(LabeledWindow(label: "Weekly · \(model.model)",
                                         detail: "\(model.model) only",
                                         resetStyle: .date, window: model.window))
        }
        return ProviderSnapshot(id: .claude, plan: nil, headline: session,
                                windows: windows, fetchedAt: usage.fetchedAt)
    }
}
