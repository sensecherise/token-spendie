import Foundation
import Security

/// Reads the Claude Code OAuth credentials from the login Keychain.
///
/// After the user grants access once, credentials are cached in Token Spendie's
/// own Data Protection Keychain item (kSecAttrAccessibleAfterFirstUnlock). This
/// survives reboots and app updates without re-prompting the user. The cache is
/// bypassed via loadFreshCredentials() when a 401 indicates Claude Code has
/// rotated its token.
struct KeychainReader: CredentialStore {
    let service: String
    private let cacheService = "com.cherise.TokenSpendie.credentials-cache"

    init(service: String = "Claude Code-credentials") {
        self.service = service
    }

    func loadCredentials() throws -> OAuthCredentials {
        if let cached = try? readCache(), !cached.isExpired(now: Date()) {
            return cached
        }
        return try loadAndCache()
    }

    func loadFreshCredentials() throws -> OAuthCredentials {
        try loadAndCache()
    }

    func credentialsExist() -> Bool {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecReturnData as String: false,
            kSecMatchLimit as String: kSecMatchLimitOne,
        ]
        return SecItemCopyMatching(query as CFDictionary, nil) == errSecSuccess
    }

    // MARK: - Private

    private func loadAndCache() throws -> OAuthCredentials {
        let creds = try readSource()
        try? writeCache(creds)
        return creds
    }

    private func readSource() throws -> OAuthCredentials {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecReturnData as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne,
        ]
        var result: AnyObject?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        switch status {
        case errSecSuccess:
            guard let data = result as? Data else { throw CredentialError.malformed }
            return try OAuthCredentialsParser.parse(data)
        case errSecItemNotFound:
            throw CredentialError.notFound
        case errSecAuthFailed, errSecUserCanceled, errSecInteractionNotAllowed:
            throw CredentialError.accessDenied
        default:
            throw CredentialError.accessDenied
        }
    }

    private func readCache() throws -> OAuthCredentials {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: cacheService,
            kSecReturnData as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne,
            kSecUseDataProtectionKeychain as String: true,
        ]
        var result: AnyObject?
        guard SecItemCopyMatching(query as CFDictionary, &result) == errSecSuccess,
              let data = result as? Data
        else { throw CredentialError.notFound }
        return try JSONDecoder().decode(CachedEntry.self, from: data).toCredentials()
    }

    private func writeCache(_ creds: OAuthCredentials) throws {
        let data = try JSONEncoder().encode(CachedEntry(from: creds))
        let searchQuery: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: cacheService,
            kSecUseDataProtectionKeychain as String: true,
        ]
        let updateAttrs: [String: Any] = [kSecValueData as String: data]
        if SecItemUpdate(searchQuery as CFDictionary, updateAttrs as CFDictionary) == errSecItemNotFound {
            var addQuery = searchQuery
            addQuery[kSecValueData as String] = data
            addQuery[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlock
            SecItemAdd(addQuery as CFDictionary, nil)
        }
    }

    private struct CachedEntry: Codable {
        let accessToken: String
        let refreshToken: String?
        let expiresAt: Double?

        init(from creds: OAuthCredentials) {
            accessToken = creds.accessToken
            refreshToken = creds.refreshToken
            expiresAt = creds.expiresAt.map { $0.timeIntervalSince1970 }
        }

        func toCredentials() -> OAuthCredentials {
            OAuthCredentials(
                accessToken: accessToken,
                refreshToken: refreshToken,
                expiresAt: expiresAt.map { Date(timeIntervalSince1970: $0) }
            )
        }
    }
}
