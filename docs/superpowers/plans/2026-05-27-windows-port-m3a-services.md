# Windows Port — M3a Services Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the M3 services layer (preferences store, usage notifier with toast notifications, launch-at-login, network/wake observers) and wire it into the M2 tray app. Keep the UI changes minimal for this PR — the preferences/floating-panel/about windows land in **M3b** so this PR stays reviewable.

**Architecture:** Each service is a small class with a clear interface, persisted via JSON files under `%APPDATA%\TokenSpendie\` (preferences + notifier state) or HKCU run-key (launch-at-login). `UsageStore` is restored to its full mac feature set: configurable refresh interval (driven by `PreferencesStore`), network-reconnect refresh, system-wake refresh, menu-bar provider picker. Toast notifications use `Microsoft.Toolkit.Uwp.Notifications` (the stable v7 package — `CommunityToolkit.WinUI.Notifications` is the same toolkit renamed; v7 is what the rest of the ecosystem still uses). AUMID is registered on first launch.

**Tech Stack additions on top of M2:** `Microsoft.Toolkit.Uwp.Notifications` v7 (toast builder + AUMID), `Microsoft.Win32.Registry` (already in .NET 8 for `net8.0-windows`), `Microsoft.Win32.SystemEvents` (in .NET 8 for `net8.0-windows`). No new test-framework packages.

**Spec:** [`docs/superpowers/specs/2026-05-26-windows-port-design.md`](../specs/2026-05-26-windows-port-design.md). M3 split: this plan covers the **services** half; the **UI** half (PreferencesWindow, FloatingPanelWindow, AboutWindow, tray context menu) lives in M3b.

**Prerequisites:**
- M2 branch (`windows-port-m2-tray-popup`) is the base.
- 113 M2 xUnit tests passing.

**Branch:** create `windows-port-m3a-services` off `windows-port-m2-tray-popup` before the first commit.

**In scope:**
- `Models/RefreshInterval`, `Models/Theme` (tier-color presets)
- `Services/PreferencesStore` (JSON, INPC)
- `Services/UsageNotifier` (thresholds, dedup, rollover, joke tables, state persistence) — pure logic, tested with `IToastSender` mock
- `Services/IToastSender` + `WinRtToastSender` (concrete) + `AumidRegistrar`
- `Services/StartupAtLogin/IStartupAtLoginService` + `RegistryRunKeyStartupService`
- `Services/NetworkAvailabilityObserver` + `Services/PowerEventObserver` (system event hooks behind small interfaces)
- `Services/UsageStore` modifications: take `PreferencesStore` for interval, expose menu-bar provider picker, accept optional observers
- `App.xaml.cs` DI wiring

**Out of scope (M3b):**
- `Windows/PreferencesWindow.xaml(.cs)` + `ViewModels/PreferencesViewModel.cs`
- `Windows/FloatingPanelWindow.xaml(.cs)`
- `Windows/AboutWindow.xaml(.cs)`
- Tray context menu (Refresh, Preferences…, About, Quit, Launch-at-login)
- Theme application to `RingIconRenderer` (currently hardcoded calm/warn/hot palette; theme integration is M3b)

**Deliberate behavioural notes for this PR:**
- The hardcoded ring palette in `RingIconRenderer` stays (theme picker is M3b).
- Preferences default values are loaded on construction. Mac defaults: `showMenuBar=true`, `showFloatingPanel=false`, `refreshInterval=60s`, `launchAtLogin=false`, `theme=default`, `menuBarProviderID=claude`.
- `RefreshInterval` rejects values faster than 60s (per spec — the endpoint rate-limits aggressively).
- Notifier state lives in `%APPDATA%\TokenSpendie\notifier-state.json`.
- Preferences live in `%APPDATA%\TokenSpendie\prefs.json`.
- All file I/O is best-effort: corrupt file → fall back to defaults (matches `SnapshotCache` posture).

---

## File structure

```
windows/
  src/TokenSpendie.Windows/
    TokenSpendie.Windows.csproj         # modified — add Microsoft.Toolkit.Uwp.Notifications
    Models/
      RefreshInterval.cs                # new — enum + extension for label/seconds
      Theme.cs                          # new — enum + tier color presets
    Services/
      PreferencesStore.cs               # new — INPC + JSON persistence
      UsageNotifier.cs                  # new — threshold logic + persistence
      NotifierState.cs                  # new — DTO for fired thresholds + last reset dates
      IToastSender.cs                   # new — interface + Joke record
      WinRtToastSender.cs               # new — Microsoft.Toolkit.Uwp.Notifications wrapper
      AumidRegistrar.cs                 # new — RegisterAumidAndComServer
      StartupAtLogin/
        IStartupAtLoginService.cs       # new — interface
        RegistryRunKeyStartupService.cs # new — HKCU run-key impl
      NetworkAvailabilityObserver.cs    # new — NetworkChange wrapper + IDisposable
      PowerEventObserver.cs             # new — SystemEvents.PowerModeChanged wrapper
      UsageStore.cs                     # modified — interval from prefs, observer hooks, menu-bar provider picker
    App.xaml.cs                          # modified — DI wiring for the new services
  tests/TokenSpendie.Windows.Tests/
    Models/RefreshIntervalTests.cs
    Models/ThemeTests.cs
    Services/PreferencesStoreTests.cs
    Services/UsageNotifierTests.cs
    Services/StartupAtLogin/RegistryRunKeyStartupServiceTests.cs
    Services/UsageStoreIntegrationTests.cs   # tests prefs-driven interval + menu-bar picker
```

---

## Conventions

- TDD: failing test before implementation for every testable type.
- File I/O classes (`PreferencesStore`, `UsageNotifier` state) follow the `SnapshotCache` pattern: corrupt files → fall back to defaults, exceptions swallowed.
- Interface for every system-event hook (network, power) so tests can drive them deterministically.
- Tests for `RegistryRunKeyStartupService` use a `HKCU\Software\TokenSpendie.Tests.{Guid}\Run` subkey passed via constructor — no global registry pollution.

---

### Task 1: Branch + add toast package

**Files:**
- Modify: `windows/src/TokenSpendie.Windows/TokenSpendie.Windows.csproj`

- [ ] **Step 1: Branch**

```powershell
git fetch origin
git checkout -b windows-port-m3a-services windows-port-m2-tray-popup
```

Expected: `Switched to a new branch 'windows-port-m3a-services'`.

- [ ] **Step 2: Add `Microsoft.Toolkit.Uwp.Notifications` package**

Edit `windows/src/TokenSpendie.Windows/TokenSpendie.Windows.csproj`. In the existing `<ItemGroup>` that has the other `<PackageReference>`s, add:

```xml
<PackageReference Include="Microsoft.Toolkit.Uwp.Notifications" Version="7.1.3" />
```

Final csproj should look like:

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
    <Using Include="System.IO" />
    <Using Include="System.Net.Http" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.3.2" />
    <PackageReference Include="H.NotifyIcon.Wpf" Version="2.1.4" />
    <PackageReference Include="Microsoft.Toolkit.Uwp.Notifications" Version="7.1.3" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Build + test (no functional changes yet)**

```powershell
dotnet build windows/TokenSpendie.Windows.sln
dotnet test windows/TokenSpendie.Windows.sln
```

Expected: build clean. 113 tests pass.

- [ ] **Step 4: Commit**

```powershell
git add windows/src/TokenSpendie.Windows/TokenSpendie.Windows.csproj
git commit -m "feat(windows): add Microsoft.Toolkit.Uwp.Notifications package (M3a Task 1)"
```

---

### Task 2: Models — RefreshInterval, Theme

**Files:**
- Create: `windows/src/TokenSpendie.Windows/Models/RefreshInterval.cs`
- Create: `windows/src/TokenSpendie.Windows/Models/Theme.cs`
- Create: `windows/tests/TokenSpendie.Windows.Tests/Models/RefreshIntervalTests.cs`
- Create: `windows/tests/TokenSpendie.Windows.Tests/Models/ThemeTests.cs`

- [ ] **Step 1: Failing tests**

`windows/tests/TokenSpendie.Windows.Tests/Models/RefreshIntervalTests.cs`:

```csharp
using FluentAssertions;
using TokenSpendie.Windows.Models;
using Xunit;

