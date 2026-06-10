import Foundation

/// The `UsageProvider` for OpenAI Codex CLI. Codex exposes no usage API, so this
/// reads the rate-limit snapshot Codex records in its local session logs (via
/// `CodexUsageReader`) and maps the rolling 5-hour (`primary`) and weekly
/// (`secondary`) windows onto a generic `ProviderSnapshot`. The numbers are as
/// fresh as the user's last Codex activity — see the design doc.
struct CodexProvider: UsageProvider {
    let id: ProviderID = .codex
    let displayName: String = "Codex"

    private let reader: CodexUsageReader

    init(reader: CodexUsageReader = CodexUsageReader()) {
        self.reader = reader
    }

    func detectCredentials() -> Bool {
        reader.detectCredentials()
    }

    /// Reads the latest local rate-limit snapshot. Throws `badResponse` when
    /// credentials exist but no snapshot has been recorded yet (e.g. a fresh
    /// login that has not run a turn) so the row shows "no usable reading"
    /// rather than a misleading 0%.
    func fetchUsage() async throws -> ProviderSnapshot {
        guard let limits = reader.latestRateLimits() else {
            throw ProviderError.badResponse
        }
        return Self.convert(limits, now: reader.now())
    }

    /// Pure `CodexRateLimits` → `ProviderSnapshot` mapping. `primary` is the
    /// rolling 5-hour session window and the headline; `secondary` is the weekly
    /// window. A window's `resetsAt` is the capture time plus its
    /// `resets_in_seconds`. `used_percent` is already 0–100 and may exceed 100
    /// when over cap, matching the existing `UsageWindow.percent` convention.
    static func convert(_ limits: CodexRateLimits, now: Date) -> ProviderSnapshot {
        func labeled(_ window: CodexRateWindow?, label: String, detail: String,
                     style: ResetStyle) -> LabeledWindow? {
            guard let window else { return nil }
            let resetsAt = window.resetsInSeconds
                .map { limits.capturedAt.addingTimeInterval($0) }
            return LabeledWindow(
                label: label, detail: detail, resetStyle: style,
                window: UsageWindow(percent: window.usedPercent, resetsAt: resetsAt))
        }

        let session = labeled(limits.primary, label: "Session",
                              detail: "5-hour window", style: .countdown)
        let weekly = labeled(limits.secondary, label: "Weekly",
                             detail: "7-day window", style: .date)
        // primary is preferred as the headline; fall back to weekly when only it
        // is present. `fetchUsage` guarantees at least one window exists.
        let headline = session ?? weekly!
        let windows = [session, weekly].compactMap { $0 }
        return ProviderSnapshot(id: .codex, plan: nil, headline: headline,
                                windows: windows, fetchedAt: now,
                                note: "estimate · from local session logs")
    }
}
