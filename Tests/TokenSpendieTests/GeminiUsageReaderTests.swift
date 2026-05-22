import XCTest
@testable import TokenSpendie

final class GeminiUsageReaderTests: XCTestCase {

    /// A throwaway ~/.gemini directory in the temp folder. `oauth` writes a
    /// stub `oauth_creds.json`; `projects` maps a project-dir name to the
    /// records its `logs.json` should contain.
    private func makeGeminiHome(oauth: Bool = false,
                                projects: [String: [[String: Any]]] = [:]) -> URL {
        let fm = FileManager.default
        let home = fm.temporaryDirectory
            .appendingPathComponent("gemini-\(UUID().uuidString)", isDirectory: true)
        try! fm.createDirectory(at: home, withIntermediateDirectories: true)
        if oauth {
            try! Data("{}".utf8)
                .write(to: home.appendingPathComponent("oauth_creds.json"))
        }
        for (name, records) in projects {
            let dir = home.appendingPathComponent("tmp/\(name)", isDirectory: true)
            try! fm.createDirectory(at: dir, withIntermediateDirectories: true)
            let data = try! JSONSerialization.data(withJSONObject: records)
            try! data.write(to: dir.appendingPathComponent("logs.json"))
        }
        return home
    }

    /// A UTC calendar so date-boundary tests do not depend on the runner's TZ.
    private var utcCalendar: Calendar {
        var c = Calendar(identifier: .gregorian)
        c.timeZone = TimeZone(identifier: "UTC")!
        return c
    }

    func testDetectCredentialsTrueWhenOAuthFileExists() {
        let reader = GeminiUsageReader(geminiHome: makeGeminiHome(oauth: true))
        XCTAssertTrue(reader.detectCredentials())
    }

    func testDetectCredentialsFalseWhenNoOAuthFile() {
        let reader = GeminiUsageReader(geminiHome: makeGeminiHome(oauth: false))
        XCTAssertFalse(reader.detectCredentials())
    }

    func testNextLocalMidnightIsStartOfTomorrow() {
        let noon = Date(timeIntervalSince1970: 1_747_915_200) // 2025-05-22 12:00 UTC
        let reader = GeminiUsageReader(geminiHome: makeGeminiHome(),
                                       now: { noon },
                                       calendar: utcCalendar)
        // 2025-05-23 00:00 UTC
        XCTAssertEqual(reader.nextLocalMidnight(),
                       Date(timeIntervalSince1970: 1_747_958_400))
    }

    /// One `type: "user"` log record.
    private func userRecord(_ message: String, at iso: String) -> [String: Any] {
        ["sessionId": "s", "messageId": 0, "type": "user",
         "message": message, "timestamp": iso]
    }

    /// A reader whose "now" is 2025-05-22 12:00 UTC, with a UTC day boundary.
    private func readerAtNoon(_ home: URL) -> GeminiUsageReader {
        GeminiUsageReader(geminiHome: home,
                          now: { Date(timeIntervalSince1970: 1_747_915_200) },
                          calendar: utcCalendar)
    }

    func testCountsTodaysPrompts() {
        let home = makeGeminiHome(projects: ["p": [
            userRecord("hello", at: "2025-05-22T01:00:00.000Z"),
            userRecord("again", at: "2025-05-22T11:59:00.000Z"),
        ]])
        XCTAssertEqual(readerAtNoon(home).requestsToday(), 2)
    }

    func testIgnoresYesterdaysPrompts() {
        let home = makeGeminiHome(projects: ["p": [
            userRecord("old", at: "2025-05-21T23:59:00.000Z"),
            userRecord("new", at: "2025-05-22T00:00:00.000Z"),
        ]])
        XCTAssertEqual(readerAtNoon(home).requestsToday(), 1)
    }

    func testIgnoresSlashCommands() {
        let home = makeGeminiHome(projects: ["p": [
            userRecord("/stats", at: "2025-05-22T01:00:00.000Z"),
            userRecord("real prompt", at: "2025-05-22T02:00:00.000Z"),
        ]])
        XCTAssertEqual(readerAtNoon(home).requestsToday(), 1)
    }

    func testIgnoresNonUserRecordTypes() {
        let home = makeGeminiHome(projects: ["p": [
            ["type": "gemini", "message": "response",
             "timestamp": "2025-05-22T01:00:00.000Z"],
            userRecord("prompt", at: "2025-05-22T02:00:00.000Z"),
        ]])
        XCTAssertEqual(readerAtNoon(home).requestsToday(), 1)
    }

    func testParsesTimestampsWithoutFractionalSeconds() {
        let home = makeGeminiHome(projects: ["p": [
            userRecord("plain", at: "2025-05-22T03:00:00Z"),
        ]])
        XCTAssertEqual(readerAtNoon(home).requestsToday(), 1)
    }

    func testSumsAcrossProjects() {
        let home = makeGeminiHome(projects: [
            "p1": [userRecord("a", at: "2025-05-22T01:00:00.000Z")],
            "p2": [userRecord("b", at: "2025-05-22T02:00:00.000Z"),
                   userRecord("c", at: "2025-05-22T03:00:00.000Z")],
        ])
        XCTAssertEqual(readerAtNoon(home).requestsToday(), 3)
    }

    func testCorruptLogFileSkippedOthersStillCounted() {
        let home = makeGeminiHome(projects: [
            "good": [userRecord("a", at: "2025-05-22T01:00:00.000Z")],
        ])
        // Add a project whose logs.json is not valid JSON.
        let badDir = home.appendingPathComponent("tmp/bad", isDirectory: true)
        try! FileManager.default.createDirectory(at: badDir,
                                                 withIntermediateDirectories: true)
        try! Data("not json".utf8)
            .write(to: badDir.appendingPathComponent("logs.json"))
        XCTAssertEqual(readerAtNoon(home).requestsToday(), 1)
    }

    func testMissingTmpDirectoryReturnsZero() {
        let home = makeGeminiHome(oauth: true) // no tmp/ created
        XCTAssertEqual(readerAtNoon(home).requestsToday(), 0)
    }
}