namespace TokenSpendie.Windows.Tests.Models;

public class RefreshIntervalTests
{
    [Fact]
    public void DefinedValuesAreSixtyAndOneTwenty()
    {
        RefreshInterval.S60.Seconds().Should().Be(60);
        RefreshInterval.S120.Seconds().Should().Be(120);
    }

    [Fact]
    public void LabelFormatsAsHumanReadableString()
    {
        RefreshInterval.S60.Label().Should().Be("60 seconds");
        RefreshInterval.S120.Label().Should().Be("2 minutes");
    }

    [Theory]
    [InlineData(60, RefreshInterval.S60)]
    [InlineData(120, RefreshInterval.S120)]
    public void FromSecondsReturnsKnownInterval(int seconds, RefreshInterval expected)
    {
        RefreshIntervalExtensions.FromSeconds(seconds).Should().Be(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(180)]
    [InlineData(999999)]
    public void FromSecondsFallsBackToS60ForUnknown(int seconds)
    {
        RefreshIntervalExtensions.FromSeconds(seconds).Should().Be(RefreshInterval.S60);
    }
}
```

`windows/tests/TokenSpendie.Windows.Tests/Models/ThemeTests.cs`:

```csharp
using System.Windows.Media;
using FluentAssertions;
using TokenSpendie.Windows.Models;
using TokenSpendie.Windows.Tests.TestSupport;
using Xunit;

namespace TokenSpendie.Windows.Tests.Models;

public class ThemeTests
{
    [Fact]
    public void AllThemesHaveDistinctDisplayNames()
    {
        var names = new[] { Theme.Default, Theme.Ocean, Theme.Sunset, Theme.Violet }
            .Select(t => t.DisplayName())
            .ToArray();
        names.Should().OnlyHaveUniqueItems();
        names.Should().Equal("Default", "Ocean", "Sunset", "Violet");
    }

    [StaFact]
    public void ColorForReturnsAColorForEveryTier()
    {
        foreach (var theme in new[] { Theme.Default, Theme.Ocean, Theme.Sunset, Theme.Violet })
        {
            foreach (var level in new[] { UsageLevel.Calm, UsageLevel.Warn, UsageLevel.Hot })
            {
                theme.ColorFor(level).Should().NotBe(default(Color),
                    $"theme {theme} level {level} should have a color");
            }
        }
    }

    [StaFact]
    public void TierColorsDifferWithinATheme()
    {
        // calm, warn, hot must be visually distinguishable within a theme.
        var t = Theme.Default;
        t.ColorFor(UsageLevel.Calm).Should().NotBe(t.ColorFor(UsageLevel.Warn));
        t.ColorFor(UsageLevel.Warn).Should().NotBe(t.ColorFor(UsageLevel.Hot));
        t.ColorFor(UsageLevel.Calm).Should().NotBe(t.ColorFor(UsageLevel.Hot));
    }
}
```

- [ ] **Step 2: Run — expect failure**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~RefreshIntervalTests|FullyQualifiedName~ThemeTests"
```

- [ ] **Step 3: Implement `RefreshInterval`**

`windows/src/TokenSpendie.Windows/Models/RefreshInterval.cs`:

```csharp
namespace TokenSpendie.Windows.Models;

/// <summary>Polling intervals offered in preferences. 60s is the floor —
/// the usage endpoint rate-limits aggressively.</summary>
public enum RefreshInterval
{
    S60 = 60,
    S120 = 120,
}

public static class RefreshIntervalExtensions
{
    public static int Seconds(this RefreshInterval interval) => (int)interval;

    public static System.TimeSpan AsTimeSpan(this RefreshInterval interval) =>
        System.TimeSpan.FromSeconds(interval.Seconds());

    public static string Label(this RefreshInterval interval) => interval switch
    {
        RefreshInterval.S60 => "60 seconds",
        RefreshInterval.S120 => "2 minutes",
        _ => $"{(int)interval} seconds",
    };

    public static RefreshInterval FromSeconds(int seconds) => seconds switch
    {
        60 => RefreshInterval.S60,
        120 => RefreshInterval.S120,
        _ => RefreshInterval.S60,
    };
}
```

- [ ] **Step 4: Implement `Theme`**

`windows/src/TokenSpendie.Windows/Models/Theme.cs`:

```csharp
using System.Windows.Media;

namespace TokenSpendie.Windows.Models;

public enum Theme { Default, Ocean, Sunset, Violet }

public static class ThemeExtensions
{
    public static string DisplayName(this Theme theme) => theme switch
    {
        Theme.Default => "Default",
        Theme.Ocean => "Ocean",
        Theme.Sunset => "Sunset",
        Theme.Violet => "Violet",
        _ => theme.ToString(),
    };

    public static Color ColorFor(this Theme theme, UsageLevel level)
    {
        var (calm, warn, hot) = TierColors(theme);
        return level switch
        {
            UsageLevel.Calm => calm,
            UsageLevel.Warn => warn,
            UsageLevel.Hot => hot,
            _ => calm,
        };
    }

    private static (Color Calm, Color Warn, Color Hot) TierColors(Theme theme) => theme switch
    {
        Theme.Default => (
            Color.FromRgb(0x5F, 0xB8, 0x78),
            Color.FromRgb(0xE0, 0xA2, 0x3F),
            Color.FromRgb(0xD9, 0x53, 0x4F)),
        Theme.Ocean => (
            Color.FromRgb(0x35, 0xC0, 0xA6),
            Color.FromRgb(0xF0, 0xBD, 0x5A),
            Color.FromRgb(0xEF, 0x6F, 0x6C)),
        Theme.Sunset => (
            Color.FromRgb(0xF0, 0xA6, 0x5E),
            Color.FromRgb(0xEC, 0x7A, 0x55),
            Color.FromRgb(0xD9, 0x4B, 0x6E)),
        Theme.Violet => (
            Color.FromRgb(0x6F, 0x8F, 0xD6),
            Color.FromRgb(0xA9, 0x74, 0xD8),
            Color.FromRgb(0xD9, 0x5F, 0x9A)),
        _ => (Color.FromRgb(0x5F, 0xB8, 0x78),
              Color.FromRgb(0xE0, 0xA2, 0x3F),
              Color.FromRgb(0xD9, 0x53, 0x4F)),
    };
}
```

- [ ] **Step 5: Run — expect pass**

Expected: 11 tests pass (4 RefreshInterval + 7 Theme — including the 4 InlineData rows).

- [ ] **Step 6: Commit**

```powershell
git add windows/src/TokenSpendie.Windows/Models windows/tests/TokenSpendie.Windows.Tests/Models
git commit -m "feat(windows): add RefreshInterval + Theme models with tier-color presets"
```

---

### Task 3: PreferencesStore

**Files:**
- Create: `windows/src/TokenSpendie.Windows/Services/PreferencesStore.cs`
- Create: `windows/tests/TokenSpendie.Windows.Tests/Services/PreferencesStoreTests.cs`

JSON-backed preferences with INPC. Per-prop save on change (mirrors mac UserDefaults behavior).

- [ ] **Step 1: Failing tests**

`windows/tests/TokenSpendie.Windows.Tests/Services/PreferencesStoreTests.cs`:

```csharp
using System;
using System.ComponentModel;
using System.IO;
using FluentAssertions;
using TokenSpendie.Windows.Models;
using TokenSpendie.Windows.Services;
using Xunit;

namespace TokenSpendie.Windows.Tests.Services;

public class PreferencesStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public PreferencesStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"prefs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "prefs.json");
    }
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private PreferencesStore Make() => new(_path);

    [Fact]
    public void DefaultsAppliedWhenFileMissing()
    {
        var p = Make();
        p.ShowMenuBar.Should().BeTrue();
        p.ShowFloatingPanel.Should().BeFalse();
        p.RefreshInterval.Should().Be(RefreshInterval.S60);
        p.LaunchAtLogin.Should().BeFalse();
        p.Theme.Should().Be(Theme.Default);
        p.MenuBarProviderID.Should().Be(ProviderID.Claude);
    }

    [Fact]
    public void ChangesPersistToDisk()
    {
        var p1 = Make();
        p1.RefreshInterval = RefreshInterval.S120;
        p1.Theme = Theme.Ocean;
        p1.MenuBarProviderID = ProviderID.Gemini;
        p1.LaunchAtLogin = true;
        p1.ShowMenuBar = false;
        p1.ShowFloatingPanel = true;

        // Re-open and verify
        var p2 = Make();
        p2.RefreshInterval.Should().Be(RefreshInterval.S120);
        p2.Theme.Should().Be(Theme.Ocean);
        p2.MenuBarProviderID.Should().Be(ProviderID.Gemini);
        p2.LaunchAtLogin.Should().BeTrue();
        p2.ShowMenuBar.Should().BeFalse();
        p2.ShowFloatingPanel.Should().BeTrue();
    }

    [Fact]
    public void GarbageFileFallsBackToDefaults()
    {
        File.WriteAllText(_path, "not json");
        var p = Make();
        p.RefreshInterval.Should().Be(RefreshInterval.S60);
        p.Theme.Should().Be(Theme.Default);
    }

    [Fact]
    public void RaisesPropertyChangedOnSet()
    {
        var p = Make();
        var fired = new System.Collections.Generic.List<string?>();
        p.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        p.Theme = Theme.Sunset;
        p.RefreshInterval = RefreshInterval.S120;

        fired.Should().Contain(nameof(PreferencesStore.Theme));
        fired.Should().Contain(nameof(PreferencesStore.RefreshInterval));
    }

    [Fact]
    public void SettingSameValueDoesNotRaiseChange()
    {
        var p = Make();
        var fired = 0;
        p.PropertyChanged += (_, _) => fired++;
        p.Theme = p.Theme;
        fired.Should().Be(0);
    }
}
```

- [ ] **Step 2: Run — expect failure**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~PreferencesStoreTests"
```

- [ ] **Step 3: Implement**

`windows/src/TokenSpendie.Windows/Services/PreferencesStore.cs`:

```csharp
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using TokenSpendie.Windows.Models;

namespace TokenSpendie.Windows.Services;

public sealed class PreferencesStore : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly string _path;

