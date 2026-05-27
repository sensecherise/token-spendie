# Windows Port — M2 Tray + Popup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a WPF tray icon and click-to-open popup panel that shows live usage from the providers built in M1. Single screen, no preferences yet (M3). Produces a clickable `TokenSpendie.Windows.exe` that lives in the tray, shows the highest-priority provider's usage ring as the tray icon, and pops the panel on left-click. The CLI binary from M1 is replaced by the WPF app's `Main`.

**Architecture:** Convert the M1 project from `OutputType=Exe` to `WinExe`, enable WPF, add an MVVM stack on `CommunityToolkit.Mvvm`. The data layer from M1 stays unchanged. A new `UsageStore` (simplified — no preferences, no network observer, no wake observer) drives polling via `PeriodicTimer`; it owns the per-provider state and exposes `INotifyPropertyChanged` for binding. The tray uses `H.NotifyIcon.Wpf`. The ring icon is rendered into a `RenderTargetBitmap` by `RingIconRenderer`. Popup positioning calls native `Shell_NotifyIconGetRect` + `SHAppBarMessage` via P/Invoke.

**Tech Stack additions on top of M1:** WPF (`<UseWPF>true</UseWPF>`), `H.NotifyIcon.Wpf` (tray), `CommunityToolkit.Mvvm` (source-generated INPC). No WPF-UI (Mica) for M2 — solid background. No theme picker (hardcoded usage-tier colors). No installer/CI changes (M4).

**Spec:** [`docs/superpowers/specs/2026-05-26-windows-port-design.md`](../specs/2026-05-26-windows-port-design.md). M0 spike findings + M1 plan are landed on the base branch.

**Prerequisites:**
- M1 branch (`windows-port-m1-core`) is the base. Branch off it for M2.
- .NET 8 SDK (or newer) on a Windows 11 22H2+ host.
- The repo's M1 test suite still green (`dotnet test windows/TokenSpendie.Windows.sln` — 68 tests pass).

**Branch:** create `windows-port-m2-tray-popup` off `windows-port-m1-core` before the first commit.

**Out of scope (deferred):**
- Preferences (theme, refresh interval, menu-bar provider, launch-at-login) — **M3**.
- Floating always-on-top panel — **M3**.
- Toast notifications + `UsageNotifier` — **M3**.
- About / Preferences windows — **M3**.
- Mica/Acrylic background (popup uses solid colour for M2) — **M3**.
- App icon `.ico` resource for the installer — **M4**.
- Velopack pack + SignPath + CI workflow — **M4**.
- Network availability + system-wake observers — **M3**.

**Reductions from the mac `UsageStore`** (deliberately deferred):

| Mac feature | M2 behaviour | Restored in |
|---|---|---|
| `Preferences.refreshInterval` configurable | Hardcoded `TimeSpan.FromSeconds(30)` | M3 |
| `Preferences.menuBarProviderID` configurable | First detected provider drives the tray ring | M3 |
| 429 backoff | Kept (per-provider exponential 120 / 300 / 900s with Retry-After) | — |
| Stale detection at 3× interval | Kept | — |
| Cache load on start | Kept | — |
| `NWPathMonitor` network reconnect refresh | Dropped | M3 |
| `NSWorkspace.didWakeNotification` wake refresh | Dropped | M3 |
| Manual refresh with 2s gap | Kept (called from tray context menu) | — |

---

## File structure

```
windows/
  src/TokenSpendie.Windows/
    TokenSpendie.Windows.csproj          # modified — UseWPF=true, OutputType=WinExe, new packages
    app.manifest                          # new — PerMonitorV2 DPI, asInvoker
    App.xaml                              # new — application resources, startup
    App.xaml.cs                           # new — DI/services bootstrap, replaces Program.cs as entry
    Models/
      UsageLevel.cs                       # new — calm/warn/hot tier enum
    Themes/
      Theme.xaml                          # new — minimal ResourceDictionary (colors only)
    Services/
      SnapshotCache.cs                    # new — per-provider JSON persistence
      UsageStore.cs                       # new — polling + INotifyPropertyChanged
    Tray/
      RingIconRenderer.cs                 # new — DrawingVisual → RenderTargetBitmap
      TrayIconLocator.cs                  # new — Shell_NotifyIconGetRect + SHAppBarMessage P/Invoke
      TrayIconController.cs               # new — wires icon + click → popup toggle
    Util/
      DpiHelper.cs                        # new — pixel↔DIP conversions
      Formatting.cs                       # new — reset countdown, "updated Ns ago", labels
    ViewModels/
      TrayIconViewModel.cs                # new — IconSource, ToolTipText, command bindings
      DetailPanelViewModel.cs             # new — projection of UsageStore.providers for the panel
    Views/
      DetailPanel.xaml(.cs)                # new — UserControl, the panel body
    Windows/
      PopupWindow.xaml(.cs)                # new — borderless transparent host for DetailPanel
  tests/TokenSpendie.Windows.Tests/
    Models/UsageLevelTests.cs
    Util/FormattingTests.cs
    Util/DpiHelperTests.cs
    Services/SnapshotCacheTests.cs
    Services/UsageStoreTests.cs
    Tray/RingIconRendererTests.cs         # STA-thread smoke + size assertions
    ViewModels/TrayIconViewModelTests.cs
    ViewModels/DetailPanelViewModelTests.cs
```

Files removed/changed:
- `Program.cs` — deleted. `App.xaml.cs` becomes the entry point via `[STAThread] static Main` in `App.xaml`'s generated partial.

---

## Conventions

- TDD: tests before implementation for every testable type (everything except XAML and P/Invoke wrappers).
- WPF and `H.NotifyIcon.Wpf` need an STA thread. The xUnit tests for `RingIconRenderer` use a small `STAFact` helper that runs the test body on a fresh STA thread. No external test-framework package — the helper is defined in `Tests/TestSupport/StaTheory.cs` (Task 0.5).
- ViewModels use CommunityToolkit.Mvvm's `[ObservableProperty]` and `[RelayCommand]` source generators. INPC plumbing is generated, not hand-rolled.
- `UsageStore` is the single source of truth. Both `TrayIconViewModel` and `DetailPanelViewModel` observe it via `INotifyPropertyChanged`.
- WPF UI thread vs polling background: `UsageStore` runs on the thread the host service starts it from; it raises `PropertyChanged` via `Application.Current?.Dispatcher.Invoke` so binding sinks are notified on the UI thread.
- Build command: `dotnet build windows/TokenSpendie.Windows.sln`. Test command: `dotnet test windows/TokenSpendie.Windows.sln`. Pin one test class with `--filter "FullyQualifiedName~<ClassName>"`.

---

### Task 1: Enable WPF + bring in MVVM/tray packages

**Files:**
- Modify: `windows/src/TokenSpendie.Windows/TokenSpendie.Windows.csproj`
- Delete: `windows/src/TokenSpendie.Windows/Program.cs`
- Create: `windows/src/TokenSpendie.Windows/app.manifest`
- Create: `windows/src/TokenSpendie.Windows/App.xaml`
- Create: `windows/src/TokenSpendie.Windows/App.xaml.cs`

- [ ] **Step 1: Branch**

```powershell
git fetch origin
git checkout -b windows-port-m2-tray-popup windows-port-m1-core
```

Expected: `Switched to a new branch 'windows-port-m2-tray-popup'`.

- [ ] **Step 2: Rewrite `TokenSpendie.Windows.csproj`**

Replace the file contents with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <UseWPF>true</UseWPF>
    <RootNamespace>TokenSpendie.Windows</RootNamespace>
    <AssemblyName>TokenSpendie.Windows</AssemblyName>
    <ApplicationManifest>app.manifest</ApplicationManifest>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.3.2" />
    <PackageReference Include="H.NotifyIcon.Wpf" Version="2.1.4" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Create `app.manifest`**

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity name="TokenSpendie.Windows" version="1.0.0.0" />
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v2">
    <security>
      <requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v3">
        <requestedExecutionLevel level="asInvoker" uiAccess="false" />
      </requestedPrivileges>
    </security>
  </trustInfo>
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
    </windowsSettings>
  </application>
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <!-- Windows 10 / 11 -->
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" />
    </application>
  </compatibility>
</assembly>
```

- [ ] **Step 4: Delete `Program.cs`**

```powershell
git rm windows/src/TokenSpendie.Windows/Program.cs
```

- [ ] **Step 5: Create `App.xaml` (minimal stub for now — populated in Task 13)**

```xml
<Application x:Class="TokenSpendie.Windows.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             ShutdownMode="OnExplicitShutdown">
  <Application.Resources />
</Application>
```

- [ ] **Step 6: Create `App.xaml.cs` (stub — Task 13 wires services)**

```csharp
using System.Windows;

namespace TokenSpendie.Windows;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Services + tray are wired in Task 13.
    }
}
```

- [ ] **Step 7: Build**

```powershell
dotnet build windows/TokenSpendie.Windows.sln
```

Expected: `Build succeeded`, no warnings. Tests still pass (`dotnet test` — count drops slightly because no SmokeTests change, but Program.cs's empty Main is gone so total stays at 68).

- [ ] **Step 8: Commit**

```powershell
git add -A windows/src/TokenSpendie.Windows windows/tests
git commit -m "feat(windows): enable WPF, add MVVM + tray packages, app.manifest (M2 Task 1)"
```

---

### Task 2: STA test helper

**Files:**
- Create: `windows/tests/TokenSpendie.Windows.Tests/TestSupport/StaFactAttribute.cs`

The WPF tests in later tasks (RingIconRenderer) need to run on an STA thread. xUnit defaults to MTA. Provide a `[StaFact]` attribute that wraps the test in a dedicated STA thread.

- [ ] **Step 1: Create the helper**

`windows/tests/TokenSpendie.Windows.Tests/TestSupport/StaFactAttribute.cs`:

```csharp
using System.Diagnostics;
using System.Threading;
using Xunit;
using Xunit.Sdk;

