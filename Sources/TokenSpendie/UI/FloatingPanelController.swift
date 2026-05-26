import AppKit
import SwiftUI

/// Manages the optional always-on-top floating usage panel.
@MainActor
final class FloatingPanelController {
    private let store: UsageStore
    private let preferences: Preferences
    private let onOpenSettings: () -> Void
    private let onOpenAbout: () -> Void
    private let onQuit: () -> Void
    private var panel: NSPanel?

    init(store: UsageStore, preferences: Preferences,
         onOpenSettings: @escaping () -> Void,
         onOpenAbout: @escaping () -> Void,
         onQuit: @escaping () -> Void) {
        self.store = store
        self.preferences = preferences
        self.onOpenSettings = onOpenSettings
        self.onOpenAbout = onOpenAbout
        self.onQuit = onQuit
    }

    /// Shows the floating panel. Safe to call repeatedly.
    func show() {
        if let panel {
            panel.orderFrontRegardless()
            return
        }
        let panel = NSPanel(
            contentRect: NSRect(x: 0, y: 0, width: 260, height: 220),
            styleMask: [.nonactivatingPanel, .titled, .closable, .fullSizeContentView],
            backing: .buffered, defer: false
        )
        panel.titleVisibility = .hidden
        panel.titlebarAppearsTransparent = true
        panel.isMovableByWindowBackground = true
        panel.level = .floating
        panel.isFloatingPanel = true
        panel.hidesOnDeactivate = false
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        panel.contentViewController = NSHostingController(
            rootView: DetailPanelView(
                store: store,
                preferences: preferences,
                onRefresh: { [weak self] in Task { await self?.store.manualRefresh() } },
                onOpenSettings: { [weak self] in self?.onOpenSettings() },
                onOpenAbout: { [weak self] in self?.onOpenAbout() },
                onQuit: { [weak self] in self?.onQuit() }
            )
        )
        panel.center()
        panel.orderFrontRegardless()
        self.panel = panel
    }

    /// Hides and releases the floating panel.
    func hide() {
        panel?.close()
        panel = nil
    }
}
