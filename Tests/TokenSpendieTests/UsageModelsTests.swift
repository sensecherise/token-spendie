import XCTest
@testable import TokenSpendie

final class UsageModelsTests: XCTestCase {
    func testSnapshotCodableRoundTrip() throws {
        let snapshot = UsageSnapshot(
            session: UsageWindow(percent: 52, resetsAt: Date(timeIntervalSince1970: 1_000_000)),
            weekly: UsageWindow(percent: 74, resetsAt: Date(timeIntervalSince1970: 2_000_000)),
            modelWeeklies: [ModelWeekly(model: "Opus", window: UsageWindow(percent: 91, resetsAt: nil))],
            fetchedAt: Date(timeIntervalSince1970: 3_000_000)
        )
        let data = try JSONEncoder().encode(snapshot)
        let decoded = try JSONDecoder().decode(UsageSnapshot.self, from: data)
        XCTAssertEqual(snapshot, decoded)
    }

    func testLoadStateEquatable() {
        XCTAssertEqual(LoadState.error(.network), LoadState.error(.network))
        XCTAssertNotEqual(LoadState.error(.network), LoadState.error(.tokenInvalid))
    }
}
