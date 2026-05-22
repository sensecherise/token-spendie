import XCTest
@testable import TokenSpendie

final class ProviderModelsTests: XCTestCase {
    private func window(_ p: Double) -> UsageWindow {
        UsageWindow(percent: p, resetsAt: Date(timeIntervalSince1970: 1_000))
    }
    private func labeled(_ label: String, _ p: Double) -> LabeledWindow {
        LabeledWindow(label: label, detail: "\(label) detail",
                      resetStyle: .countdown, window: window(p))
    }

    func testProviderIDIsRawStringCodable() throws {
        let data = try JSONEncoder().encode(ProviderID.claude)
        XCTAssertEqual(String(data: data, encoding: .utf8), "\"claude\"")
        XCTAssertEqual(try JSONDecoder().decode(ProviderID.self, from: data), .claude)
    }

    func testProviderSnapshotCodableRoundTrip() throws {
        let snapshot = ProviderSnapshot(
            id: .claude,
            plan: "Max",
            headline: labeled("Session · 5h", 47),
            windows: [labeled("Session · 5h", 47),
                      LabeledWindow(label: "Weekly · all", detail: "all models",
                                    resetStyle: .date, window: window(31))],
            fetchedAt: Date(timeIntervalSince1970: 5_000)
        )
        let data = try JSONEncoder().encode(snapshot)
        XCTAssertEqual(try JSONDecoder().decode(ProviderSnapshot.self, from: data), snapshot)
    }

    func testProviderSnapshotCodableWithNilPlan() throws {
        let snapshot = ProviderSnapshot(
            id: .claude, plan: nil,
            headline: labeled("Session · 5h", 12),
            windows: [labeled("Session · 5h", 12)],
            fetchedAt: Date(timeIntervalSince1970: 9))
        let data = try JSONEncoder().encode(snapshot)
        XCTAssertEqual(try JSONDecoder().decode(ProviderSnapshot.self, from: data), snapshot)
    }

    func testProviderUsageStateIsMutable() {
        var usage = ProviderUsage(id: .claude, displayName: "Claude",
                                  state: .loading, snapshot: nil)
        usage.state = .ok
        XCTAssertEqual(usage.state, .ok)
    }

    func testProviderUsageEquatableDistinguishesByState() {
        let a = ProviderUsage(id: .claude, displayName: "Claude", state: .loading, snapshot: nil)
        let b = ProviderUsage(id: .claude, displayName: "Claude", state: .ok, snapshot: nil)
        XCTAssertNotEqual(a, b)
    }
}
