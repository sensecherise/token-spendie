import Foundation

/// Stores the user-entered API token in UserDefaults.
///
/// The app deliberately does not use the Keychain — the user opted for plain
/// manual entry. The token is therefore stored as plaintext in
/// `~/Library/Preferences/com.cherise.TokenSpendie.plist`.
final class TokenStore {
    private let defaults: UserDefaults
    private static let key = "apiToken"

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
    }

    /// The saved token, or `nil` if none is saved or the saved value is blank.
    var token: String? {
        guard let raw = defaults.string(forKey: Self.key) else { return nil }
        let trimmed = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        return trimmed.isEmpty ? nil : trimmed
    }

    /// True when a non-blank token is stored.
    var hasToken: Bool { token != nil }

    /// Saves `token`, trimming surrounding whitespace.
    /// Throws `TokenStoreError.blank` if it is empty after trimming.
    func save(_ token: String) throws {
        let trimmed = token.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { throw TokenStoreError.blank }
        defaults.set(trimmed, forKey: Self.key)
    }

    /// Removes the stored token, if any.
    func clear() {
        defaults.removeObject(forKey: Self.key)
    }
}

/// Failure mode for `TokenStore.save`.
enum TokenStoreError: Error, Equatable {
    case blank
}
