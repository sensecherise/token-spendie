import XCTest
@testable import TokenSpendie

final class UsageProviderTests: XCTestCase {
    private func http(_ status: Int) -> HTTPURLResponse {
        HTTPURLResponse(url: URL(string: "https://api.anthropic.com/api/oauth/usage")!,
                        statusCode: status, httpVersion: nil, headerFields: [:])!
    }

    func testEndpointProviderDecodes200() async throws {
        let body = Data(#"{"five_hour":{"utilization":50},"seven_day":{"utilization":60}}"#.utf8)
        let provider = EndpointUsageProvider(
            transport: { _ in (body, self.http(200)) },
            now: { Date(timeIntervalSince1970: 42) }
        )
        let snapshot = try await provider.fetchUsage(accessToken: "tok")
        XCTAssertEqual(snapshot.session.percent, 50, accuracy: 0.001)
        XCTAssertEqual(snapshot.weekly.percent, 60, accuracy: 0.001)
        XCTAssertEqual(snapshot.fetchedAt, Date(timeIntervalSince1970: 42))
    }

    func testEndpointProviderEmptyTokenThrowsUnauthorizedWithoutCallingTransport() async {
        var transportCalled = false
        let provider = EndpointUsageProvider(transport: { _ in
            transportCalled = true
            return (Data(), self.http(200))
        })
        await assertThrows(provider, .unauthorized, accessToken: "")
        XCTAssertFalse(transportCalled, "transport must not be invoked for an empty token")
    }

    func testEndpointProvider401ThrowsUnauthorized() async {
        let provider = EndpointUsageProvider(transport: { _ in (Data(), self.http(401)) })
        await assertThrows(provider, .unauthorized)
    }

    func testEndpointProvider500ThrowsBadResponse() async {
        let provider = EndpointUsageProvider(transport: { _ in (Data(), self.http(500)) })
        await assertThrows(provider, .badResponse)
    }

    func testEndpointProviderSendsBearerHeader() async throws {
        var captured: URLRequest?
        let body = Data(#"{"five_hour":{"utilization":10},"seven_day":{"utilization":10}}"#.utf8)
        let provider = EndpointUsageProvider(transport: { request in
            captured = request
            return (body, self.http(200))
        })
        _ = try await provider.fetchUsage(accessToken: "secret-token")
        XCTAssertEqual(captured?.value(forHTTPHeaderField: "Authorization"), "Bearer secret-token")
        XCTAssertEqual(captured?.value(forHTTPHeaderField: "Accept"), "application/json")
    }

    private func assertThrows(_ provider: ClaudeUsageEndpoint, _ expected: ProviderError,
                              accessToken: String = "tok",
                              file: StaticString = #filePath, line: UInt = #line) async {
        do {
            _ = try await provider.fetchUsage(accessToken: accessToken)
            XCTFail("expected \(expected)", file: file, line: line)
        } catch {
            XCTAssertEqual(error as? ProviderError, expected, file: file, line: line)
        }
    }
}
