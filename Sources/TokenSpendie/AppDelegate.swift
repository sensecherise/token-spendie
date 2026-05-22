import AppKit
import SwiftUI
import Combine

final class AppDelegate: NSObject, NSApplicationDelegate {
    private var preferences: Preferences!
    private var store: UsageStore!
    private var menuBar: MenuBarController!
    private var floatingPanel: FloatingPanelController!
    private var settingsWindow: NSWindow?
    private var cancellables = Set<AnyCancellable>()

    @MainActor
    func applicationDidFinishLaunching(_ notification: Notification) {
        preferences = Preferences()
        store = UsageStore(
            providers: [ClaudeProvider(), GeminiProvider()],
            preferences: preferences
        )
        menuBar = MenuBarController(store: store, preferences: preferences,
                                   onOpenSettings: { [weak self] in self?.showSettings() },
                                   onQuit: { NSApp.terminate(nil) })
        floatingPanel = FloatingPanelController(store: store, preferences: preferences,
                                               onOpenSettings: { [weak self] in self?.showSettings() },
                                               onQuit: { NSApp.terminate(nil) })

        applyDisplayPreferences()
        store.start()

        // React to display-preference changes made outside PreferencesView (e.g. auto re-enable).
        preferences.objectWillChange
            .receive(on: RunLoop.main)
            .sink { [weak self] in
                self?.applyDisplayPreferences()
            }
            .store(in: &cancellables)
    }

    /// Shows/hides each surface to match preferences.
    @MainActor
    private func applyDisplayPreferences() {
        if preferences.showMenuBar { menuBar.install() } else { menuBar.remove() }
        if preferences.showFloatingPanel { floatingPanel.show() } else { floatingPanel.hide() }
    }

    @MainActor
    private func showSettings() {
        if let settingsWindow {
            settingsWindow.makeKeyAndOrderFront(nil)
            NSApp.activate(ignoringOtherApps: true)
            return
        }
        let view = PreferencesView(
            preferences: preferences,
            onDisplayChanged: { [weak self] in self?.applyDisplayPreferences() },
            onIntervalChanged: { [weak self] in self?.store.rescheduleTimer() }
        )
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 300, height: 320),
            styleMask: [.titled, .closable], backing: .buffered, defer: false
        )
        window.title = "Token Spendie"
        window.contentViewController = NSHostingController(rootView: view)
        window.isReleasedWhenClosed = false
        window.center()
        window.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
        settingsWindow = window
    }
}
