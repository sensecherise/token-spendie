import SwiftUI
import ServiceManagement

/// Wraps `SMAppService` so launch-at-login can be toggled and queried.
enum LoginItem {
    static var isEnabled: Bool {
        SMAppService.mainApp.status == .enabled
    }

    /// Returns true on success. A failure (e.g. unsigned build restrictions) is reported back
    /// so the UI can revert the toggle.
    @discardableResult
    static func setEnabled(_ enabled: Bool) -> Bool {
        do {
            if enabled {
                try SMAppService.mainApp.register()
            } else {
                try SMAppService.mainApp.unregister()
            }
            return true
        } catch {
            return false
        }
    }
}

struct PreferencesView: View {
    @ObservedObject var preferences: Preferences
    var onDisplayChanged: () -> Void
    var onIntervalChanged: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            Text("Claude Usage Widget").font(.system(size: 14, weight: .bold))

            VStack(alignment: .leading, spacing: 8) {
                Text("DISPLAY").font(.system(size: 10, weight: .heavy)).foregroundStyle(.secondary)
                Toggle("Show menu bar item", isOn: $preferences.showMenuBar)
                    .onChange(of: preferences.showMenuBar) { _ in enforceAtLeastOneSurface(changed: .menuBar) }
                Toggle("Show floating panel", isOn: $preferences.showFloatingPanel)
                    .onChange(of: preferences.showFloatingPanel) { _ in enforceAtLeastOneSurface(changed: .floating) }
            }

            VStack(alignment: .leading, spacing: 8) {
                Text("REFRESH").font(.system(size: 10, weight: .heavy)).foregroundStyle(.secondary)
                Picker("Interval", selection: $preferences.refreshInterval) {
                    ForEach(RefreshInterval.allCases) { Text($0.label).tag($0) }
                }
                .onChange(of: preferences.refreshInterval) { _ in onIntervalChanged() }
            }

            Toggle("Launch at login", isOn: $preferences.launchAtLogin)
                .onChange(of: preferences.launchAtLogin) { newValue in
                    if !LoginItem.setEnabled(newValue) {
                        preferences.launchAtLogin = LoginItem.isEnabled   // revert on failure
                    }
                }

            HStack {
                Spacer()
                Button("Quit") { NSApp.terminate(nil) }
            }
        }
        .padding(20)
        .frame(width: 300)
        .onAppear { preferences.launchAtLogin = LoginItem.isEnabled }
    }

    private enum Surface { case menuBar, floating }

    /// At least one display surface must stay enabled; re-enable the other if both go off.
    private func enforceAtLeastOneSurface(changed: Surface) {
        if !preferences.showMenuBar && !preferences.showFloatingPanel {
            switch changed {
            case .menuBar: preferences.showFloatingPanel = true
            case .floating: preferences.showMenuBar = true
            }
        }
        onDisplayChanged()
    }
}
