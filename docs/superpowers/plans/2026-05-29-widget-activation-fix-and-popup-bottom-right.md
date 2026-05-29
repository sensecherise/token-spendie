# Widget Activation Fix + Tray Popup Bottom-Right Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Caveman mode active — progress chatter is terse, plan content is normal English.

**Goal:** Make the M6 widget actually render content on the Windows 11 Widget Board (manifest realign so the widget host activates our COM server), surface a "No CLI detected" card when neither Claude nor Gemini credentials are present, ship a one-command local sideload script, and move the tray ring-click popup from bottom-left to bottom-right.

**Architecture:** The widget MSIX manifest currently uses a desktop-bridge (`runFullTrust` + `EntryPoint="Windows.FullTrustApplication"`) shape that diverges from the canonical Microsoft Widgets sample. The widget host reads our `windows.appExtension` block (the widget appears in *Pin widgets* with the correct DisplayName) but `CoCreateInstance` against our registered `<com:Class>` never reaches our exe — the OS COM SCM rejects the activation in the desktop-bridge path. We align the manifest with the UWP-packaged sample shape (Windows.Universal target, `App` Id, no entry-point override, `broadFileSystemAccess` capability), drop the now-unnecessary `WindowsAppSDKSelfContained` band-aid from the csproj, ship an `EmptyCard.json` template plus an `IsEmpty(snapshot)` discriminator in `CardRenderer`, replace the inline sideload commands used during M6 testing with `windows/scripts/sideload-msix.ps1`, and flip the bottom-left anchor math in `TrayIconController.PositionBoard` to bottom-right.

**Tech Stack:** Same as M6 — `net8.0-windows10.0.19041.0`, `Microsoft.WindowsAppSDK 1.8.260508005`, `AdaptiveCards.Templating 1.5.0`, WPF for `RingPngRenderer`, xUnit + FluentAssertions for tests, PowerShell 5.1+ for the sideload script (PowerShell 5.1 ships in-box on Win10/11; the script does not require pwsh 7).

**Spec:** [`docs/superpowers/specs/2026-05-29-widget-activation-fix-and-popup-bottom-right-design.md`](../specs/2026-05-29-widget-activation-fix-and-popup-bottom-right-design.md)