    private bool _showMenuBar = true;
    private bool _showFloatingPanel = false;
    private RefreshInterval _refreshInterval = RefreshInterval.S60;
    private bool _launchAtLogin = false;
    private Theme _theme = Theme.Default;
    private ProviderID _menuBarProviderID = ProviderID.Claude;

    public bool ShowMenuBar { get => _showMenuBar; set => SetAndPersist(ref _showMenuBar, value); }
    public bool ShowFloatingPanel { get => _showFloatingPanel; set => SetAndPersist(ref _showFloatingPanel, value); }
    public RefreshInterval RefreshInterval { get => _refreshInterval; set => SetAndPersist(ref _refreshInterval, value); }
    public bool LaunchAtLogin { get => _launchAtLogin; set => SetAndPersist(ref _launchAtLogin, value); }
    public Theme Theme { get => _theme; set => SetAndPersist(ref _theme, value); }
    public ProviderID MenuBarProviderID { get => _menuBarProviderID; set => SetAndPersist(ref _menuBarProviderID, value); }

    public PreferencesStore() : this(DefaultPath()) { }

    public PreferencesStore(string path)
    {
        _path = path;
        Load();
    }

    public static string DefaultPath() =>
        Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
            "TokenSpendie", "prefs.json");

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            using var stream = File.OpenRead(_path);
            var dto = JsonSerializer.Deserialize<PreferencesDto>(stream, JsonOptions);
            if (dto is null) return;
            _showMenuBar = dto.ShowMenuBar ?? _showMenuBar;
            _showFloatingPanel = dto.ShowFloatingPanel ?? _showFloatingPanel;
            _refreshInterval = dto.RefreshIntervalSeconds is { } secs
                ? RefreshIntervalExtensions.FromSeconds(secs)
                : _refreshInterval;
            _launchAtLogin = dto.LaunchAtLogin ?? _launchAtLogin;
            _theme = dto.Theme ?? _theme;
            _menuBarProviderID = dto.MenuBarProviderID ?? _menuBarProviderID;
        }
        catch { /* defaults survive */ }
    }

    private void Save()
    {
        try
        {
            var parent = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            var dto = new PreferencesDto
            {
                ShowMenuBar = _showMenuBar,
                ShowFloatingPanel = _showFloatingPanel,
                RefreshIntervalSeconds = _refreshInterval.Seconds(),
                LaunchAtLogin = _launchAtLogin,
                Theme = _theme,
                MenuBarProviderID = _menuBarProviderID,
            };
            using var stream = File.Create(_path);
            JsonSerializer.Serialize(stream, dto, JsonOptions);
        }
        catch { /* best-effort */ }
    }

    private void SetAndPersist<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(storage, value)) return;
        storage = value;
        Save();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static readonly JsonSerializerOptions JsonOptions = BuildOptions();

    private static JsonSerializerOptions BuildOptions()
    {
        var opts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
        };
        opts.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return opts;
    }

    private sealed class PreferencesDto
    {
        public bool? ShowMenuBar { get; set; }
        public bool? ShowFloatingPanel { get; set; }
        public int? RefreshIntervalSeconds { get; set; }
        public bool? LaunchAtLogin { get; set; }
        public Theme? Theme { get; set; }
        public ProviderID? MenuBarProviderID { get; set; }
    }
}
```

- [ ] **Step 4: Run — expect pass**

Expected: 5 tests pass.

- [ ] **Step 5: Commit**

```powershell
git add windows/src/TokenSpendie.Windows/Services/PreferencesStore.cs windows/tests/TokenSpendie.Windows.Tests/Services/PreferencesStoreTests.cs
git commit -m "feat(windows): add PreferencesStore (JSON persistence + INPC)"
```

---

### Task 4: IToastSender + Joke record

**Files:**
- Create: `windows/src/TokenSpendie.Windows/Services/IToastSender.cs`

- [ ] **Step 1: Implement**

`windows/src/TokenSpendie.Windows/Services/IToastSender.cs`:

```csharp
namespace TokenSpendie.Windows.Services;

/// <summary>One toast notification to send.</summary>
public record Joke(string Title, string Body);

/// <summary>Wraps the WinRT toast machinery so <see cref="UsageNotifier"/>
/// can be tested without invoking real toasts.</summary>
public interface IToastSender
{
    /// <summary>Sends the joke as a toast. <paramref name="dedupTag"/> is a stable
    /// per-window-threshold id; the same tag replaces any earlier toast.</summary>
    void Send(Joke joke, string dedupTag);
}
```

- [ ] **Step 2: Build**

```powershell
dotnet build windows/TokenSpendie.Windows.sln
```

Expected: clean.

- [ ] **Step 3: Commit**

```powershell
git add windows/src/TokenSpendie.Windows/Services/IToastSender.cs
git commit -m "feat(windows): add IToastSender + Joke record"
```

---

### Task 5: UsageNotifier

**Files:**
- Create: `windows/src/TokenSpendie.Windows/Services/NotifierState.cs`
- Create: `windows/src/TokenSpendie.Windows/Services/UsageNotifier.cs`
- Create: `windows/tests/TokenSpendie.Windows.Tests/Services/UsageNotifierTests.cs`

- [ ] **Step 1: Failing tests**

`windows/tests/TokenSpendie.Windows.Tests/Services/UsageNotifierTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using NSubstitute;
using TokenSpendie.Windows.Models;
using TokenSpendie.Windows.Services;
using Xunit;

namespace TokenSpendie.Windows.Tests.Services;

