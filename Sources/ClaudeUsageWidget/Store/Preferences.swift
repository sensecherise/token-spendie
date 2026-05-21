import Foundation
import Combine

/// How often the widget polls when no panel is open.
enum RefreshInterval: Int, CaseIterable, Identifiable {
    case s30 = 30
    case s60 = 60
    case s120 = 120

    var id: Int { rawValue }
    var seconds: TimeInterval { TimeInterval(rawValue) }
    var label: String {
        switch self {
        case .s30: return "30 seconds"
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

    private enum Keys {
        static let showMenuBar = "showMenuBar"
        static let showFloatingPanel = "showFloatingPanel"
        static let refreshInterval = "refreshInterval"
        static let launchAtLogin = "launchAtLogin"
    }

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        self.showMenuBar = defaults.object(forKey: Keys.showMenuBar) as? Bool ?? true
        self.showFloatingPanel = defaults.object(forKey: Keys.showFloatingPanel) as? Bool ?? false
        let storedInterval = defaults.object(forKey: Keys.refreshInterval) as? Int ?? RefreshInterval.s60.rawValue
        self.refreshInterval = RefreshInterval(rawValue: storedInterval) ?? .s60
        self.launchAtLogin = defaults.object(forKey: Keys.launchAtLogin) as? Bool ?? false
    }
}
