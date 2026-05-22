import Foundation
import Security

/// Reads the Claude Code OAuth credentials from the login Keychain.
struct KeychainReader: CredentialStore {
    let service: String

    init(service: String = "Claude Code-credentials") {
        self.service = service
    }

    func loadCredentials() throws -> OAuthCredentials {
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

    func credentialsExist() -> Bool {
        // kSecReturnData:false asks only whether the item exists. Returning
        // attributes (not the secret) does not prompt for user consent.
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecReturnData as String: false,
            kSecMatchLimit as String: kSecMatchLimitOne,
        ]
        return SecItemCopyMatching(query as CFDictionary, nil) == errSecSuccess
    }
}