public class UsageNotifierTests : IDisposable
{
    private readonly string _dir;
    private readonly string _statePath;
    private readonly IToastSender _toasts = Substitute.For<IToastSender>();

    public UsageNotifierTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"un-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _statePath = Path.Combine(_dir, "notifier-state.json");
    }
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private UsageNotifier Make() => new(_toasts, _statePath);

    private static ProviderUsage Usage(ProviderID id, string name,
        double sessionPercent, double weeklyPercent,
        DateTimeOffset? sessionResets = null, DateTimeOffset? weeklyResets = null)
    {
        var session = new LabeledWindow("Session", "5-hour window", ResetStyle.Countdown,
            new UsageWindow(sessionPercent, sessionResets));
        var weekly = new LabeledWindow("Weekly", "all models", ResetStyle.Date,
            new UsageWindow(weeklyPercent, weeklyResets));
        var snap = new ProviderSnapshot(id, null, session, new[] { session, weekly },
            DateTimeOffset.FromUnixTimeSeconds(0));
        return new ProviderUsage(id, name) { State = LoadState.Ok, Snapshot = snap };
    }

    [Fact]
    public void FiresWhenSessionCrossesFifty()
    {
        var notifier = Make();
        notifier.Check(new[] { Usage(ProviderID.Claude, "Claude", sessionPercent: 55, weeklyPercent: 10) });
        _toasts.Received().Send(Arg.Any<Joke>(), Arg.Is<string>(t => t.Contains("claude.session.50")));
    }

    [Fact]
    public void DoesNotFireUnderFifty()
    {
        var notifier = Make();
        notifier.Check(new[] { Usage(ProviderID.Claude, "Claude", sessionPercent: 49, weeklyPercent: 10) });
        _toasts.DidNotReceiveWithAnyArgs().Send(default!, default!);
    }

    [Fact]
    public void FiresAllCrossedThresholdsOnTheSameCheck()
    {
        var notifier = Make();
        notifier.Check(new[] { Usage(ProviderID.Claude, "Claude", sessionPercent: 95, weeklyPercent: 10) });
        _toasts.Received().Send(Arg.Any<Joke>(), Arg.Is<string>(t => t.Contains(".50")));
        _toasts.Received().Send(Arg.Any<Joke>(), Arg.Is<string>(t => t.Contains(".70")));
        _toasts.Received().Send(Arg.Any<Joke>(), Arg.Is<string>(t => t.Contains(".90")));
        _toasts.DidNotReceive().Send(Arg.Any<Joke>(), Arg.Is<string>(t => t.Contains(".99")));
    }

    [Fact]
    public void EachThresholdFiresAtMostOncePerWindow()
    {
        var notifier = Make();
        var u = Usage(ProviderID.Claude, "Claude", sessionPercent: 55, weeklyPercent: 10);
        notifier.Check(new[] { u });
        _toasts.ClearReceivedCalls();
        // Second check at higher but same band — must NOT re-fire.
        notifier.Check(new[] { Usage(ProviderID.Claude, "Claude", sessionPercent: 65, weeklyPercent: 10) });
        _toasts.DidNotReceiveWithAnyArgs().Send(default!, default!);
    }

    [Fact]
    public void WindowRolloverClearsFiredSet()
    {
        var oldReset = DateTimeOffset.FromUnixTimeSeconds(1000);
        var newReset = DateTimeOffset.FromUnixTimeSeconds(2000);
        var notifier = Make();

        notifier.Check(new[] { Usage(ProviderID.Claude, "Claude", sessionPercent: 55, weeklyPercent: 10, sessionResets: oldReset) });
        _toasts.ClearReceivedCalls();

        // Same band 55%, but resetsAt changed → window rolled over → fire again.
        notifier.Check(new[] { Usage(ProviderID.Claude, "Claude", sessionPercent: 55, weeklyPercent: 10, sessionResets: newReset) });
        _toasts.Received().Send(Arg.Any<Joke>(), Arg.Is<string>(t => t.Contains("claude.session.50")));
    }

    [Fact]
    public void StatePersistsAcrossInstances()
    {
        var n1 = Make();
        n1.Check(new[] { Usage(ProviderID.Claude, "Claude", sessionPercent: 55, weeklyPercent: 10) });
        _toasts.ClearReceivedCalls();

        var n2 = Make();   // reads same statePath
        n2.Check(new[] { Usage(ProviderID.Claude, "Claude", sessionPercent: 60, weeklyPercent: 10) });
        _toasts.DidNotReceiveWithAnyArgs().Send(default!, default!);
    }

    [Fact]
    public void IgnoresProvidersWithoutSnapshot()
    {
        var notifier = Make();
        var noData = new ProviderUsage(ProviderID.Claude, "Claude") { State = LoadState.Loading };
        notifier.Check(new[] { noData });
        _toasts.DidNotReceiveWithAnyArgs().Send(default!, default!);
    }

    [Fact]
    public void SessionAndWeeklyTablesAreDistinct()
    {
        var notifier = Make();
        notifier.Check(new[] { Usage(ProviderID.Claude, "Claude", sessionPercent: 0, weeklyPercent: 55) });
        _toasts.Received().Send(Arg.Any<Joke>(), Arg.Is<string>(t => t.Contains("claude.weekly.50")));
    }
}
```

- [ ] **Step 2: Run — expect failure**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~UsageNotifierTests"
```

- [ ] **Step 3: Implement `NotifierState` (persistence DTO)**

`windows/src/TokenSpendie.Windows/Services/NotifierState.cs`:

```csharp
namespace TokenSpendie.Windows.Services;

internal sealed class NotifierState
{
    public System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<double>>
        FiredThresholds { get; set; } = new();
    public System.Collections.Generic.Dictionary<string, System.DateTimeOffset>
        LastResetDates { get; set; } = new();
}
```

- [ ] **Step 4: Implement `UsageNotifier`**