namespace TokenSpendie.Windows.Tests.TestSupport;

[XunitTestCaseDiscoverer(
    "TokenSpendie.Windows.Tests.TestSupport.StaFactDiscoverer",
    "TokenSpendie.Windows.Tests")]
public sealed class StaFactAttribute : FactAttribute { }

public sealed class StaFactDiscoverer : IXunitTestCaseDiscoverer
{
    private readonly IMessageSink _diag;
    public StaFactDiscoverer(IMessageSink diag) => _diag = diag;

    public System.Collections.Generic.IEnumerable<IXunitTestCase> Discover(
        ITestFrameworkDiscoveryOptions opts, ITestMethod method, IAttributeInfo factAttribute)
    {
        yield return new StaTestCase(_diag, opts.MethodDisplayOrDefault(), opts.MethodDisplayOptionsOrDefault(), method);
    }
}

public sealed class StaTestCase : XunitTestCase
{
    [System.Obsolete("Called by serializer", error: true)]
    public StaTestCase() { }

    public StaTestCase(IMessageSink diag, TestMethodDisplay display, TestMethodDisplayOptions options, ITestMethod method)
        : base(diag, display, options, method) { }

    public override System.Threading.Tasks.Task<RunSummary> RunAsync(
        IMessageSink diagnosticMessageSink, IMessageBus messageBus,
        object[] constructorArguments, ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource)
    {
        var tcs = new System.Threading.Tasks.TaskCompletionSource<RunSummary>();
        var thread = new Thread(() =>
        {
            try
            {
                var summary = base.RunAsync(diagnosticMessageSink, messageBus,
                    constructorArguments, aggregator, cancellationTokenSource)
                    .GetAwaiter().GetResult();
                tcs.SetResult(summary);
            }
            catch (System.Exception ex) { tcs.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}
```

- [ ] **Step 2: Build + ensure existing tests still pass**

```powershell
dotnet test windows/TokenSpendie.Windows.sln
```

Expected: 68 tests pass (the discoverer is registered but no `[StaFact]` is used yet).

- [ ] **Step 3: Commit**

```powershell
git add windows/tests/TokenSpendie.Windows.Tests/TestSupport
git commit -m "test(windows): add StaFact helper for STA-thread xUnit cases"
```

---

### Task 3: UsageLevel

**Files:**
- Create: `windows/src/TokenSpendie.Windows/Models/UsageLevel.cs`
- Create: `windows/tests/TokenSpendie.Windows.Tests/Models/UsageLevelTests.cs`

- [ ] **Step 1: Failing tests**

`windows/tests/TokenSpendie.Windows.Tests/Models/UsageLevelTests.cs`:

```csharp
using FluentAssertions;
using TokenSpendie.Windows.Models;
using Xunit;

namespace TokenSpendie.Windows.Tests.Models;

public class UsageLevelTests
{
    [Theory]
    [InlineData(0, UsageLevel.Calm)]
    [InlineData(69.999, UsageLevel.Calm)]
    [InlineData(70, UsageLevel.Warn)]
    [InlineData(89.999, UsageLevel.Warn)]
    [InlineData(90, UsageLevel.Hot)]
    [InlineData(150, UsageLevel.Hot)]
    public void ForPercentMapsBoundaries(double percent, UsageLevel expected)
    {
        UsageLevelExtensions.ForPercent(percent).Should().Be(expected);
    }
}
```

- [ ] **Step 2: Run — expect failure**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~UsageLevelTests"
```

- [ ] **Step 3: Implement**

`windows/src/TokenSpendie.Windows/Models/UsageLevel.cs`:

```csharp
namespace TokenSpendie.Windows.Models;

public enum UsageLevel { Calm, Warn, Hot }

public static class UsageLevelExtensions
{
    public static UsageLevel ForPercent(double percent) =>
        percent >= 90 ? UsageLevel.Hot
        : percent >= 70 ? UsageLevel.Warn
        : UsageLevel.Calm;
}
```

- [ ] **Step 4: Run — expect pass**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~UsageLevelTests"
```

Expected: 6 tests pass.

- [ ] **Step 5: Commit**

```powershell
git add windows/src/TokenSpendie.Windows/Models windows/tests/TokenSpendie.Windows.Tests/Models
git commit -m "feat(windows): add UsageLevel tier enum + ForPercent boundaries"
```

---

### Task 4: Formatting

**Files:**
- Create: `windows/src/TokenSpendie.Windows/Util/Formatting.cs`
- Create: `windows/tests/TokenSpendie.Windows.Tests/Util/FormattingTests.cs`

Ports `Sources/TokenSpendie/UI/Formatting.swift`. Pure functions; no DI needed.

- [ ] **Step 1: Failing tests**

`windows/tests/TokenSpendie.Windows.Tests/Util/FormattingTests.cs`:

```csharp
using System;
using FluentAssertions;
using TokenSpendie.Windows.Util;
using Xunit;

namespace TokenSpendie.Windows.Tests.Util;

public class FormattingTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    [Fact] public void ResetCountdownNullReturnsEmpty() =>
        Formatting.ResetCountdown(null, Now).Should().Be("");

    [Fact] public void ResetCountdownPastReturnsResettingNow() =>
        Formatting.ResetCountdown(Now.AddSeconds(-5), Now).Should().Be("resetting now");

    [Fact] public void ResetCountdownMinutesOnly() =>
        Formatting.ResetCountdown(Now.AddMinutes(40), Now).Should().Be("resets in 40m");

    [Fact] public void ResetCountdownHoursAndMinutes() =>
        Formatting.ResetCountdown(Now.AddMinutes(2 * 60 + 47), Now).Should().Be("resets in 2h 47m");

    [Fact] public void ResetDateNullReturnsEmpty() =>
        Formatting.ResetDate(null).Should().Be("");

    [Fact] public void ResetDateFormatsAsWeekday()
    {
        var date = new DateTimeOffset(2026, 5, 25, 0, 0, 0, TimeSpan.Zero); // Monday
        Formatting.ResetDate(date).Should().Be("resets Mon, May 25");
    }

    [Fact] public void UpdatedAgoJustNow() =>
        Formatting.UpdatedAgo(Now, Now).Should().Be("updated just now");

    [Fact] public void UpdatedAgoSeconds() =>
        Formatting.UpdatedAgo(Now.AddSeconds(-10), Now).Should().Be("updated 10s ago");

    [Fact] public void UpdatedAgoMinutes() =>
        Formatting.UpdatedAgo(Now.AddMinutes(-5), Now).Should().Be("updated 5m ago");

    [Fact] public void UpdatedAgoHours() =>
        Formatting.UpdatedAgo(Now.AddHours(-2), Now).Should().Be("updated 2h ago");
}
```

- [ ] **Step 2: Run — expect failure**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~FormattingTests"
```

- [ ] **Step 3: Implement**

`windows/src/TokenSpendie.Windows/Util/Formatting.cs`:

```csharp
using System.Globalization;

namespace TokenSpendie.Windows.Util;

public static class Formatting
{
    public static string ResetCountdown(DateTimeOffset? date, DateTimeOffset now)
    {
        if (date is null) return "";
        var remaining = (int)(date.Value - now).TotalSeconds;
        if (remaining <= 0) return "resetting now";
        var hours = remaining / 3600;
        var minutes = (remaining % 3600) / 60;
        return hours > 0
            ? $"resets in {hours}h {minutes}m"
            : $"resets in {minutes}m";
    }

    public static string ResetDate(DateTimeOffset? date) =>
        date is null
            ? ""
            : $"resets {date.Value.ToString("ddd, MMM d", CultureInfo.InvariantCulture)}";

    public static string UpdatedAgo(DateTimeOffset date, DateTimeOffset now)
    {
        var elapsed = (int)(now - date).TotalSeconds;
        if (elapsed < 3) return "updated just now";
        if (elapsed < 60) return $"updated {elapsed}s ago";
        if (elapsed < 3600) return $"updated {elapsed / 60}m ago";
        return $"updated {elapsed / 3600}h ago";
    }
}
```

- [ ] **Step 4: Run — expect pass**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~FormattingTests"
```

Expected: 10 tests pass.

- [ ] **Step 5: Commit**

```powershell
git add windows/src/TokenSpendie.Windows/Util windows/tests/TokenSpendie.Windows.Tests/Util
git commit -m "feat(windows): port Formatting (countdown, reset date, updated-ago)"
```

---

### Task 5: DpiHelper

**Files:**
- Create: `windows/src/TokenSpendie.Windows/Util/DpiHelper.cs`
- Create: `windows/tests/TokenSpendie.Windows.Tests/Util/DpiHelperTests.cs`

- [ ] **Step 1: Failing tests**

`windows/tests/TokenSpendie.Windows.Tests/Util/DpiHelperTests.cs`:

```csharp
using FluentAssertions;
using TokenSpendie.Windows.Util;
using Xunit;

namespace TokenSpendie.Windows.Tests.Util;

public class DpiHelperTests
{
    [Theory]
    [InlineData(100, 1.0, 100)]
    [InlineData(100, 1.5, 67)]   // ceiling(100/1.5)? — see impl note; pixels->DIPs rounds down via int conversion
    [InlineData(100, 2.0, 50)]
    public void PixelsToDipsDivides(double pixels, double scale, double expected)
    {
        DpiHelper.PixelsToDips(pixels, scale).Should().BeApproximately(expected, 1.0);
    }

    [Theory]
    [InlineData(50, 1.0, 50)]
    [InlineData(50, 1.5, 75)]
    [InlineData(50, 2.0, 100)]
    public void DipsToPixelsMultiplies(double dips, double scale, double expected)
    {
        DpiHelper.DipsToPixels(dips, scale).Should().BeApproximately(expected, 0.001);
    }

    [Fact]
    public void IconPxRoundsUpToInt()
    {
        DpiHelper.IconPx(1.0).Should().Be(16);
        DpiHelper.IconPx(1.25).Should().Be(20);
        DpiHelper.IconPx(1.5).Should().Be(24);
        DpiHelper.IconPx(2.0).Should().Be(32);
    }
}
```

- [ ] **Step 2: Run — expect failure**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~DpiHelperTests"
```

- [ ] **Step 3: Implement**

`windows/src/TokenSpendie.Windows/Util/DpiHelper.cs`:

```csharp
namespace TokenSpendie.Windows.Util;

/// <summary>Pixel ↔ DIP conversion + tray icon size for a given DPI scale.</summary>
public static class DpiHelper
{
    /// <summary>96 DPI = 1.0 scale = 1 DIP per pixel.</summary>
    public const double BaselineDpi = 96.0;

    public static double PixelsToDips(double pixels, double scale) => pixels / scale;
    public static double DipsToPixels(double dips, double scale) => dips * scale;

    /// <summary>The tray icon size in physical pixels for the given DPI scale.
    /// 16 at 100%, 20 at 125%, 24 at 150%, 32 at 200%.</summary>
    public static int IconPx(double scale) => (int)System.Math.Ceiling(16 * scale);
}
```

- [ ] **Step 4: Run — expect pass**

Expected: 10 tests pass.

- [ ] **Step 5: Commit**

```powershell
git add windows/src/TokenSpendie.Windows/Util windows/tests/TokenSpendie.Windows.Tests/Util
git commit -m "feat(windows): add DpiHelper (px/DIP/IconPx)"
```

---

### Task 6: SnapshotCache

**Files:**
- Create: `windows/src/TokenSpendie.Windows/Services/SnapshotCache.cs`
- Create: `windows/tests/TokenSpendie.Windows.Tests/Services/SnapshotCacheTests.cs`

Per-provider JSON file under `%LOCALAPPDATA%\TokenSpendie\snapshot-<provider>.json`. Round-trip a `ProviderSnapshot`. Corruption-tolerant load returns null.

- [ ] **Step 1: Failing tests**

`windows/tests/TokenSpendie.Windows.Tests/Services/SnapshotCacheTests.cs`:

```csharp
using System;
using System.IO;
using FluentAssertions;
using TokenSpendie.Windows.Models;
using TokenSpendie.Windows.Services;
using Xunit;

namespace TokenSpendie.Windows.Tests.Services;

public class SnapshotCacheTests : IDisposable
{
    private readonly string _dir;
    public SnapshotCacheTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"sc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private SnapshotCache Cache(ProviderID id) =>
        new(Path.Combine(_dir, $"snapshot-{id.ToString().ToLowerInvariant()}.json"));

    private static ProviderSnapshot SampleSnapshot()
    {
        var headline = new LabeledWindow("Session", "5-hour window",
            ResetStyle.Countdown, new UsageWindow(47, DateTimeOffset.FromUnixTimeSeconds(100)));
        return new ProviderSnapshot(
            Id: ProviderID.Claude, Plan: null,
            Headline: headline, Windows: new[] { headline },
            FetchedAt: DateTimeOffset.FromUnixTimeSeconds(999));
    }

    [Fact]
    public void LoadReturnsNullWhenFileMissing()
    {
        Cache(ProviderID.Claude).Load().Should().BeNull();
    }

    [Fact]
    public void SaveThenLoadRoundTrips()
    {
        var cache = Cache(ProviderID.Claude);
        var snap = SampleSnapshot();
        cache.Save(snap);
        cache.Load().Should().Be(snap);
    }

    [Fact]
    public void LoadReturnsNullWhenJsonIsGarbage()
    {
        var cache = Cache(ProviderID.Claude);
        File.WriteAllText(cache.FileUrl, "not json");
        cache.Load().Should().BeNull();
    }

    [Fact]
    public void SaveCreatesParentDirectory()
    {
        var nestedDir = Path.Combine(_dir, "nested", "deep");
        var cache = new SnapshotCache(Path.Combine(nestedDir, "snapshot-claude.json"));
        cache.Save(SampleSnapshot());
        File.Exists(cache.FileUrl).Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run — expect failure**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~SnapshotCacheTests"
```

- [ ] **Step 3: Implement**

`windows/src/TokenSpendie.Windows/Services/SnapshotCache.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using TokenSpendie.Windows.Models;

namespace TokenSpendie.Windows.Services;

/// <summary>Persists one <see cref="ProviderSnapshot"/> for one provider as JSON.</summary>
public sealed class SnapshotCache
{
    public string FileUrl { get; }

    private static readonly JsonSerializerOptions Options;

    static SnapshotCache()
    {
        Options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        Options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    public SnapshotCache(string fileUrl)
    {
        FileUrl = fileUrl;
    }

    public static string DefaultPathFor(ProviderID id)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TokenSpendie");
        return Path.Combine(dir, $"snapshot-{id.ToString().ToLowerInvariant()}.json");
    }

    public ProviderSnapshot? Load()
    {
        try
        {
            if (!File.Exists(FileUrl)) return null;
            using var stream = File.OpenRead(FileUrl);
            return JsonSerializer.Deserialize<ProviderSnapshot>(stream, Options);
        }
        catch { return null; }
    }

    public void Save(ProviderSnapshot snapshot)
    {
        try
        {
            var parent = Path.GetDirectoryName(FileUrl);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            using var stream = File.Create(FileUrl);
            JsonSerializer.Serialize(stream, snapshot, Options);
        }
        catch { /* best-effort */ }
    }
}
```

- [ ] **Step 4: Run — expect pass**

Expected: 4 tests pass.

- [ ] **Step 5: Commit**

```powershell
git add windows/src/TokenSpendie.Windows/Services windows/tests/TokenSpendie.Windows.Tests/Services
git commit -m "feat(windows): add SnapshotCache (per-provider JSON persistence)"
```

---

### Task 7: UsageStore

**Files:**
- Create: `windows/src/TokenSpendie.Windows/Services/UsageStore.cs`
- Create: `windows/tests/TokenSpendie.Windows.Tests/Services/UsageStoreTests.cs`

This is the M2 polling driver. **Reductions from mac:** no preferences (hardcoded interval), no network observer, no wake observer. Backoff, stale detection, manual refresh, cache load — all kept.

- [ ] **Step 1: Failing tests**

`windows/tests/TokenSpendie.Windows.Tests/Services/UsageStoreTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TokenSpendie.Windows.Data;
using TokenSpendie.Windows.Models;
using TokenSpendie.Windows.Services;
using Xunit;

namespace TokenSpendie.Windows.Tests.Services;

public class UsageStoreTests : IDisposable
{
    private readonly string _dir;
    private DateTimeOffset _now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    public UsageStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"us-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private SnapshotCache Cache(ProviderID id) =>
        new(Path.Combine(_dir, $"snapshot-{id.ToString().ToLowerInvariant()}.json"));

    private UsageStore MakeStore(params IUsageProvider[] providers) =>
        new(providers, id => Cache(id), now: () => _now);

    private static ProviderSnapshot Snap(ProviderID id, double percent = 47, DateTimeOffset? fetchedAt = null)
    {
        var headline = new LabeledWindow("Session", "5-hour window",
            ResetStyle.Countdown, new UsageWindow(percent, null));
        return new ProviderSnapshot(id, null, headline, new[] { headline },
            fetchedAt ?? DateTimeOffset.FromUnixTimeSeconds(0));
    }

    private static IUsageProvider Stub(ProviderID id, string name,
        bool detected, Func<CancellationToken, Task<ProviderSnapshot>>? fetch = null)
    {
        var p = Substitute.For<IUsageProvider>();
        p.Id.Returns(id);
        p.DisplayName.Returns(name);
        p.DetectCredentials().Returns(detected);
        if (fetch is not null)
            p.FetchUsageAsync(Arg.Any<CancellationToken>()).Returns(ci => fetch((CancellationToken)ci[0]));
        return p;
    }

    [Fact]
    public async Task RefreshFetchesOnlyDetectedProviders()
    {
        var claude = Stub(ProviderID.Claude, "Claude", true, _ => Task.FromResult(Snap(ProviderID.Claude)));
        var gemini = Stub(ProviderID.Gemini, "Gemini", false);
        var store = MakeStore(claude, gemini);

        await store.RefreshAsync();

        store.Providers.Should().ContainSingle();
        store.Providers[0].Id.Should().Be(ProviderID.Claude);
        store.Providers[0].State.Should().Be(LoadState.Ok);
        await gemini.DidNotReceive().FetchUsageAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnauthorizedMapsToLoginExpiredErrorState()
    {
        var claude = Stub(ProviderID.Claude, "Claude", true,
            _ => throw new ProviderUnauthorizedException());
        var store = MakeStore(claude);

        await store.RefreshAsync();

        store.Providers[0].State.Should().Be(LoadState.Error(UsageErrorKind.LoginExpired));
    }

    [Fact]
    public async Task NetworkErrorWithCachedSnapshotMarksStale()
    {
        var snap = Snap(ProviderID.Claude);
        Cache(ProviderID.Claude).Save(snap);

        var claude = Stub(ProviderID.Claude, "Claude", true,
            _ => throw new ProviderNetworkException(new System.Net.Http.HttpRequestException("offline")));
        var store = MakeStore(claude);
        store.Start();        // loads cache
        store.Providers[0].State.Should().Be(LoadState.Stale, "cache is older than 60s relative to _now");
    }

    [Fact]
    public async Task RateLimitedBacksOffNextRefresh()
    {
        var calls = 0;
        var claude = Stub(ProviderID.Claude, "Claude", true,
            _ =>
            {
                calls++;
                throw new ProviderRateLimitedException(TimeSpan.FromMinutes(5));
            });
        var store = MakeStore(claude);

        await store.RefreshAsync();
        calls.Should().Be(1);

        // Within backoff window — second refresh skips the provider.
        _now += TimeSpan.FromMinutes(1);
        await store.RefreshAsync();
        calls.Should().Be(1, "backoff was 5 minutes");

        // Past backoff — refreshes again.
        _now += TimeSpan.FromMinutes(5);
        await store.RefreshAsync();
        calls.Should().Be(2);
    }

    [Fact]
    public async Task ManualRefreshIgnoresBackoff()
    {
        var calls = 0;
        var claude = Stub(ProviderID.Claude, "Claude", true,
            _ =>
            {
                calls++;
                throw new ProviderRateLimitedException(TimeSpan.FromMinutes(5));
            });
        var store = MakeStore(claude);
        await store.RefreshAsync();
        calls.Should().Be(1);

        await store.ManualRefreshAsync();
        calls.Should().Be(2);
    }

    [Fact]
    public async Task ManualRefreshSwallowsRapidDoubleClick()
    {
        var calls = 0;
        var claude = Stub(ProviderID.Claude, "Claude", true,
            _ => { calls++; return Task.FromResult(Snap(ProviderID.Claude)); });
        var store = MakeStore(claude);

        await store.ManualRefreshAsync();
        await store.ManualRefreshAsync();           // immediate second call — gated by 2s gap
        calls.Should().Be(1);

        _now += TimeSpan.FromSeconds(3);
        await store.ManualRefreshAsync();
        calls.Should().Be(2);
    }

    [Fact]
    public async Task StartLoadsFreshCachedSnapshotAsOk()
    {
        var snap = Snap(ProviderID.Claude, fetchedAt: _now.AddSeconds(-10));
        Cache(ProviderID.Claude).Save(snap);

        var claude = Stub(ProviderID.Claude, "Claude", true,
            _ => Task.FromResult(Snap(ProviderID.Claude, percent: 99)));
        var store = MakeStore(claude);

        store.Start();
        store.Providers.Should().ContainSingle();
        store.Providers[0].State.Should().Be(LoadState.Ok);
        store.Providers[0].Snapshot.Should().Be(snap);
    }

    [Fact]
    public async Task PropertyChangedRaisedForProviders()
    {
        var claude = Stub(ProviderID.Claude, "Claude", true,
            _ => Task.FromResult(Snap(ProviderID.Claude)));
        var store = MakeStore(claude);
        var fired = new List<string?>();
        store.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        await store.RefreshAsync();

        fired.Should().Contain(nameof(UsageStore.Providers));
    }
}
```

- [ ] **Step 2: Run — expect failure**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~UsageStoreTests"
```

- [ ] **Step 3: Implement**

`windows/src/TokenSpendie.Windows/Services/UsageStore.cs`:

```csharp
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using TokenSpendie.Windows.Data;
using TokenSpendie.Windows.Models;

namespace TokenSpendie.Windows.Services;

/// <summary>
/// Drives polling of every registered provider, owns the per-provider state
/// and surfaces it as <see cref="Providers"/> through <see cref="INotifyPropertyChanged"/>.
/// </summary>
public sealed class UsageStore : INotifyPropertyChanged, IAsyncDisposable
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ManualRefreshMinGap = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan FreshCacheWindow = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan[] BackoffSteps =
        { TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15) };

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<ProviderUsage> Providers { get; private set; } =
        System.Array.Empty<ProviderUsage>();
    public bool IsRefreshing { get; private set; }

