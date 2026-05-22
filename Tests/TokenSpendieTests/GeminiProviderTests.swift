import XCTest
@testable import TokenSpendie

final class GeminiProviderTests: XCTestCase {

    private var utcCalendar: Calendar {
        var c = Calendar(identifier: .gregorian)
        c.timeZone = TimeZone(identifier: "UTC")!
        return c
    }

    /// A ~/.gemini dir with `oauth` optionally present and one project whose
    /// `logs.json` holds `promptCount` user prompts dated today.
    private func makeGeminiHome(oauth: Bool, promptCount: Int) -> URL {
        let fm = FileManager.default
        let home = fm.temporaryDirectory
            .appendingPathComponent("gemini-\(UUID().uuidString)", isDirectory: true)
        try! fm.createDirectory(at: home, withIntermediateDirectories: true)
        if oauth {
            try! Data("{}".utf8)
                .write(to: home.appendingPathComponent("oauth_creds.json"))
        }
        let dir = home.appendingPathComponent("tmp/p", isDirectory: true)
        try! fm.createDirectory(at: dir, withIntermediateDirectories: true)
        let records: [[String: Any]] = (0..<promptCount).map { i in
            ["type": "user", "message": "prompt \(i)",
             "timestamp": "2025-05-22T0\(i % 9):00:00.000Z"]
        }
        let data = try! JSONSerialization.data(withJSONObject: records)
        try! data.write(to: dir.appendingPathComponent("logs.json"))
        return home
    }

    /// 2025-05-22 12:00 UTC, UTC day boundary.
    private func reader(_ home: URL) -> GeminiUsageReader {
        GeminiUsageReader(geminiHome: home,
                          now: { Date(timeIntervalSince1970: 1_747_915_200) },
                          calendar: utcCalendar)
    }

    func testDetectCredentialsDelegatesToReader() {
        let present = GeminiProvider(
            reader: reader(makeGeminiHome(oauth: true, promptCount: 0)))
        let absent = GeminiProvider(
            reader: reader(makeGeminiHome(oauth: false, promptCount: 0)))
        XCTAssertTrue(present.detectCredentials())
        XCTAssertFalse(absent.detectCredentials())
    }

    func testFetchUsageProducesGeminiDailySnapshot() async throws {
        let provider = GeminiProvider(
            reader: reader(makeGeminiHome(oauth: true, promptCount: 3)))
        let snapshot = try await provider.fetchUsage()

        XCTAssertEqual(snapshot.id, .gemini)
        XCTAssertNil(snapshot.plan)
        XCTAssertEqual(snapshot.windows.count, 1)
        XCTAssertEqual(snapshot.headline.label, "Daily")
        XCTAssertEqual(snapshot.headline.resetStyle, .countdown)
        XCTAssertEqual(snapshot.headline.window.percent, 0.3, accuracy: 0.0001)
        // resetsAt is the next local midnight: 2025-05-23 00:00 UTC.
        XCTAssertEqual(snapshot.headline.window.resetsAt,
                       Date(timeIntervalSince1970: 1_747_958_400))
        // fetchedAt is the reader's clock.
        XCTAssertEqual(snapshot.fetchedAt,
                       Date(timeIntervalSince1970: 1_747_915_200))
    }

    func testConvertPercentMath() {
        let reset = Date(timeIntervalSince1970: 0)
        let now = Date(timeIntervalSince1970: 0)
        XCTAssertEqual(
            GeminiProvider.convert(count: 0, resetsAt: reset, now: now)
                .headline.window.percent, 0, accuracy: 0.0001)
        XCTAssertEqual(
            GeminiProvider.convert(count: 500, resetsAt: reset, now: now)
                .headline.window.percent, 50, accuracy: 0.0001)
        // Over the free-tier cap — percent exceeds 100, like the Claude model.
        XCTAssertEqual(
            GeminiProvider.convert(count: 1500, resetsAt: reset, now: now)
                .headline.window.percent, 150, accuracy: 0.0001)
    }

    func testConvertDetailString() {
        let snapshot = GeminiProvider.convert(
            count: 420,
            resetsAt: Date(timeIntervalSince1970: 0),
            now: Date(timeIntervalSince1970: 0))
        XCTAssertEqual(snapshot.headline.detail, "≈420 of 1000 requests")
    }

    func testConvertMarksSnapshotAsAnEstimate() {
        let snapshot = GeminiProvider.convert(
            count: 1,
            resetsAt: Date(timeIntervalSince1970: 0),
            now: Date(timeIntervalSince1970: 0))
        XCTAssertEqual(snapshot.note, "estimate · counted from local logs")
    }
}
