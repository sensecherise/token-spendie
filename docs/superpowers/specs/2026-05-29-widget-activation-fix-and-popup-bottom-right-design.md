# Widget Activation Fix + Tray Popup Bottom-Right (Design)

> **Date:** 2026-05-29
> **Branch off:** `windows-port-m6-widget` (M6 widget PR #24, Tasks 1–8 already landed there)
> **Targets:** unstick the empty Widget Board card, move the tray popup from bottom-left to bottom-right, ship a one-command sideload script, and surface a meaningful empty state when neither Claude nor Gemini credentials are present.

## Problem statement

After installing the M6 widget MSIX (`Sensecherise.TokenSpendie.WidgetProvider_1.0.0.0_x64`) and pinning *Token Spendie — Full* on a Windows 11 Widget Board:

- The widget host renders the title bar (`Token Spendie — Full`) and the host's loading skeleton (grey rectangles), but never replaces the skeleton with our Adaptive Card content. No `TokenSpendie.WidgetProvider.exe` process is visible while the board is open; no entry appears in the `Application` event log. The exe itself runs cleanly when launched manually with `-RegisterProcessAsComServer`, so the binary is fine — the widget host is not activating the COM server.
- Separately, the tray ring icon's click-popup (`WidgetsBoardWindow`) currently anchors bottom-left. The user wants it bottom-right so it lands on the same corner of the screen as the Win11 system tray.

## Goals

1. Get the widget board to render the live Session / Full Adaptive Cards from the MSIX-installed provider.
2. When no CLI credentials are present, render a clean "No CLI detected" card instead of pushing a 0% reading or letting the host's skeleton sit forever.
3. Move the tray popup anchor to bottom-right with the same 8 px gutter as today's bottom-left.
4. Replace the inline manual-sideload sequence used during M6 testing with a checked-in `windows/scripts/sideload-msix.ps1` so future iterations are one command.

## Non-goals

- Microsoft Store submission.
- Replacing the Velopack tray installer with the MSIX. Both surfaces still ship independently.
- Adding a `tokenspendie://` URL-scheme handler in the tray app (the medium Full card's *Open Token Spendie* action keeps pointing at it as a hook for a future PR).
- Implementing M6 Tasks 9–13 (SignPath production signing, CI MSIX job, README updates) — those continue as planned downstream.

## Root-cause hypothesis (widget activation)

Manifest comparison against `microsoft/WindowsAppSDK-Samples/Samples/Widgets/cs-console-packaged/CsConsoleWidgetProvider/Package.appxmanifest` reveals five structural divergences from a known-working sample:

| Element | Current `Package.appxmanifest` | MS sample |
|---|---|---|
| `IgnorableNamespaces` | `uap uap3 com rescap` | `uap uap3 rescap` (com xmlns declared but not ignorable) |
| `TargetDeviceFamily` | only `Windows.Desktop` | both `Windows.Universal` and `Windows.Desktop` |
| `Application Id` | `TokenSpendieWidgetProvider` | `App` |
| `EntryPoint` | `Windows.FullTrustApplication` | (absent — UWP default `<RootNamespace>.Program`) |
| `Capabilities` | `<rescap:Capability Name="runFullTrust" />` | (no capability block; widget provider runs as UWP-packaged) |
| `AppListEntry` | `none` | (absent — default visible) |

The full-trust desktop-bridge mode changes the COM activation context that the widget host uses to instantiate the registered `<com:Class>`. The host can read the `windows.appExtension` block (which is why the widget shows up in *Pin widgets* with the correct display name) but the corresponding `CoCreateInstance(CLSID_WidgetProvider, CLSCTX_LOCAL_SERVER, ...)` call never reaches our exe. No entry appears in the `Application` event log because the OS COM SCM is the layer rejecting / silently dropping the activation, not the exe itself.

The fix is to align with the sample shape: declare the provider as a UWP-packaged app, drop full-trust, and grant `broadFileSystemAccess` so the sandboxed provider can still read `~/.claude` and `~/.gemini`.

## Design

### 1. Manifest realign

`windows/src/TokenSpendie.WidgetProvider/Package.appxmanifest`:

- Drop `com` from `IgnorableNamespaces` (xmlns declaration stays).
- Add the `Windows.Universal` `TargetDeviceFamily` alongside `Windows.Desktop`. Both bound to the same `MinVersion="10.0.19041.0"` / `MaxVersionTested="10.0.22621.0"`.
- Change `<Application Id="TokenSpendieWidgetProvider"...>` → `<Application Id="App"...>`.
- Replace `EntryPoint="Windows.FullTrustApplication"` with `EntryPoint="TokenSpendie.WidgetProvider.Program"`.
- Drop `AppListEntry="none"` from `<uap:VisualElements>`. The package becomes visible in Start; acceptable for the widget-only install path (users who want only the tray app use the Velopack installer).
- Replace the `<Capabilities>` block with `<rescap:Capability Name="broadFileSystemAccess" />`. `runFullTrust` goes away.

The `<com:Extension Category="windows.comServer">` and `<uap3:Extension Category="windows.appExtension">` blocks keep their existing CLSID (`579614A3-768E-46A5-846C-78784B4232A1`) and the two `<Definition>` entries (`TokenSpendie.Session` small, `TokenSpendie.Full` medium) unchanged.

### 2. csproj cleanup

`windows/src/TokenSpendie.WidgetProvider/TokenSpendie.WidgetProvider.csproj`:

- Drop `<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>`. That property was a band-aid for the MSB3824 errors that appeared during the full-trust packaging attempt in M6 Task 3; with the manifest moving to a proper UWP-packaged shape, the runtime resolves via package dependency on `Microsoft.WindowsAppRuntime.1.8` (already installed system-wide on the dev box).
- Drop the singular `<RuntimeIdentifier>win-x64</RuntimeIdentifier>` introduced as a side-effect of `WindowsAppSDKSelfContained`. Keep `<Platforms>x64</Platforms>` only.
- Keep `<EnableMsixTooling>true</EnableMsixTooling>`, `<AppxPackage>true</AppxPackage>`, `<UseWPF>true</UseWPF>` (still needed for `RingPngRenderer`).

Expected outcome: MSIX shrinks from ~51 MB to ~3-5 MB because the WinAppSDK runtime is no longer bundled.

### 3. Empty-state card

New `windows/src/TokenSpendie.WidgetProvider/Cards/EmptyCard.json`:

```json
{
  "type": "AdaptiveCard",
  "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
  "version": "1.5",
  "body": [
    { "type": "TextBlock", "text": "Token Spendie", "weight": "Bolder", "size": "Medium" },
    { "type": "TextBlock", "text": "No CLI detected.", "weight": "Bolder", "spacing": "Small" },
    { "type": "TextBlock",
      "text": "Install Claude Code or Gemini CLI to start tracking usage.",
      "isSubtle": true, "wrap": true, "spacing": "Small" }
  ]
}
```

No actions — per design choice we don't add an `Action.OpenUrl` to docs (avoids host URL-sanitization concerns) and we don't add an `Action.Execute Refresh` (a refresh on an empty snapshot just yields another empty snapshot).

`CardRenderer.cs` gains:

```csharp
public string Render(string kind, string size, UsageSnapshot snapshot)
{
    if (IsEmpty(snapshot)) return _emptyTemplate.Value;
    return kind switch { ... };  // unchanged Session / Full / Error
}

private static bool IsEmpty(UsageSnapshot s) =>
    s.Session.Percent == 0 && s.Weekly.Percent == 0 && s.ModelWeeklies.Count == 0;
```

`_emptyTemplate` joins the existing `Lazy<string>` pair as a third lazy-loaded `EmbeddedResource`. The empty card has no `${...}` template fields so it's served as-is — no `AdaptiveCardTemplate.Expand` round-trip needed.

Justification for `IsEmpty`: a real Claude or Gemini snapshot cannot have 0% session AND 0% weekly AND zero model weeklies simultaneously — the providers always populate at least one number when credentials are valid. The only path into a fully-zero snapshot is `SnapshotFetcher.Empty()`, returned when neither `ClaudeProvider.DetectCredentials()` nor `GeminiProvider.DetectCredentials()` succeeded.

New xUnit test `CardRendererEmptyTests`: feeds the empty snapshot, asserts the rendered JSON contains `"No CLI detected"` and does **not** contain `"data:image/png;base64,"` (the empty card has no ring image).

### 4. Sideload script

New `windows/scripts/sideload-msix.ps1` (full content in spec appendix below). Responsibilities:

1. `dotnet msbuild ... -p:GenerateAppxPackageOnBuild=true` to package the MSIX.
2. Reuse a per-user self-signed cert in `Cert:\CurrentUser\My` with subject `CN=SignPath OSS` if present, otherwise generate one (CodeSigningCert preset, RSA 2048, 1 year, KeyExportPolicy Exportable).
3. Export PFX to `windows/releases/widget-sideload/sideload-cert.pfx` (gitignored via existing `releases/` rule).
4. If the cert is not in `Cert:\LocalMachine\TrustedPeople`, prompt UAC to import it.
5. Locate `signtool.exe` from the `Microsoft.Windows.SDK.BuildTools` NuGet cache; sign the produced MSIX with SHA256.
6. `Remove-AppxPackage` any prior install, then `Add-AppxPackage` the freshly signed MSIX.
7. `-Uninstall` switch reverses 6 + 4.

The script subject (`CN=SignPath OSS`) is identical to the `<Identity Publisher>` in the manifest, so the signed MSIX validates without manifest editing. The same subject is what SignPath's production cert uses for the EXE (per M4); reusing the literal here is deliberate so future production-signed MSIX produced by CI installs identically over a sideloaded MSIX.

### 5. Tray popup bottom-right

`windows/src/TokenSpendie.Windows/Tray/TrayIconController.cs:207-215`:

```csharp
private static void PositionBoard(WidgetsBoardWindow board)
{
    // Anchor bottom-right of work area with an 8 px gutter. ActualWidth /
    // ActualHeight are only meaningful after Show() has run a layout pass.
    var work   = SystemParameters.WorkArea;
    var width  = board.ActualWidth  > 0 ? board.ActualWidth  : 520;
    var height = board.ActualHeight > 0 ? board.ActualHeight : 600;
    board.Left = work.Right - width - 8;
    board.Top  = System.Math.Max(work.Top + 8, work.Bottom - height - 8);
}
```

`520` matches `WidgetsBoardWindow.xaml`'s declared `Width="520"`. Height stays `SizeToContent`.

For testability, extract the math into a pure helper so xUnit can verify without WPF:

```csharp
// windows/src/TokenSpendie.Windows/Tray/TrayPositioning.cs (new)
internal static class TrayPositioning
{
    public static (double Left, double Top) BottomRight(Rect workArea, double width, double height)
    {
        var left = workArea.Right - width - 8;
        var top  = System.Math.Max(workArea.Top + 8, workArea.Bottom - height - 8);
        return (left, top);
    }
}
```

`TrayIconController.PositionBoard` becomes a 3-line WPF-bridge that calls `TrayPositioning.BottomRight`. New `TrayPositioningTests.cs` asserts bottom-right anchoring against a fake `Rect`.

## Out of band: diagnostic logging (deferred)

If Section 1's manifest realign does not fix activation on first install, the fallback is to add `OutputDebugString`-style logging at the four `IWidgetProvider` entrypoints (`CreateWidget`, `OnActionInvoked`, `OnWidgetContextChanged`, `RegistrationManager` constructor) so DbgView can capture host-driven calls. This is **not** in the initial PR — only added if smoke testing after Section 1 still shows the host failing to activate. Adding it pre-emptively bloats the COM exe for a probably-fixed bug.

## File summary

```
docs/superpowers/specs/
  2026-05-29-widget-activation-fix-and-popup-bottom-right-design.md   # THIS FILE
docs/superpowers/plans/
  2026-05-29-widget-activation-fix-and-popup-bottom-right.md          # NEXT (writing-plans skill)

windows/src/TokenSpendie.WidgetProvider/
  Package.appxmanifest                                                 # MODIFIED  — Section 1
  TokenSpendie.WidgetProvider.csproj                                   # MODIFIED  — Section 2
  Cards/EmptyCard.json                                                 # NEW       — Section 3
  Cards/CardRenderer.cs                                                # MODIFIED  — Section 3

windows/src/TokenSpendie.Windows/Tray/
  TrayIconController.cs                                                # MODIFIED  — Section 5
  TrayPositioning.cs                                                   # NEW       — Section 5

windows/tests/TokenSpendie.Windows.Tests/Widgets/
  CardRendererEmptyTests.cs                                            # NEW       — Section 3
windows/tests/TokenSpendie.Windows.Tests/Tray/
  TrayPositioningTests.cs                                              # NEW       — Section 5

windows/scripts/
  sideload-msix.ps1                                                    # NEW       — Section 4
```

## Test plan

- `dotnet test windows/TokenSpendie.Windows.sln` — expect **183 + 2 = 185** passing (1 `CardRendererEmpty` + 1 `TrayPositioning` minimum; more if positioning grows edge cases).
- `dotnet build windows/TokenSpendie.Windows.sln -c Release` clean.
- `pwsh windows/scripts/sideload-msix.ps1` installs cleanly; `Win+W` → pin `Token Spendie — Session` → ring + percent render (or "No CLI detected" if no creds); same for `Token Spendie — Full`.
- Tray ring click → popup appears bottom-right with 8 px gutter from screen edges.
- `pwsh windows/scripts/sideload-msix.ps1 -Uninstall` removes package + cert cleanly.

## Risks

- **Manifest realign breaks something else.** Mitigation: the M6 PR (#24) is the merge target, and the smoke is local sideload before any push.
- **`broadFileSystemAccess` UAC prompt on first widget activation** may surprise users. Documented in README as part of the M6 install instructions follow-up.
- **MSIX size drop assumes WindowsAppRuntime 1.8 is present on the target machine.** Sideload script does not check. Acceptable for local dev (the user installs runtime as a one-time setup). For wider distribution the manifest's `<Dependencies>` will need a `<PackageDependency>` on the runtime — out of scope for this PR.

## Appendix: full sideload script

See Section 4 above and `windows/scripts/sideload-msix.ps1` in the resulting commit.