`windows/src/TokenSpendie.Windows/Services/UsageNotifier.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using TokenSpendie.Windows.Models;

namespace TokenSpendie.Windows.Services;

/// <summary>
/// Fires toast notifications when session/weekly usage crosses 50/70/90/99 %.
/// Each threshold fires at most once per window cycle. Window rollover is
/// detected via <c>resetsAt</c> change.
/// </summary>
public sealed class UsageNotifier
{
    private static readonly double[] Thresholds = { 50, 70, 90, 99 };

    private static readonly Dictionary<double, Joke> SessionJokes = new()
    {
        [50] = new Joke("Session 50% gone 👀", "Still going? Bold. Very bold."),
        [70] = new Joke("Session 70% cooked 🍳",
            "At this point Claude knows more about your life than your therapist."),
        [90] = new Joke("Session 90%... okay wow 😰",
            "What are you even building in there. Go touch some grass."),
        [99] = new Joke("Session basically done. Go take a break ☕",
            "Tokens ended. Skill issue. Grab a coffee, you've earned it."),
    };

    private static readonly Dictionary<double, Joke> WeeklyJokes = new()
    {
        [50] = new Joke("Weekly 50% gone already 🙃",
            "It's only midweek. Totally fine. Everything is fine."),
        [70] = new Joke("Weekly 70% used 😬",
            "Claude is starting to recognise your typing pattern. This is your fault."),
        [90] = new Joke("Weekly 90% — almost speedrunning this 🏃",
            "New record? Probably a new record. Who needs tokens anyway."),
        [99] = new Joke("Weekly tokens ended. Seeya next week 👋",
            "Go outside. Touch grass. Tell your friends you exist. Tokens reset soon™."),
    };

    private readonly IToastSender _toasts;
    private readonly string _statePath;

    public UsageNotifier(IToastSender toasts) : this(toasts, DefaultStatePath()) { }

    public UsageNotifier(IToastSender toasts, string statePath)
    {
        _toasts = toasts;
        _statePath = statePath;
    }

    public static string DefaultStatePath() =>
        Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
            "TokenSpendie", "notifier-state.json");

    public void Check(IEnumerable<ProviderUsage> providers)
    {
        var state = Load();
        foreach (var usage in providers)
        {
            if (usage.Snapshot is null) continue;
            foreach (var window in usage.Snapshot.Windows)
            {
                var labelLower = window.Label.ToLowerInvariant();
                var jokes = labelLower.Contains("session") ? SessionJokes : WeeklyJokes;
                var key = $"{usage.Id.ToString().ToLowerInvariant()}.{LabelKey(window.Label)}";
                CheckOne(window.Window, key, jokes, state);
            }
        }
        Save(state);
    }

    private static string LabelKey(string label) =>
        // "Weekly · Opus" → "weekly.opus" so it doesn't collide with the "weekly" all-models row.
        label.ToLowerInvariant()
            .Replace(" · ", ".")
            .Replace(" ", "");

    private void CheckOne(UsageWindow window, string key, Dictionary<double, Joke> jokes, NotifierState state)
    {
        if (!state.FiredThresholds.TryGetValue(key, out var fired)) fired = new();
        var firedSet = new HashSet<double>(fired);

        if (window.ResetsAt is { } newReset)
        {
            if (state.LastResetDates.TryGetValue(key, out var lastReset) && lastReset != newReset)
                firedSet.Clear();
            state.LastResetDates[key] = newReset;
        }

        foreach (var threshold in Thresholds)
        {
            if (window.Percent < threshold) continue;
            if (firedSet.Contains(threshold)) continue;
            firedSet.Add(threshold);
            if (jokes.TryGetValue(threshold, out var joke))
            {
                var tag = $"{key}.{(int)threshold}";
                _toasts.Send(joke, tag);
            }
        }

        state.FiredThresholds[key] = firedSet.ToList();
    }

    private NotifierState Load()
    {
        try
        {
            if (!File.Exists(_statePath)) return new NotifierState();
            using var stream = File.OpenRead(_statePath);
            return JsonSerializer.Deserialize<NotifierState>(stream) ?? new NotifierState();
        }
        catch { return new NotifierState(); }
    }

    private void Save(NotifierState state)
    {
        try
        {
            var parent = Path.GetDirectoryName(_statePath);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            using var stream = File.Create(_statePath);
            JsonSerializer.Serialize(stream, state);
        }
        catch { /* best-effort */ }
    }
}
```

- [ ] **Step 5: Run — expect pass**

Expected: 8 tests pass.

- [ ] **Step 6: Commit**

```powershell
git add windows/src/TokenSpendie.Windows/Services/NotifierState.cs windows/src/TokenSpendie.Windows/Services/UsageNotifier.cs windows/tests/TokenSpendie.Windows.Tests/Services/UsageNotifierTests.cs
git commit -m "feat(windows): add UsageNotifier (50/70/90/99 thresholds, dedup, rollover, persistence)"
```

---

### Task 6: WinRtToastSender + AumidRegistrar

**Files:**
- Create: `windows/src/TokenSpendie.Windows/Services/AumidRegistrar.cs`
- Create: `windows/src/TokenSpendie.Windows/Services/WinRtToastSender.cs`

The concrete toast sender wraps `ToastContentBuilder` from `Microsoft.Toolkit.Uwp.Notifications`. The AUMID registrar uses `DesktopNotificationManagerCompat` from the same package. No unit tests — these are integration-only.

- [ ] **Step 1: Implement `AumidRegistrar`**

`windows/src/TokenSpendie.Windows/Services/AumidRegistrar.cs`:

```csharp
namespace TokenSpendie.Windows.Services;

/// <summary>Registers the AUMID + COM server required to surface toasts from an
/// unpackaged WPF app. Safe to call multiple times — Windows ignores duplicates.</summary>
public static class AumidRegistrar
{
    public const string Aumid = "Sensecherise.TokenSpendie";

    public static void Register()
    {
        try
        {
            Microsoft.Toolkit.Uwp.Notifications.ToastNotificationManagerCompat.OnActivated += _ => { };
        }
        catch
        {
            // Silently swallow — toasts will simply fail to surface, the rest of the app continues.
        }
    }
}
```

(`ToastNotificationManagerCompat.OnActivated += handler` is the canonical "register the AUMID and COM server" entry point in v7 of the Toolkit. Subscribing once at startup performs the registration as a side effect.)

- [ ] **Step 2: Implement `WinRtToastSender`**

`windows/src/TokenSpendie.Windows/Services/WinRtToastSender.cs`:

```csharp
using Microsoft.Toolkit.Uwp.Notifications;

namespace TokenSpendie.Windows.Services;

public sealed class WinRtToastSender : IToastSender
{
    public void Send(Joke joke, string dedupTag)
    {
        try
        {
            new ToastContentBuilder()
                .AddText(joke.Title)
                .AddText(joke.Body)
                .AddArgument("threshold", dedupTag)
                .Show(toast =>
                {
                    toast.Tag = dedupTag;
                    toast.Group = "tokenspendie";
                });
        }
        catch
        {
            // AUMID not registered, or toast subsystem unavailable — fail silently.
        }
    }
}
```

- [ ] **Step 3: Build**

```powershell
dotnet build windows/TokenSpendie.Windows.sln
```

Expected: clean.

- [ ] **Step 4: Commit**

```powershell
git add windows/src/TokenSpendie.Windows/Services/AumidRegistrar.cs windows/src/TokenSpendie.Windows/Services/WinRtToastSender.cs
git commit -m "feat(windows): add WinRtToastSender + AumidRegistrar (Microsoft.Toolkit.Uwp.Notifications)"
```

---

### Task 7: Launch-at-login

**Files:**
- Create: `windows/src/TokenSpendie.Windows/Services/StartupAtLogin/IStartupAtLoginService.cs`
- Create: `windows/src/TokenSpendie.Windows/Services/StartupAtLogin/RegistryRunKeyStartupService.cs`
- Create: `windows/tests/TokenSpendie.Windows.Tests/Services/StartupAtLogin/RegistryRunKeyStartupServiceTests.cs`

Tests use a per-test registry subkey to avoid global state pollution.

- [ ] **Step 1: Failing tests**

`windows/tests/TokenSpendie.Windows.Tests/Services/StartupAtLogin/RegistryRunKeyStartupServiceTests.cs`:

```csharp
using System;
using FluentAssertions;
using Microsoft.Win32;
using TokenSpendie.Windows.Services.StartupAtLogin;
using Xunit;

namespace TokenSpendie.Windows.Tests.Services.StartupAtLogin;

public class RegistryRunKeyStartupServiceTests : IDisposable
{
    private readonly string _testKey = $@"Software\TokenSpendie.Tests.{Guid.NewGuid():N}\Run";

    public void Dispose()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(_testKey, throwOnMissingSubKey: false); } catch { }
    }

    private RegistryRunKeyStartupService Make(string exePath = @"C:\fake\TokenSpendie.exe") =>
        new(_testKey, "TokenSpendie", exePath);

    [Fact]
    public void IsEnabledFalseWhenKeyMissing()
    {
        Make().IsEnabled().Should().BeFalse();
    }

    [Fact]
    public void EnableWritesRegistryValue()
    {
        var svc = Make(@"C:\Apps\TokenSpendie.exe");
        svc.Enable();
        svc.IsEnabled().Should().BeTrue();

        using var key = Registry.CurrentUser.OpenSubKey(_testKey);
        var value = key?.GetValue("TokenSpendie") as string;
        value.Should().Contain("TokenSpendie.exe");
        value.Should().Contain("--hidden");
    }

    [Fact]
    public void DisableRemovesValue()
    {
        var svc = Make();
        svc.Enable();
        svc.Disable();
        svc.IsEnabled().Should().BeFalse();
    }

    [Fact]
    public void IsEnabledFalseIfPathMismatched()
    {
        // A different exe path stored under the same value name should NOT count as enabled
        // (handles user-relocated installs).
        var svc = Make(@"C:\NewPath\TokenSpendie.exe");
        using (var key = Registry.CurrentUser.CreateSubKey(_testKey))
            key!.SetValue("TokenSpendie", @"""C:\OldPath\TokenSpendie.exe"" --hidden");

        svc.IsEnabled().Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run — expect failure**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~RegistryRunKeyStartupServiceTests"
```

