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
            windows: [labeled("Session · 5h", 47), labeled("Weekly · all", 31)],
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

    func testProviderUsageCarriesStateWithoutSnapshot() {
        let usage = ProviderUsage(id: .claude, displayName: "Claude",
                                  state: .loading, snapshot: nil)
        XCTAssertEqual(usage.state, .loading)
        XCTAssertNil(usage.snapshot)
    }
}
