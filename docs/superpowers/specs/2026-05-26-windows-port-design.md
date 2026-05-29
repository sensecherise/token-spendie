# Token Spendie — Windows Port Design

**Status:** Approved (brainstorming complete, ready for implementation plan).
**Date:** 2026-05-26.
**M0 spike findings:** [`docs/superpowers/findings/2026-05-26-windows-creds-spike.md`](../findings/2026-05-26-windows-creds-spike.md)
**Scope:** Native Windows app with full feature parity to the macOS Swift app. Mac app continues to ship from `Sources/TokenSpendie/` unchanged.

---

## Goals

Ship a Windows tray application that mirrors the macOS Token Spendie feature set:

- Tray icon shows session usage as a ring; color encodes threshold band.
- Click the tray to open a popup with session, weekly, and per-model windows.
- Optional always-on-top floating panel.
- Toast notifications at 50 / 70 / 90 / 99 % thresholds, once per window cycle, with the same joke copy as the mac build.
- Read OAuth credentials Claude Code already stored; never log the user in.
- Launch-at-login toggle.
- Auto-updating installer.

No new product features. Behavior diverges from mac only where Windows platform conventions require it (tray cells render icon only — percent text moves to tooltip).

## Non-goals

- Cross-platform single codebase. Mac app stays Swift; Windows app is standalone.
- Telemetry, crash reporting, file logging (parity with mac, which has none).
- Onboarding wizard, in-app account switching, settings sync.

---

## Stack

| Concern | Choice |
|---|---|
| Runtime | .NET 8 (LTS through Nov 2026) |
| UI | WPF |
| Language | C# 12, `nullable` enabled, `<TreatWarningsAsErrors>true` |
| MVVM | `CommunityToolkit.Mvvm` (source-generated `ObservableProperty` / `RelayCommand`) |
| Host / DI / config | `Microsoft.Extensions.Hosting` `GenericHost` |
| HTTP | `HttpClient` (built-in) with `SocketsHttpHandler { PooledConnectionLifetime = 5m }` |
| JSON | `System.Text.Json` (built-in) |
| Tray | `H.NotifyIcon.Wpf` |
| Fluent theming | `WPF-UI` |
| Toasts | `CommunityToolkit.WinUI.Notifications` |
| Tests | xUnit + FluentAssertions + NSubstitute |
| Installer / auto-update | Velopack |
| Code signing | SignPath.io OSS plan |
| Distribution | GitHub Releases + WinGet manifest in `microsoft/winget-pkgs` |

### Minimum Windows version

**Windows 11 22H2 or later (x64 and arm64).** Picked over Win10 to avoid the runtime branching that Mica/Acrylic and toast-rendering quirks would otherwise require. A Win10 backport can be considered post-v1 if user demand justifies the fallback code; until then the installer refuses on older systems.

---

## Top-level architecture

Single-instance `.exe`. No taskbar entry. The tray icon is the primary UI; closing any window does not exit the app. App lifetime is bound to the tray icon's lifetime.

- `Program.cs` builds a `Host`, parses startup args, starts WPF `App`.
- `App.xaml.cs` resolves singleton services: `UsageStore`, `PreferencesStore`, `UsageNotifier`, `IStartupAtLoginService`, providers, credential reader, `TrayIconController`.
- `UsageStore` runs the polling loop on a background `PeriodicTimer` with a `CancellationToken`. Updates raise `INotifyPropertyChanged` on the UI thread via `Application.Current.Dispatcher`.
- Tray-click and context-menu actions are commands on `TrayIconViewModel`, which delegates to `TrayIconController` for window lifecycle.

### Startup flags

| Flag | Effect |
|---|---|
| (none) | Normal launch. Tray installs. Popup auto-opens once (matches first-launch UX). |
| `--hidden` | Tray installs and services start. Popup does not auto-open. Floating panel is not restored even if `Preferences.FloatingPanelEnabled` is true; the user must click the tray to engage. Used by the Run-key launch-at-login entry. |
| `--verbose` | Writes operational messages to stderr. No file logging. Used for diagnosing user bug reports interactively. |

Argument parsing lives in `Program.cs` and surfaces as `StartupOptions` injected into `App`.

---

## Module mapping

One-to-one port of Swift modules to C# classes. Names and folder layout from §"Project layout".

