import Foundation
import Combine

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
    /// Which provider's ring rides the menu bar.
    @Published var menuBarProviderID: ProviderID {
        didSet { defaults.set(menuBarProviderID.rawValue, forKey: Keys.menuBarProviderID) }
    }

    private enum Keys {
        static let showMenuBar = "showMenuBar"
        static let showFloatingPanel = "showFloatingPanel"
        static let refreshInterval = "refreshInterval"
        static let launchAtLogin = "launchAtLogin"
        static let theme = "theme"
        static let menuBarProviderID = "menuBarProviderID"
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
        let storedProviderID = defaults.string(forKey: Keys.menuBarProviderID)
        self.menuBarProviderID = storedProviderID.flatMap(ProviderID.init(rawValue:)) ?? .claude
    }
}