    private readonly IUsageProvider[] _registered;
    private readonly Dictionary<ProviderID, SnapshotCache> _caches;
    private readonly Func<DateTimeOffset> _now;

    private readonly Dictionary<ProviderID, ProviderUsage> _usageByID = new();
    private readonly Dictionary<ProviderID, DateTimeOffset> _backoffUntil = new();
    private readonly Dictionary<ProviderID, int> _consecutiveRateLimits = new();
    private readonly Dictionary<ProviderID, DateTimeOffset> _lastSuccess = new();

    private DateTimeOffset? _lastManualRefresh;
    private PeriodicTimer? _timer;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public UsageStore(
        IEnumerable<IUsageProvider> providers,
        Func<ProviderID, SnapshotCache>? cacheFactory = null,
        Func<DateTimeOffset>? now = null)
    {
        _registered = providers.ToArray();
        cacheFactory ??= id => new SnapshotCache(SnapshotCache.DefaultPathFor(id));
        _caches = _registered.ToDictionary(p => p.Id, p => cacheFactory(p.Id));
        _now = now ?? (() => DateTimeOffset.Now);
    }

    /// <summary>Loads cached snapshots, starts the polling timer.</summary>
    public void Start()
    {
        foreach (var provider in _registered)
        {
            var cached = _caches[provider.Id].Load();
            if (cached is not null)
            {
                var fresh = (_now() - cached.FetchedAt) < FreshCacheWindow;
                _usageByID[provider.Id] = new ProviderUsage(provider.Id, provider.DisplayName)
                {
                    State = fresh ? LoadState.Ok : LoadState.Stale,
                    Snapshot = cached,
                };
                if (fresh) _lastSuccess[provider.Id] = cached.FetchedAt;
            }
            else
            {
                _usageByID[provider.Id] = new ProviderUsage(provider.Id, provider.DisplayName)
                {
                    State = LoadState.Loading,
                };
            }
        }
        Publish(_registered.Select(p => p.Id));

        _cts = new CancellationTokenSource();
        _timer = new PeriodicTimer(RefreshInterval);
        _loopTask = LoopAsync(_cts.Token);
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        try
        {
            while (await _timer!.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                MarkStaleIfNeeded();
                await RefreshAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* graceful shutdown */ }
    }

    /// <summary>One refresh cycle. Detect every registered provider, then fetch
    /// each detected one (skipping those currently in 429 backoff unless ignoringBackoff).</summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await RunCycleAsync(ignoringBackoff: false, ct).ConfigureAwait(false);
    }