**Branch:** `widget-activation-fix-and-popup-bottom-right` off `develop` (M6 PR #24 merged into develop as `76e3392`).

**Expected commit count:** 5 (manifest+csproj, empty card, sideload script, tray popup, end-to-end smoke / PR docs).

**Prerequisites:**
- `develop` includes M6 (post-PR #24 merge).
- 183 xUnit tests green.
- WindowsAppRuntime 1.8 installed system-wide (`Get-AppxPackage Microsoft.WindowsAppRuntime.1.8`).
- Microsoft.Windows.SDK.BuildTools NuGet cached (so `signtool.exe` is resolvable for the sideload script).

**In scope:**
- `windows/src/TokenSpendie.WidgetProvider/Package.appxmanifest` realign.
- `windows/src/TokenSpendie.WidgetProvider/TokenSpendie.WidgetProvider.csproj` cleanup.
- New `windows/src/TokenSpendie.WidgetProvider/Cards/EmptyCard.json` + CardRenderer route.
- New `windows/scripts/sideload-msix.ps1`.
- New `windows/src/TokenSpendie.Windows/Tray/TrayPositioning.cs` extracted helper + tests.
- Modified `windows/src/TokenSpendie.Windows/Tray/TrayIconController.cs` (uses the helper).
- 2 new xUnit tests (`CardRendererEmptyTests`, `TrayPositioningTests`).

**Out of scope (deferred):**
- Diagnostic logging in the widget provider — only added if Section 1's manifest realign does not unblock activation on first install (see Spec § "Out of band: diagnostic logging").
- Microsoft Store submission ($19 dev fee).
- `tokenspendie://` URL-scheme handler in the tray app.
- SignPath production signing for the MSIX (still M6 Task 9–11).

---

## File structure

```
docs/superpowers/plans/
  2026-05-29-widget-activation-fix-and-popup-bottom-right.md  # this file

windows/
  src/
    TokenSpendie.WidgetProvider/
      Package.appxmanifest                                    # modified — Section 1
      TokenSpendie.WidgetProvider.csproj                      # modified — Section 1
      Cards/
        EmptyCard.json                                        # new — Section 2
        CardRenderer.cs                                       # modified — Section 2

    TokenSpendie.Windows/
      Tray/
        TrayPositioning.cs                                    # new — Section 4
        TrayIconController.cs                                 # modified — Section 4

  tests/TokenSpendie.Windows.Tests/
    Widgets/
      CardRendererEmptyTests.cs                               # new — Section 2
    Tray/
      TrayPositioningTests.cs                                 # new — Section 4

  scripts/
    sideload-msix.ps1                                         # new — Section 3
```

---

## Conventions

- **TDD where unit-testable.** Empty card has a real test; tray positioning has a math-only test against a fake `Rect`. The manifest realign is verified by `Add-AppxPackage` succeeding and the live widget rendering (Section 5).
- **Hooks never skipped.** Every commit goes through the pre-commit hook normally.
- **Never commit certs.** Self-signed PFX used by `sideload-msix.ps1` lives under `windows/releases/widget-sideload/`, gitignored via the existing `releases/` rule.
- **Caveman mode active** for chat; plan content is plain English.

---

### Task 1: Branch off develop + verify clean baseline

**Files:** (none)

**Goal:** Start from a `develop` that already has M6 (PR #24) and tests passing.

- [ ] **Step 1: Verify develop is up to date and tests pass**

```powershell
git fetch origin
git checkout develop
git pull --ff-only origin develop
git log --oneline -3
```

Expected most recent commit: `76e3392 Merge pull request #24 from sensecherise/windows-port-m6-widget`.

```powershell
dotnet test windows/TokenSpendie.Windows.sln
```

Expected: **183 tests, all green**.

- [ ] **Step 2: Create branch**

```powershell
git checkout -b widget-activation-fix-and-popup-bottom-right develop
```

- [ ] **Step 3: Sanity build**

```powershell
dotnet build windows/TokenSpendie.Windows.sln -c Release
```

Expected: clean build.

**Verification:** `git status` clean, on branch `widget-activation-fix-and-popup-bottom-right`, parent is current `develop` tip.

**Commit:** none (no changes yet).

---

### Task 2: Realign manifest + csproj for UWP packaging

**Files:**
- Modify: `windows/src/TokenSpendie.WidgetProvider/Package.appxmanifest`
- Modify: `windows/src/TokenSpendie.WidgetProvider/TokenSpendie.WidgetProvider.csproj`

**Goal:** Bring the manifest and csproj into alignment with the canonical Microsoft widget provider shape so the widget host's `CoCreateInstance` against our registered CLSID actually reaches `Program.Main`.

- [ ] **Step 1: Rewrite `Package.appxmanifest`**

Replace the full file with:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Package
  xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
  xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
  xmlns:uap3="http://schemas.microsoft.com/appx/manifest/uap/windows10/3"
  xmlns:com="http://schemas.microsoft.com/appx/manifest/com/windows10"
  xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
  IgnorableNamespaces="uap uap3 rescap">

  <Identity
    Name="Sensecherise.TokenSpendie.WidgetProvider"
    Publisher="CN=SignPath OSS"
    Version="1.0.0.0"
    ProcessorArchitecture="x64" />

  <Properties>
    <DisplayName>Token Spendie Widgets</DisplayName>
    <PublisherDisplayName>nong.seng</PublisherDisplayName>
    <Logo>assets\StoreLogo.png</Logo>
  </Properties>

  <Dependencies>
    <TargetDeviceFamily Name="Windows.Universal" MinVersion="10.0.19041.0" MaxVersionTested="10.0.22621.0" />
    <TargetDeviceFamily Name="Windows.Desktop"   MinVersion="10.0.19041.0" MaxVersionTested="10.0.22621.0" />
  </Dependencies>

  <Resources>
    <Resource Language="en-us" />
  </Resources>

  <Applications>
    <Application Id="App"
                 Executable="TokenSpendie.WidgetProvider.exe"
                 EntryPoint="TokenSpendie.WidgetProvider.Program">
      <uap:VisualElements
        DisplayName="Token Spendie Widgets"
        Description="Token Spendie widget provider for the Windows 11 Widget Board."
        BackgroundColor="transparent"
        Square150x150Logo="assets\Square150x150Logo.png"
        Square44x44Logo="assets\Square44x44Logo.png" />

      <Extensions>

        <com:Extension Category="windows.comServer">
          <com:ComServer>
            <com:ExeServer Executable="TokenSpendie.WidgetProvider.exe"
                           DisplayName="Token Spendie Widget Provider"
                           Arguments="-RegisterProcessAsComServer">
              <com:Class Id="579614A3-768E-46A5-846C-78784B4232A1"
                         DisplayName="Token Spendie Widget Provider" />
            </com:ExeServer>
          </com:ComServer>
        </com:Extension>

        <uap3:Extension Category="windows.appExtension">
          <uap3:AppExtension Name="com.microsoft.windows.widgets"
                             DisplayName="Token Spendie Widgets"
                             Id="TokenSpendieWidgetExtension"
                             PublicFolder="Public">
            <uap3:Properties>
              <WidgetProvider>
                <ProviderIcons>
                  <Icon Path="assets\Square44x44Logo.png" />
                </ProviderIcons>

                <Activation>
                  <CreateInstance ClassId="579614A3-768E-46A5-846C-78784B4232A1" />
                </Activation>

                <Definitions>

                  <Definition Id="TokenSpendie.Session"
                              DisplayName="Token Spendie — Session"
                              Description="Current 5-hour session usage at a glance."
                              AllowMultiple="false"
                              IsCustomizable="false">
                    <Capabilities>
                      <Capability>
                        <Size Name="small" />
                      </Capability>
                    </Capabilities>
                    <ThemeResources>
                      <Icons>
                        <Icon Path="assets\Square44x44Logo.png" />
                      </Icons>
                      <Screenshots>
                        <Screenshot Path="assets\Widget-Session-preview.png"
                                    DisplayAltText="Token Spendie session widget preview" />
                      </Screenshots>
                    </ThemeResources>
                  </Definition>

                  <Definition Id="TokenSpendie.Full"
                              DisplayName="Token Spendie — Full"
                              Description="Session and weekly usage with breakdown."
                              AllowMultiple="false"
                              IsCustomizable="false">
                    <Capabilities>
                      <Capability>
                        <Size Name="medium" />
                      </Capability>
                    </Capabilities>
                    <ThemeResources>
                      <Icons>
                        <Icon Path="assets\Square44x44Logo.png" />
                      </Icons>
                      <Screenshots>
                        <Screenshot Path="assets\Widget-Full-preview.png"
                                    DisplayAltText="Token Spendie full widget preview" />
                      </Screenshots>
                    </ThemeResources>
                  </Definition>

                </Definitions>
              </WidgetProvider>
            </uap3:Properties>
          </uap3:AppExtension>
        </uap3:Extension>

      </Extensions>
    </Application>
  </Applications>

  <Capabilities>
    <rescap:Capability Name="broadFileSystemAccess" />
  </Capabilities>

</Package>
```

Key differences vs the M6 version:

- `IgnorableNamespaces` drops `com` (the xmlns declaration stays so the COM extension elements still parse, but it is no longer an ignorable namespace — the OS now actively validates COM extension contents).
- `TargetDeviceFamily` gains `Windows.Universal` alongside `Windows.Desktop`.
- `<Application Id>` becomes `App` (canonical sample value).
- `EntryPoint` becomes `TokenSpendie.WidgetProvider.Program` instead of `Windows.FullTrustApplication`.
- `AppListEntry="none"` is removed — the app becomes Start-menu-visible. Acceptable for the widget-only install path.
- The `<Capabilities>` block replaces `runFullTrust` with `broadFileSystemAccess`. The provider runs in the UWP sandbox; `broadFileSystemAccess` is the rescap that grants `~/.claude` and `~/.gemini` reads. First widget activation will prompt the user for the rescap once.

- [ ] **Step 2: Rewrite `TokenSpendie.WidgetProvider.csproj`**

Replace the file with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <TargetPlatformMinVersion>10.0.17763.0</TargetPlatformMinVersion>
    <RootNamespace>TokenSpendie.WidgetProvider</RootNamespace>
    <AssemblyName>TokenSpendie.WidgetProvider</AssemblyName>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>12</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <NoWarn>$(NoWarn);CA1416</NoWarn>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <UseWPF>true</UseWPF>

    <Platforms>x64</Platforms>
    <EnableMsixTooling>true</EnableMsixTooling>
    <AppxPackage>true</AppxPackage>
    <AppxPackageSigningEnabled>false</AppxPackageSigningEnabled>
    <GenerateAppxPackageOnBuild>false</GenerateAppxPackageOnBuild>
    <AppxManifest>Package.appxmanifest</AppxManifest>

    <Version Condition="'$(Version)' == ''">0.0.0-dev</Version>
    <AssemblyVersion>$([System.Text.RegularExpressions.Regex]::Replace($(Version), '[-+].*$', '')).0</AssemblyVersion>
    <FileVersion>$(AssemblyVersion)</FileVersion>
    <InformationalVersion>$(Version)</InformationalVersion>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.WindowsAppSDK" Version="1.8.260508005" />
    <PackageReference Include="Microsoft.Windows.SDK.BuildTools" Version="10.0.26100.8249" />
    <PackageReference Include="AdaptiveCards.Templating" Version="1.5.0" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="System.IO" />
    <Using Include="System.Net.Http" />
  </ItemGroup>

  <ItemGroup>
    <Compile Include="..\TokenSpendie.Windows\Data\IUsageProvider.cs"           Link="Data\IUsageProvider.cs" />
    <Compile Include="..\TokenSpendie.Windows\Data\ClaudeProvider.cs"           Link="Data\ClaudeProvider.cs" />
    <Compile Include="..\TokenSpendie.Windows\Data\ClaudeJsonFileReader.cs"     Link="Data\ClaudeJsonFileReader.cs" />
    <Compile Include="..\TokenSpendie.Windows\Data\GeminiProvider.cs"           Link="Data\GeminiProvider.cs" />
    <Compile Include="..\TokenSpendie.Windows\Data\GeminiUsageReader.cs"        Link="Data\GeminiUsageReader.cs" />
    <Compile Include="..\TokenSpendie.Windows\Data\EndpointUsageProvider.cs"    Link="Data\EndpointUsageProvider.cs" />
    <Compile Include="..\TokenSpendie.Windows\Data\IClaudeUsageEndpoint.cs"     Link="Data\IClaudeUsageEndpoint.cs" />
    <Compile Include="..\TokenSpendie.Windows\Data\ICredentialReader.cs"        Link="Data\ICredentialReader.cs" />
    <Compile Include="..\TokenSpendie.Windows\Data\OAuthCredentials.cs"         Link="Data\OAuthCredentials.cs" />
    <Compile Include="..\TokenSpendie.Windows\Data\OAuthCredentialsParser.cs"   Link="Data\OAuthCredentialsParser.cs" />
    <Compile Include="..\TokenSpendie.Windows\Data\UsageDecoder.cs"             Link="Data\UsageDecoder.cs" />
    <Compile Include="..\TokenSpendie.Windows\Models\*.cs"                      Link="Models\%(FileName).cs"
             Exclude="..\TokenSpendie.Windows\Models\Theme.cs" />
  </ItemGroup>

  <ItemGroup>
    <None Include="Package.appxmanifest">
      <SubType>Designer</SubType>
    </None>
  </ItemGroup>

  <ItemGroup>
    <Content Include="assets\**\*.*">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
    <EmbeddedResource Include="Cards\*.json" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="TokenSpendie.Windows.Tests" />
  </ItemGroup>

</Project>
```

Key differences vs the M6 csproj:

- `<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>` is gone. The runtime resolves via package dependency on `Microsoft.WindowsAppRuntime.1.8` (installed system-wide).
- `<RuntimeIdentifier>win-x64</RuntimeIdentifier>` (singular) is gone — only `<Platforms>x64</Platforms>` remains. Singular `RuntimeIdentifier` was required by `WindowsAppSDKSelfContained`; with self-contained off, `Platforms` is enough.
- `<UseWPF>true</UseWPF>` stays (still required by `RingPngRenderer`).
- Everything else (PackageReference set, linked Compile globs, EmbeddedResource for Cards) stays identical to M6.

- [ ] **Step 3: Restore and build**

```powershell
dotnet restore windows/TokenSpendie.Windows.sln
dotnet build windows/TokenSpendie.Windows.sln -c Release
```

Expected: clean build. If MSBuild complains that `App` Id conflicts with the canonical UWP App template, double-check the `EntryPoint="TokenSpendie.WidgetProvider.Program"` value matches the actual `namespace.class` of `Program.cs` (it does — `namespace TokenSpendie.WidgetProvider; public static class Program`).

- [ ] **Step 4: Tests**

```powershell
dotnet test windows/TokenSpendie.Windows.sln
```

Expected: **183 green** (unchanged from develop tip).

- [ ] **Step 5: Generate MSIX locally and confirm size drop**

```powershell
dotnet msbuild windows/src/TokenSpendie.WidgetProvider/TokenSpendie.WidgetProvider.csproj `
  -t:Build -p:Configuration=Release -p:Platform=x64 -p:Version=0.0.0-local `
  -p:GenerateAppxPackageOnBuild=true
```

Locate the produced MSIX and verify size:

```powershell
$msix = Get-ChildItem windows/src/TokenSpendie.WidgetProvider/AppPackages -Recurse -Filter "*.msix" |
  Sort-Object LastWriteTime -Descending | Select-Object -First 1
"$($msix.Name): $([Math]::Round($msix.Length/1MB,1)) MB"
```

Expected: **3-5 MB** (was ~51 MB in M6 with `WindowsAppSDKSelfContained=true`). If the file is still >40 MB, recheck Step 2 — `WindowsAppSDKSelfContained` is probably still set somewhere (Directory.Build.props or an `<EnableMsixTooling>` side-effect).

- [ ] **Step 6: Inspect the embedded manifest**

```powershell
$tmp = Join-Path $env:TEMP "msix-inspect"
Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
Copy-Item $msix.FullName "$tmp\package.zip"
Expand-Archive -Path "$tmp\package.zip" -DestinationPath $tmp -Force
Select-String -Path (Join-Path $tmp "AppxManifest.xml") -Pattern "Application Id|EntryPoint|TargetDeviceFamily|broadFileSystemAccess|runFullTrust"
```

Expected output contains:
- `Application Id="App"`
- `EntryPoint="TokenSpendie.WidgetProvider.Program"`
- Two `TargetDeviceFamily` entries (Universal + Desktop)
- `broadFileSystemAccess`
- (no occurrence of `runFullTrust`)

- [ ] **Step 7: Commit**

```powershell
git add windows/src/TokenSpendie.WidgetProvider/Package.appxmanifest `
        windows/src/TokenSpendie.WidgetProvider/TokenSpendie.WidgetProvider.csproj
git commit -m "fix(windows): realign widget MSIX manifest with WindowsAppSDK sample shape"
```

**Verification:** Tests still 183 green; MSIX shrinks from ~51 MB to ~3-5 MB; embedded manifest carries the new App Id, entry point, dual TargetDeviceFamily, and `broadFileSystemAccess`.

**Commit-message-prefix:** `fix(windows): realign widget MSIX manifest with WindowsAppSDK sample shape`

---

### Task 3: Empty-state card

**Files:**
- Create: `windows/src/TokenSpendie.WidgetProvider/Cards/EmptyCard.json`
- Modify: `windows/src/TokenSpendie.WidgetProvider/Cards/CardRenderer.cs`
- Create: `windows/tests/TokenSpendie.Windows.Tests/Widgets/CardRendererEmptyTests.cs`

**Goal:** When neither Claude nor Gemini credentials are present (`SnapshotFetcher.Empty()` path), render a clean "No CLI detected" card instead of pushing a 0% reading.

- [ ] **Step 1: Create `Cards/EmptyCard.json`**

```json
{
  "type": "AdaptiveCard",
  "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
  "version": "1.5",
  "body": [
    {
      "type": "TextBlock",
      "text": "Token Spendie",
      "weight": "Bolder",
      "size": "Medium"
    },
    {
      "type": "TextBlock",
      "text": "No CLI detected.",
      "weight": "Bolder",
      "spacing": "Small"
    },
    {
      "type": "TextBlock",
      "text": "Install Claude Code or Gemini CLI to start tracking usage.",
      "isSubtle": true,
      "wrap": true,
      "spacing": "Small"
    }
  ]
}
```

No `actions`. No template fields. Served as-is.

- [ ] **Step 2: Write failing test**

Create `windows/tests/TokenSpendie.Windows.Tests/Widgets/CardRendererEmptyTests.cs`:

```csharp
extern alias widget;

using FluentAssertions;
using Xunit;
using CardRenderer = widget::TokenSpendie.WidgetProvider.Cards.CardRenderer;
using ModelWeekly = widget::TokenSpendie.Windows.Models.ModelWeekly;
using UsageSnapshot = widget::TokenSpendie.Windows.Models.UsageSnapshot;
using UsageWindow = widget::TokenSpendie.Windows.Models.UsageWindow;

namespace TokenSpendie.Windows.Tests.Widgets;

public class CardRendererEmptyTests
{
    [Fact]
    public void RendersNoCliDetectedWhenSnapshotIsEmpty()
    {
        var empty = new UsageSnapshot(
            Session: new UsageWindow(0, null),
            Weekly: new UsageWindow(0, null),
            ModelWeeklies: Array.Empty<ModelWeekly>(),
            FetchedAt: DateTimeOffset.UtcNow);

        var renderer = new CardRenderer();
        var json = renderer.Render("TokenSpendie.Session", "small", empty);

        json.Should().Contain("No CLI detected.");
        json.Should().Contain("Install Claude Code or Gemini CLI");
        json.Should().NotContain("data:image/png;base64,");
        json.Should().NotContain("%");  // no percent rendering
    }

    [Fact]
    public void FullKindAlsoRendersEmptyCardWhenSnapshotIsEmpty()
    {
        var empty = new UsageSnapshot(
            Session: new UsageWindow(0, null),
            Weekly: new UsageWindow(0, null),
            ModelWeeklies: Array.Empty<ModelWeekly>(),
            FetchedAt: DateTimeOffset.UtcNow);

        var renderer = new CardRenderer();
        var json = renderer.Render("TokenSpendie.Full", "medium", empty);

        json.Should().Contain("No CLI detected.");
        json.Should().NotContain("Action.OpenUrl");  // empty card has no actions
    }
}
```

Run — expect failure (renderer doesn't have empty-state behaviour yet):

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~CardRendererEmptyTests"
```

Expected: FAIL.

- [ ] **Step 3: Modify `CardRenderer.cs`**

Replace the existing `Render(string, string, UsageSnapshot)` method, add the `_emptyTemplate` lazy, and add the `IsEmpty` discriminator. Full updated file:

```csharp
using System.Reflection;
using AdaptiveCards.Templating;
using TokenSpendie.WidgetProvider.Rendering;
using TokenSpendie.Windows.Models;

namespace TokenSpendie.WidgetProvider.Cards;

public sealed class CardRenderer
{
    private readonly Lazy<string> _sessionTemplate = new(() => LoadTemplate("SessionCard.json"));
    private readonly Lazy<string> _fullTemplate = new(() => LoadTemplate("FullCard.json"));
    private readonly Lazy<string> _emptyTemplate = new(() => LoadTemplate("EmptyCard.json"));

    public string Render(string kind, string size, UsageSnapshot snapshot)
    {
        if (IsEmpty(snapshot)) return _emptyTemplate.Value;
        return kind switch
        {
            "TokenSpendie.Session" => RenderSession(snapshot),
            "TokenSpendie.Full" => RenderFull(snapshot, size),
            _ => RenderError($"Unknown widget kind: {kind}"),
        };
    }

    public string RenderSession(UsageSnapshot snapshot)
    {
        var pct = snapshot.Session.Percent;
        var level = UsageLevelExtensions.ForPercent(pct);
        var ringPng = RingPngRenderer.RenderBase64(pct, level);

        var data = new
        {
            ringDataUri = $"data:image/png;base64,{ringPng}",
            percentText = $"{pct:F0}%",
            footerText = FormatFooter(snapshot),
        };

        var template = new AdaptiveCardTemplate(_sessionTemplate.Value);
        return template.Expand(data);
    }

    public string RenderFull(UsageSnapshot snapshot, string size)
    {
        var sPct = snapshot.Session.Percent;
        var wPct = snapshot.Weekly.Percent;
        var level = UsageLevelExtensions.ForPercent(sPct);
        var ringPng = RingPngRenderer.RenderBase64(sPct, level);

        var data = new
        {
            ringDataUri = $"data:image/png;base64,{ringPng}",
            sessionLine = $"Session  {sPct:F0}%",
            weeklyLine = $"Weekly  {wPct:F0}%",
            footerText = FormatFooter(snapshot),
        };

        var template = new AdaptiveCardTemplate(_fullTemplate.Value);
        return template.Expand(data);
    }

    public static string RenderError(string message)
    {
        var card = new
        {
            type = "AdaptiveCard",
            version = "1.5",
            body = new object[]
            {
                new { type = "TextBlock", text = "Token Spendie", weight = "Bolder" },
                new { type = "TextBlock", text = message, wrap = true, isSubtle = true },
            },
        };
        return System.Text.Json.JsonSerializer.Serialize(card);
    }

    private static bool IsEmpty(UsageSnapshot s) =>
        s.Session.Percent == 0 && s.Weekly.Percent == 0 && s.ModelWeeklies.Count == 0;

    private static string FormatFooter(UsageSnapshot snapshot)
    {
        var ago = DateTimeOffset.UtcNow - snapshot.FetchedAt;
        if (ago < TimeSpan.FromMinutes(1)) return "Just refreshed";
        if (ago < TimeSpan.FromHours(1)) return $"Refreshed {(int)ago.TotalMinutes}m ago";
        if (ago < TimeSpan.FromDays(1)) return $"Refreshed {(int)ago.TotalHours}h ago";
        return $"Refreshed {(int)ago.TotalDays}d ago";
    }

    private static string LoadTemplate(string name)
    {
        var asm = Assembly.GetExecutingAssembly();
        var resource = $"TokenSpendie.WidgetProvider.Cards.{name}";
        using var stream = asm.GetManifestResourceStream(resource)
                          ?? throw new FileNotFoundException(
                              $"Embedded card template '{resource}' not found in {asm.GetName().Name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
```

- [ ] **Step 4: Run all tests**

```powershell
dotnet test windows/TokenSpendie.Windows.sln
```

Expected: **183 + 2 = 185 green**.

- [ ] **Step 5: Commit**

```powershell
git add windows/src/TokenSpendie.WidgetProvider/Cards/EmptyCard.json `
        windows/src/TokenSpendie.WidgetProvider/Cards/CardRenderer.cs `
        windows/tests/TokenSpendie.Windows.Tests/Widgets/CardRendererEmptyTests.cs
git commit -m "feat(windows): empty-state widget card when no CLI credentials detected"
```

**Verification:** 185 tests green; both Session and Full kinds route to the empty card when the snapshot is fully zero.

**Commit-message-prefix:** `feat(windows): empty-state widget card when no CLI credentials detected`

---

### Task 4: Sideload script

**Files:**
- Create: `windows/scripts/sideload-msix.ps1`

**Goal:** A single command that builds, self-signs, trusts, and installs the MSIX on the dev box. `-Uninstall` reverses it.

- [ ] **Step 1: Create `windows/scripts/sideload-msix.ps1`**

```powershell
#requires -Version 5.1
<#
.SYNOPSIS
    Build, self-sign, and sideload TokenSpendie.WidgetProvider MSIX for local smoke.

.DESCRIPTION
    Dev-machine smoke path. CI signs via SignPath (future M6 Task 11); this script
    signs with a per-user self-signed certificate whose subject matches the MSIX
    Identity Publisher (CN=SignPath OSS) so the same install path validates against
    both signed and locally-signed packages.

.PARAMETER Version
    Semver string. Defaults to "0.0.0-local".

.PARAMETER Uninstall
    Remove the installed package and the LocalMachine\TrustedPeople trust entry.

.EXAMPLE
    powershell windows\scripts\sideload-msix.ps1
.EXAMPLE
    powershell windows\scripts\sideload-msix.ps1 -Version 1.2.3-local
.EXAMPLE
    powershell windows\scripts\sideload-msix.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    [string]$Version = "0.0.0-local",
    [switch]$Uninstall
)

$ErrorActionPreference = "Stop"

$repo    = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$pkgName = "Sensecherise.TokenSpendie.WidgetProvider"
$subject = "CN=SignPath OSS"

if ($Uninstall) {
    Write-Host "Uninstalling widget MSIX..." -ForegroundColor Yellow
    Get-AppxPackage $pkgName -ErrorAction SilentlyContinue |
        ForEach-Object { Remove-AppxPackage -Package $_.PackageFullName }
    Get-ChildItem Cert:\LocalMachine\TrustedPeople -ErrorAction SilentlyContinue |
        Where-Object Subject -eq $subject |
        ForEach-Object {
            Write-Host "Removing trusted cert: $($_.Thumbprint)"
            Remove-Item $_.PSPath -Force
        }
    Write-Host "Uninstalled." -ForegroundColor Green
    exit 0
}

# 1. Build MSIX
Write-Host "Building widget provider $Version..." -ForegroundColor Cyan
Push-Location $repo
try {
    dotnet msbuild windows/src/TokenSpendie.WidgetProvider/TokenSpendie.WidgetProvider.csproj `
        -t:Build -p:Configuration=Release -p:Platform=x64 -p:Version=$Version `
        -p:GenerateAppxPackageOnBuild=true
    if ($LASTEXITCODE -ne 0) { throw "dotnet msbuild exit $LASTEXITCODE" }
} finally { Pop-Location }

$msix = Get-ChildItem (Join-Path $repo "windows\src\TokenSpendie.WidgetProvider\AppPackages") `
    -Recurse -Filter "*.msix" |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $msix) { throw "MSIX not produced under AppPackages." }
Write-Host "Built: $($msix.FullName)"

# 2. Self-signed cert (reuse if present)
$cert = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object Subject -eq $subject | Select-Object -First 1
if (-not $cert) {
    Write-Host "Generating self-signed cert..." -ForegroundColor Cyan
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert -Subject $subject `
        -KeyAlgorithm RSA -KeyLength 2048 -KeyExportPolicy Exportable `
        -NotAfter (Get-Date).AddYears(1) `
        -CertStoreLocation "Cert:\CurrentUser\My"
}

$pfxDir = Join-Path $repo "windows\releases\widget-sideload"
New-Item -ItemType Directory -Force -Path $pfxDir | Out-Null
$pfxPath = Join-Path $pfxDir "sideload-cert.pfx"
$pwd = ConvertTo-SecureString -String "tokenspendie-local" -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $pwd | Out-Null
Write-Host "PFX: $pfxPath"

# 3. Trust in LocalMachine\TrustedPeople (admin scope)
$trusted = Get-ChildItem Cert:\LocalMachine\TrustedPeople -ErrorAction SilentlyContinue |
    Where-Object Subject -eq $subject
if (-not $trusted) {
    Write-Host "Importing cert into LocalMachine\TrustedPeople (UAC prompt)..." -ForegroundColor Cyan
    $importCmd = "Import-PfxCertificate -FilePath '$pfxPath' -CertStoreLocation Cert:\LocalMachine\TrustedPeople -Password (ConvertTo-SecureString 'tokenspendie-local' -Force -AsPlainText) | Out-Null"
    Start-Process powershell -Verb RunAs -Wait -ArgumentList @("-NoProfile", "-Command", $importCmd)
    $verify = Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Subject -eq $subject
    if (-not $verify) { throw "Cert import was cancelled or failed." }
} else {
    Write-Host "Cert already trusted: $($trusted.Thumbprint)"
}

# 4. signtool from BuildTools NuGet
$signtool = Get-ChildItem "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools\*\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending | Select-Object -First 1
if (-not $signtool) {
    throw "signtool.exe not found in NuGet cache. Run 'dotnet restore windows/TokenSpendie.Windows.sln' first."
}

# 5. Sign MSIX
Write-Host "Signing MSIX..." -ForegroundColor Cyan
& $signtool.FullName sign /fd SHA256 /f $pfxPath /p tokenspendie-local $msix.FullName
if ($LASTEXITCODE -ne 0) { throw "signtool sign exit $LASTEXITCODE" }

$sig = Get-AuthenticodeSignature $msix.FullName
if ($sig.Status -ne "Valid") { throw "Post-sign verification: $($sig.Status)" }

# 6. Install
Write-Host "Installing $($msix.Name)..." -ForegroundColor Cyan
Get-AppxPackage $pkgName -ErrorAction SilentlyContinue |
    ForEach-Object { Remove-AppxPackage -Package $_.PackageFullName }
Add-AppxPackage -Path $msix.FullName

Write-Host ""
Write-Host "Installed." -ForegroundColor Green
Write-Host "Next: Win+W -> + -> pin Token Spendie - Session / Full"
Write-Host "Uninstall: powershell $($PSCommandPath) -Uninstall"
```

- [ ] **Step 2: Confirm gitignore covers the sideload artifacts**

```powershell
git check-ignore windows/releases/widget-sideload/sideload-cert.pfx
```

Expected output: `windows/releases/widget-sideload/sideload-cert.pfx`. If empty, append `releases/` to `windows/.gitignore` — but it should already be present from M6 Task 9.

- [ ] **Step 3: Smoke run the script**

```powershell
powershell -ExecutionPolicy Bypass -File windows/scripts/sideload-msix.ps1
```

Expected steps:
1. dotnet msbuild output (clean build).
2. "Built: ...\TokenSpendie.WidgetProvider_0.0.0-local_x64.msix".
3. Cert generation or reuse message.
4. UAC prompt if cert is not yet in TrustedPeople.
5. "Signing MSIX..." followed by "Successfully signed".
6. "Installed."

If any step fails, fix the script and re-run. Don't move on with a half-finished sideload script.

- [ ] **Step 4: Smoke uninstall**

```powershell
powershell -ExecutionPolicy Bypass -File windows/scripts/sideload-msix.ps1 -Uninstall
```

Expected: package removed, cert removed, "Uninstalled." printed.

- [ ] **Step 5: Re-install in preparation for Task 6 smoke**

```powershell
powershell -ExecutionPolicy Bypass -File windows/scripts/sideload-msix.ps1
```

Leave the package installed; Task 6 smoke-tests the live widget on this same install.

- [ ] **Step 6: Commit**

```powershell
git add windows/scripts/sideload-msix.ps1
git commit -m "feat(windows): add sideload-msix.ps1 dev-machine MSIX sideload script"
```

**Verification:** `sideload-msix.ps1` builds + signs + installs cleanly on first run, and `-Uninstall` reverses it.

**Commit-message-prefix:** `feat(windows): add sideload-msix.ps1 dev-machine MSIX sideload script`

---

### Task 5: Tray popup bottom-right

**Files:**
- Create: `windows/src/TokenSpendie.Windows/Tray/TrayPositioning.cs`
- Modify: `windows/src/TokenSpendie.Windows/Tray/TrayIconController.cs`
- Create: `windows/tests/TokenSpendie.Windows.Tests/Tray/TrayPositioningTests.cs`

**Goal:** Anchor the WidgetsBoardWindow at the bottom-right of the screen's work area instead of the bottom-left, while extracting the math into a pure helper so xUnit can exercise it.

- [ ] **Step 1: Write failing test**

Create `windows/tests/TokenSpendie.Windows.Tests/Tray/TrayPositioningTests.cs`:

```csharp
using System.Windows;
using FluentAssertions;
using TokenSpendie.Windows.Tray;
using Xunit;

namespace TokenSpendie.Windows.Tests.Tray;

public class TrayPositioningTests
{
    [Fact]
    public void BottomRightAnchorsLeftSoBoardSitsAgainstWorkAreaRightEdgeWithGutter()
    {
        // Work area: 1920x1080, taskbar 40px on bottom => Bottom=1040.
        var work = new Rect(0, 0, 1920, 1040);
        var (left, top) = TrayPositioning.BottomRight(work, width: 520, height: 600);

        left.Should().Be(1920 - 520 - 8);   // = 1392
        top.Should().Be(1040 - 600 - 8);    //  = 432
    }

    [Fact]
    public void BottomRightClampsTopToAtLeastWorkAreaTopPlusGutter()
    {
        // Board taller than work area => Top should be clamped at workTop + 8.
        var work = new Rect(0, 0, 1920, 700);
        var (_, top) = TrayPositioning.BottomRight(work, width: 520, height: 1200);

        top.Should().Be(work.Top + 8);
    }

    [Fact]
    public void BottomRightUsesProvidedRectOriginNotOriginZero()
    {
        // Secondary monitor at x=1920 => work.Left=1920, work.Right=3840.
        var work = new Rect(1920, 0, 1920, 1040);
        var (left, _) = TrayPositioning.BottomRight(work, width: 520, height: 600);

        left.Should().Be(3840 - 520 - 8);   // = 3312
    }
}
```

Run — expect failure (`TrayPositioning` does not exist):

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~TrayPositioningTests"
```

Expected: FAIL.

- [ ] **Step 2: Create `TrayPositioning.cs`**

`windows/src/TokenSpendie.Windows/Tray/TrayPositioning.cs`:

```csharp
using System;
using System.Windows;

namespace TokenSpendie.Windows.Tray;

internal static class TrayPositioning
{
    private const double Gutter = 8.0;

    /// <summary>
    /// Anchor a popup at the bottom-right of <paramref name="workArea"/>, leaving a
    /// fixed gutter from the right and bottom edges. If the popup is taller than
    /// the work area, clamp the top to the work area's top edge (plus gutter)
    /// so the popup stays visible.
    /// </summary>
    public static (double Left, double Top) BottomRight(Rect workArea, double width, double height)
    {
        var left = workArea.Right - width - Gutter;
        var top  = Math.Max(workArea.Top + Gutter, workArea.Bottom - height - Gutter);
        return (left, top);
    }
}
```

- [ ] **Step 3: Run positioning tests — expect pass**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~TrayPositioningTests"
```

Expected: 3 pass.

- [ ] **Step 4: Modify `TrayIconController.cs` to call the helper**

Replace lines 207-215 of `windows/src/TokenSpendie.Windows/Tray/TrayIconController.cs` (the `PositionBoard` method) with:

```csharp
    private static void PositionBoard(WidgetsBoardWindow board)
    {
        // ActualWidth / ActualHeight are only meaningful after Show() has
        // run a layout pass. Width fallback matches WidgetsBoardWindow.xaml's
        // declared Width="520".
        var work   = SystemParameters.WorkArea;
        var width  = board.ActualWidth  > 0 ? board.ActualWidth  : 520;
        var height = board.ActualHeight > 0 ? board.ActualHeight : 600;
        var (left, top) = TrayPositioning.BottomRight(work, width, height);
        board.Left = left;
        board.Top  = top;
    }
```

- [ ] **Step 5: Run all tests**

```powershell
dotnet test windows/TokenSpendie.Windows.sln
```

Expected: **185 + 3 = 188 green**.

- [ ] **Step 6: Build**

```powershell
dotnet build windows/TokenSpendie.Windows.sln -c Release
```

Expected: clean.

- [ ] **Step 7: Commit**

```powershell
git add windows/src/TokenSpendie.Windows/Tray/TrayPositioning.cs `
        windows/src/TokenSpendie.Windows/Tray/TrayIconController.cs `
        windows/tests/TokenSpendie.Windows.Tests/Tray/TrayPositioningTests.cs
git commit -m "feat(windows): tray ring popup anchors bottom-right of work area"
```

**Verification:** 188 tests green; `TrayPositioning.BottomRight` math is independent of WPF; `TrayIconController` is a 4-line bridge.

**Commit-message-prefix:** `feat(windows): tray ring popup anchors bottom-right of work area`

---

### Task 6: End-to-end smoke + PR

**Files:**
- (verification only — no code changes in this task; commit if README is touched)

**Goal:** Validate the entire chain on the actual machine. Confirm widget activates, renders an Adaptive Card (Session and Full), and the tray popup lands on the bottom-right. Push branch, open PR against `develop`.

- [ ] **Step 1: Re-sideload the freshly built MSIX**

```powershell
powershell -ExecutionPolicy Bypass -File windows/scripts/sideload-msix.ps1
```

Expected: clean install. If the previous Task 4 install is still in place, the script removes it first and adds the rebuilt version.

- [ ] **Step 2: Pin Session widget and verify rendering**

1. Press `Win+W` to open the Widget Board.
2. Click `+` (Pin widgets).
3. Find **Token Spendie — Session** in the list. Both the widget name and the preview screenshot should be visible.
4. Click `Pin`.

Expected outcomes (whichever applies):
- **If `~/.claude/` or `~/.gemini/` has credentials on this machine:** the card renders a ring + percent (e.g., "45%" with a green/amber/red arc) and a "Refreshed Xm ago" footer.
- **If neither provider has credentials:** the card renders "No CLI detected." + "Install Claude Code or Gemini CLI to start tracking usage."

The host's grey skeleton should *not* persist — that was the M6 bug Task 2 fixed.

- [ ] **Step 3: Pin Full widget and verify rendering**

Pin **Token Spendie — Full** the same way. Expected: a wider 4×2 card with the ring + "Session XX%" + "Weekly YY%" + Refresh + "Open Token Spendie" actions, or the same empty-state card if no creds.

- [ ] **Step 4: Refresh action smoke**

Click the **Refresh** action on either pinned card. The card should update within ~2 seconds.

- [ ] **Step 5: Tray popup bottom-right smoke**

1. Confirm the tray app is running (`Get-Process TokenSpendie.Windows`).
2. Click the tray ring icon.
3. Confirm the `WidgetsBoardWindow` appears at the bottom-right of the screen with an 8 px gutter from the right edge and the taskbar top.
4. Click outside / press Escape — popup hides.
5. Click ring again — popup reappears at the bottom-right (same position).

If the popup appears at the bottom-left, the tray app is still running the pre-fix build. Restart it (right-click tray icon → Quit, then launch from Start).

- [ ] **Step 6: If smoke fails (widget still empty), enable diagnostic logging**

(Skip this step if Steps 2–4 passed.)

Add `OutputDebugString` calls at each `IWidgetProvider` entrypoint. In `WidgetProvider.cs`, replace the existing constructor or insert at the start of `CreateWidget`:

```csharp
System.Diagnostics.Trace.WriteLine(
    $"[TokenSpendie] CreateWidget kind={widgetContext.DefinitionId} size={widgetContext.Size}");
```

Repeat for `OnActionInvoked`, `OnWidgetContextChanged`, `Activate`, `Deactivate`. Also add `Trace.WriteLine("[TokenSpendie] COM server entry")` at the top of `Program.Main`.

Rebuild + re-sideload via `windows/scripts/sideload-msix.ps1`. Run DbgView (https://learn.microsoft.com/sysinternals/downloads/dbgview) with **Capture Global Win32** enabled. Re-pin the widget. Inspect DbgView output:

- If the COM server entry line **does not** appear → COM activation still failing. Check Step 7.
- If the entry line appears but no `CreateWidget` lines appear → the widget host instantiated the COM server but isn't calling our methods. Check `WidgetManager` is being acquired (compare to MS sample's `RecoverRunningWidgets` flow).
- If `CreateWidget` runs but `UpdateWidget` throws → the inline `catch` blocks in `WidgetProvider.UpdateWidget` are eating an exception. Temporarily replace `catch { ... }` with `catch (Exception ex) { Trace.WriteLine($"[TokenSpendie] UpdateWidget failed: {ex}"); throw; }` to surface the cause.

Once the cause is identified, fix it (likely a manifest tweak — recompare against MS sample) and commit:

```powershell
git add windows/src/TokenSpendie.WidgetProvider/<changed files>
git commit -m "fix(windows): <specific cause>"
```

Then remove the diagnostic `Trace.WriteLine` calls and commit a follow-up `chore(windows): drop widget diagnostic logging`.

- [ ] **Step 7: Push branch**

```powershell
git push -u origin widget-activation-fix-and-popup-bottom-right
```

- [ ] **Step 8: Open PR vs develop**

Write the PR body to a temp file `windows/.pr-body-widget-fix.md`:

```markdown
## Summary
- Realigns the M6 widget MSIX manifest with the WindowsAppSDK widget provider sample shape (`Application Id="App"`, `EntryPoint="TokenSpendie.WidgetProvider.Program"`, both `Windows.Universal` and `Windows.Desktop` `TargetDeviceFamily`, `broadFileSystemAccess` rescap instead of `runFullTrust`), which unsticks COM activation so the widget host actually instantiates `TokenSpendie.WidgetProvider.exe`.
- Adds an `EmptyCard.json` template + `IsEmpty(snapshot)` route in `CardRenderer` so users without Claude or Gemini credentials see "No CLI detected. Install Claude Code or Gemini CLI to start tracking usage." instead of a 0% reading or the host's loading skeleton.
- Adds `windows/scripts/sideload-msix.ps1` for one-command local sideload (build + self-sign + install + uninstall via `-Uninstall`).
- Flips the tray ring popup anchor from bottom-left to bottom-right via an extracted `TrayPositioning.BottomRight` helper. Math is now WPF-free and unit-tested.
- Drops `WindowsAppSDKSelfContained=true` from the widget csproj (was a band-aid for the desktop-bridge MSB3824 errors during M6 Task 3). MSIX shrinks from ~51 MB to ~3-5 MB; runtime resolves via the `Microsoft.WindowsAppRuntime.1.8` system-installed framework.

## Smoke results
- `dotnet test windows/TokenSpendie.Windows.sln` — 188 green (183 prior + 2 `CardRendererEmpty` + 3 `TrayPositioning`).
- `dotnet build windows/TokenSpendie.Windows.sln -c Release` clean.
- `powershell windows/scripts/sideload-msix.ps1` installs cleanly; Pin widgets shows both Token Spendie kinds; pinning either renders the live card (or the empty-state card on machines with no CLI credentials).
- Tray ring click anchors popup bottom-right with 8 px gutter.

## Out of scope (still M6 follow-ups)
- SignPath production signing for the MSIX (M6 Task 9 + 11).
- CI MSIX job in `.github/workflows/windows-release.yml` (M6 Task 11).
- README install instructions for the widget (M6 Task 13).
- `tokenspendie://` URL-scheme handler in the tray app.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
```

Then:

```powershell
gh pr create --base develop --head widget-activation-fix-and-popup-bottom-right `
  --title "fix(windows): widget MSIX activation + empty-state card + bottom-right tray popup" `
  --body-file windows/.pr-body-widget-fix.md
Remove-Item windows/.pr-body-widget-fix.md
```

- [ ] **Step 9: Confirm CI passes on the PR**

```powershell
gh pr checks
```

Expected: green. If the existing CI workflow only builds the EXE (M6 Task 11's `msix` job is not yet wired), CI verifies `dotnet build` + `dotnet test` against the modified projects — both should pass.

**Verification:** PR open against develop, CI green, sideloaded widget renders the live card on the dev box, tray popup hugs bottom-right.

**Commit-message-prefix:** none (Task 6 introduces no commits unless Step 6 diagnostic-logging path fires).

---

## Handoff

**State after this PR merges:**

- Pinned Token Spendie widgets render live Adaptive Cards on the Windows 11 Widget Board.
- Empty-state card surfaces when the dev box has no Claude or Gemini credentials.
- One-command local sideload via `powershell windows/scripts/sideload-msix.ps1`.
- Tray popup anchors bottom-right with 8 px gutter, same as the M6 board layout direction.
- MSIX is ~3-5 MB instead of ~51 MB.

**Known follow-ups (not this PR):**

- SignPath production signing for the MSIX (still M6 Task 9–11 territory).
- CI MSIX job — the `windows-release.yml` `msix` job needs `-p:GenerateAppxPackageOnBuild=true` per the M6 PR caveats.
- README "Token Spendie as a Windows Widget" section (M6 Task 13).
- `tokenspendie://open` URL-scheme handler in the tray app so the medium widget's "Open Token Spendie" action actually launches/focuses the tray app.

**Cross-task dependency map:**

- Task 1 → all subsequent.
- Task 2 (manifest realign) → Task 4 (sideload script installs the realigned MSIX), Task 6 (smoke).
- Task 3 (empty card) → Task 6 (smoke).
- Task 4 (sideload script) → Task 6 (smoke).
- Task 5 (tray popup) → Task 6 (smoke).
- Task 6 = smoke + PR.

---

## Self-review

**Spec coverage vs request:**

- Section 1 (Manifest realign) → Task 2.
- Section 2 (csproj cleanup) → Task 2 (combined with manifest because they have to land together for build to succeed).
- Section 3 (Empty-state card) → Task 3.
- Section 4 (Sideload script) → Task 4.
- Section 5 (Tray popup bottom-right) → Task 5.
- "Out of band: diagnostic logging (deferred)" → Task 6 Step 6 (fallback only).
- End-to-end smoke → Task 6.

All spec sections are addressed.

**Placeholder scan:** Every `<...>` in the plan is inside a code block (e.g., `<changed files>` in the diagnostic-logging fallback, `<specific cause>` in the same fallback). No "TBD"/"TODO"/"implement later" outside of code blocks.

**Type consistency:**

- `TrayPositioning.BottomRight(Rect, double, double) -> (double Left, double Top)` declared in Task 5 Step 2 and consumed in Task 5 Step 4.
- `CardRenderer.Render(string, string, UsageSnapshot)` signature unchanged from M6; new internal `IsEmpty(UsageSnapshot)` helper is private static and used only by `Render`.
- `_emptyTemplate` is a `Lazy<string>` matching the existing `_sessionTemplate` / `_fullTemplate` pattern.
- Empty card template has no `${...}` placeholders, so `_emptyTemplate.Value` is returned directly without an `AdaptiveCardTemplate.Expand` call. The two tests assert literal substrings.

**Spec/plan agreement on file paths:** All file paths in the plan match the spec's "File summary" table.

**Scope check:** Single PR, 5 commits, ~600 LOC including tests. Focused.