| Swift (mac) | C# (Win) |
|---|---|
| `main.swift` | `Program.cs` |
| `AppDelegate.swift` | `App.xaml.cs` |
| `UI/MenuBarController` | `Tray/TrayIconController` + `TrayIconViewModel` |
| `UI/DetailPanelView` | `Views/DetailPanel.xaml` + `DetailPanelViewModel` |
| `UI/FloatingPanelController` | `Windows/FloatingPanelWindow` |
| `UI/PreferencesView` | `Windows/PreferencesWindow` + `PreferencesViewModel` |
| `UI/AboutView` | `Windows/AboutWindow` |
| `UI/Theme.swift` | `Themes/Theme.xaml` (ResourceDictionary) |
| `UI/Formatting.swift` | `Util/Formatting.cs` |
| `Store/UsageStore` | `Services/UsageStore` |
| `Store/SnapshotCache` | `Services/SnapshotCache` |
| `Store/Preferences` | `Services/PreferencesStore` |
| `Notifications/UsageNotifier` | `Services/UsageNotifier` |
| `Data/UsageProvider` | `Data/IUsageProvider` |
| `Data/ClaudeProvider` | `Data/ClaudeProvider` |
| `Data/GeminiProvider` + `GeminiUsageReader` | `Data/GeminiProvider` + `Data/GeminiUsageReader` |
| `Data/EndpointUsageProvider` | `Data/EndpointUsageProvider` |
| `Data/UsageDecoder` | `Data/UsageDecoder` |
| `Data/KeychainReader` | `Data/ClaudeJsonFileReader` behind `Data/ICredentialReader` |
| `Data/OAuthCredentials` | `Data/OAuthCredentials` + `Data/OAuthCredentialsParser` |
| `Model/UsageModels.swift` | `Models/*.cs` (one record per file) |

Concurrency: providers are `async`; polling on `Task.Run` + `PeriodicTimer`; UI marshalling via `Dispatcher.InvokeAsync`; deterministic shutdown via `IAsyncDisposable`.

---

## Credential reading

**Storage location** (confirmed by M0 spike, [findings](../findings/2026-05-26-windows-creds-spike.md)): `%USERPROFILE%\.claude\.credentials.json` — plain JSON, same `claudeAiOauth.{accessToken,refreshToken,expiresAt}` shape as the macOS Keychain blob. Credential Manager is unused; no DPAPI wrapping. The Windows JSON adds three advisory fields (`scopes`, `subscriptionType`, `rateLimitTier`) that the parser ignores — `System.Text.Json`'s default behaviour for unknown properties is to drop them, so the mac-derived `OAuthCredentials` record needs no change.

**Design:**

```csharp
public interface ICredentialReader
{
    bool CredentialsExist();
    Task<OAuthCredentials> LoadCredentialsAsync(CancellationToken ct = default);
}
```

One concrete implementation (the storage location the spike confirms). No chained reader — YAGNI until a second location is proven to exist in the wild.

`OAuthCredentialsParser` is unchanged from mac (the JSON shape is server-side, not platform-specific).

**Error model:** `NotFound`, `AccessDenied`, `Malformed` — same enum as mac, even though Windows rarely produces `AccessDenied` (no equivalent of Keychain consent prompts; ACL failures only).

**Retry on 401:** identical to mac. `ClaudeProvider` re-reads credentials once on `Unauthorized` and retries (Claude Code rewrites the file when it refreshes its token).

**Concurrent-write race** (G9): open with `FileShare.ReadWrite`, retry parse on `JsonException` with 50 ms backoff, three attempts max.

---

## Tray icon and ring drawing

Tray host: `H.NotifyIcon.Wpf.TaskbarIcon` declared in `App.xaml`, bound to `TrayIconViewModel` exposing `IconSource`, `ToolTipText`, `LeftClickCommand`, `ContextMenu`.

**Display divergence from mac:** Windows tray cells render icon only — no inline text. The percent reading moves to the tooltip and to the popup header. The mac string `🟢 47%` becomes the tooltip `Token Spendie — Session 47% · Weekly 23%`.

**Drawing:** pure WPF pipeline. `DrawingVisual` rendered to a `RenderTargetBitmap`, frozen, assigned to `IconSource`. No GDI handle management, no `DestroyIcon` calls.

