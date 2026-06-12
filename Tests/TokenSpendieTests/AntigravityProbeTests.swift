import XCTest
@testable import TokenSpendie

final class AntigravityProbeTests: XCTestCase {
    // MARK: - Process matching

    func testMatchesIDELanguageServerWithCSRFToken() throws {
        let ps = """
          312 /usr/libexec/syslogd
          845 /Applications/Antigravity.app/Contents/Resources/app/extensions/antigravity/bin/language_server_macos_arm --csrf_token abc123 --app_data_dir /Users/x/antigravity --extension_server_port 42100
        """
        let match = try XCTUnwrap(AntigravityProbe.firstMatch(inProcessList: ps))
        XCTAssertEqual(match.pid, 845)
        XCTAssertEqual(match.csrfToken, "abc123")
        XCTAssertEqual(match.extensionPort, 42100)
    }

    func testSkipsIDEServerWithoutCSRFToken() {
        let ps = "  845 /apps/antigravity/bin/language_server_macos --app_data_dir antigravity"
        XCTAssertNil(AntigravityProbe.firstMatch(inProcessList: ps))
    }

    func testMatchesAgyCLIWithEmptyCSRFToken() throws {
        let ps = "  77 /Users/cherise/.local/bin/agy chat"
        let match = try XCTUnwrap(AntigravityProbe.firstMatch(inProcessList: ps))
        XCTAssertEqual(match.pid, 77)
        XCTAssertEqual(match.csrfToken, "")
        XCTAssertNil(match.extensionPort)
    }

    func testMatchesAntigravityCLIPathSegmentAsCLI() throws {
        // The CLI dir hosts a language_server binary too — must classify as
        // CLI (no CSRF requirement), not as a tokenless IDE to skip.
        let ps = "  91 /Users/x/.gemini/antigravity-cli/bin/language_server --port 1"
        let match = try XCTUnwrap(AntigravityProbe.firstMatch(inProcessList: ps))
        XCTAssertEqual(match.pid, 91)
        XCTAssertEqual(match.csrfToken, "")
    }

    func testRejectsLookalikes() {
        let ps = """
          11 /usr/bin/vim antigravity-notes.md
          12 /usr/local/bin/biology --mode agy
          13 /opt/other/language_server_macos --csrf_token zzz
        """
        XCTAssertNil(AntigravityProbe.firstMatch(inProcessList: ps))
    }

    // MARK: - lsof parsing

    func testParsesListeningPorts() {
        let lsof = """
        COMMAND   PID USER   FD   TYPE  DEVICE SIZE/OFF NODE NAME
        language  845 user   23u  IPv4  0x0        0t0  TCP 127.0.0.1:42100 (LISTEN)
        language  845 user   24u  IPv6  0x0        0t0  TCP [::1]:42101 (LISTEN)
        language  845 user   25u  IPv4  0x0        0t0  TCP 127.0.0.1:9999->127.0.0.1:1234 (ESTABLISHED)
        """
        XCTAssertEqual(AntigravityProbe.parseListeningPorts(lsof), [42100, 42101])
    }

    // MARK: - Endpoint candidates

    func testCandidateOrderHTTPSPortsThenHTTPExtensionPort() {
        let endpoints = AntigravityProbe.candidateEndpoints(
            listeningPorts: [42100, 42101], extensionPort: 42100, csrfToken: "t")
        XCTAssertEqual(endpoints.map { "\($0.scheme):\($0.port)" },
                       ["https:42100", "https:42101", "http:42100"])
        XCTAssertEqual(endpoints[0].csrfToken, "t")
    }

    // MARK: - Localhost-only TLS policy

    func testTrustPolicyOnlyAcceptsLocalhost() {
        XCTAssertTrue(AntigravityProbe.shouldTrustHost("127.0.0.1"))
        XCTAssertTrue(AntigravityProbe.shouldTrustHost("LOCALHOST"))
        XCTAssertFalse(AntigravityProbe.shouldTrustHost("example.com"))
        XCTAssertFalse(AntigravityProbe.shouldTrustHost("127.0.0.1.evil.com"))
    }
}
