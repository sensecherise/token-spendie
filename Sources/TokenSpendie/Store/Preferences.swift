import Foundation
import Combine

/// Which credential source the widget uses.
enum CredentialMode: String, CaseIterable, Identifiable {
    case auto
    case manual

    var id: String { rawValue }

    var label: String {
        switch self {
        case .auto:   return "Claude Code Keychain"
        case .manual: return "Manual token"
        }
    }
}

/// How often the widget polls. 60 seconds is the floor — the usage endpoint
/// rate-limits aggressively, so faster polling is not offered.
enum RefreshInterval: Int, CaseIterable, Identifiable {
    case s60 = 60
    case s120 = 120

    var id: Int { rawValue }
    var seconds: TimeInterval { TimeInterval(rawValue) }
    var label: String {
        switch self {
        case .s60: return "60 seconds"
        case .s120: return "2 minutes"
        }
    }
}

/// Observable, UserDefaults-backed preferences.
@MainActor
final class Preferences: ObservableObject {
    private let defaults: UserDefaults

    @Published var showMenuBar: Bool { didSet { defaults.set(showMenuBar, forKey: Keys.showMenuBar) } }
    @Published var showFloatingPanel: Bool { didSet { defaults.set(showFloatingPanel, forKey: Keys.showFloatingPanel) } }
    @Published var refreshInterval: RefreshInterval { didSet { defaults.set(refreshInterval.rawValue, forKey: Keys.refreshInterval) } }
    @Published var launchAtLogin: Bool { didSet { defaults.set(launchAtLogin, forKey: Keys.launchAtLogin) } }
    @Published var theme: Theme { didSet { defaults.set(theme.rawValue, forKey: Keys.theme) } }
    @Published var credentialMode: CredentialMode { didSet { defaults.set(credentialMode.rawValue, forKey: Keys.credentialMode) } }

    private enum Keys {
        static let showMenuBar = "showMenuBar"
        static let showFloatingPanel = "showFloatingPanel"
        static let refreshInterval = "refreshInterval"
        static let launchAtLogin = "launchAtLogin"
        static let theme = "theme"
        static let credentialMode = "credentialMode"
    }

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        self.showMenuBar = defaults.object(forKey: Keys.showMenuBar) as? Bool ?? true
        self.showFloatingPanel = defaults.object(forKey: Keys.showFloatingPanel) as? Bool ?? false
        let storedInterval = defaults.object(forKey: Keys.refreshInterval) as? Int ?? RefreshInterval.s60.rawValue
        self.refreshInterval = RefreshInterval(rawValue: storedInterval) ?? .s60
        self.launchAtLogin = defaults.object(forKey: Keys.launchAtLogin) as? Bool ?? false
        let storedTheme = defaults.string(forKey: Keys.theme)
        self.theme = storedTheme.flatMap(Theme.init(rawValue:)) ?? .default
        let storedMode = defaults.string(forKey: Keys.credentialMode)
        self.credentialMode = storedMode.flatMap(CredentialMode.init(rawValue:)) ?? .auto
    }
}