    /// <summary>User-initiated refresh from the tray menu. Skips backoff. Ignored
    /// while a refresh is already running or within 2s of the previous manual refresh.</summary>
    public async Task ManualRefreshAsync(CancellationToken ct = default)
    {
        if (IsRefreshing) return;
        if (_lastManualRefresh is { } last && (_now() - last) < ManualRefreshMinGap) return;
        _lastManualRefresh = _now();
        await RunCycleAsync(ignoringBackoff: true, ct).ConfigureAwait(false);
    }

    private async Task RunCycleAsync(bool ignoringBackoff, CancellationToken ct)
    {
        IsRefreshing = true;
        Raise(nameof(IsRefreshing));
        try
        {
            var detected = _registered.Where(p => p.DetectCredentials()).ToArray();
            var detectedIds = detected.Select(p => p.Id).ToHashSet();

            // Drop entries for providers no longer detected.
            foreach (var id in _usageByID.Keys.Except(detectedIds).ToArray())
                _usageByID.Remove(id);

            foreach (var provider in detected)
                await RefreshOneAsync(provider, ignoringBackoff, ct).ConfigureAwait(false);

            Publish(detected.Select(p => p.Id));
        }
        finally
        {
            IsRefreshing = false;
            Raise(nameof(IsRefreshing));
        }
    }

    private async Task RefreshOneAsync(IUsageProvider provider, bool ignoringBackoff, CancellationToken ct)
    {
        var id = provider.Id;
        if (!ignoringBackoff && _backoffUntil.TryGetValue(id, out var until) && _now() < until)
            return;

        _usageByID.TryAdd(id, new ProviderUsage(id, provider.DisplayName) { State = LoadState.Loading });

        try
        {
            var snapshot = await provider.FetchUsageAsync(ct).ConfigureAwait(false);
            ApplySuccess(snapshot, id, provider.DisplayName);
        }
        catch (ProviderUnauthorizedException)
        {
            SetState(LoadState.Error(UsageErrorKind.LoginExpired), id);
        }
        catch (ProviderNetworkException)
        {
            Degrade(UsageErrorKind.Network, id);
        }
        catch (ProviderBadResponseException)
        {
            Degrade(UsageErrorKind.BadResponse, id);
        }
        catch (ProviderRateLimitedException ex)
        {
            ApplyRateLimitBackoff(ex.RetryAfter, id);
        }
        catch (CredentialNotFoundException)
        {
            SetState(LoadState.Error(UsageErrorKind.ClaudeCodeNotFound), id);
        }
        catch (CredentialMalformedException)
        {
            SetState(LoadState.Error(UsageErrorKind.ClaudeCodeNotFound), id);
        }
        catch (CredentialAccessDeniedException)
        {
            SetState(LoadState.Error(UsageErrorKind.CredentialAccessDenied), id);
        }
        catch
        {
            Degrade(UsageErrorKind.BadResponse, id);
        }
    }

    private void ApplySuccess(ProviderSnapshot snapshot, ProviderID id, string displayName)
    {
        _usageByID[id] = new ProviderUsage(id, displayName)
        {
            State = LoadState.Ok,
            Snapshot = snapshot,
        };
        _caches[id].Save(snapshot);
        _lastSuccess[id] = _now();
        _backoffUntil.Remove(id);
        _consecutiveRateLimits.Remove(id);
    }

    private void SetState(LoadState state, ProviderID id)
    {
        if (!_usageByID.TryGetValue(id, out var usage)) return;
        usage.State = state;
    }

    /// <summary>Soft failure: keep the cached snapshot as stale if there is one,
    /// otherwise surface the error.</summary>
    private void Degrade(UsageErrorKind kind, ProviderID id)
    {
        if (_usageByID.TryGetValue(id, out var usage) && usage.Snapshot is not null)
            usage.State = LoadState.Stale;
        else
            SetState(LoadState.Error(kind), id);
    }

    private void ApplyRateLimitBackoff(TimeSpan? retryAfter, ProviderID id)
    {
        var count = _consecutiveRateLimits.TryGetValue(id, out var c) ? c + 1 : 1;
        _consecutiveRateLimits[id] = count;
        var fallback = BackoffSteps[System.Math.Min(count - 1, BackoffSteps.Length - 1)];
        _backoffUntil[id] = _now() + (retryAfter ?? fallback);
        Degrade(UsageErrorKind.BadResponse, id);
    }

