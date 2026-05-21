import XCTest
@testable import ClaudeUsageWidget

final class CredentialRouterTests: XCTestCase {
    private final class StubStore: CredentialStore {
        let result: Result<OAuthCredentials, Error>
        init(_ result: Result<OAuthCredentials, Error>) { self.result = result }
        func loadCredentials() throws -> OAuthCredentials { try result.get() }
    }

    private func creds(_ token: String) -> OAuthCredentials {
        OAuthCredentials(accessToken: token, refreshToken: nil, expiresAt: nil)
    }

    func testAutoModeUsesKeychain() throws {
        let router = CredentialRouter(
            mode: .auto,
            keychain: StubStore(.success(creds("from-keychain"))),
            manual: StubStore(.failure(CredentialError.notFound))
        )
        XCTAssertEqual(try router.loadCredentials().accessToken, "from-keychain")
    }

    func testManualModeUsesManualStore() throws {
        let router = CredentialRouter(
            mode: .manual,
            keychain: StubStore(.failure(CredentialError.notFound)),
            manual: StubStore(.success(creds("from-manual")))
        )
        XCTAssertEqual(try router.loadCredentials().accessToken, "from-manual")
    }

    func testModeIsMutable() throws {
        let router = CredentialRouter(
            mode: .auto,
            keychain: StubStore(.success(creds("k"))),
            manual: StubStore(.success(creds("m")))
        )
        router.mode = .manual
        XCTAssertEqual(try router.loadCredentials().accessToken, "m")
    }
}
