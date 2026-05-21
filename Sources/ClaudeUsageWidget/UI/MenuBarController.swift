import AppKit
import SwiftUI
import Combine

/// Manages the menu bar status item and its detail dropdown.
///
/// The dropdown is a borderless `NSPanel` positioned manually, flush under the
/// status item — `NSPopover` could not be reliably anchored to a status item on
/// this macOS, so placement is computed directly instead.
@MainActor
final class MenuBarController: NSObject {
    private let store: UsageStore
    private let preferences: Preferences
    private let onOpenSettings: () -> Void
    private var statusItem: NSStatusItem?
    private var storeObserver: AnyCancellable?
    private var themeObserver: AnyCancellable?
    private var panel: NSPanel?
    private var clickMonitor: Any?

    init(store: UsageStore, preferences: Preferences, onOpenSettings: @escaping () -> Void) {
        self.store = store
        self.preferences = preferences
        self.onOpenSettings = onOpenSettings
        super.init()
    }

    /// Shows the status item. Safe to call repeatedly.
    func install() {
        guard statusItem == nil else { return }
        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        guard let button = item.button else { return }
        button.imagePosition = .imageLeading
        button.target = self
        button.action = #selector(togglePanel)
        self.statusItem = item

        refreshButton()
        storeObserver = store.objectWillChange.sink { [weak self] _ in
            DispatchQueue.main.async { self?.refreshButton() }
        }
        themeObserver = preferences.objectWillChange.sink { [weak self] _ in
            DispatchQueue.main.async { self?.refreshButton() }
        }
    }

    /// Removes the status item.
    func remove() {
        closePanel()
        storeObserver?.cancel()
        storeObserver = nil
        themeObserver?.cancel()
        themeObserver = nil
        if let statusItem { NSStatusBar.system.removeStatusItem(statusItem) }
        statusItem = nil
    }

    /// Updates the status button's image + title from the current store state.
    private func refreshButton() {
        guard let button = statusItem?.button else { return }
        switch store.state {
        case .error(.claudeCodeNotFound):
            button.image = nil
            button.title = "✳ –"
        case .error:
            button.image = nil
            button.title = "✳ !"
        case .loading where store.snapshot == nil:
            button.image = nil
            button.title = "✳ …"
        default:
            let percent = store.snapshot?.session.percent ?? 0
            let color = NSColor(preferences.theme.color(for: UsageLevel.forPercent(percent)))
            button.image = Self.ringImage(percent: percent, color: color)
            button.title = " \(Int(percent.rounded()))%"
        }
    }

    /// Draws the session ring as a small image for the status button.
    private static func ringImage(percent: Double, color: NSColor,
                                  diameter: CGFloat = 15, lineWidth: CGFloat = 3) -> NSImage {
        let image = NSImage(size: NSSize(width: diameter, height: diameter))
        image.lockFocus()
        let center = NSPoint(x: diameter / 2, y: diameter / 2)
        let radius = (diameter - lineWidth) / 2

        let track = NSBezierPath()
        track.appendArc(withCenter: center, radius: radius, startAngle: 0, endAngle: 360)
        track.lineWidth = lineWidth
        NSColor.tertiaryLabelColor.setStroke()
        track.stroke()

        let fraction = min(max(percent / 100, 0), 1)
        if fraction > 0 {
            let progress = NSBezierPath()
            progress.appendArc(withCenter: center, radius: radius,
                               startAngle: 90, endAngle: 90 - 360 * fraction, clockwise: true)
            progress.lineWidth = lineWidth
            progress.lineCapStyle = .round
            color.setStroke()
            progress.stroke()
        }
        image.unlockFocus()
        image.isTemplate = false
        return image
    }

    // MARK: - Dropdown panel

    @objc private func togglePanel() {
        if panel != nil { closePanel() } else { openPanel() }
    }

    private func openPanel() {
        guard let button = statusItem?.button, let buttonWindow = button.window else { return }

        let content = DetailPanelView(
            store: store,
            preferences: preferences,
            onRefresh: { [weak self] in Task { await self?.store.refreshNow() } },
            onOpenSettings: { [weak self] in
                self?.closePanel()
                self?.onOpenSettings()
            }
        )
        .background(.regularMaterial)
        .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))

        let hosting = NSHostingController(rootView: content)
        let size = hosting.view.fittingSize

        let panel = NSPanel(
            contentRect: NSRect(origin: .zero, size: size),
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered, defer: false
        )
        panel.contentViewController = hosting
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = true
        panel.level = .popUpMenu
        panel.hidesOnDeactivate = false

        // Position: top edge flush under the button, horizontally centered on it,
        // clamped to the screen's visible area.
        let buttonInWindow = button.convert(button.bounds, to: nil)
        let buttonOnScreen = buttonWindow.convertToScreen(buttonInWindow)
        var x = buttonOnScreen.midX - size.width / 2
        let y = buttonOnScreen.minY - size.height
        if let visible = buttonWindow.screen?.visibleFrame {
            x = min(max(x, visible.minX + 8), visible.maxX - size.width - 8)
        }
        panel.setFrame(NSRect(x: x, y: y, width: size.width, height: size.height), display: true)
        panel.makeKeyAndOrderFront(nil)
        self.panel = panel
        store.setPanelVisible(true, source: .menuBar)

        // Dismiss when the user clicks anywhere outside the panel.
        clickMonitor = NSEvent.addGlobalMonitorForEvents(
            matching: [.leftMouseDown, .rightMouseDown]
        ) { [weak self] _ in
            self?.closePanel()
        }
    }

    private func closePanel() {
        if let clickMonitor {
            NSEvent.removeMonitor(clickMonitor)
            self.clickMonitor = nil
        }
        panel?.orderOut(nil)
        guard panel != nil else { return }
        panel = nil
        store.setPanelVisible(false, source: .menuBar)
    }
}
