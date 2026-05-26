import AppKit
import SwiftUI
import Combine
import UserNotifications

final class AppDelegate: NSObject, NSApplicationDelegate {
    private var preferences: Preferences!
    private var store: UsageStore!
    private var menuBar: MenuBarController!
    private var floatingPanel: FloatingPanelController!
    private var notifier: UsageNotifier!
    private var settingsWindow: NSWindow?
    private var aboutWindow: NSWindow?
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
                                   onOpenAbout: { [weak self] in self?.showAbout() },
                                   onQuit: { NSApp.terminate(nil) })
        floatingPanel = FloatingPanelController(store: store, preferences: preferences,
                                               onOpenSettings: { [weak self] in self?.showSettings() },
                                               onOpenAbout: { [weak self] in self?.showAbout() },
                                               onQuit: { NSApp.terminate(nil) })

        applyDisplayPreferences()

        // Request notification permission and start notifier.
        UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound]) { _, _ in }
        notifier = UsageNotifier()
        store.$providers
            .receive(on: RunLoop.main)
            .sink { [weak self] providers in
                self?.notifier.check(providers: providers)
            }
            .store(in: &cancellables)

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
    private func showAbout() {
        if let aboutWindow {
            aboutWindow.makeKeyAndOrderFront(nil)
            NSApp.activate(ignoringOtherApps: true)
            return
        }
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 260, height: 220),
            styleMask: [.titled, .closable], backing: .buffered, defer: false
        )
        window.title = "About Token Spendie"
        window.contentViewController = NSHostingController(rootView: AboutView())
        window.isReleasedWhenClosed = false
        window.center()
        window.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
        aboutWindow = window
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