    private void MarkStaleIfNeeded()
    {
        var threshold = RefreshInterval * 3;
        foreach (var (id, usage) in _usageByID.ToArray())
        {
            if (usage.State != LoadState.Ok) continue;
            if (_lastSuccess.TryGetValue(id, out var last) && (_now() - last) > threshold)
                usage.State = LoadState.Stale;
        }
    }

    private void Publish(IEnumerable<ProviderID> order)
    {
        Providers = order
            .Select(id => _usageByID.TryGetValue(id, out var u) ? u : null)
            .Where(u => u is not null)
            .Cast<ProviderUsage>()
            .ToArray();
        Raise(nameof(Providers));
    }

    private void Raise(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_loopTask is not null)
        {
            try { await _loopTask.ConfigureAwait(false); } catch { }
        }
        _timer?.Dispose();
        _cts?.Dispose();
    }
}
```

- [ ] **Step 4: Run — expect pass**

Expected: 8 tests pass.

- [ ] **Step 5: Commit**

```powershell
git add windows/src/TokenSpendie.Windows/Services windows/tests/TokenSpendie.Windows.Tests/Services
git commit -m "feat(windows): add UsageStore (M2 polling driver, backoff, stale, cache)"
```

---

### Task 8: RingIconRenderer

**Files:**
- Create: `windows/src/TokenSpendie.Windows/Tray/RingIconRenderer.cs`
- Create: `windows/tests/TokenSpendie.Windows.Tests/Tray/RingIconRendererTests.cs`

Pure WPF rendering of the tray-icon ring. Needs STA — tests use `[StaFact]` from Task 2.

- [ ] **Step 1: Failing tests**

`windows/tests/TokenSpendie.Windows.Tests/Tray/RingIconRendererTests.cs`:

```csharp
using System.Windows.Media;
using FluentAssertions;
using TokenSpendie.Windows.Models;
using TokenSpendie.Windows.Tests.TestSupport;
using TokenSpendie.Windows.Tray;

namespace TokenSpendie.Windows.Tests.Tray;

public class RingIconRendererTests
{
    [StaFact]
    public void RenderProducesNonNullFrozenBitmap()
    {
        var icon = RingIconRenderer.Render(percent: 50, level: UsageLevel.Warn, dpiScale: 1.0);
        icon.Should().NotBeNull();
        icon!.IsFrozen.Should().BeTrue("the bitmap must be frozen to be assigned across threads");
    }

    [StaFact]
    public void RenderUsesIconPxForBitmapSize()
    {
        // At 2.0 scale → 32×32 px.
        var icon = RingIconRenderer.Render(percent: 75, level: UsageLevel.Warn, dpiScale: 2.0);
        ((System.Windows.Media.Imaging.RenderTargetBitmap)icon!).PixelWidth.Should().Be(32);
        ((System.Windows.Media.Imaging.RenderTargetBitmap)icon).PixelHeight.Should().Be(32);
    }

    [StaFact]
    public void RenderClampsOverHundredPercentToFullRing()
    {
        // Should not throw; should produce a valid bitmap at any input.
        RingIconRenderer.Render(percent: 150, level: UsageLevel.Hot, dpiScale: 1.0).Should().NotBeNull();
        RingIconRenderer.Render(percent: -5, level: UsageLevel.Calm, dpiScale: 1.0).Should().NotBeNull();
    }
}
```

- [ ] **Step 2: Run — expect failure**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~RingIconRendererTests"
```

- [ ] **Step 3: Implement**

`windows/src/TokenSpendie.Windows/Tray/RingIconRenderer.cs`:

```csharp
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TokenSpendie.Windows.Models;
using TokenSpendie.Windows.Util;

namespace TokenSpendie.Windows.Tray;

/// <summary>
/// Renders a usage-ring tray icon: a faint track plus a coloured arc that sweeps
/// clockwise from 12 o'clock by `percent`%. Always returns a frozen
/// <see cref="RenderTargetBitmap"/> safe to assign across threads.
/// </summary>
public static class RingIconRenderer
{
    private static readonly Brush TrackBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));

    private static readonly Brush CalmBrush = new SolidColorBrush(Color.FromRgb(0x5F, 0xB8, 0x78));
    private static readonly Brush WarnBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xA2, 0x3F));
    private static readonly Brush HotBrush = new SolidColorBrush(Color.FromRgb(0xD9, 0x53, 0x4F));

    static RingIconRenderer()
    {
        TrackBrush.Freeze();
        CalmBrush.Freeze();
        WarnBrush.Freeze();
        HotBrush.Freeze();
    }

    public static ImageSource Render(double percent, UsageLevel level, double dpiScale)
    {
        var px = DpiHelper.IconPx(dpiScale);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var center = new Point(px / 2.0, px / 2.0);
            var stroke = System.Math.Max(2.0, px / 8.0);
            var radius = (px - stroke) / 2.0;

            dc.DrawEllipse(null, new Pen(TrackBrush, stroke), center, radius, radius);

            var fraction = System.Math.Clamp(percent / 100.0, 0, 1);
            if (fraction > 0)
            {
                var startPoint = new Point(center.X, center.Y - radius);
                var endPoint = PointOnCircle(center, radius, -90 + 360 * fraction);
                var arc = new ArcSegment(
                    endPoint, new Size(radius, radius),
                    rotationAngle: 0,
                    isLargeArc: fraction > 0.5,
                    sweepDirection: SweepDirection.Clockwise,
                    isStroked: true);

                var figure = new PathFigure { StartPoint = startPoint };
                figure.Segments.Add(arc);
                var pen = new Pen(BrushFor(level), stroke)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                };
                dc.DrawGeometry(null, pen, new PathGeometry(new[] { figure }));
            }
        }

        var rtb = new RenderTargetBitmap(
            px, px, DpiHelper.BaselineDpi * dpiScale, DpiHelper.BaselineDpi * dpiScale,
            PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    private static Point PointOnCircle(Point center, double radius, double angleDeg)
    {
        var angleRad = angleDeg * System.Math.PI / 180.0;
        return new Point(center.X + radius * System.Math.Cos(angleRad),
                         center.Y + radius * System.Math.Sin(angleRad));
    }

    private static Brush BrushFor(UsageLevel level) => level switch
    {
        UsageLevel.Calm => CalmBrush,
        UsageLevel.Warn => WarnBrush,
        UsageLevel.Hot => HotBrush,
        _ => CalmBrush,
    };
}
```

- [ ] **Step 4: Run — expect pass**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~RingIconRendererTests"
```

Expected: 3 tests pass.

- [ ] **Step 5: Commit**

```powershell
git add windows/src/TokenSpendie.Windows/Tray windows/tests/TokenSpendie.Windows.Tests/Tray
git commit -m "feat(windows): add RingIconRenderer (WPF DrawingVisual → frozen RTB)"
```

---

### Task 9: TrayIconLocator (P/Invoke)

**Files:**
- Create: `windows/src/TokenSpendie.Windows/Tray/TrayIconLocator.cs`

P/Invoke wrappers around `Shell_NotifyIconGetRect` + `SHAppBarMessage(ABM_GETTASKBARPOS)`. No unit tests — these talk to live shell state. Smoke-tested via the running app.

- [ ] **Step 1: Implement**

`windows/src/TokenSpendie.Windows/Tray/TrayIconLocator.cs`:

```csharp
using System.Runtime.InteropServices;
using System.Windows;

namespace TokenSpendie.Windows.Tray;

/// <summary>Native shell-rect + taskbar-edge probes for popup positioning.</summary>
public static class TrayIconLocator
{
    public enum TaskbarEdge { Left, Top, Right, Bottom }

    /// <summary>The screen rect (in physical pixels) of the tray icon
    /// identified by (hwnd, uID). Returns null if the icon is currently
    /// in the Win11 overflow flyout (Shell_NotifyIconGetRect returns E_FAIL).</summary>
    public static Rect? GetIconRect(nint hwnd, uint uID)
    {
        var id = new NOTIFYICONIDENTIFIER
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONIDENTIFIER>(),
            hWnd = hwnd,
            uID = uID,
        };
        var hr = Shell_NotifyIconGetRect(ref id, out var rect);
        return hr == 0
            ? new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top)
            : null;
    }

    /// <summary>The taskbar edge (which screen side it docks to). Defaults to <see cref="TaskbarEdge.Bottom"/>
    /// if the call fails.</summary>
    public static TaskbarEdge GetTaskbarEdge()
    {
        var data = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>() };
        const uint ABM_GETTASKBARPOS = 5;
        if (SHAppBarMessage(ABM_GETTASKBARPOS, ref data) == nint.Zero)
            return TaskbarEdge.Bottom;
        return data.uEdge switch
        {
            0 => TaskbarEdge.Left,
            1 => TaskbarEdge.Top,
            2 => TaskbarEdge.Right,
            3 => TaskbarEdge.Bottom,
            _ => TaskbarEdge.Bottom,
        };
    }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern nint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [StructLayout(LayoutKind.Sequential)]
    private struct NOTIFYICONIDENTIFIER
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public System.Guid guidItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public uint cbSize;
        public nint hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public int lParam;
    }
}
```

- [ ] **Step 2: Build**

```powershell
dotnet build windows/TokenSpendie.Windows.sln
```

Expected: Build succeeded, no warnings.

- [ ] **Step 3: Commit**

```powershell
git add windows/src/TokenSpendie.Windows/Tray
git commit -m "feat(windows): add TrayIconLocator (Shell_NotifyIconGetRect + ABM_GETTASKBARPOS)"
```

---

### Task 10: TrayIconViewModel

**Files:**
- Create: `windows/src/TokenSpendie.Windows/ViewModels/TrayIconViewModel.cs`
- Create: `windows/tests/TokenSpendie.Windows.Tests/ViewModels/TrayIconViewModelTests.cs`

Drives the tray icon's icon, tooltip, and left-click command. Listens to `UsageStore.PropertyChanged` and re-renders the ring when the headline provider's snapshot changes.

- [ ] **Step 1: Failing tests**

`windows/tests/TokenSpendie.Windows.Tests/ViewModels/TrayIconViewModelTests.cs`:

```csharp
using System;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using TokenSpendie.Windows.Data;
using TokenSpendie.Windows.Models;
using TokenSpendie.Windows.Services;
using TokenSpendie.Windows.Tests.TestSupport;
using TokenSpendie.Windows.ViewModels;
using Xunit;

