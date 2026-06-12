import XCTest
@testable import TokenSpendie

private struct ProbeStub: AntigravityProbing {
    var present = true
    var result: Result<AntigravityQuotaData, ProviderError> =
        .failure(.notRunning)

    func isProcessPresent() -> Bool { present }
    func fetchQuotaData() async throws -> AntigravityQuotaData {
        try result.get()
    }
}

final class AntigravityProviderTests: XCTestCase {
    private var cacheURL: URL!

    override func setUpWithError() throws {
        cacheURL = FileManager.default.temporaryDirectory
            .appendingPathComponent("ag-cache-\(UUID().uuidString).json")
    }

    override func tearDownWithError() throws {
        try? FileManager.default.removeItem(at: cacheURL)
    }

    private static let userStatusBody = Data(#"""
    {
      "userStatus": {
        "email": "user@example.com",
        "userTier": {"name": "Google AI Pro"},
        "planStatus": {"planInfo": {"planDisplayName": "Pro"}},
        "cascadeModelConfigData": {
          "clientModelConfigs": [
            {"label": "Claude Sonnet 4.5", "modelOrAlias": {"model": "M_CLAUDE"},
             "quotaInfo": {"remainingFraction": 0.82, "resetTime": "2026-06-12T18:00:00Z"}},
            {"label": "Gemini 3 Pro (Low)", "modelOrAlias": {"model": "M_PRO_LOW"},
             "quotaInfo": {"remainingFraction": 0.4, "resetTime": "1765562400"}},
            {"label": "No Quota Model", "modelOrAlias": {"model": "M_X"}}
          ]
        }
      }
    }
    """#.utf8)

    private static let commandConfigsBody = Data(#"""
    {"clientModelConfigs": [
       {"label": "Gemini 3 Flash", "modelOrAlias": {"model": "M_FLASH"},
        "quotaInfo": {"remainingFraction": 0.95}}
    ]}
    """#.utf8)

    private let now = { UsageDecoder.parseDate("2026-06-12T10:00:00Z")! }

    private func provider(_ probe: ProbeStub) -> AntigravityProvider {
        AntigravityProvider(probe: probe,
                            cache: SnapshotCache(fileURL: cacheURL),
                            now: now)
    }

    // MARK: - Decoding

    func testFetchDecodesUserStatusIntoWindows() async throws {
        var probe = ProbeStub()
        probe.result = .success(AntigravityQuotaData(source: .userStatus,
                                                     body: Self.userStatusBody))
        let snapshot = try await provider(probe).fetchUsage()

        XCTAssertEqual(snapshot.id, .antigravity)
        XCTAssertEqual(snapshot.plan, "Google AI Pro")
        XCTAssertEqual(snapshot.windows.map(\.label),
                       ["Claude Sonnet 4.5", "Gemini 3 Pro (Low)"])
        // Headline = highest used: 1 - 0.4 = 60%.
        XCTAssertEqual(snapshot.headline.label, "Gemini 3 Pro (Low)")
        XCTAssertEqual(snapshot.headline.window.percent, 60, accuracy: 0.001)
        XCTAssertEqual(snapshot.windows[0].window.percent, 18, accuracy: 0.001)
        // ISO reset on window 0; epoch-string reset on window 1.
        XCTAssertEqual(snapshot.windows[0].window.resetsAt,
                       UsageDecoder.parseDate("2026-06-12T18:00:00Z"))
        XCTAssertEqual(snapshot.windows[1].window.resetsAt,
                       Date(timeIntervalSince1970: 1765562400))
        XCTAssertEqual(snapshot.windows[0].resetStyle, .countdown)
        XCTAssertEqual(snapshot.fetchedAt, now())
        XCTAssertNotNil(snapshot.note)
    }

    func testFetchDecodesCommandModelConfigsFallback() async throws {
        var probe = ProbeStub()
        probe.result = .success(AntigravityQuotaData(source: .commandModelConfigs,
                                                     body: Self.commandConfigsBody))
        let snapshot = try await provider(probe).fetchUsage()
        XCTAssertEqual(snapshot.windows.map(\.label), ["Gemini 3 Flash"])
        XCTAssertEqual(snapshot.headline.window.percent, 5, accuracy: 0.001)
        XCTAssertNil(snapshot.plan)
    }

    func testErrorCodeOrNoWindowsThrowsBadResponse() async {
        for body in [
            Data(#"{"code": 16, "message": "unauthenticated"}"#.utf8),
            Data(#"{"userStatus": {"cascadeModelConfigData": {"clientModelConfigs": []}}}"#.utf8),
            Data("not json".utf8),
        ] {
            var probe = ProbeStub()
            probe.result = .success(AntigravityQuotaData(source: .userStatus, body: body))
            do {
                _ = try await provider(probe).fetchUsage()
                XCTFail("expected badResponse")
            } catch {
                XCTAssertEqual(error as? ProviderError, .badResponse)
            }
        }
    }

    func testFetchWithoutProcessThrowsNotRunning() async {
        let probe = ProbeStub(present: false, result: .failure(.notRunning))
        do {
            _ = try await provider(probe).fetchUsage()
            XCTFail("expected notRunning")
        } catch {
            XCTAssertEqual(error as? ProviderError, .notRunning)
        }
    }

    // MARK: - Row semantics (detect = process OR cache < 7 days)

    func testDetectTrueWhenProcessRuns() {
        XCTAssertTrue(provider(ProbeStub(present: true)).detectCredentials())
    }

    func testDetectUsesCacheTTLWhenProcessAbsent() {
        let probe = ProbeStub(present: false)
        let window = LabeledWindow(label: "Claude", detail: "quota",
                                   resetStyle: .countdown,
                                   window: UsageWindow(percent: 10, resetsAt: nil))

        // No cache → not detected.
        XCTAssertFalse(provider(probe).detectCredentials())

        // Fresh cache (3 days old) → detected.
        let cache = SnapshotCache(fileURL: cacheURL)
        cache.save(ProviderSnapshot(id: .antigravity, plan: nil, headline: window,
                                    windows: [window],
                                    fetchedAt: now().addingTimeInterval(-3 * 86400)))
        XCTAssertTrue(provider(probe).detectCredentials())

        // Stale cache (8 days old) → not detected.
        cache.save(ProviderSnapshot(id: .antigravity, plan: nil, headline: window,
                                    windows: [window],
                                    fetchedAt: now().addingTimeInterval(-8 * 86400)))
        XCTAssertFalse(provider(probe).detectCredentials())
    }
}
