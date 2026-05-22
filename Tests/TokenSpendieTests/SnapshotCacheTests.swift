import XCTest
@testable import TokenSpendie

final class SnapshotCacheTests: XCTestCase {
    private func tempURL() -> URL {
        FileManager.default.temporaryDirectory
            .appendingPathComponent("cache-\(UUID().uuidString).json")
    }

    private let sample = ProviderSnapshot(
        id: .claude,
        plan: "Max",
        headline: LabeledWindow(label: "Session · 5h", detail: "5-hour window",
                                resetStyle: .countdown,
                                window: UsageWindow(percent: 30, resetsAt: nil)),
        windows: [LabeledWindow(label: "Session · 5h", detail: "5-hour window",
                                resetStyle: .countdown,
                                window: UsageWindow(percent: 30, resetsAt: nil))],
        fetchedAt: Date(timeIntervalSince1970: 555))

    func testLoadReturnsNilWhenAbsent() {
        let cache = SnapshotCache(fileURL: tempURL())
        XCTAssertNil(cache.load())
    }

    func testSaveThenLoadRoundTrips() {
        let url = tempURL()
        defer { try? FileManager.default.removeItem(at: url) }
        let cache = SnapshotCache(fileURL: url)
        cache.save(sample)
        XCTAssertEqual(cache.load(), sample)
    }

    func testLoadReturnsNilForCorruptFile() throws {
        let url = tempURL()
        defer { try? FileManager.default.removeItem(at: url) }
        try Data("corrupt".utf8).write(to: url)
        XCTAssertNil(SnapshotCache(fileURL: url).load())
    }

    func testDefaultURLIsPerProvider() {
        let claude = SnapshotCache.defaultURL(for: .claude)
        XCTAssertEqual(claude.lastPathComponent, "snapshot-claude.json")
    }
}