namespace TokenSpendie.Windows.Tests.ViewModels;

public class TrayIconViewModelTests
{
    private static IUsageProvider Stub(ProviderID id, string name, ProviderSnapshot? snap)
    {
        var p = Substitute.For<IUsageProvider>();
        p.Id.Returns(id);
        p.DisplayName.Returns(name);
        p.DetectCredentials().Returns(true);
        p.FetchUsageAsync(default).ReturnsForAnyArgs(Task.FromResult(snap!));
        return p;
    }

    private static ProviderSnapshot Snap(double percent)
    {
        var headline = new LabeledWindow("Session", "5-hour window",
            ResetStyle.Countdown, new UsageWindow(percent, null));
        return new ProviderSnapshot(ProviderID.Claude, null, headline,
            new[] { headline }, DateTimeOffset.FromUnixTimeSeconds(0));
    }

    [StaFact]
    public async Task ToolTipReflectsHeadlinePercent()
    {
        var store = new UsageStore(new[] { Stub(ProviderID.Claude, "Claude", Snap(47.3)) });
        await store.RefreshAsync();
        var vm = new TrayIconViewModel(store);
        vm.ToolTipText.Should().Contain("47");
        vm.ToolTipText.Should().Contain("Claude");
    }

    [StaFact]
    public async Task IconSourceIsSetAfterRefresh()
    {
        var store = new UsageStore(new[] { Stub(ProviderID.Claude, "Claude", Snap(50)) });
        await store.RefreshAsync();
        var vm = new TrayIconViewModel(store);
        vm.IconSource.Should().NotBeNull();
    }

    [StaFact]
    public void ToolTipFallbackBeforeData()
    {
        var store = new UsageStore(new[] { Stub(ProviderID.Claude, "Claude", Snap(50)) });
        var vm = new TrayIconViewModel(store);
        vm.ToolTipText.Should().Be("Token Spendie — loading…");
    }

    [StaFact]
    public async Task LeftClickCommandRaisesShowPopupRequested()
    {
        var store = new UsageStore(new[] { Stub(ProviderID.Claude, "Claude", Snap(10)) });
        var vm = new TrayIconViewModel(store);
        var fired = 0;
        vm.ShowPopupRequested += (_, _) => fired++;
        vm.LeftClickCommand.Execute(null);
        fired.Should().Be(1);
    }
}
```

- [ ] **Step 2: Run — expect failure**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~TrayIconViewModelTests"
```

- [ ] **Step 3: Implement**

`windows/src/TokenSpendie.Windows/ViewModels/TrayIconViewModel.cs`:

```csharp
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TokenSpendie.Windows.Models;
using TokenSpendie.Windows.Services;
using TokenSpendie.Windows.Tray;

namespace TokenSpendie.Windows.ViewModels;

public partial class TrayIconViewModel : ObservableObject
{
    private readonly UsageStore _store;
    private double _dpiScale = 1.0;

    [ObservableProperty] private ImageSource? _iconSource;
    [ObservableProperty] private string _toolTipText = "Token Spendie — loading…";

    public event System.EventHandler? ShowPopupRequested;

    public TrayIconViewModel(UsageStore store)
    {
        _store = store;
        _store.PropertyChanged += OnStorePropertyChanged;
        RecomputeFromStore();
    }

    /// <summary>Called by the host when the icon's monitor DPI changes.</summary>
    public void OnDpiChanged(double newScale)
    {
        _dpiScale = newScale;
        RecomputeFromStore();
    }

    [RelayCommand]
    private void LeftClick() => ShowPopupRequested?.Invoke(this, System.EventArgs.Empty);

    private void OnStorePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(UsageStore.Providers))
            RecomputeFromStore();
    }

    private void RecomputeFromStore()
    {
        var headline = HeadlineProvider();
        if (headline?.Snapshot is null)
        {
            ToolTipText = "Token Spendie — loading…";
            return;
        }

        var percent = headline.Snapshot.Headline.Window.Percent;
        var level = UsageLevelExtensions.ForPercent(percent);
        IconSource = RingIconRenderer.Render(percent, level, _dpiScale);
        ToolTipText = $"Token Spendie — {headline.DisplayName} {percent:F0}%";
    }

    private ProviderUsage? HeadlineProvider()
    {
        foreach (var u in _store.Providers)
        {
            if (u.State == LoadState.Ok || u.State == LoadState.Stale) return u;
        }
        return _store.Providers.Count > 0 ? _store.Providers[0] : null;
    }
}
```

- [ ] **Step 4: Run — expect pass**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~TrayIconViewModelTests"
```

Expected: 4 tests pass.

- [ ] **Step 5: Commit**

```powershell
git add windows/src/TokenSpendie.Windows/ViewModels windows/tests/TokenSpendie.Windows.Tests/ViewModels
git commit -m "feat(windows): add TrayIconViewModel (icon + tooltip + click)"
```

---

### Task 11: DetailPanelViewModel

**Files:**
- Create: `windows/src/TokenSpendie.Windows/ViewModels/DetailPanelViewModel.cs`
- Create: `windows/tests/TokenSpendie.Windows.Tests/ViewModels/DetailPanelViewModelTests.cs`

Projects `UsageStore.Providers` into a list of `ProviderRow` view-models for the panel binding.

- [ ] **Step 1: Failing tests**

`windows/tests/TokenSpendie.Windows.Tests/ViewModels/DetailPanelViewModelTests.cs`:

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using TokenSpendie.Windows.Data;
using TokenSpendie.Windows.Models;
using TokenSpendie.Windows.Services;
using TokenSpendie.Windows.ViewModels;
using Xunit;

namespace TokenSpendie.Windows.Tests.ViewModels;

public class DetailPanelViewModelTests
{
    private static IUsageProvider Stub(ProviderID id, string name, double percent)
    {
        var headline = new LabeledWindow("Session", "5-hour window",
            ResetStyle.Countdown, new UsageWindow(percent, DateTimeOffset.FromUnixTimeSeconds(100)));
        var snap = new ProviderSnapshot(id, null, headline, new[] { headline },
            DateTimeOffset.FromUnixTimeSeconds(0));
        var p = Substitute.For<IUsageProvider>();
        p.Id.Returns(id);
        p.DisplayName.Returns(name);
        p.DetectCredentials().Returns(true);
        p.FetchUsageAsync(default).ReturnsForAnyArgs(Task.FromResult(snap));
        return p;
    }

    [Fact]
    public async Task RowsReflectStoreProvidersInOrder()
    {
        var store = new UsageStore(new[]
        {
            Stub(ProviderID.Claude, "Claude", 47),
            Stub(ProviderID.Gemini, "Gemini", 3),
        });
        await store.RefreshAsync();

        var vm = new DetailPanelViewModel(store);
        vm.Rows.Select(r => r.DisplayName).Should().Equal("Claude", "Gemini");
        vm.Rows[0].HeadlinePercent.Should().BeApproximately(47, 0.001);
        vm.Rows[1].HeadlinePercent.Should().BeApproximately(3, 0.001);
    }

    [Fact]
    public async Task RowExposesLevelAndCountdown()
    {
        var store = new UsageStore(new[] { Stub(ProviderID.Claude, "Claude", 95) });
        await store.RefreshAsync();
        var vm = new DetailPanelViewModel(store);
        vm.Rows[0].Level.Should().Be(UsageLevel.Hot);
        vm.Rows[0].HeadlineDetail.Should().Be("5-hour window");
    }

    [Fact]
    public async Task RowsUpdateWhenStoreRefreshes()
    {
        var store = new UsageStore(new[] { Stub(ProviderID.Claude, "Claude", 10) });
        var vm = new DetailPanelViewModel(store);
        vm.Rows.Should().BeEmpty();
        await store.RefreshAsync();
        vm.Rows.Should().ContainSingle();
    }
}
```

- [ ] **Step 2: Run — expect failure**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~DetailPanelViewModelTests"
```

- [ ] **Step 3: Implement**

`windows/src/TokenSpendie.Windows/ViewModels/DetailPanelViewModel.cs`:

```csharp
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using TokenSpendie.Windows.Models;
using TokenSpendie.Windows.Services;

namespace TokenSpendie.Windows.ViewModels;

public partial class DetailPanelViewModel : ObservableObject
{
    private readonly UsageStore _store;

    [ObservableProperty]
    private IReadOnlyList<ProviderRowViewModel> _rows = System.Array.Empty<ProviderRowViewModel>();

    public DetailPanelViewModel(UsageStore store)
    {
        _store = store;
        _store.PropertyChanged += OnStoreChanged;
        RecomputeRows();
    }

    private void OnStoreChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(UsageStore.Providers)) RecomputeRows();
    }

    private void RecomputeRows()
    {
        Rows = _store.Providers
            .Where(p => p.Snapshot is not null)
            .Select(p => new ProviderRowViewModel(p))
            .ToArray();
    }
}

