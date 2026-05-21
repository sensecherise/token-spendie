import Foundation

/// Routes credential loading to the Keychain reader (auto mode) or the
/// manual-token store (manual mode). `mode` is updated by `AppDelegate` when
/// the preference changes.
final class CredentialRouter: CredentialStore {
    var mode: CredentialMode
    private let keychain: CredentialStore
    private let manual: CredentialStore

    init(mode: CredentialMode, keychain: CredentialStore, manual: CredentialStore) {
        self.mode = mode
        self.keychain = keychain
        self.manual = manual
    }

    func loadCredentials() throws -> OAuthCredentials {
        switch mode {
        case .auto:   return try keychain.loadCredentials()
        case .manual: return try manual.loadCredentials()
        }
    }
}
