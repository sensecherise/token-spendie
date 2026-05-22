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
            Text("Token Spendie").font(.system(size: 14, weight: .bold))

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

            VStack(alignment: .leading, spacing: 8) {
                Text("APPEARANCE").font(.system(size: 10, weight: .heavy)).foregroundStyle(.secondary)
                HStack(spacing: 8) {
                    ForEach(Theme.allCases) { theme in
                        Button { preferences.theme = theme } label: {
                            VStack(spacing: 4) {
                                HStack(spacing: 2) {
                                    swatch(theme.color(for: .calm))
                                    swatch(theme.color(for: .warn))
                                    swatch(theme.color(for: .hot))
                                }
                                Text(theme.displayName).font(.system(size: 9))
                            }
                            .padding(6)
                            .background(
                                RoundedRectangle(cornerRadius: 6)
                                    .fill(preferences.theme == theme
                                          ? Color.accentColor.opacity(0.25)
                                          : Color.clear)
                            )
                        }
                        .buttonStyle(.plain)
                    }
                }
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
        .onAppear {
            preferences.launchAtLogin = LoginItem.isEnabled
        }
    }

    private func swatch(_ color: Color) -> some View {
        RoundedRectangle(cornerRadius: 3).fill(color).frame(width: 14, height: 14)
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
