import XCTest
@testable import ClaudeUsageWidget

final class UsageDecoderTests: XCTestCase {
    let fetchedAt = Date(timeIntervalSince1970: 1_700_000_000)

    func testDecodesPercentUtilization() throws {
        // Real endpoint payload shape: utilization is a percentage; resets_at has microseconds.
        let json = """
        {
          "five_hour":      {"utilization": 44.0, "resets_at": "2026-05-21T10:20:00.431249+00:00"},
          "seven_day":      {"utilization": 8.0,  "resets_at": "2026-05-26T08:00:00.431273+00:00"},
          "seven_day_opus": {"utilization": 91.0, "resets_at": "2026-05-26T08:00:00.431280+00:00"}
        }
        """
        let snapshot = try UsageDecoder.decode(Data(json.utf8), fetchedAt: fetchedAt)
        XCTAssertEqual(snapshot.session.percent, 44, accuracy: 0.001)
        XCTAssertEqual(snapshot.weekly.percent, 8, accuracy: 0.001)
        XCTAssertEqual(snapshot.modelWeeklies.count, 1)
        XCTAssertEqual(snapshot.modelWeeklies.first?.model, "Opus")
        XCTAssertEqual(snapshot.modelWeeklies.first?.window.percent ?? 0, 91, accuracy: 0.001)
        XCTAssertEqual(snapshot.fetchedAt, fetchedAt)
    }

    func testParsesMicrosecondResetTime() throws {
        let json = #"{"five_hour":{"utilization":1,"resets_at":"2026-05-21T10:20:00.431249+00:00"},"seven_day":{"utilization":1}}"#
        let snapshot = try UsageDecoder.decode(Data(json.utf8), fetchedAt: fetchedAt)
        let resetsAt = try XCTUnwrap(snapshot.session.resetsAt)
        var utc = Calendar(identifier: .gregorian)
        utc.timeZone = TimeZone(identifier: "UTC")!
        let parts = utc.dateComponents([.year, .month, .day, .hour, .minute], from: resetsAt)
        XCTAssertEqual(parts.year, 2026)
        XCTAssertEqual(parts.month, 5)
        XCTAssertEqual(parts.day, 21)
        XCTAssertEqual(parts.hour, 10)
        XCTAssertEqual(parts.minute, 20)
    }

    func testNullWindowIsOmitted() throws {
        // The endpoint sends explicit JSON null for windows that do not apply.
        let json = """
        {"five_hour":{"utilization":5},"seven_day":{"utilization":6},"seven_day_opus":null,"seven_day_sonnet":{"utilization":7}}
        """
        let snapshot = try UsageDecoder.decode(Data(json.utf8), fetchedAt: fetchedAt)
        XCTAssertEqual(snapshot.modelWeeklies.map(\.model), ["Sonnet"])
    }

    func testDecodesOpusAndSonnetWeekly() throws {
        let json = """
        {
          "five_hour": {"utilization": 1}, "seven_day": {"utilization": 2},
          "seven_day_opus": {"utilization": 3}, "seven_day_sonnet": {"utilization": 4}
        }
        """
        let snapshot = try UsageDecoder.decode(Data(json.utf8), fetchedAt: fetchedAt)
        XCTAssertEqual(snapshot.modelWeeklies.map(\.model), ["Opus", "Sonnet"])
    }

    func testMissingRequiredWindowThrowsBadResponse() {
        let json = #"{"five_hour": {"utilization": 5}}"#
        XCTAssertThrowsError(try UsageDecoder.decode(Data(json.utf8), fetchedAt: fetchedAt)) { error in
            XCTAssertEqual(error as? ProviderError, .badResponse)
        }
    }

    func testGarbageThrowsBadResponse() {
        XCTAssertThrowsError(try UsageDecoder.decode(Data("nonsense".utf8), fetchedAt: fetchedAt)) { error in
            XCTAssertEqual(error as? ProviderError, .badResponse)
        }
    }
}
