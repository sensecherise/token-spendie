import Foundation

/// Decodes the `/api/oauth/usage` JSON payload into a `UsageSnapshot`.
enum UsageDecoder {
    static func decode(_ data: Data, fetchedAt: Date) throws -> UsageSnapshot {
        guard let root = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] else {
            throw ProviderError.badResponse
        }

        func window(_ key: String) -> UsageWindow? {
            // A window key may be absent or an explicit JSON null; both mean "not present".
            guard let raw = root[key] as? [String: Any],
                  let utilization = (raw["utilization"] as? NSNumber)?.doubleValue else {
                return nil
            }
            // The endpoint reports utilization directly as a percentage (0–100).
            let resetsAt = (raw["resets_at"] as? String).flatMap(parseDate)
            return UsageWindow(percent: utilization, resetsAt: resetsAt)
        }

        guard let session = window("five_hour"), let weekly = window("seven_day") else {
            throw ProviderError.badResponse
        }

        var modelWeeklies: [ModelWeekly] = []
        if let opus = window("seven_day_opus") {
            modelWeeklies.append(ModelWeekly(model: "Opus", window: opus))
        }
        if let sonnet = window("seven_day_sonnet") {
            modelWeeklies.append(ModelWeekly(model: "Sonnet", window: sonnet))
        }

        return UsageSnapshot(session: session, weekly: weekly,
                             modelWeeklies: modelWeeklies, fetchedAt: fetchedAt)
    }

    /// Parses the endpoint's ISO 8601 timestamps. The endpoint emits microsecond
    /// precision (e.g. "2026-05-21T10:20:00.431249+00:00"), which `ISO8601DateFormatter`
    /// will not accept, so the fractional-seconds component is stripped before parsing.
    static func parseDate(_ string: String) -> Date? {
        let withoutFraction = string.replacingOccurrences(
            of: #"\.\d+"#, with: "", options: .regularExpression)
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime]
        return formatter.date(from: withoutFraction)
    }
}
