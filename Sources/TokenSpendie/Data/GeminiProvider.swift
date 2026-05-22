import Foundation

/// The `UsageProvider` for Gemini CLI. Gemini exposes no usage API, so this
/// counts today's prompts from the CLI's local `logs.json` files (via
/// `GeminiUsageReader`) and reports them against the free-tier 1000/day quota.
/// The count is an approximate lower bound — see the design doc.
struct GeminiProvider: UsageProvider {
    let id: ProviderID = .gemini
    let displayName: String = "Gemini"

    /// The free-tier daily request quota the count is shown against.
    static let dailyQuota = 1000

    private let reader: GeminiUsageReader

    init(reader: GeminiUsageReader = GeminiUsageReader()) {
        self.reader = reader
    }

    func detectCredentials() -> Bool {
        reader.detectCredentials()
    }

    /// Reads the local logs and builds the snapshot. Never throws — there is no
    /// network or token, and a file-read failure simply counts as 0.
    func fetchUsage() async throws -> ProviderSnapshot {
        Self.convert(count: reader.requestsToday(),
                     resetsAt: reader.nextLocalMidnight(),
                     now: reader.now())
    }

    /// Pure `count` → `ProviderSnapshot` mapping. One `Daily` window; the
    /// percent may exceed 100 if the user is over the free-tier cap, matching
    /// the existing `UsageWindow.percent` convention.
    static func convert(count: Int, resetsAt: Date, now: Date) -> ProviderSnapshot {
        let percent = Double(count) / Double(dailyQuota) * 100
        let window = UsageWindow(percent: percent, resetsAt: resetsAt)
        let daily = LabeledWindow(label: "Daily",
                                  detail: "≈\(count) of \(dailyQuota) requests",
                                  resetStyle: .countdown,
                                  window: window)
        return ProviderSnapshot(id: .gemini, plan: nil, headline: daily,
                                windows: [daily], fetchedAt: now,
                                note: "estimate · counted from local logs")
    }
}
