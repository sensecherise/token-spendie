import Foundation
import Security

/// Stores a user-pasted Claude OAuth token in the app's own Keychain item.
/// Because the app creates and owns this item, reading it never prompts.
final class ManualTokenStore: CredentialStore {
    let service: String

    init(service: String = "com.cherise.ClaudeUsage.token") {
        self.service = service
    }

    private var baseQuery: [String: Any] {
        [kSecClass as String: kSecClassGenericPassword,
         kSecAttrService as String: service]
    }

    func loadCredentials() throws -> OAuthCredentials {
        var query = baseQuery
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne
        var result: AnyObject?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        switch status {
        case errSecSuccess:
            guard let data = result as? Data,
                  let token = String(data: data, encoding: .utf8),
                  !token.isEmpty else {
                throw CredentialError.malformed
            }
            return OAuthCredentials(accessToken: token, refreshToken: nil, expiresAt: nil)
        case errSecItemNotFound:
            throw CredentialError.notFound
        default:
            throw CredentialError.accessDenied
        }
    }

    /// Saves (replacing any existing) token. Throws `.malformed` if blank.
    func save(token: String) throws {
        let trimmed = token.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { throw CredentialError.malformed }
        SecItemDelete(baseQuery as CFDictionary)
        var add = baseQuery
        add[kSecValueData as String] = Data(trimmed.utf8)
        let status = SecItemAdd(add as CFDictionary, nil)
        guard status == errSecSuccess else { throw CredentialError.accessDenied }
    }

    /// Removes the stored token, if any.
    func clear() {
        SecItemDelete(baseQuery as CFDictionary)
    }

    /// True if a non-empty token is currently stored.
    var hasToken: Bool {
        (try? loadCredentials()) != nil
    }
}
