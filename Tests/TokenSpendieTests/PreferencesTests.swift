import XCTest
@testable import TokenSpendie

final class PreferencesTests: XCTestCase {
    private func freshDefaults() -> UserDefaults {
        let suite = "prefs-test-\(UUID().uuidString)"
        return UserDefaults(suiteName: suite)!
    }

    @MainActor
    func testDefaultsWhenUnset() {
        let prefs = Preferences(defaults: freshDefaults())
        XCTAssertTrue(prefs.showMenuBar)
        XCTAssertFalse(prefs.showFloatingPanel)
        XCTAssertEqual(prefs.refreshInterval, .s60)
        XCTAssertFalse(prefs.launchAtLogin)
    }

    @MainActor
    func testValuesPersist() {
        let defaults = freshDefaults()
        let first = Preferences(defaults: defaults)
        first.showFloatingPanel = true
        first.refreshInterval = .s120
        let second = Preferences(defaults: defaults)
        XCTAssertTrue(second.showFloatingPanel)
        XCTAssertEqual(second.refreshInterval, .s120)
    }

    func testRefreshIntervalSeconds() {
        XCTAssertEqual(RefreshInterval.s60.seconds, 60)
        XCTAssertEqual(RefreshInterval.s120.seconds, 120)
    }

    @MainActor
    func testThemeDefaultsToDefault() {
        let prefs = Preferences(defaults: freshDefaults())
        XCTAssertEqual(prefs.theme, .default)
    }

    @MainActor
    func testThemePersists() {
        let defaults = freshDefaults()
        let first = Preferences(defaults: defaults)
        first.theme = .ocean
        let second = Preferences(defaults: defaults)
        XCTAssertEqual(second.theme, .ocean)
    }

    func testRefreshIntervalDropsThirtySeconds() {
        XCTAssertNil(RefreshInterval(rawValue: 30))
        XCTAssertEqual(RefreshInterval.allCases, [.s60, .s120])
    }

    @MainActor
    func testMenuBarProviderIDDefaultsToClaude() {
        let prefs = Preferences(defaults: freshDefaults())
        XCTAssertEqual(prefs.menuBarProviderID, .claude)
    }

    @MainActor
    func testMenuBarProviderIDWritesRawValueToDefaults() {
        let defaults = freshDefaults()
        let prefs = Preferences(defaults: defaults)
        prefs.menuBarProviderID = .claude
        XCTAssertEqual(defaults.string(forKey: "menuBarProviderID"), "claude",
                       "didSet persists the raw value to UserDefaults")
    }

    @MainActor
    func testMenuBarProviderIDFallsBackForUnknownStoredValue() {
        let defaults = freshDefaults()
        defaults.set("nonsense", forKey: "menuBarProviderID")
        XCTAssertEqual(Preferences(defaults: defaults).menuBarProviderID, .claude,
                       "an unrecognized stored value falls back to .claude")
    }
}