public sealed class ProviderRowViewModel
{
    public ProviderRowViewModel(ProviderUsage usage)
    {
        DisplayName = usage.DisplayName;
        State = usage.State;
        var snapshot = usage.Snapshot!;
        HeadlinePercent = snapshot.Headline.Window.Percent;
        HeadlineLabel = snapshot.Headline.Label;
        HeadlineDetail = snapshot.Headline.Detail;
        Level = UsageLevelExtensions.ForPercent(HeadlinePercent);
        Windows = snapshot.Windows;
        Note = snapshot.Note;
        FetchedAt = snapshot.FetchedAt;
    }

    public string DisplayName { get; }
    public LoadState State { get; }
    public double HeadlinePercent { get; }
    public string HeadlineLabel { get; }
    public string HeadlineDetail { get; }
    public UsageLevel Level { get; }
    public IReadOnlyList<LabeledWindow> Windows { get; }
    public string? Note { get; }
    public System.DateTimeOffset FetchedAt { get; }
}
```

- [ ] **Step 4: Run — expect pass**

Expected: 3 tests pass.

- [ ] **Step 5: Commit**

```powershell
git add windows/src/TokenSpendie.Windows/ViewModels windows/tests/TokenSpendie.Windows.Tests/ViewModels
git commit -m "feat(windows): add DetailPanelViewModel + ProviderRowViewModel"
```

---

### Task 12: DetailPanel (XAML UserControl)

**Files:**
- Create: `windows/src/TokenSpendie.Windows/Views/DetailPanel.xaml`
- Create: `windows/src/TokenSpendie.Windows/Views/DetailPanel.xaml.cs`

A `UserControl` bound to `DetailPanelViewModel`. One row per provider: name, percent, ring/bar, and label/detail.

No unit tests for XAML — visual verification is the smoke run in Task 15.

- [ ] **Step 1: Create the user control**

`windows/src/TokenSpendie.Windows/Views/DetailPanel.xaml`:

```xml
<UserControl x:Class="TokenSpendie.Windows.Views.DetailPanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:TokenSpendie.Windows.ViewModels"
             d:DataContext="{d:DesignInstance Type=vm:DetailPanelViewModel}"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d"
             Background="#1E1E22" Foreground="#EAEAEC" MinWidth="320" Padding="16">
  <UserControl.Resources>
    <Style x:Key="HeaderText" TargetType="TextBlock">
      <Setter Property="FontWeight" Value="SemiBold"/>
      <Setter Property="FontSize" Value="14"/>
    </Style>
    <Style x:Key="DetailText" TargetType="TextBlock">
      <Setter Property="Opacity" Value="0.7"/>
      <Setter Property="FontSize" Value="12"/>
    </Style>
    <Style x:Key="PercentText" TargetType="TextBlock">
      <Setter Property="FontWeight" Value="SemiBold"/>
      <Setter Property="FontSize" Value="20"/>
    </Style>
  </UserControl.Resources>

  <ItemsControl ItemsSource="{Binding Rows}">
    <ItemsControl.ItemTemplate>
      <DataTemplate>
        <Grid Margin="0 0 0 16">
          <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*"/>
            <ColumnDefinition Width="Auto"/>
          </Grid.ColumnDefinitions>
          <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
          </Grid.RowDefinitions>

          <TextBlock Grid.Row="0" Grid.Column="0" Text="{Binding DisplayName}"
                     Style="{StaticResource HeaderText}"/>
          <TextBlock Grid.Row="0" Grid.Column="1"
                     Text="{Binding HeadlinePercent, StringFormat={}{0:F0}%}"
                     Style="{StaticResource PercentText}"/>

          <TextBlock Grid.Row="1" Grid.Column="0" Grid.ColumnSpan="2"
                     Text="{Binding HeadlineDetail}"
                     Style="{StaticResource DetailText}"/>
        </Grid>
      </DataTemplate>
    </ItemsControl.ItemTemplate>
  </ItemsControl>
</UserControl>
```

- [ ] **Step 2: Create the code-behind**

`windows/src/TokenSpendie.Windows/Views/DetailPanel.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace TokenSpendie.Windows.Views;

