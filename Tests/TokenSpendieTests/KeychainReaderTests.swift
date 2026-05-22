import XCTest
@testable import TokenSpendie

final class KeychainReaderTests: XCTestCase {
    func testMissingItemThrowsNotFound() {
        let reader = KeychainReader(service: "TokenSpendie-NoSuchItem-\(UUID().uuidString)")
        XCTAssertThrowsError(try reader.loadCredentials()) { error in
            XCTAssertEqual(error as? CredentialError, .notFound)
        }
    }
}
