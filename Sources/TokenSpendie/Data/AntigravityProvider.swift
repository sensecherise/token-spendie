import Foundation

/// The `UsageProvider` for Google Antigravity. Best-effort: reads per-model
/// quota from the language server the Antigravity IDE / `agy` CLI runs
/// locally (see `AntigravityProbe`). No credentials are read or stored.
///
/// Row semantics: the row exists while the process is running OR a cached
/// snapshot is newer than 7 days, so quitting Antigravity leaves a `stale`
/// row instead of dropping it.
struct AntigravityProvider: UsageProvider {
    let id: ProviderID = .antigravity
    let displayName: String = "Antigravity"

    /// How long a cached snapshot keeps the row alive without a process.
    static let cacheRowTTL: TimeInterval = 7 * 86400

    private let probe: AntigravityProbing
    /// Read-only handle on the same cache file `UsageStore` writes for this
    /// provider — used solely for the row-TTL check above.
    private let cache: SnapshotCache
    private let now: () -> Date

    init(probe: AntigravityProbing = AntigravityProbe(),
         cache: SnapshotCache = SnapshotCache(fileURL: SnapshotCache.defaultURL(for: .antigravity)),
         now: @escaping () -> Date = Date.init) {
        self.probe = probe
        self.cache = cache
        self.now = now
    }

    func detectCredentials() -> Bool {
        if probe.isProcessPresent() { return true }
        guard let cached = cache.load() else { return false }
        return now().timeIntervalSince(cached.fetchedAt) < Self.cacheRowTTL
    }

    func fetchUsage() async throws -> ProviderSnapshot {
        let quota = try await probe.fetchQuotaData()
        return try Self.decode(quota, fetchedAt: now())
    }

    // MARK: - Decoding

    /// Maps a quota payload to one window per model config that reports a
    /// `remainingFraction`. Headline = the highest-used window.
    static func decode(_ quota: AntigravityQuotaData, fetchedAt: Date) throws -> ProviderSnapshot {
        guard let root = (try? JSONSerialization.jsonObject(with: quota.body)) as? [String: Any] else {
            throw ProviderError.badResponse
        }
        // A non-zero / non-"ok" top-level `code` is an RPC error envelope.
        if let code = root["code"] {
            let okCodes: Set<String> = ["0", "ok", "success"]
            let text = String(describing: code).lowercased()
            if !okCodes.contains(text) { throw ProviderError.badResponse }
        }

        let configs: [[String: Any]]
        let plan: String?
        switch quota.source {
        case .userStatus:
            guard let userStatus = root["userStatus"] as? [String: Any] else {
                throw ProviderError.badResponse
            }
            let configData = userStatus["cascadeModelConfigData"] as? [String: Any]
            configs = configData?["clientModelConfigs"] as? [[String: Any]] ?? []
            plan = Self.planName(in: userStatus)
        case .commandModelConfigs:
            configs = root["clientModelConfigs"] as? [[String: Any]] ?? []
            plan = nil
        }

        let windows: [LabeledWindow] = configs.compactMap { config in
            guard let label = config["label"] as? String,
                  let quotaInfo = config["quotaInfo"] as? [String: Any],
                  let remaining = (quotaInfo["remainingFraction"] as? NSNumber)?.doubleValue else {
                return nil
            }
            let percent = min(max((1 - remaining) * 100, 0), 100)
            let resetsAt = (quotaInfo["resetTime"] as? String).flatMap(Self.parseResetTime)
            return LabeledWindow(label: label, detail: "model quota",
                                 resetStyle: .countdown,
                                 window: UsageWindow(percent: percent, resetsAt: resetsAt))
        }
        guard let headline = windows.max(by: { $0.window.percent < $1.window.percent }) else {
            throw ProviderError.badResponse
        }
        return ProviderSnapshot(id: .antigravity, plan: plan, headline: headline,
                                windows: windows, fetchedAt: fetchedAt,
                                note: "live while Antigravity runs")
    }

    /// `userTier.name` is the real subscription tier; `planInfo` names are the
    /// fallback chain (CodexBar-verified preference order).
    private static func planName(in userStatus: [String: Any]) -> String? {
        if let tier = userStatus["userTier"] as? [String: Any],
           let name = (tier["name"] as? String)?.trimmingCharacters(in: .whitespaces),
           !name.isEmpty {
            return name
        }
        guard let planStatus = userStatus["planStatus"] as? [String: Any],
              let planInfo = planStatus["planInfo"] as? [String: Any] else { return nil }
        for key in ["planDisplayName", "displayName", "productName", "planName", "planShortName"] {
            if let value = (planInfo[key] as? String)?.trimmingCharacters(in: .whitespaces),
               !value.isEmpty {
                return value
            }
        }
        return nil
    }

    /// `resetTime` arrives as ISO-8601 or epoch-seconds-as-string.
    private static func parseResetTime(_ value: String) -> Date? {
        if let date = UsageDecoder.parseDate(value) { return date }
        if let seconds = Double(value) { return Date(timeIntervalSince1970: seconds) }
        return nil
    }
}