public partial class DetailPanel : UserControl
{
    public DetailPanel()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 3: Build**

```powershell
dotnet build windows/TokenSpendie.Windows.sln
```

Expected: Build succeeded.

- [ ] **Step 4: Commit**

```powershell
git add windows/src/TokenSpendie.Windows/Views
git commit -m "feat(windows): add DetailPanel UserControl (per-provider rows)"
```

---

### Task 13: PopupWindow

**Files:**
- Create: `windows/src/TokenSpendie.Windows/Windows/PopupWindow.xaml`
- Create: `windows/src/TokenSpendie.Windows/Windows/PopupWindow.xaml.cs`

A borderless `Window` that hosts a `DetailPanel`. Positioned by the controller via `Top`/`Left`. Dismissed on focus-loss or Esc.

- [ ] **Step 1: Create the XAML**

`windows/src/TokenSpendie.Windows/Windows/PopupWindow.xaml`:

```xml
<Window x:Class="TokenSpendie.Windows.Windows.PopupWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:views="clr-namespace:TokenSpendie.Windows.Views"
        WindowStyle="None" ResizeMode="NoResize" ShowInTaskbar="False"
        AllowsTransparency="True" Background="Transparent" Topmost="True"
        SizeToContent="WidthAndHeight" WindowStartupLocation="Manual">
  <Border CornerRadius="12" Background="#1E1E22" Padding="0">
    <Border.Effect>
      <DropShadowEffect BlurRadius="16" ShadowDepth="0" Opacity="0.45"/>
    </Border.Effect>
    <views:DetailPanel/>
  </Border>
</Window>
```

- [ ] **Step 2: Create the code-behind**

`windows/src/TokenSpendie.Windows/Windows/PopupWindow.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Input;

namespace TokenSpendie.Windows.Windows;

public partial class PopupWindow : Window
{
    public PopupWindow()
    {
        InitializeComponent();
        Deactivated += (_, _) => Hide();
        KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
    }
}
```

- [ ] **Step 3: Build**

```powershell
dotnet build windows/TokenSpendie.Windows.sln
```

Expected: Build succeeded.

- [ ] **Step 4: Commit**

```powershell
git add windows/src/TokenSpendie.Windows/Windows
git commit -m "feat(windows): add PopupWindow (borderless, transparent, Esc/deactivate dismiss)"
```

---

### Task 14: TrayIconController + App wiring

**Files:**
- Create: `windows/src/TokenSpendie.Windows/Tray/TrayIconController.cs`
- Modify: `windows/src/TokenSpendie.Windows/App.xaml`
- Modify: `windows/src/TokenSpendie.Windows/App.xaml.cs`

`TrayIconController` owns the `TaskbarIcon`, listens to `TrayIconViewModel.ShowPopupRequested`, computes popup placement, and toggles the popup window. App.xaml.cs wires the lifetime.

- [ ] **Step 1: Implement the controller**

`windows/src/TokenSpendie.Windows/Tray/TrayIconController.cs`:

```csharp
using System.Windows;
using H.NotifyIcon;
using TokenSpendie.Windows.ViewModels;
using TokenSpendie.Windows.Windows;

namespace TokenSpendie.Windows.Tray;

public sealed class TrayIconController : System.IDisposable
{
    private readonly TaskbarIcon _icon;
    private readonly TrayIconViewModel _vm;
    private readonly DetailPanelViewModel _panelVm;
    private PopupWindow? _popup;

    public TrayIconController(TrayIconViewModel vm, DetailPanelViewModel panelVm)
    {
        _vm = vm;
        _panelVm = panelVm;
        _icon = new TaskbarIcon
        {
            DataContext = _vm,
            ToolTipText = _vm.ToolTipText,
            Visibility = Visibility.Visible,
        };
        // Bind icon source dynamically as it changes.
        var iconBinding = new System.Windows.Data.Binding(nameof(TrayIconViewModel.IconSource))
        {
            Source = _vm,
        };
        System.Windows.Data.BindingOperations.SetBinding(_icon,
            TaskbarIcon.IconSourceProperty, iconBinding);
        var toolTipBinding = new System.Windows.Data.Binding(nameof(TrayIconViewModel.ToolTipText))
        {
            Source = _vm,
        };
        System.Windows.Data.BindingOperations.SetBinding(_icon,
            TaskbarIcon.ToolTipTextProperty, toolTipBinding);
        _icon.LeftClickCommand = _vm.LeftClickCommand;
        _vm.ShowPopupRequested += OnShowPopupRequested;
    }

    private void OnShowPopupRequested(object? sender, System.EventArgs e)
    {
        if (_popup is { IsVisible: true })
        {
            _popup.Hide();
            return;
        }
        _popup ??= new PopupWindow { DataContext = _panelVm };
        PositionPopup(_popup);
        _popup.Show();
        _popup.Activate();
    }

    private void PositionPopup(PopupWindow popup)
    {
        // Best-effort: place near the cursor, clamped to working area.
        // (Full Shell_NotifyIconGetRect-based anchoring is implementable but
        // requires hwnd + uID from H.NotifyIcon's internal tray entry, which
        // we wire in a follow-up. For M2, cursor-anchored placement is
        // acceptable and matches the G2 fallback in the spec.)
        var cursor = System.Windows.Forms.Cursor.Position;
        var work = SystemParameters.WorkArea;
        popup.Left = System.Math.Clamp(cursor.X, work.Left, work.Right - 320);
        popup.Top = System.Math.Clamp(cursor.Y, work.Top, work.Bottom - 200);
    }

    public void Dispose()
    {
        _icon.Dispose();
        _popup?.Close();
    }
}
```

> Note: This depends on `System.Windows.Forms` for `Cursor.Position`. WPF apps can reference WinForms — but to avoid adding `<UseWindowsForms>true</UseWindowsForms>`, use `System.Windows.Input.Mouse.GetPosition(null)` instead — except that returns DIPs relative to no source which is unreliable. Cleaner: P/Invoke `GetCursorPos`. Use the P/Invoke:

Replace `var cursor = System.Windows.Forms.Cursor.Position;` with:

```csharp
GetCursorPos(out var pt);
var cursor = pt;
```

Add at the bottom of the class:

```csharp
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
private struct POINT { public int X; public int Y; }
[System.Runtime.InteropServices.DllImport("user32.dll")]
private static extern bool GetCursorPos(out POINT lpPoint);
```

And update the clamping to use `pt.X` / `pt.Y`.

- [ ] **Step 2: Rewrite `App.xaml`**

`windows/src/TokenSpendie.Windows/App.xaml`:

```xml
<Application x:Class="TokenSpendie.Windows.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             ShutdownMode="OnExplicitShutdown"
             Startup="App_Startup"
             Exit="App_Exit">
  <Application.Resources/>
</Application>
```

- [ ] **Step 3: Rewrite `App.xaml.cs`**

`windows/src/TokenSpendie.Windows/App.xaml.cs`:

```csharp
using System.Windows;
using TokenSpendie.Windows.Data;
using TokenSpendie.Windows.Services;
using TokenSpendie.Windows.Tray;
using TokenSpendie.Windows.ViewModels;

namespace TokenSpendie.Windows;

public partial class App : Application
{
    private UsageStore? _store;
    private TrayIconController? _tray;

    private void App_Startup(object sender, StartupEventArgs e)
    {
        var providers = new IUsageProvider[]
        {
            new ClaudeProvider(new ClaudeJsonFileReader(), new EndpointUsageProvider()),
            new GeminiProvider(),
        };
        _store = new UsageStore(providers);
        _store.Start();

        var trayVm = new TrayIconViewModel(_store);
        var panelVm = new DetailPanelViewModel(_store);
        _tray = new TrayIconController(trayVm, panelVm);
    }

    private async void App_Exit(object sender, ExitEventArgs e)
    {
        _tray?.Dispose();
        if (_store is not null) await _store.DisposeAsync();
    }
}
```

- [ ] **Step 4: Build**

```powershell
dotnet build windows/TokenSpendie.Windows.sln
```

Expected: Build succeeded.

- [ ] **Step 5: Commit**

```powershell
git add windows/src/TokenSpendie.Windows
git commit -m "feat(windows): wire TrayIconController + App lifetime (tray + popup)"
```

---

### Task 15: Smoke run + final test pass

**Files:** (none modified)

- [ ] **Step 1: Run the full test suite**

```powershell
dotnet test windows/TokenSpendie.Windows.sln
```

Expected: every M1 test + the new M2 unit tests pass. Approximate count: 68 (M1) + 6 (UsageLevel) + 10 (Formatting) + 6 (DpiHelper) + 4 (SnapshotCache) + 8 (UsageStore) + 3 (RingIconRenderer) + 4 (TrayIconViewModel) + 3 (DetailPanelViewModel) = **112 tests**.

- [ ] **Step 2: Build Release**

```powershell
dotnet build windows/TokenSpendie.Windows.sln -c Release
```

Expected: Build succeeded.

- [ ] **Step 3: Smoke-run the WPF app**

```powershell
Start-Process windows/src/TokenSpendie.Windows/bin/Release/net8.0-windows/TokenSpendie.Windows.exe
```

Verify by observation:

1. A tray icon appears (you may need to expand the Windows 11 overflow chevron the first time — known G1).
2. The icon shows a colored ring (the headline provider's usage).
3. Hovering the icon shows a tooltip like `Token Spendie — Claude 47%`.
4. Left-clicking the icon pops a small dark panel near the cursor.
5. The panel shows one row per detected provider with name + percent + detail.
6. Clicking outside the panel (anywhere) closes it. Hitting `Esc` also closes it.
7. Wait 30s, observe the ring/tooltip update (poll cycle).

If any of these fail, capture the failure mode (no icon, panel never appears, popup doesn't dismiss, ring color wrong) before reporting.

Close the app via Task Manager (or `Stop-Process -Name TokenSpendie.Windows`) — there is no Quit menu yet (M3).

- [ ] **Step 4: Commit nothing**

There is nothing to commit at this step — it's verification only.

---

### Task 16: PR and handoff

**Files:** (none)

- [ ] **Step 1: Verify branch state**

```powershell
git status
git log --oneline windows-port-m1-core..HEAD
```

Expected: working tree clean. Roughly 14 commits across Tasks 1–14.

- [ ] **Step 2: Secret-leak scan**

```powershell
git log -p windows-port-m1-core..HEAD `
  | Select-String -Pattern 'ey[A-Za-z0-9_\-]{20,}|sk-ant-|oat[A-Za-z0-9_\-]{20,}' `
  | Measure-Object | ForEach-Object { if ($_.Count -eq 0) { "clean" } else { "DIRTY" } }
```

Expected: `clean`.

- [ ] **Step 3: Push the branch**

```powershell
git push -u origin windows-port-m2-tray-popup
```

- [ ] **Step 4: Open a PR** (via `gh` if installed, else browser via the `pull/new/<branch>` URL git printed)

Base: `windows-port-m1-core`. Title: `feat(windows): M2 tray + popup`.

Body:
```
## Summary
- Converts the M1 project from console Exe to WPF WinExe, with CommunityToolkit.Mvvm + H.NotifyIcon.Wpf.
- Adds `UsageStore` (M2 polling driver) + `SnapshotCache` for per-provider persistence.
- Adds `RingIconRenderer` (DrawingVisual → frozen RenderTargetBitmap) for the tray icon.
- Adds `TrayIconLocator` P/Invoke wrappers (`Shell_NotifyIconGetRect`, `ABM_GETTASKBARPOS`) for popup anchoring (Task 17 will wire the icon-rect anchor; M2 uses cursor-anchored fallback per G2).
- Adds `DetailPanel` UserControl + `PopupWindow` borderless host (Mica brush deferred to M3).
- Headless CLI from M1 removed; WPF `App.xaml.cs` is the new entry.

## Test plan
- [ ] `dotnet test windows/TokenSpendie.Windows.sln` — ~112 tests, all green.
- [ ] Run `TokenSpendie.Windows.exe` and verify: tray icon visible, ring color matches usage tier, left-click opens panel, panel shows provider rows, panel dismisses on focus-loss and Esc.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
```

- [ ] **Step 5: Notify completion**

Reply in chat: "M2 tray + popup complete. PR: <url>. <N> tests green. Ready for M3 (preferences + floating panel + notifications)."

---

## Self-review

**Spec coverage.** M2's spec milestone scope is "Tray + popup. TrayIconController, RingIconRenderer, PopupWindow, DetailPanel. Single screen, no Preferences yet." Every named class is in the plan:

- `TrayIconController` — Task 14
- `RingIconRenderer` — Task 8
- `PopupWindow` — Task 13
- `DetailPanel` — Task 12

Supporting infrastructure called out by the spec but not in the milestone-summary one-liner: `UsageStore` + `SnapshotCache` (Tasks 6–7), `TrayIconLocator` (Task 9), `DpiHelper` (Task 5), MVVM stack (Tasks 10–11), tier color logic (Task 3), formatting (Task 4).

**Deliberate reductions from the mac `UsageStore`** are listed in the plan header. Network observer + wake observer + configurable interval + menu-bar provider picker are all M3 work per the spec's milestone breakdown.

**Placeholder scan.** Every `<…>` in the plan is in code-block examples (`<N>` for test count, `<url>` for the PR URL) — those are runtime substitutions for the user to fill at the moment they're produced. No author placeholders.

**Type consistency.**
- `UsageStore` exposes `Providers` (Tasks 7, 10, 11) — same name in producer and consumers.
- `TrayIconViewModel.ShowPopupRequested` (Task 10) → consumed by `TrayIconController` (Task 14).
- `DetailPanelViewModel.Rows` returns `IReadOnlyList<ProviderRowViewModel>` (Task 11) — bound by `DetailPanel.xaml` `ItemsControl.ItemsSource="{Binding Rows}"` (Task 12).
- `RingIconRenderer.Render(double percent, UsageLevel level, double dpiScale)` (Task 8) is called by `TrayIconViewModel` (Task 10) with the same signature.
- `DpiHelper.IconPx(double scale)` (Task 5) is called by `RingIconRenderer.Render` (Task 8).
- `LoadState.Stale` is referenced by `UsageStore` and `TrayIconViewModel`; it was defined in M1 Task 4. No drift.

**Cross-task dependencies.**
- Task 1 enables WPF; every later task depends on it.
- Task 2 enables STA testing; Task 8 (RingIconRenderer) and Task 10 (TrayIconViewModel) require it.
- Tasks 3–6 (UsageLevel, Formatting, DpiHelper, SnapshotCache) are independent leaves.
- Task 7 (UsageStore) depends on SnapshotCache (6) + M1 data types.
- Task 8 depends on UsageLevel (3) + DpiHelper (5).
- Task 9 (P/Invoke wrapper) is independent — only used by Task 14.
- Task 10 (TrayIconViewModel) depends on UsageStore (7) + RingIconRenderer (8) + UsageLevel (3).
- Task 11 (DetailPanelViewModel) depends on UsageStore (7) + UsageLevel (3).
- Task 12 (DetailPanel.xaml) depends on Task 11.
- Task 13 (PopupWindow.xaml) depends on Task 12.
- Task 14 (TrayIconController + App wiring) depends on every previous task.
- Task 15 verifies the assembled app.
- Task 16 packages the work.

**Test posture.** Every non-XAML / non-P-Invoke type has unit tests with concrete cases. XAML files (DetailPanel, PopupWindow, App) and P/Invoke (TrayIconLocator) are verified by Task 15's smoke run. ViewModels that touch WPF types (TrayIconViewModel — produces an `ImageSource` via RingIconRenderer) are tested under `[StaFact]` to satisfy WPF's threading requirements.

**Branch discipline.** Branch created in Task 1 Step 1 (off `windows-port-m1-core`). Every implementation task ends with a commit. Task 16 secret-scans + pushes + opens the PR.
