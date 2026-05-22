import XCTest
@testable import TokenSpendie

final class OAuthCredentialsTests: XCTestCase {
    func testParsesSecondsExpiry() throws {
        let json = #"{"claudeAiOauth":{"accessToken":"abc","refreshToken":"ref","expiresAt":1700000000}}"#
        let creds = try OAuthCredentialsParser.parse(Data(json.utf8))
        XCTAssertEqual(creds.accessToken, "abc")
        XCTAssertEqual(creds.refreshToken, "ref")
        XCTAssertEqual(creds.expiresAt, Date(timeIntervalSince1970: 1_700_000_000))
    }

    func testParsesMillisecondsExpiry() throws {
        let json = #"{"claudeAiOauth":{"accessToken":"abc","expiresAt":1700000000000}}"#
        let creds = try OAuthCredentialsParser.parse(Data(json.utf8))
        XCTAssertEqual(creds.expiresAt, Date(timeIntervalSince1970: 1_700_000_000))
        XCTAssertNil(creds.refreshToken)
    }

    func testMissingAccessTokenThrowsMalformed() {
        let json = #"{"claudeAiOauth":{"refreshToken":"ref"}}"#
        XCTAssertThrowsError(try OAuthCredentialsParser.parse(Data(json.utf8))) { error in
            XCTAssertEqual(error as? CredentialError, .malformed)
        }
    }

    func testGarbageThrowsMalformed() {
        XCTAssertThrowsError(try OAuthCredentialsParser.parse(Data("not json".utf8))) { error in
            XCTAssertEqual(error as? CredentialError, .malformed)
        }
    }

    func testIsExpired() {
        let past = OAuthCredentials(accessToken: "a", refreshToken: nil,
                                    expiresAt: Date(timeIntervalSince1970: 100))
        XCTAssertTrue(past.isExpired(now: Date(timeIntervalSince1970: 200)))
        XCTAssertFalse(past.isExpired(now: Date(timeIntervalSince1970: 50)))
        let noExpiry = OAuthCredentials(accessToken: "a", refreshToken: nil, expiresAt: nil)
        XCTAssertFalse(noExpiry.isExpired(now: Date()))
    }
}
