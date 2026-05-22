import Foundation

/// Decoded OAuth credentials from the Claude Code Keychain item.
struct OAuthCredentials: Equatable {
    let accessToken: String
    let refreshToken: String?
    let expiresAt: Date?

    func isExpired(now: Date) -> Bool {
        guard let expiresAt else { return false }
        return now >= expiresAt
    }
}

/// Failure modes for obtaining credentials.
enum CredentialError: Error, Equatable {
    case notFound       // no Keychain item
    case accessDenied   // user denied / cancelled Keychain access
    case malformed      // item exists but JSON could not be parsed
}

/// Parses the `claudeAiOauth` JSON blob. Separated from Keychain I/O for testability.
enum OAuthCredentialsParser {
    static func parse(_ data: Data) throws -> OAuthCredentials {
        guard
            let root = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any],
            let oauth = root["claudeAiOauth"] as? [String: Any],
            let accessToken = oauth["accessToken"] as? String, !accessToken.isEmpty
        else {
            throw CredentialError.malformed
        }
        let refreshToken = oauth["refreshToken"] as? String
        var expiresAt: Date?
        if let raw = (oauth["expiresAt"] as? NSNumber)?.doubleValue {
            // Heuristic: values past year ~2001 in ms are > 1e12; treat those as milliseconds.
            let seconds = raw > 1_000_000_000_000 ? raw / 1000.0 : raw
            expiresAt = Date(timeIntervalSince1970: seconds)
        }
        return OAuthCredentials(accessToken: accessToken, refreshToken: refreshToken, expiresAt: expiresAt)
    }
}

/// Abstraction over credential storage so the store can be tested without the Keychain.
protocol CredentialStore {
    func loadCredentials() throws -> OAuthCredentials
}
