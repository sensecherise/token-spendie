import XCTest
@testable import TokenSpendie

final class CodexProviderTests: XCTestCase {

    /// A throwaway ~/.codex with an optional `auth.json` and an optional rollout
    /// file holding a single `rate_limits` snapshot at `2026-06-01T12:00:00Z`.
    private func makeCodexHome(auth: Bool, snapshot: Bool) -> URL {
        let fm = FileManager.default
        let home = fm.temporaryDirectory
            .appendingPathComponent("codex-\(UUID().uuidString)", isDirectory: true)
        try! fm.createDirectory(at: home, withIntermediateDirectories: true)
        if auth {
            try! Data(#"{"tokens":{}}"#.utf8)
                .write(to: home.appendingPathComponent("auth.json"))
        }
        if snapshot {
            let dir = home.appendingPathComponent("sessions/2026/06/01",
                                                  isDirectory: true)
            try! fm.createDirectory(at: dir, withIntermediateDirectories: true)
            let record: [String: Any] = [
                "timestamp": "2026-06-01T12:00:00.000Z", "type": "event_msg",
                "payload": ["type": "token_count", "rate_limits": [
                    "primary": ["used_percent": 60.0, "resets_in_seconds": 3600.0],
                    "secondary": ["used_percent": 12.0, "resets_in_seconds": 86400.0],
                ]]]
            let line = String(
                data: try! JSONSerialization.data(withJSONObject: record),
                encoding: .utf8)!
            try! Data(line.utf8).write(to: dir.appendingPathComponent("rollout-a.jsonl"))
        }
        return home
    }

    private func reader(_ home: URL) -> CodexUsageReader {
        CodexUsageReader(codexHome: home,
                         now: { Date(timeIntervalSince1970: 1_700_000_000) })
    }

    func testDetectCredentialsDelegatesToReader() {
        let present = CodexProvider(
            reader: reader(makeCodexHome(auth: true, snapshot: false)))
        let absent = CodexProvider(
            reader: reader(makeCodexHome(auth: false, snapshot: false)))
        XCTAssertTrue(present.detectCredentials())
        XCTAssertFalse(absent.detectCredentials())
    }

    func testFetchUsageBuildsCodexSnapshot() async throws {
        let provider = CodexProvider(
            reader: reader(makeCodexHome(auth: true, snapshot: true)))
        let snapshot = try await provider.fetchUsage()

        let captured = GeminiUsageReader.parseTimestamp("2026-06-01T12:00:00.000Z")!

        XCTAssertEqual(snapshot.id, .codex)
        XCTAssertNil(snapshot.plan)
        XCTAssertEqual(snapshot.note, "estimate · from local session logs")
        XCTAssertEqual(snapshot.windows.count, 2)

        XCTAssertEqual(snapshot.headline.label, "Session")
        XCTAssertEqual(snapshot.headline.resetStyle, .countdown)
        XCTAssertEqual(snapshot.headline.window.percent, 60, accuracy: 0.0001)
        XCTAssertEqual(snapshot.headline.window.resetsAt,
                       captured.addingTimeInterval(3600))

        let weekly = snapshot.windows[1]
        XCTAssertEqual(weekly.label, "Weekly")
        XCTAssertEqual(weekly.resetStyle, .date)
        XCTAssertEqual(weekly.window.percent, 12, accuracy: 0.0001)
        XCTAssertEqual(weekly.window.resetsAt, captured.addingTimeInterval(86400))

        // fetchedAt is the reader's clock, not the capture time.
        XCTAssertEqual(snapshot.fetchedAt, Date(timeIntervalSince1970: 1_700_000_000))
    }

    func testFetchUsageThrowsWhenNoSnapshotRecorded() async {
        let provider = CodexProvider(
            reader: reader(makeCodexHome(auth: true, snapshot: false)))
        do {
            _ = try await provider.fetchUsage()
            XCTFail("expected badResponse")
        } catch {
            XCTAssertEqual(error as? ProviderError, .badResponse)
        }
    }

    // MARK: - convert

    private func limits(primary: CodexRateWindow?, secondary: CodexRateWindow?)
        -> CodexRateLimits {
        CodexRateLimits(primary: primary, secondary: secondary,
                        capturedAt: Date(timeIntervalSince1970: 0))
    }

    func testConvertUsesPrimaryAsHeadline() {
        let snapshot = CodexProvider.convert(
            limits(primary: CodexRateWindow(usedPercent: 25, resetsInSeconds: 60),
                   secondary: CodexRateWindow(usedPercent: 5, resetsInSeconds: 600)),
            now: Date(timeIntervalSince1970: 0))
        XCTAssertEqual(snapshot.headline.label, "Session")
        XCTAssertEqual(snapshot.headline.window.percent, 25, accuracy: 0.0001)
    }

    func testConvertFallsBackToWeeklyHeadlineWhenNoPrimary() {
        let snapshot = CodexProvider.convert(
            limits(primary: nil,
                   secondary: CodexRateWindow(usedPercent: 5, resetsInSeconds: 600)),
            now: Date(timeIntervalSince1970: 0))
        XCTAssertEqual(snapshot.windows.count, 1)
        XCTAssertEqual(snapshot.headline.label, "Weekly")
    }

    func testConvertPassesThroughPercentOverCap() {
        let snapshot = CodexProvider.convert(
            limits(primary: CodexRateWindow(usedPercent: 142, resetsInSeconds: nil),
                   secondary: nil),
            now: Date(timeIntervalSince1970: 0))
        XCTAssertEqual(snapshot.headline.window.percent, 142, accuracy: 0.0001)
    }

    func testConvertNilResetsWhenResetSecondsAbsent() {
        let snapshot = CodexProvider.convert(
            limits(primary: CodexRateWindow(usedPercent: 10, resetsInSeconds: nil),
                   secondary: nil),
            now: Date(timeIntervalSince1970: 0))
        XCTAssertNil(snapshot.headline.window.resetsAt)
    }
}
