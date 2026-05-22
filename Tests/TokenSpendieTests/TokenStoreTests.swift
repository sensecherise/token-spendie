import XCTest
@testable import TokenSpendie

final class TokenStoreTests: XCTestCase {
    private func makeStore() -> TokenStore {
        TokenStore(defaults: UserDefaults(suiteName: "TokenStoreTests-\(UUID().uuidString)")!)
    }

    func testNoTokenByDefault() {
        let store = makeStore()
        XCTAssertNil(store.token)
        XCTAssertFalse(store.hasToken)
    }

    func testSaveThenLoad() throws {
        let store = makeStore()
        try store.save("sk-ant-oat01-abc")
        XCTAssertEqual(store.token, "sk-ant-oat01-abc")
        XCTAssertTrue(store.hasToken)
    }

    func testSaveTrimsWhitespace() throws {
        let store = makeStore()
        try store.save("  sk-ant-oat01-abc\n")
        XCTAssertEqual(store.token, "sk-ant-oat01-abc")
    }

    func testSaveBlankThrows() {
        let store = makeStore()
        XCTAssertThrowsError(try store.save("   ")) { error in
            XCTAssertEqual(error as? TokenStoreError, .blank)
        }
        XCTAssertNil(store.token)
    }

    func testClearRemovesToken() throws {
        let store = makeStore()
        try store.save("sk-ant-oat01-abc")
        store.clear()
        XCTAssertNil(store.token)
        XCTAssertFalse(store.hasToken)
    }
}