```csharp
public static ImageSource RenderRingIcon(double percent, Color color, double dpiScale)
{
    int px = (int)Math.Ceiling(16 * dpiScale);   // 16, 20, 24, 32
    var visual = new DrawingVisual();
    using (var dc = visual.RenderOpen())
    {
        var center = new Point(px / 2.0, px / 2.0);
        double stroke = Math.Max(2.0, px / 8.0);
        double radius = (px - stroke) / 2.0;

        dc.DrawEllipse(null, new Pen(TrackBrush, stroke), center, radius, radius);

        var fraction = Math.Clamp(percent / 100.0, 0, 1);
        if (fraction > 0)
        {
            var fig = new PathFigure { StartPoint = new Point(center.X, center.Y - radius) };
            fig.Segments.Add(new ArcSegment(
                PointOnCircle(center, radius, -90 + 360 * fraction),
                new Size(radius, radius),
                rotationAngle: 0,
                isLargeArc: fraction > 0.5,
                sweepDirection: SweepDirection.Clockwise,
                isStroked: true));
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(color), stroke)
                { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
                new PathGeometry(new[] { fig }));
        }
    }
    var rtb = new RenderTargetBitmap(px, px, 96 * dpiScale, 96 * dpiScale, PixelFormats.Pbgra32);
    rtb.Render(visual);
    rtb.Freeze();
    return rtb;
}
```

**DPI awareness:** app manifest declares `PerMonitorV2`. The controller subscribes to `Window.DpiChanged` on the popup window and re-renders the icon at the new scale; `H.NotifyIcon` picks up the new `IconSource` without flicker.

**Icon states (parity with mac):**

| State | Icon | Tooltip |
|---|---|---|
| No provider detected | grey dot | "Claude Code not found" |
| Error | red `!` overlay on ring | error message |
| Loading (no snapshot yet) | grey ring with rotating accent (16 fps via `DispatcherTimer`) | "Loading…" |
| Normal | colored ring | "Session N% · Weekly M%" |

Color comes from `Preferences.Theme.ColorFor(UsageLevel.ForPercent(p))` — same enum semantics as mac.

**Interactions:**

- Left-click → toggle popup.
- Right-click → context menu (Refresh, Preferences…, About, Quit, Launch-at-login toggle).
- Hover → tooltip.
- Double-click → show floating panel (if enabled).

**Win11 tray overflow (G1):** Windows 11 hides new tray icons in the overflow flyout by default with no programmatic API to pin them. First-run popup includes a one-time instruction with screenshot ("To keep Token Spendie visible: Settings > Personalization > Taskbar > Other system tray icons → Token Spendie → On"). Documented in README.

---

## Popup panel and floating panel

### Anchored popup

Borderless `Window`:

```xml
<Window WindowStyle="None" ResizeMode="NoResize" ShowInTaskbar="False"
        AllowsTransparency="True" Background="Transparent" Topmost="True"
        SizeToContent="WidthAndHeight" />
```

Content root: a `Border` with `CornerRadius=12`, background set to WPF-UI Mica brush (Win11 22H2+), drop shadow via `Effect`.

**Positioning** (Windows 11 taskbar can sit on any edge; never assume bottom):