- [ ] **Step 3: Implement interface**

`windows/src/TokenSpendie.Windows/Services/StartupAtLogin/IStartupAtLoginService.cs`:

```csharp
namespace TokenSpendie.Windows.Services.StartupAtLogin;

public interface IStartupAtLoginService
{
    bool IsEnabled();
    void Enable();
    void Disable();
}
```

- [ ] **Step 4: Implement registry service**

`windows/src/TokenSpendie.Windows/Services/StartupAtLogin/RegistryRunKeyStartupService.cs`:

```csharp
using Microsoft.Win32;

namespace TokenSpendie.Windows.Services.StartupAtLogin;

public sealed class RegistryRunKeyStartupService : IStartupAtLoginService
{
    private const string DefaultKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly string _subKey;
    private readonly string _valueName;
    private readonly string _exePath;

    public RegistryRunKeyStartupService() : this(DefaultKey, "TokenSpendie", CurrentExePath()) { }

    public RegistryRunKeyStartupService(string subKey, string valueName, string exePath)
    {
        _subKey = subKey;
        _valueName = valueName;
        _exePath = exePath;
    }

    private static string CurrentExePath() =>
        System.Environment.ProcessPath ?? System.AppContext.BaseDirectory;

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_subKey);
        var raw = key?.GetValue(_valueName) as string;
        if (string.IsNullOrEmpty(raw)) return false;
        // Stored value is `"<path>" --hidden`. Compare the quoted path portion.
        return raw.Contains($"\"{_exePath}\"", System.StringComparison.OrdinalIgnoreCase);
    }

    public void Enable()
    {
        using var key = Registry.CurrentUser.CreateSubKey(_subKey)
            ?? throw new System.InvalidOperationException("Cannot open HKCU Run subkey for write.");
        key.SetValue(_valueName, $"\"{_exePath}\" --hidden");
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_subKey, writable: true);
        key?.DeleteValue(_valueName, throwOnMissingValue: false);
    }
}
```

- [ ] **Step 5: Run — expect pass**

Expected: 4 tests pass.

- [ ] **Step 6: Commit**

```powershell
git add windows/src/TokenSpendie.Windows/Services/StartupAtLogin windows/tests/TokenSpendie.Windows.Tests/Services/StartupAtLogin
git commit -m "feat(windows): add RegistryRunKeyStartupService (launch-at-login via HKCU Run)"
```

---

### Task 8: Network + power observers

**Files:**
- Create: `windows/src/TokenSpendie.Windows/Services/INetworkAvailabilityObserver.cs`
- Create: `windows/src/TokenSpendie.Windows/Services/NetworkAvailabilityObserver.cs`
- Create: `windows/src/TokenSpendie.Windows/Services/IPowerEventObserver.cs`
- Create: `windows/src/TokenSpendie.Windows/Services/PowerEventObserver.cs`

Interfaces for testability; concrete impls wrap `NetworkChange` and `SystemEvents`.

- [ ] **Step 1: Implement interfaces**

`windows/src/TokenSpendie.Windows/Services/INetworkAvailabilityObserver.cs`:

```csharp
namespace TokenSpendie.Windows.Services;

public interface INetworkAvailabilityObserver : System.IDisposable
{
    /// <summary>Raised when the host transitions from offline → online.</summary>
    event System.EventHandler? Reconnected;
}
```

`windows/src/TokenSpendie.Windows/Services/IPowerEventObserver.cs`:

```csharp
namespace TokenSpendie.Windows.Services;

public interface IPowerEventObserver : System.IDisposable
{
    /// <summary>Raised when the host resumes from sleep/hibernate.</summary>
    event System.EventHandler? Resumed;
}
```

- [ ] **Step 2: Implement `NetworkAvailabilityObserver`**

`windows/src/TokenSpendie.Windows/Services/NetworkAvailabilityObserver.cs`:

```csharp
using System.Net.NetworkInformation;

namespace TokenSpendie.Windows.Services;

public sealed class NetworkAvailabilityObserver : INetworkAvailabilityObserver
{
    public event System.EventHandler? Reconnected;

    private bool _wasAvailable = NetworkInterface.GetIsNetworkAvailable();

    public NetworkAvailabilityObserver()
    {
        NetworkChange.NetworkAvailabilityChanged += OnAvailabilityChanged;
    }

    private void OnAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        var now = e.IsAvailable;
        var reconnected = now && !_wasAvailable;
        _wasAvailable = now;
        if (reconnected)
            Reconnected?.Invoke(this, System.EventArgs.Empty);
    }

    public void Dispose()
    {
        NetworkChange.NetworkAvailabilityChanged -= OnAvailabilityChanged;
    }
}
```

- [ ] **Step 3: Implement `PowerEventObserver`**

`windows/src/TokenSpendie.Windows/Services/PowerEventObserver.cs`:

```csharp
using Microsoft.Win32;

namespace TokenSpendie.Windows.Services;

public sealed class PowerEventObserver : IPowerEventObserver
{
    public event System.EventHandler? Resumed;

    public PowerEventObserver()
    {
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
            Resumed?.Invoke(this, System.EventArgs.Empty);
    }

    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }
}
```

- [ ] **Step 4: Build**

```powershell
dotnet build windows/TokenSpendie.Windows.sln
```

Expected: clean. Tests still pass.

- [ ] **Step 5: Commit**

```powershell
git add windows/src/TokenSpendie.Windows/Services/INetworkAvailabilityObserver.cs windows/src/TokenSpendie.Windows/Services/NetworkAvailabilityObserver.cs windows/src/TokenSpendie.Windows/Services/IPowerEventObserver.cs windows/src/TokenSpendie.Windows/Services/PowerEventObserver.cs
git commit -m "feat(windows): add network availability + power resume observers"
```

---

### Task 9: UsageStore integration

**Files:**
- Modify: `windows/src/TokenSpendie.Windows/Services/UsageStore.cs`
- Create: `windows/tests/TokenSpendie.Windows.Tests/Services/UsageStoreIntegrationTests.cs`

Replace the M2 hardcoded `RefreshInterval = 30s` constant with one driven by `PreferencesStore`. Optional observer hooks for network/power. Expose `MenuBarProviderID` driven by preferences.

- [ ] **Step 1: Failing tests**

`windows/tests/TokenSpendie.Windows.Tests/Services/UsageStoreIntegrationTests.cs`:

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using TokenSpendie.Windows.Data;
using TokenSpendie.Windows.Models;
using TokenSpendie.Windows.Services;
using Xunit;

namespace TokenSpendie.Windows.Tests.Services;

