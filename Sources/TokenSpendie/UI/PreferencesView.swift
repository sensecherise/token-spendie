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
    let manualTokenStore: ManualTokenStore
    @State private var draftToken: String = ""
    @State private var tokenSaved: Bool = false
    @State private var verifyState: TokenVerifyState = .idle
    @State private var verifyTask: Task<Void, Never>?
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

            VStack(alignment: .leading, spacing: 8) {
                Text("CREDENTIAL").font(.system(size: 10, weight: .heavy)).foregroundStyle(.secondary)
                Picker("Source", selection: $preferences.credentialMode) {
                    ForEach(CredentialMode.allCases) { Text($0.label).tag($0) }
                }
                if preferences.credentialMode == .manual {
                    HStack(spacing: 5) {
                        Image(systemName: tokenSaved ? "checkmark.circle.fill" : "exclamationmark.circle")
                            .foregroundStyle(tokenSaved ? Color.green : Color.secondary)
                        Text(tokenSaved ? "Token saved" : "No token saved")
                            .font(.system(size: 10)).foregroundStyle(.secondary)
                    }
                    Text("Run `claude setup-token` in Terminal, then paste the token below.")
                        .font(.system(size: 10)).foregroundStyle(.secondary)
                    SecureField("Paste token", text: $draftToken)
                    HStack(spacing: 8) {
                        Button("Save") { saveAndVerify() }
                            .disabled(draftToken.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
                                      || verifyState == .verifying)
                        verifyIndicator
                        Spacer()
                        if tokenSaved {
                            Button("Clear") { clearToken() }
                        }
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
            tokenSaved = manualTokenStore.hasToken
        }
    }

    private func swatch(_ color: Color) -> some View {
        RoundedRectangle(cornerRadius: 3).fill(color).frame(width: 14, height: 14)
    }

    /// Inline state for the save-then-verify flow.
    private enum TokenVerifyState: Equatable { case idle, verifying, verified, rejected, unreachable }

    /// The inline spinner / result indicator next to the Save button.
    @ViewBuilder private var verifyIndicator: some View {
        switch verifyState {
        case .idle:
            EmptyView()
        case .verifying:
            HStack(spacing: 5) {
                ProgressView().controlSize(.small)
                Text("Checking…").font(.system(size: 10)).foregroundStyle(.secondary)
            }
        case .verified:
            Label("Token works", systemImage: "checkmark.circle.fill")
                .font(.system(size: 10)).foregroundStyle(.green)
        case .rejected:
            Label("Server rejected the token", systemImage: "xmark.circle.fill")
                .font(.system(size: 10)).foregroundStyle(.red)
        case .unreachable:
            Label("Saved — couldn't verify yet", systemImage: "questionmark.circle")
                .font(.system(size: 10)).foregroundStyle(.orange)
        }
    }

    /// Saves the token immediately, then checks it against the usage endpoint.
    /// The token is saved regardless of the check result — a rate-limited or
    /// unreachable endpoint must not block saving.
    private func saveAndVerify() {
        let token = draftToken.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !token.isEmpty else { return }
        verifyTask?.cancel()
        try? manualTokenStore.save(token: token)
        tokenSaved = manualTokenStore.hasToken
        draftToken = ""
        verifyState = .verifying
        verifyTask = Task { @MainActor in
            do {
                _ = try await EndpointUsageProvider().fetchUsage(accessToken: token)
                if Task.isCancelled { return }
                verifyState = .verified
            } catch ProviderError.unauthorized {
                if Task.isCancelled { return }
                verifyState = .rejected
            } catch {
                if Task.isCancelled { return }
                verifyState = .unreachable
            }
        }
    }

    /// Clears the stored token and the field.
    private func clearToken() {
        verifyTask?.cancel()
        manualTokenStore.clear()
        draftToken = ""
        tokenSaved = false
        verifyState = .idle
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