1. Query the tray-icon rect via `Shell_NotifyIconGetRect` (P/Invoke in `Tray/TrayIconLocator`).
2. Query the taskbar edge via `SHAppBarMessage(ABM_GETTASKBARPOS)`.
3. Position the popup flush against the taskbar edge, centered on the icon's perpendicular axis, then clamp to the working-area rect of the icon's screen.
4. `WindowStartupLocation=Manual`; `Left`/`Top` are set in DIPs (convert from device pixels using the icon's monitor DPI via `DpiHelper`).

**Fallback (G2):** if `Shell_NotifyIconGetRect` returns failure (the icon is in the Win11 overflow flyout), place the popup at the cursor position, clamped to the working area.

**Dismissal:**

- Primary: `Window.Deactivated` (focus leaves) → close.
- Fallback: low-level mouse hook (`SetWindowsHookExW(WH_MOUSE_LL)`) installed only while the popup is open. Mirrors the mac `NSEvent.addGlobalMonitorForEvents` approach.
- `Esc` → close.

Single-instance: `TrayIconController` holds the `Window` reference; the left-click command toggles.

### Floating always-on-top panel

Separate `Window`:

```xml
<Window WindowStyle="None" ResizeMode="CanResizeWithGrip" ShowInTaskbar="False"
        Topmost="True" MinWidth="280" MinHeight="180"
        AllowsTransparency="True" Background="Transparent" />
```

- Custom title bar invokes `DragMove()` on `MouseLeftButtonDown` (guarded against the `LeftButton != Pressed` exception, G5).
- Position and size persisted in `PreferencesStore`; validated against `SystemParameters.WorkArea` on load (reset to default if no overlap with any connected display — handles monitor disconnect, G7).
- Close button hides the window (does not exit). Re-shown from the Preferences toggle or the tray context menu.

### Shared content user control

Both the popup and the floating-panel windows host one instance each of `Views/DetailPanel.xaml` (a `UserControl` bound to `DetailPanelViewModel`). Both wire to the same singleton `UsageStore`. Updates flow via `INotifyPropertyChanged`.

### Edit menu (not needed)

WPF `TextBox` ships with Ctrl-X/C/V/A handlers and a default context menu regardless of taskbar presence — no Windows analog of the mac `LSUIElement` Edit-menu workaround is required.

---

## Toast notifications

API: `CommunityToolkit.WinUI.Notifications` wrapping WinRT `ToastNotificationManager`. No Windows App SDK runtime dependency.

### AppUserModelID (AUMID) requirement

Windows surfaces toasts only from apps with a registered AUMID and a matching Start Menu shortcut. Both are created at install time by Velopack (Section "Distribution"). For belt-and-suspenders robustness, on first launch the app also calls `DesktopNotificationManagerCompat.RegisterAumidAndComServer("Sensecherise.TokenSpendie", iconPath)` — handles the case where the user copied an unpacked build instead of running the installer (G4).

### Logic

`Services/UsageNotifier` ports `UsageNotifier.swift` line-for-line:

- Same thresholds `[50, 70, 90, 99]`.
- Same per-window dedup keyed by `{providerID}.{windowLabel}` with rollover detection via `resetsAt` comparison.
- Same session and weekly joke tables — copied verbatim as `static readonly` dictionaries.
- Persistence written to `%APPDATA%\TokenSpendie\notifier-state.json`.

### Sending

```csharp
new ToastContentBuilder()
    .AddText(joke.Title)
    .AddText(joke.Body)
    .AddArgument("threshold", id)
    .Show(toast => toast.Tag = id);   // Tag dedups across restarts
```

The toast sender is wrapped behind a thin `IToastSender` interface so tests can assert calls without invoking WinRT.

### Focus Assist / DND (G10)

Windows suppresses toast surfacing during Focus Assist but the app still receives a "delivered" callback. This matches the mac DND posture — do not try to override.

---

## Launch-at-login

Abstraction:

```csharp
public interface IStartupAtLoginService
{
    bool IsEnabled();
    void Enable();
    void Disable();
}
```

Bound to `Preferences.LaunchAtLogin`. Same revert-on-failure pattern as mac (`PreferencesView.swift:81`).

**Implementation:** `RegistryRunKeyStartupService` writes to `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`:

```
TokenSpendie = "C:\Users\<user>\AppData\Local\Programs\TokenSpendie\TokenSpendie.exe" --hidden
```

- Per-user, no admin elevation.
- The `--hidden` flag is the contract defined in "Startup flags" above.
- `IsEnabled()` reads the value and compares it to `Process.GetCurrentProcess().MainModule.FileName`; mismatched paths trigger a re-write (handles user-relocated installs).
- `Disable()` deletes the value.

The tray right-click menu surfaces the same toggle for parity with the Preferences pane.

(MSIX startup task is not in scope — we ship via Velopack, not MSIX. See "Distribution".)

---

## Distribution

### Installer: Velopack

A single `.exe` installer. Per-user install to `%LOCALAPPDATA%\Programs\TokenSpendie`; no admin elevation. Velopack handles:

- Start Menu shortcut with the `System.AppUserModel.ID` property set (satisfies the toast AUMID requirement).
- Add/Remove Programs entry.
- Delta auto-update from a static feed hosted on GitHub Releases.
- Uninstall hook that removes the Run-key entry and the AUMID registration.

Update check policy: once per day on a background timer; silent install on next restart.

### Channels

| Channel | Windows | Mac parity |
|---|---|---|
| Package manager | `winget install Sensecherise.TokenSpendie` (manifest in `microsoft/winget-pkgs`) | `brew install --cask sensecherise/tap/token-spendie` |
| Direct download | `TokenSpendie-Setup-<version>.exe` on GitHub Releases | `TokenSpendie-<version>.pkg` |
| Auto-update | Velopack feed → GitHub Releases | `brew upgrade` |

Both platforms publish to the same GitHub Release per `v*.*.*` tag. Mac asset name (`TokenSpendie-<version>.zip`) is preserved so the existing Homebrew cask URL is unaffected.

### Code signing

**SignPath.io OSS plan.** Free EV-grade trust for open-source projects with GitHub Actions integration. Apply once the repo's Windows directory has activity. Until SignPath is approved, the first release ships unsigned and the README documents the SmartScreen "More info → Run anyway" workaround. (G6: signed builds avoid the false-positive AV flags that frequently hit fresh unsigned self-contained `.exe` files.)

### CI release pipeline

Existing workflow `.github/workflows/ci-pipeline.yaml` is renamed to `build-macos` and a sibling `build-windows` job is added under the same `on: push: tags: ['v*.*.*']` trigger. Both jobs append assets to the same release; `softprops/action-gh-release@v2` is idempotent on a shared tag.

```yaml
build-windows:
  runs-on: windows-latest
  permissions:
    contents: write
  steps:
    - uses: actions/checkout@v4
    - uses: actions/setup-dotnet@v4
      with: { dotnet-version: '8.0.x' }
    - name: Restore + test
      run: |
        dotnet restore windows/TokenSpendie.Windows.sln
        dotnet test  windows/TokenSpendie.Windows.sln -c Release --no-restore
    - name: Publish
      shell: pwsh
      run: |
        $v = $env:GITHUB_REF_NAME -replace '^v',''
        dotnet publish windows/src/TokenSpendie.Windows -c Release -r win-x64 `
          --self-contained -p:PublishSingleFile=true -p:Version=$v `
          -o windows/build/publish-x64
    - name: Velopack pack
      shell: pwsh
      run: |
        dotnet tool install --global vpk
        vpk pack --packId Sensecherise.TokenSpendie --packVersion $v `
          --packDir windows/build/publish-x64 --mainExe TokenSpendie.exe `
          --icon windows/src/TokenSpendie.Windows/Resources/AppIcon.ico `
          --outputDir windows/build/release
    - name: SignPath submit
      uses: signpath/github-action-submit-signing-request@v1
      with:
        api-token: ${{ secrets.SIGNPATH_API_TOKEN }}
        organization-id: ${{ vars.SIGNPATH_ORG_ID }}
        project-slug: token-spendie
        signing-policy-slug: release-signing
        artifact-configuration-slug: installer
        wait-for-completion: true
        output-artifact-directory: windows/build/signed
    - uses: softprops/action-gh-release@v2
      with:
        files: windows/build/signed/*.exe
    - name: Update WinGet manifest
      env: { WINGET_TOKEN: ${{ secrets.WINGET_PR_TOKEN }} }
      run: pwsh windows/scripts/update-winget.ps1 -Version $v
```

The job runs twice in a matrix for `win-x64` and `win-arm64`.

### Why not MSIX

Cleaner in theory (auto AUMID, declarative startup task) but the install friction (Store-runtime feel, per-machine quirks, sideload-cert burden) outweighs the wins for a small tray app. Velopack covers AUMID and startup via standard registry + shortcut writes with less ceremony, and the WinGet listing covers the official-package-manager angle either way.

---

## Project layout

```
claude-widget/
  Package.swift                       # mac, unchanged
  Sources/TokenSpendie/                # mac, unchanged
  build.sh                             # mac, unchanged
  scripts/package.sh                   # mac, unchanged
  Resources/AppIcon-1024.png           # shared — Win build derives AppIcon.ico
  .github/workflows/ci-pipeline.yaml   # adds build-windows job
  windows/
    TokenSpendie.Windows.sln
    Directory.Build.props              # TFM net8.0-windows, nullable, warnings-as-errors
    src/
      TokenSpendie.Windows/
        TokenSpendie.Windows.csproj    # WPF, OutputType=WinExe, RID=win-x64;win-arm64
        App.xaml / App.xaml.cs
        Program.cs
        AssemblyInfo.cs
        app.manifest                   # PerMonitorV2 DPI, asInvoker
        Resources/
          AppIcon.ico
          Strings.resx
        Themes/Theme.xaml
        Tray/
          TrayIconController.cs
          RingIconRenderer.cs
          TrayIconLocator.cs           # Shell_NotifyIconGetRect, ABM_GETTASKBARPOS
        Windows/
          PopupWindow.xaml(.cs)
          FloatingPanelWindow.xaml(.cs)
          PreferencesWindow.xaml(.cs)
          AboutWindow.xaml(.cs)
        Views/DetailPanel.xaml(.cs)    # shared UserControl
        ViewModels/
          TrayIconViewModel.cs
          DetailPanelViewModel.cs
          PreferencesViewModel.cs
        Services/
          UsageStore.cs
          SnapshotCache.cs
          PreferencesStore.cs
          UsageNotifier.cs
          IToastSender.cs
          ToastSender.cs
          StartupAtLogin/
            IStartupAtLoginService.cs
            RegistryRunKeyStartupService.cs
        Data/
          IUsageProvider.cs
          ClaudeProvider.cs
          GeminiProvider.cs
          GeminiUsageReader.cs
          EndpointUsageProvider.cs
          UsageDecoder.cs
          ICredentialReader.cs
          ClaudeJsonFileReader.cs
          OAuthCredentials.cs
          OAuthCredentialsParser.cs
        Models/
          UsageWindow.cs
          UsageSnapshot.cs
          ProviderSnapshot.cs
          LabeledWindow.cs
          ProviderID.cs
          UsageLevel.cs
        Util/
          Formatting.cs
          DpiHelper.cs
          StartupOptions.cs
    tests/
      TokenSpendie.Windows.Tests/
        TokenSpendie.Windows.Tests.csproj   # xUnit, FluentAssertions, NSubstitute
        Data/
          OAuthCredentialsParserTests.cs
          UsageDecoderTests.cs
          ClaudeProviderTests.cs
          GeminiUsageReaderTests.cs
        Services/
          UsageNotifierTests.cs
          SnapshotCacheTests.cs
          PreferencesStoreTests.cs
        Util/
          FormattingTests.cs
        Fixtures/                       # JSON fixtures copied from mac tests
    build.ps1                           # local build → windows/build/
    scripts/
      make-ico.ps1                      # Resources/AppIcon-1024.png → AppIcon.ico
      update-winget.ps1
```

---

## Settings storage

| Data | Path |
|---|---|
| Preferences (theme, refresh interval, launch-at-login, floating-panel state, position) | `%APPDATA%\TokenSpendie\prefs.json` (roaming) |
| Cached snapshot | `%LOCALAPPDATA%\TokenSpendie\snapshot.json` |
| Notifier fired-threshold state | `%APPDATA%\TokenSpendie\notifier-state.json` |

JSON files (not registry) to match mac `UserDefaults` ergonomics, and so the on-disk state is inspectable. The registry is touched only for the launch-at-login Run-key entry.

---

## Testing

Framework: xUnit + FluentAssertions + NSubstitute.

Coverage targets (mirror mac):

- `OAuthCredentialsParser` — same JSON shapes the mac tests use, including the seconds-vs-milliseconds heuristic.
- `UsageDecoder` — same fixtures.
- `ClaudeProvider` — retry-on-401 via fake `IEndpointUsageProvider` + fake `ICredentialReader`.
- `GeminiUsageReader` — temp-directory filesystem tests for `logs.json` counting and `nextLocalMidnight()`.
- `UsageNotifier` — threshold dedup, window-rollover detection, persistence round-trip. Toast send mocked via `IToastSender`.
- `SnapshotCache`, `PreferencesStore` — round-trip and corruption-tolerant load.
- `Formatting` — numbers, durations, percent.

No UI tests. Views are thin (binding only); ViewModels are unit-tested. Same posture as the mac repo.

JSON fixtures are copied byte-for-byte from `Tests/TokenSpendieTests/` so the parser behavior on Windows is verifiably identical to mac.

---

## Risks and unknowns

### Hard unknowns

| # | Unknown | Resolution path |
|---|---|---|
| U1 | ~~Where Claude Code stores OAuth credentials on Windows~~ | **Resolved (M0):** `%USERPROFILE%\.claude\.credentials.json` (plain JSON). |
| U2 | ~~Whether Claude Code on Windows uses the same `claudeAiOauth.{accessToken,refreshToken,expiresAt}` JSON shape~~ | **Resolved (M0):** same `claudeAiOauth.*` shape; three extra non-required fields (`scopes`, `subscriptionType`, `rateLimitTier`) ignored by the parser. `expiresAt` is in milliseconds. |
| U3 | ~~Gemini CLI Windows paths for `logs.json` and OAuth credentials~~ | **Resolved (M0):** OAuth at `%USERPROFILE%\.gemini\oauth_creds.json` (same as mac). **Logs format diverges:** Gemini CLI v0.43.0 leaves `logs.json` empty and writes prompts to `%USERPROFILE%\.gemini\tmp\<project>\chats\session-*.jsonl` (JSONL, `content:[{text}]`). M3 reader must consume the JSONL session files, not `logs.json`. See [findings](../findings/2026-05-26-windows-creds-spike.md) §U3b. |
| U4 | SignPath.io OSS plan acceptance | Apply once the Windows directory has activity. Fallback: ship unsigned for v1; add a paid OV cert if grant is denied. |
| U5 | WinGet listing approval cadence | Microsoft moderation takes 1–7 days first time. First release does not gate on WinGet; manifest submitted asynchronously. |

### Likely gotchas, with mitigations

| # | Gotcha | Mitigation |
|---|---|---|
| G1 | Windows 11 hides new tray icons in the overflow flyout by default; no API to pin. | First-run instruction popup with screenshot; documented in README. |
| G2 | `Shell_NotifyIconGetRect` returns `E_FAIL` for icons in the Win11 overflow flyout. | Detect failure → fall back to cursor position for popup placement. |
| G4 | Toasts silently dropped if the AUMID-tagged Start Menu shortcut is missing. | Belt-and-suspenders: call `DesktopNotificationManagerCompat.RegisterAumidAndComServer` on first launch in addition to the Velopack install-hook registration. |
| G5 | `DragMove()` throws if called outside a left-button-down handler. | Guard `e.LeftButton == MouseButtonState.Pressed`. |
| G6 | Self-contained single-file `.exe` bundles ~40 MB of CLR; antivirus heuristics may flag fresh unsigned builds. | SignPath signing from day one; if flagged, submit to VirusTotal and vendor whitelists. |
| G7 | Floating panel may restore off-screen after a monitor disconnect. | Validate position against `SystemParameters.WorkArea` for each connected display; reset to default if no overlap. |
| G8 | `HttpClient` DNS caching stale across network changes on a long-lived process. | `SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }`. |
| G9 | Claude Code may rewrite the credentials file while we read it → partial JSON. | Open with `FileShare.ReadWrite`, retry parse on `JsonException` with 50 ms backoff, three attempts max. |
| G10 | Focus Assist / DND suppresses toast surfacing but the app still receives "delivered" callbacks. | Acceptable, mirrors mac DND. Do not override. |
| G11 | Per-monitor DPI changes redraw the icon; `RenderTargetBitmap` allocations leak GPU memory if not frozen. | Always `Freeze()` the bitmap before assigning. |
| G12 | DPAPI credentials are tied to the Windows user profile; copying `.credentials.json` between users breaks decryption. | Document — owned by Claude Code, not Token Spendie. |

(G3 from the brainstorm — Win10 Mica/Acrylic fallback — is dropped because the minimum supported version is Windows 11 22H2.)

---

## Implementation milestones

| Milestone | Scope | Estimate |
|---|---|---|
| M0 — Spike | Resolve U1 / U2 / U3 on a real Windows box. If credentials live somewhere wildly different from the candidates, revise Section "Credential reading" before proceeding. | 1–2 days |
| M1 — Core | Models, data layer, providers, full test coverage. Headless console binary that prints snapshots — proves the data path end to end. | 1 week |
| M2 — Tray + popup | `TrayIconController`, `RingIconRenderer`, `PopupWindow`, `DetailPanel`. Single screen, no Preferences yet. | 1 week |
| M3 — Preferences + floating panel + notifications | `PreferencesWindow`, `FloatingPanelWindow`, `UsageNotifier`, `RegistryRunKeyStartupService`. | 1 week |
| M4 — Installer + CI + signing | Velopack pack, SignPath integration, GitHub Actions Windows job, first signed release. | 3–5 days |
| M5 — WinGet submission | PR to `microsoft/winget-pkgs`. Passive wait for Microsoft moderation. | 1–7 days elapsed |

Total: ~4–5 weeks for one engineer.