public class UsageStoreIntegrationTests : IDisposable
{
    private readonly string _dir;
    private DateTimeOffset _now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    public UsageStoreIntegrationTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"usi-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private static IUsageProvider Stub(ProviderID id, string name,
        Func<CancellationToken, Task<ProviderSnapshot>> fetch)
    {
        var p = Substitute.For<IUsageProvider>();
        p.Id.Returns(id);
        p.DisplayName.Returns(name);
        p.DetectCredentials().Returns(true);
        p.FetchUsageAsync(Arg.Any<CancellationToken>()).Returns(ci => fetch((CancellationToken)ci[0]));
        return p;
    }

    private static ProviderSnapshot Snap(ProviderID id)
    {
        var headline = new LabeledWindow("Session", "5h", ResetStyle.Countdown, new UsageWindow(10, null));
        return new ProviderSnapshot(id, null, headline, new[] { headline },
            DateTimeOffset.FromUnixTimeSeconds(0));
    }

    [Fact]
    public void MenuBarProviderReturnsPreferenceWhenPresent()
    {
        var prefs = new PreferencesStore(Path.Combine(_dir, "prefs.json"))
        {
            MenuBarProviderID = ProviderID.Gemini,
        };
        var store = new UsageStore(
            new[]
            {
                Stub(ProviderID.Claude, "Claude", _ => Task.FromResult(Snap(ProviderID.Claude))),
                Stub(ProviderID.Gemini, "Gemini", _ => Task.FromResult(Snap(ProviderID.Gemini))),
            },
            preferences: prefs,
            now: () => _now);

        store.MenuBarProvider().Should().BeNull("no refresh yet");
        // After a refresh the rows exist and the picked provider should be Gemini.
        store.RefreshAsync().GetAwaiter().GetResult();
        store.MenuBarProvider()!.Id.Should().Be(ProviderID.Gemini);
    }

    [Fact]
    public void MenuBarProviderFallsBackToFirstWhenPreferenceMissing()
    {
        var prefs = new PreferencesStore(Path.Combine(_dir, "prefs.json"))
        {
            MenuBarProviderID = ProviderID.Gemini,
        };
        var store = new UsageStore(
            new[] { Stub(ProviderID.Claude, "Claude", _ => Task.FromResult(Snap(ProviderID.Claude))) },
            preferences: prefs,
            now: () => _now);
        store.RefreshAsync().GetAwaiter().GetResult();
        // Gemini isn't registered → fall back to first detected (Claude).
        store.MenuBarProvider()!.Id.Should().Be(ProviderID.Claude);
    }

    [Fact]
    public async Task NetworkReconnectTriggersRefresh()
    {
        var net = Substitute.For<INetworkAvailabilityObserver>();
        var calls = 0;
        var store = new UsageStore(
            new[] { Stub(ProviderID.Claude, "Claude",
                _ => { calls++; return Task.FromResult(Snap(ProviderID.Claude)); }) },
            preferences: new PreferencesStore(Path.Combine(_dir, "prefs.json")),
            now: () => _now,
            network: net);

        await store.RefreshAsync();          // baseline = 1
        calls.Should().Be(1);

        // Fire the reconnect event.
        net.Reconnected += Raise.Event<EventHandler>(net, EventArgs.Empty);
        await Task.Delay(50);                 // let async handler run

        calls.Should().Be(2);
    }
}
```

- [ ] **Step 2: Run — expect failure**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~UsageStoreIntegrationTests"
```

- [ ] **Step 3: Modify `UsageStore`**

Open `windows/src/TokenSpendie.Windows/Services/UsageStore.cs`. Make the following edits:

(a) Replace the class-level constant:

```csharp
// REPLACE:
public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);
// WITH (just remove the line — interval now comes from preferences):
```

(b) Add fields and constructor parameters for preferences, network, power:

After the existing private fields and before the constructor, add:

```csharp
private readonly PreferencesStore? _preferences;
private readonly INetworkAvailabilityObserver? _network;
private readonly IPowerEventObserver? _power;
```

Replace the constructor with:

```csharp
public UsageStore(
    IEnumerable<IUsageProvider> providers,
    Func<ProviderID, SnapshotCache>? cacheFactory = null,
    Func<DateTimeOffset>? now = null,
    PreferencesStore? preferences = null,
    INetworkAvailabilityObserver? network = null,
    IPowerEventObserver? power = null)
{
    _registered = providers.ToArray();
    cacheFactory ??= id => new SnapshotCache(SnapshotCache.DefaultPathFor(id));
    _caches = _registered.ToDictionary(p => p.Id, p => cacheFactory(p.Id));
    _now = now ?? (() => DateTimeOffset.Now);
    _preferences = preferences;
    _network = network;
    _power = power;

    if (_network is not null) _network.Reconnected += OnNetworkReconnected;
    if (_power is not null) _power.Resumed += OnPowerResumed;
}

private void OnNetworkReconnected(object? sender, EventArgs e) => _ = RefreshAsync();
private void OnPowerResumed(object? sender, EventArgs e) => _ = RefreshAsync();
```

(c) Drive the polling timer interval from preferences:

In `Start()`, replace:

```csharp
_timer = new PeriodicTimer(RefreshInterval);
```

with:

```csharp
var interval = _preferences?.RefreshInterval.AsTimeSpan() ?? TimeSpan.FromSeconds(60);
_timer = new PeriodicTimer(interval);
```

In `MarkStaleIfNeeded()`, replace `RefreshInterval * 3` with:

```csharp
var interval = _preferences?.RefreshInterval.AsTimeSpan() ?? TimeSpan.FromSeconds(60);
var threshold = interval * 3;
```

(d) Add `MenuBarProvider()`:

Right after the `Providers` property declaration, add:

```csharp
/// <summary>The provider whose ring drives the tray icon. Uses the
/// preferences pick when that provider is detected; otherwise the first
/// detected provider; otherwise null.</summary>
public ProviderUsage? MenuBarProvider()
{
    var preferred = _preferences?.MenuBarProviderID;
    if (preferred is { } pref)
    {
        foreach (var u in Providers)
            if (u.Id == pref) return u;
    }
    return Providers.Count > 0 ? Providers[0] : null;
}
```

(e) Unsubscribe in `DisposeAsync`:

Add at the start of `DisposeAsync`:

```csharp
if (_network is not null) _network.Reconnected -= OnNetworkReconnected;
if (_power is not null) _power.Resumed -= OnPowerResumed;
```

- [ ] **Step 4: Run — expect pass on the new tests and on existing UsageStoreTests**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~UsageStore"
```

Expected: previous 8 `UsageStoreTests` + 3 new `UsageStoreIntegrationTests` = 11 pass.

The previous tests use `MakeStore` without preferences/network/power — the optional ctor parameters default to null, so behaviour is unchanged for those tests except that `RefreshInterval` is now 60s (from the null-fallback) instead of 30s. None of the previous tests assert on raw interval value, so they still pass.

- [ ] **Step 5: Commit**

```powershell
git add windows/src/TokenSpendie.Windows/Services/UsageStore.cs windows/tests/TokenSpendie.Windows.Tests/Services/UsageStoreIntegrationTests.cs
git commit -m "feat(windows): wire UsageStore to preferences + network/power observers + menu-bar provider picker"
```

---

### Task 10: App.xaml.cs DI wiring + notifier hook

**Files:**
- Modify: `windows/src/TokenSpendie.Windows/App.xaml.cs`

Add all the new services at app startup. Hook `UsageNotifier.Check` to `UsageStore.PropertyChanged`. Register AUMID. Sync `LaunchAtLogin` preference to the registry on startup.

- [ ] **Step 1: Rewrite `App.xaml.cs`**

Open `windows/src/TokenSpendie.Windows/App.xaml.cs` and replace the contents with:

```csharp
using System.ComponentModel;
using System.Windows;
using TokenSpendie.Windows.Data;
using TokenSpendie.Windows.Services;
using TokenSpendie.Windows.Services.StartupAtLogin;
using TokenSpendie.Windows.Tray;
using TokenSpendie.Windows.ViewModels;

namespace TokenSpendie.Windows;

public partial class App : Application
{
    private PreferencesStore? _preferences;
    private UsageStore? _store;
    private UsageNotifier? _notifier;
    private TrayIconController? _tray;
    private INetworkAvailabilityObserver? _network;
    private IPowerEventObserver? _power;
    private IStartupAtLoginService? _startup;

    private void App_Startup(object sender, StartupEventArgs e)
    {
        AumidRegistrar.Register();

        _preferences = new PreferencesStore();
        _startup = new RegistryRunKeyStartupService();
        _preferences.LaunchAtLogin = _startup.IsEnabled();
        _preferences.PropertyChanged += OnPreferencesChanged;

        _network = new NetworkAvailabilityObserver();
        _power = new PowerEventObserver();

        _notifier = new UsageNotifier(new WinRtToastSender());

        var providers = new IUsageProvider[]
        {
            new ClaudeProvider(new ClaudeJsonFileReader(), new EndpointUsageProvider()),
            new GeminiProvider(),
        };
        _store = new UsageStore(
            providers,
            preferences: _preferences,
            network: _network,
            power: _power);

        _store.PropertyChanged += OnStorePropertyChanged;
        _store.Start();

        var trayVm = new TrayIconViewModel(_store);
        var panelVm = new DetailPanelViewModel(_store);
        _tray = new TrayIconController(trayVm, panelVm);
    }

