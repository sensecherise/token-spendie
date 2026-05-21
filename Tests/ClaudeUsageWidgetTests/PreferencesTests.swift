import XCTest
@testable import ClaudeUsageWidget

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
        first.refreshInterval = .s30
        let second = Preferences(defaults: defaults)
        XCTAssertTrue(second.showFloatingPanel)
        XCTAssertEqual(second.refreshInterval, .s30)
    }

    func testRefreshIntervalSeconds() {
        XCTAssertEqual(RefreshInterval.s30.seconds, 30)
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

    @MainActor
    func testCredentialModeDefaultsToAuto() {
        let prefs = Preferences(defaults: freshDefaults())
        XCTAssertEqual(prefs.credentialMode, .auto)
    }

    @MainActor
    func testCredentialModePersists() {
        let defaults = freshDefaults()
        let first = Preferences(defaults: defaults)
        first.credentialMode = .manual
        let second = Preferences(defaults: defaults)
        XCTAssertEqual(second.credentialMode, .manual)
    }
}
