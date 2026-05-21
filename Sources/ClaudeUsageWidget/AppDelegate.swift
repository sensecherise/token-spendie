import AppKit
import SwiftUI
import Combine

final class AppDelegate: NSObject, NSApplicationDelegate {
    private var preferences: Preferences!
    private var manualTokenStore: ManualTokenStore!
    private var credentialRouter: CredentialRouter!
    private var store: UsageStore!
    private var menuBar: MenuBarController!
    private var floatingPanel: FloatingPanelController!
    private var settingsWindow: NSWindow?
    private var cancellables = Set<AnyCancellable>()

    @MainActor
    func applicationDidFinishLaunching(_ notification: Notification) {
        preferences = Preferences()
        manualTokenStore = ManualTokenStore()
        credentialRouter = CredentialRouter(
            mode: preferences.credentialMode,
            keychain: KeychainReader(),
            manual: manualTokenStore
        )
        store = UsageStore(
            provider: EndpointUsageProvider(),
            credentials: credentialRouter,
            cache: SnapshotCache(fileURL: SnapshotCache.defaultURL()),
            preferences: preferences
        )
        menuBar = MenuBarController(store: store, preferences: preferences,
                                   onOpenSettings: { [weak self] in self?.showSettings() },
                                   onQuit: { NSApp.terminate(nil) })
        floatingPanel = FloatingPanelController(store: store, preferences: preferences,
                                               onOpenSettings: { [weak self] in self?.showSettings() },
                                               onQuit: { NSApp.terminate(nil) })

        installEditMenu()
        applyDisplayPreferences()
        store.start()

        // React to display-preference changes made outside PreferencesView (e.g. auto re-enable).
        preferences.objectWillChange
            .receive(on: RunLoop.main)
            .sink { [weak self] in
                guard let self else { return }
                self.applyDisplayPreferences()
                let newMode = self.preferences.credentialMode
                if self.credentialRouter.mode != newMode {
                    self.credentialRouter.mode = newMode
                    Task { await self.store.refreshNow() }
                }
            }
            .store(in: &cancellables)
    }

    /// Installs a minimal Edit menu. This app is an `LSUIElement` accessory app
    /// with no visible menu bar, but without a main menu the standard
    /// Cmd-X/C/V/A key equivalents have no route to the focused text field —
    /// so the token field could not be pasted into. The menu stays hidden;
    /// only its key equivalents matter.
    @MainActor
    private func installEditMenu() {
        let mainMenu = NSMenu()
        let editItem = NSMenuItem()
        mainMenu.addItem(editItem)
        let editMenu = NSMenu(title: "Edit")
        editItem.submenu = editMenu
        editMenu.addItem(withTitle: "Cut", action: #selector(NSText.cut(_:)), keyEquivalent: "x")
        editMenu.addItem(withTitle: "Copy", action: #selector(NSText.copy(_:)), keyEquivalent: "c")
        editMenu.addItem(withTitle: "Paste", action: #selector(NSText.paste(_:)), keyEquivalent: "v")
        editMenu.addItem(withTitle: "Select All", action: #selector(NSText.selectAll(_:)), keyEquivalent: "a")
        NSApp.mainMenu = mainMenu
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
            manualTokenStore: manualTokenStore,
            onDisplayChanged: { [weak self] in self?.applyDisplayPreferences() },
            onIntervalChanged: { [weak self] in self?.store.rescheduleTimer() }
        )
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 300, height: 320),
            styleMask: [.titled, .closable], backing: .buffered, defer: false
        )
        window.title = "Claude Usage Widget"
        window.contentViewController = NSHostingController(rootView: view)
        window.isReleasedWhenClosed = false
        window.center()
        window.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
        settingsWindow = window
    }
}
