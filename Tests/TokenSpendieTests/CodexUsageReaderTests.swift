import XCTest
@testable import TokenSpendie

final class CodexUsageReaderTests: XCTestCase {

    /// A throwaway ~/.codex directory in the temp folder. `auth` writes a stub
    /// `auth.json`; `files` maps a session-file path (relative to `sessions/`)
    /// to the rollout records its `.jsonl` should contain, one JSON per line.
    private func makeCodexHome(auth: Bool = false,
                               files: [String: [[String: Any]]] = [:]) -> URL {
        let fm = FileManager.default
        let home = fm.temporaryDirectory
            .appendingPathComponent("codex-\(UUID().uuidString)", isDirectory: true)
        try! fm.createDirectory(at: home, withIntermediateDirectories: true)
        if auth {
            try! Data(#"{"tokens":{}}"#.utf8)
                .write(to: home.appendingPathComponent("auth.json"))
        }
        for (relativePath, records) in files {
            let url = home.appendingPathComponent("sessions/\(relativePath)")
            try! fm.createDirectory(at: url.deletingLastPathComponent(),
                                    withIntermediateDirectories: true)
            let lines = records.map {
                String(data: try! JSONSerialization.data(withJSONObject: $0),
                       encoding: .utf8)!
            }
            try! Data(lines.joined(separator: "\n").utf8).write(to: url)
        }
        return home
    }

    /// A `token_count` rollout record carrying a `rate_limits` snapshot, shaped
    /// like the modern Codex CLI (`rate_limits` nested under `payload`).
    private func event(primary: Double?, secondary: Double?,
                       resetsP: Double? = nil, resetsS: Double? = nil,
                       at iso: String) -> [String: Any] {
        func window(_ percent: Double?, _ resets: Double?) -> [String: Any]? {
            guard let percent else { return nil }
            var w: [String: Any] = ["used_percent": percent]
            if let resets { w["resets_in_seconds"] = resets }
            return w
        }
        var limits: [String: Any] = [:]
        if let p = window(primary, resetsP) { limits["primary"] = p }
        if let s = window(secondary, resetsS) { limits["secondary"] = s }
        return ["timestamp": iso, "type": "event_msg",
                "payload": ["type": "token_count", "rate_limits": limits]]
    }

    func testDetectCredentialsTrueWhenAuthFileExists() {
        let reader = CodexUsageReader(codexHome: makeCodexHome(auth: true))
        XCTAssertTrue(reader.detectCredentials())
    }

    func testDetectCredentialsFalseWhenNoAuthFile() {
        let reader = CodexUsageReader(codexHome: makeCodexHome(auth: false))
        XCTAssertFalse(reader.detectCredentials())
    }

    func testNoSessionsDirectoryReturnsNil() {
        let reader = CodexUsageReader(codexHome: makeCodexHome(auth: true))
        XCTAssertNil(reader.latestRateLimits())
    }

    func testReadsSingleSnapshot() {
        let home = makeCodexHome(files: ["2026/06/01/rollout-a.jsonl": [
            event(primary: 42.5, secondary: 10, resetsP: 3600, resetsS: 86400,
                  at: "2026-06-01T12:00:00.000Z"),
        ]])
        let limits = CodexUsageReader(codexHome: home).latestRateLimits()
        XCTAssertEqual(limits?.primary?.usedPercent, 42.5)
        XCTAssertEqual(limits?.primary?.resetsInSeconds, 3600)
        XCTAssertEqual(limits?.secondary?.usedPercent, 10)
        XCTAssertEqual(limits?.secondary?.resetsInSeconds, 86400)
        XCTAssertEqual(limits?.capturedAt,
                       GeminiUsageReader.parseTimestamp("2026-06-01T12:00:00.000Z"))
    }

    func testPicksLatestSnapshotByTimestampWithinAFile() {
        let home = makeCodexHome(files: ["2026/06/01/rollout-a.jsonl": [
            event(primary: 10, secondary: 1, at: "2026-06-01T09:00:00.000Z"),
            event(primary: 80, secondary: 9, at: "2026-06-01T15:00:00.000Z"),
            event(primary: 50, secondary: 5, at: "2026-06-01T12:00:00.000Z"),
        ]])
        XCTAssertEqual(
            CodexUsageReader(codexHome: home).latestRateLimits()?.primary?.usedPercent, 80)
    }

    func testPicksLatestSnapshotAcrossFiles() {
        let home = makeCodexHome(files: [
            "2026/06/01/rollout-old.jsonl": [
                event(primary: 30, secondary: 3, at: "2026-06-01T08:00:00.000Z")],
            "2026/06/02/rollout-new.jsonl": [
                event(primary: 70, secondary: 7, at: "2026-06-02T08:00:00.000Z")],
        ])
        XCTAssertEqual(
            CodexUsageReader(codexHome: home).latestRateLimits()?.primary?.usedPercent, 70)
    }

    func testNewerFileWithoutRateLimitsDoesNotShadowOlderData() {
        // A fresh session whose only token_count event has null rate_limits
        // (e.g. exec mode) must not hide the real reading from an earlier file.
        let home = makeCodexHome(files: [
            "2026/06/01/rollout-real.jsonl": [
                event(primary: 55, secondary: 5, at: "2026-06-01T08:00:00.000Z")],
            "2026/06/02/rollout-empty.jsonl": [
                ["timestamp": "2026-06-02T08:00:00.000Z", "type": "event_msg",
                 "payload": ["type": "token_count", "rate_limits": NSNull()]]],
        ])
        XCTAssertEqual(
            CodexUsageReader(codexHome: home).latestRateLimits()?.primary?.usedPercent, 55)
    }

    func testIgnoresRecordsWithoutRateLimits() {
        let home = makeCodexHome(files: ["2026/06/01/rollout-a.jsonl": [
            ["timestamp": "2026-06-01T09:00:00.000Z", "type": "event_msg",
             "payload": ["type": "agent_message", "message": "hi"]],
            event(primary: 33, secondary: 3, at: "2026-06-01T10:00:00.000Z"),
        ]])
        XCTAssertEqual(
            CodexUsageReader(codexHome: home).latestRateLimits()?.primary?.usedPercent, 33)
    }

    func testAcceptsTopLevelRateLimitsShape() {
        // Older Codex builds placed rate_limits at the record's top level.
        let home = makeCodexHome(files: ["2026/06/01/rollout-a.jsonl": [
            ["timestamp": "2026-06-01T10:00:00.000Z",
             "rate_limits": ["primary": ["used_percent": 21.0]]],
        ]])
        XCTAssertEqual(
            CodexUsageReader(codexHome: home).latestRateLimits()?.primary?.usedPercent, 21)
    }

    func testCorruptLineSkippedOthersStillRead() {
        let home = makeCodexHome(files: ["2026/06/01/rollout-a.jsonl": [
            event(primary: 44, secondary: 4, at: "2026-06-01T10:00:00.000Z"),
        ]])
        // Append a non-JSON line to the file.
        let url = home.appendingPathComponent("sessions/2026/06/01/rollout-a.jsonl")
        let existing = try! String(contentsOf: url)
        try! Data((existing + "\nnot json").utf8).write(to: url)
        XCTAssertEqual(
            CodexUsageReader(codexHome: home).latestRateLimits()?.primary?.usedPercent, 44)
    }

    func testWindowMissingPercentIsIgnored() {
        // A primary with no used_percent yields no primary window.
        let home = makeCodexHome(files: ["2026/06/01/rollout-a.jsonl": [
            event(primary: nil, secondary: 6, at: "2026-06-01T10:00:00.000Z"),
        ]])
        let limits = CodexUsageReader(codexHome: home).latestRateLimits()
        XCTAssertNil(limits?.primary)
        XCTAssertEqual(limits?.secondary?.usedPercent, 6)
    }
}
