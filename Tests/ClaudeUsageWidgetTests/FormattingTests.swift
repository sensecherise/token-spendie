import XCTest
@testable import ClaudeUsageWidget

final class FormattingTests: XCTestCase {
    func testLevelTiers() {
        XCTAssertEqual(UsageLevel.forPercent(0), .calm)
        XCTAssertEqual(UsageLevel.forPercent(69.9), .calm)
        XCTAssertEqual(UsageLevel.forPercent(70), .warn)
        XCTAssertEqual(UsageLevel.forPercent(89.9), .warn)
        XCTAssertEqual(UsageLevel.forPercent(90), .hot)
        XCTAssertEqual(UsageLevel.forPercent(150), .hot)
    }

    func testResetCountdown() {
        let now = Date(timeIntervalSince1970: 0)
        let in2h47m = Date(timeIntervalSince1970: 2 * 3600 + 47 * 60)
        XCTAssertEqual(Formatting.resetCountdown(to: in2h47m, now: now), "resets in 2h 47m")
        let in40m = Date(timeIntervalSince1970: 40 * 60)
        XCTAssertEqual(Formatting.resetCountdown(to: in40m, now: now), "resets in 40m")
        XCTAssertEqual(Formatting.resetCountdown(to: now, now: now), "resetting now")
        XCTAssertEqual(Formatting.resetCountdown(to: nil, now: now), "")
    }

    func testUpdatedAgo() {
        let now = Date(timeIntervalSince1970: 1000)
        XCTAssertEqual(Formatting.updatedAgo(Date(timeIntervalSince1970: 990), now: now), "updated 10s ago")
        XCTAssertEqual(Formatting.updatedAgo(Date(timeIntervalSince1970: 700), now: now), "updated 5m ago")
        XCTAssertEqual(Formatting.updatedAgo(now, now: now), "updated just now")
    }
}
