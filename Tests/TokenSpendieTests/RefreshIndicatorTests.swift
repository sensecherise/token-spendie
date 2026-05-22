import XCTest
@testable import TokenSpendie

final class RefreshIndicatorTests: XCTestCase {

    // MARK: - FetchingEllipsis

    private func dots(_ seconds: TimeInterval) -> Int {
        FetchingEllipsis.dotCount(at: Date(timeIntervalSinceReferenceDate: seconds))
    }

    func testDotCountCyclesOneToThree() {
        // period is 0.4s; sampled mid-window to avoid float-boundary ambiguity.
        XCTAssertEqual(dots(0.1), 1)   // tick 0
        XCTAssertEqual(dots(0.5), 2)   // tick 1
        XCTAssertEqual(dots(0.9), 3)   // tick 2
        XCTAssertEqual(dots(1.3), 1)   // tick 3 — wraps
        XCTAssertEqual(dots(1.7), 2)   // tick 4
    }

    func testDotCountIsStableWithinAPeriod() {
        // Any time inside the first 0.4s window stays at 1 dot.
        XCTAssertEqual(dots(0.05), 1)
        XCTAssertEqual(dots(0.39), 1)
    }

    // MARK: - RefreshStatusResolver

    private let t0 = Date(timeIntervalSinceReferenceDate: 1_000_000)

    func testResolveReportsFetchingWhenARefreshIsRunning() {
        let status = RefreshStatusResolver.resolve(
            isFetching: true,
            snapshotFetchedAt: t0,
            rateLimitedUntil: nil,
            isStale: false,
            now: t0)
        XCTAssertEqual(status, .fetching)
    }

    func testResolveFetchingWinsOverEveryOtherState() {
        // Stale AND rate limited, but a refresh is running -> still .fetching.
        let status = RefreshStatusResolver.resolve(
            isFetching: true,
            snapshotFetchedAt: t0.addingTimeInterval(-7200),
            rateLimitedUntil: t0.addingTimeInterval(300),
            isStale: true,
            now: t0)
        XCTAssertEqual(status, .fetching)
    }

    func testResolveIsIdleWhenThereIsNoSnapshot() {
        let status = RefreshStatusResolver.resolve(
            isFetching: false,
            snapshotFetchedAt: nil,
            rateLimitedUntil: nil,
            isStale: false,
            now: t0)
        XCTAssertEqual(status, .idle)
    }

    func testResolveReportsRateLimitWithRoundedUpMinutes() {
        // 5m30s until the limit resets -> rounds up to 6m. Rate limit also
        // takes priority over the stale flag.
        let status = RefreshStatusResolver.resolve(
            isFetching: false,
            snapshotFetchedAt: t0,
            rateLimitedUntil: t0.addingTimeInterval(330),
            isStale: true,
            now: t0)
        XCTAssertEqual(status, .text("rate limited — retry in 6m"))
    }

    func testResolveReportsOfflineWhenStale() {
        let status = RefreshStatusResolver.resolve(
            isFetching: false,
            snapshotFetchedAt: t0.addingTimeInterval(-7200),
            rateLimitedUntil: nil,
            isStale: true,
            now: t0)
        XCTAssertEqual(status, .text("offline — updated 2h ago"))
    }

    func testResolveReportsUpdatedAgoWhenLive() {
        let status = RefreshStatusResolver.resolve(
            isFetching: false,
            snapshotFetchedAt: t0.addingTimeInterval(-300),
            rateLimitedUntil: nil,
            isStale: false,
            now: t0)
        XCTAssertEqual(status, .text("updated 5m ago"))
    }
}