    private void OnStorePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UsageStore.Providers))
        {
            _notifier?.Check(_store!.Providers);
        }
    }

    private void OnPreferencesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PreferencesStore.LaunchAtLogin)) return;
        if (_startup is null || _preferences is null) return;
        if (_preferences.LaunchAtLogin && !_startup.IsEnabled()) _startup.Enable();
        else if (!_preferences.LaunchAtLogin && _startup.IsEnabled()) _startup.Disable();
    }

    private async void App_Exit(object sender, ExitEventArgs e)
    {
        _tray?.Dispose();
        _network?.Dispose();
        _power?.Dispose();
        if (_store is not null) await _store.DisposeAsync();
    }
}
```

- [ ] **Step 2: Build**

```powershell
dotnet build windows/TokenSpendie.Windows.sln -c Release
```

Expected: clean.

- [ ] **Step 3: Run full test suite**

```powershell
dotnet test windows/TokenSpendie.Windows.sln
```

Expected: all previous + new tests green. Approx count: 113 (M2) + 4 (RefreshInterval) + 7 (Theme — with InlineData) + 5 (PreferencesStore) + 8 (UsageNotifier) + 4 (RegistryRunKeyStartupService) + 3 (UsageStoreIntegration) = **~144 tests**.

- [ ] **Step 4: Smoke run**

```powershell
Get-Process -Name "TokenSpendie.Windows" -ErrorAction SilentlyContinue | Stop-Process -Force
$p = Start-Process -FilePath "windows/src/TokenSpendie.Windows/bin/Release/net8.0-windows/TokenSpendie.Windows.exe" -PassThru -RedirectStandardError "tsw-m3a-err.log" -RedirectStandardOutput "tsw-m3a-out.log"
Start-Sleep -Seconds 10
if ($p.HasExited) {
  "CRASHED, code=$($p.ExitCode)"
  Get-Content tsw-m3a-err.log
} else {
  "OK running"
  Stop-Process -Id $p.Id
}
```

Expected: process stays alive 10s. If it crashes, capture stderr — likely cause is `Microsoft.Toolkit.Uwp.Notifications` requiring an environment we haven't provided (Win10 1903+; should be fine on Win11 22H2). If `AumidRegistrar.Register()` throws despite the try/catch, harden it further.

- [ ] **Step 5: Commit**

```powershell
git add windows/src/TokenSpendie.Windows/App.xaml.cs
git commit -m "feat(windows): wire M3a services into App lifetime (prefs + notifier + startup + observers)"
```

---

### Task 11: PR + handoff

**Files:** (none)

- [ ] **Step 1: Verify branch state**

```powershell
git status
git log --oneline windows-port-m2-tray-popup..HEAD
```

Expected: working tree clean. Approximately 9 implementation commits across Tasks 1–10.

- [ ] **Step 2: Secret-leak scan**

```powershell
git log -p windows-port-m2-tray-popup..HEAD `
  | Select-String -Pattern 'ey[A-Za-z0-9_\-]{20,}|sk-ant-|oat[A-Za-z0-9_\-]{20,}' `
  | Measure-Object | ForEach-Object { if ($_.Count -eq 0) { "clean" } else { "DIRTY" } }
```

Expected: `clean`.

- [ ] **Step 3: Push the branch**

```powershell
git push -u origin windows-port-m3a-services
```

- [ ] **Step 4: Open a PR**

Base: `windows-port-m2-tray-popup`. Title: `feat(windows): M3a services (preferences + notifier + startup + observers)`.

Body:
```
## Summary
- Adds `PreferencesStore` (JSON-backed, INPC) for theme/interval/launch-at-login/menu-bar-provider/floating-panel toggles.
- Adds `RefreshInterval` + `Theme` enums (tier-color presets ready for M3b's theme picker).
- Adds `UsageNotifier` (50/70/90/99 thresholds, dedup, window rollover, joke tables, JSON state). Toasts via `Microsoft.Toolkit.Uwp.Notifications`.
- Adds `RegistryRunKeyStartupService` (HKCU Run key, launch-at-login).
- Adds `NetworkAvailabilityObserver` + `PowerEventObserver` for refresh on reconnect/resume.
- Restores `UsageStore` features deferred from M2: configurable refresh interval from prefs, observer hooks, menu-bar provider picker.
- App.xaml.cs wires it all in DI-style at startup.

UI for changing preferences/floating panel/about/quit comes in **M3b**. For this PR, prefs use defaults (interval 60s, theme Default, etc.) until M3b's UI lands.

## Test plan
- [ ] `dotnet test windows/TokenSpendie.Windows.sln` — ~144 tests, all green.
- [ ] Smoke run: `TokenSpendie.Windows.exe` stays alive ≥10s; tray icon and popup still work as in M2.
- [ ] After ≥60s real-world use that crosses the 50% session threshold (manually pin a provider's snapshot if needed), a toast notification appears.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
```

- [ ] **Step 5: Notify completion**

Reply in chat: "M3a services complete. PR: <url>. ~144 tests green. Ready for M3b (preferences window, floating panel, about, tray context menu)."

---

## Self-review

**Spec coverage.** M3 milestone scope per spec:
> M3 — Preferences + floating panel + notifications: `PreferencesWindow`, `FloatingPanelWindow`, `UsageNotifier`, `RegistryRunKeyStartupService`.

This plan covers the services half:
- `UsageNotifier` (Task 5) ✅
- `RegistryRunKeyStartupService` (Task 7) ✅
- `PreferencesStore` (Task 3) — supporting the windows in M3b
- Network/power observers (Task 8) — restores mac UsageStore parity from spec
- `UsageStore` integration (Task 9) — restores mac parity

The UI half (PreferencesWindow, FloatingPanelWindow, AboutWindow, tray context menu) is explicitly deferred to **M3b** and called out in the plan header.

**Placeholder scan.** Every `<…>` in this plan is in code-block examples (`<url>` for PR URL after push) — runtime substitutions, not author placeholders.

**Type consistency.**
- `PreferencesStore.RefreshInterval` (Task 3, type `RefreshInterval` from Task 2) → read by `UsageStore` (Task 9) via `_preferences?.RefreshInterval.AsTimeSpan()`.
- `PreferencesStore.MenuBarProviderID` (Task 3, type `ProviderID` from M1) → read by `UsageStore.MenuBarProvider()` (Task 9).
- `IToastSender.Send(Joke, string)` defined Task 4, implemented Task 6, consumed Task 5.
- `IStartupAtLoginService.{IsEnabled, Enable, Disable}` defined Task 7, consumed App.xaml.cs Task 10.
- `INetworkAvailabilityObserver.Reconnected` event (Task 8) wired in `UsageStore` ctor (Task 9) and disposed in `DisposeAsync`.
- `Theme` (Task 2) used in PreferencesStore (Task 3); `ColorFor` is not yet consumed in this PR — that's M3b's theme application work.

**Cross-task dependencies.**
- Task 1 adds the toast package; nothing else uses it until Task 6.
- Task 2 (models) is a leaf; consumed by Tasks 3, 9.
- Task 3 (PreferencesStore) consumed by Tasks 9, 10.
- Task 4 (IToastSender interface) consumed by Tasks 5, 6.
- Task 5 (UsageNotifier) consumed by Task 10.
- Task 6 (WinRtToastSender + AumidRegistrar) consumed by Task 10.
- Task 7 (startup service) consumed by Task 10.
- Task 8 (observers) consumed by Tasks 9, 10.
- Task 9 (UsageStore integration) consumed by Task 10.
- Task 10 wires the lot; the smoke run validates everything together.
- Task 11 packages.

**Branch and commit discipline.** Branch created in Task 1 Step 1 (off `windows-port-m2-tray-popup`). Every implementation task ends with a commit. Task 11 secret-scans + pushes + opens the PR.
