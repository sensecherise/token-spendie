import XCTest
@testable import ClaudeUsageWidget

final class ManualTokenStoreTests: XCTestCase {
    private func testStore() -> ManualTokenStore {
        ManualTokenStore(service: "ClaudeUsageWidget-Test-\(UUID().uuidString)")
    }

    func testSaveThenLoadRoundTrips() throws {
        let store = testStore()
        defer { store.clear() }
        try store.save(token: "tok-abc-123")
        XCTAssertEqual(try store.loadCredentials().accessToken, "tok-abc-123")
    }

    func testLoadWithNoTokenThrowsNotFound() {
        let store = testStore()
        XCTAssertThrowsError(try store.loadCredentials()) { error in
            XCTAssertEqual(error as? CredentialError, .notFound)
        }
    }

    func testSaveTrimsWhitespace() throws {
        let store = testStore()
        defer { store.clear() }
        try store.save(token: "  tok-xyz \n")
        XCTAssertEqual(try store.loadCredentials().accessToken, "tok-xyz")
    }

    func testSaveEmptyThrowsMalformed() {
        let store = testStore()
        XCTAssertThrowsError(try store.save(token: "   ")) { error in
            XCTAssertEqual(error as? CredentialError, .malformed)
        }
    }

    func testClearRemovesToken() throws {
        let store = testStore()
        try store.save(token: "tok")
        store.clear()
        XCTAssertThrowsError(try store.loadCredentials())
    }
}
