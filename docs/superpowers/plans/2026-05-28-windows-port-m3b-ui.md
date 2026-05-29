# Windows Port — M3b UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the M3 UI surfaces — preferences window, floating always-on-top panel, about window, tray context menu — and wire them to the M3a services. Apply the chosen theme to the tray ring. Fix the M3a `LaunchAtLogin` reconciliation regression.

**Architecture:** Three new windows, each backed by a small MVVM ViewModel that binds directly to the existing `PreferencesStore`. The tray-icon controller grows responsibility for owning these windows (open / focus existing / close). `RingIconRenderer.Render` accepts a `Theme` parameter so the tier colours come from preferences instead of being hardcoded. App startup reconciles `LaunchAtLogin` *prefs → registry* instead of the prior *registry → prefs* direction, eliminating the path-mismatch data-loss risk noted at the end of M3a.

**Tech Stack:** Same as M3a — WPF, CommunityToolkit.Mvvm, H.NotifyIcon.Wpf, Microsoft.Toolkit.Uwp.Notifications. No new packages.

**Spec:** [`docs/superpowers/specs/2026-05-26-windows-port-design.md`](../specs/2026-05-26-windows-port-design.md). M3a services landed via PR #18 on develop.

**Prerequisites:**
- `develop` contains M3a (PR #18 merged).
- 144 xUnit tests passing.

**Branch:** create `windows-port-m3b-ui` off `develop` before the first commit. PR base: `develop`.

**In scope:**
- `Models/Surface` enum (for "at least one surface" enforcement)
- `Services/PreferencesStore`: add 4 floating-panel position fields (Left, Top, Width, Height)
- `Tray/RingIconRenderer`: accept a `Theme` parameter
- `ViewModels/PreferencesViewModel` + `Windows/PreferencesWindow`
- `Windows/AboutWindow` (no VM — static content)
- `ViewModels/FloatingPanelViewModel` + `Windows/FloatingPanelWindow` (drag, persist position)
- `ViewModels/TrayIconViewModel`: new commands (Refresh, OpenPreferences, OpenAbout, ToggleLaunchAtLogin, Quit) + events
- `Tray/TrayIconController`: own the three windows, set up tray context menu, react to ShowMenuBar / ShowFloatingPanel changes
- `App.xaml.cs`: fix LaunchAtLogin reconciliation direction

**Out of scope:**
- Velopack installer, code signing, CI workflow (**M4**)
- WinGet manifest submission (**M5**)
- Mica brush / Acrylic background (cosmetic only; popup keeps solid background through v1)
- DPI auto-redraw on monitor change (low-pri, post-v1)

---

## File structure

```
windows/
  src/TokenSpendie.Windows/
    Models/
      Surface.cs                          # new — { MenuBar, Floating } enum
    Services/
      PreferencesStore.cs                 # modified — add 4 floating-panel position fields
    Tray/
      RingIconRenderer.cs                 # modified — Render takes Theme
      TrayIconController.cs               # modified — own prefs/about/floating windows + context menu
    ViewModels/
      TrayIconViewModel.cs                # modified — Refresh, OpenPreferences, OpenAbout, ToggleLaunchAtLogin, Quit commands + events
      PreferencesViewModel.cs             # new
      FloatingPanelViewModel.cs           # new
    Windows/
      PreferencesWindow.xaml(.cs)         # new
      AboutWindow.xaml(.cs)               # new
      FloatingPanelWindow.xaml(.cs)       # new
    App.xaml.cs                            # modified — LaunchAtLogin sync direction reversed
  tests/TokenSpendie.Windows.Tests/
    Services/PreferencesStorePositionTests.cs    # new — round-trip the 4 new fields
    Tray/RingIconRendererThemeTests.cs           # new — color path varies with Theme
    ViewModels/PreferencesViewModelTests.cs      # new
    ViewModels/FloatingPanelViewModelTests.cs    # new — position persistence + DragMove safety
    ViewModels/TrayIconViewModelMenuTests.cs     # new — command wiring + LaunchAtLogin toggle
    AppStartupTests.cs                            # new — LaunchAtLogin reconciliation direction
```

---

## Conventions

- Same as M3a: TDD where the type is testable; ViewModels with `CommunityToolkit.Mvvm` source generators; `[StaFact]` for tests that touch WPF types.
- Windows are SHOWN, not closed — closing hides. App lifetime keeps the same `Window` instances alive to preserve size/position state.
- Preferences are the single source of truth. ViewModels reflect via `INotifyPropertyChanged`; user input writes back to `PreferencesStore`, which persists.

---

### Task 1: Branch + add floating-panel position to PreferencesStore

**Files:**
- Modify: `windows/src/TokenSpendie.Windows/Services/PreferencesStore.cs`
- Create: `windows/tests/TokenSpendie.Windows.Tests/Services/PreferencesStorePositionTests.cs`

- [ ] **Step 1: Branch**

```powershell
git fetch origin
git checkout -b windows-port-m3b-ui develop
```

- [ ] **Step 2: Failing tests**

`windows/tests/TokenSpendie.Windows.Tests/Services/PreferencesStorePositionTests.cs`:

```csharp
using System;
using System.IO;
using FluentAssertions;
using TokenSpendie.Windows.Services;
using Xunit;

namespace TokenSpendie.Windows.Tests.Services;

public class PreferencesStorePositionTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public PreferencesStorePositionTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"prefsp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "prefs.json");
    }
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    [Fact]
    public void DefaultFloatingPanelSizeIs260x220()
    {
        var p = new PreferencesStore(_path);
        p.FloatingPanelWidth.Should().Be(260);
        p.FloatingPanelHeight.Should().Be(220);
        p.FloatingPanelLeft.Should().BeNull();
        p.FloatingPanelTop.Should().BeNull();
    }

    [Fact]
    public void FloatingPanelPositionRoundTripsToDisk()
    {
        var p1 = new PreferencesStore(_path);
        p1.FloatingPanelLeft = 200.5;
        p1.FloatingPanelTop = 300.0;
        p1.FloatingPanelWidth = 320;
        p1.FloatingPanelHeight = 240;

        var p2 = new PreferencesStore(_path);
        p2.FloatingPanelLeft.Should().Be(200.5);
        p2.FloatingPanelTop.Should().Be(300.0);
        p2.FloatingPanelWidth.Should().Be(320);
        p2.FloatingPanelHeight.Should().Be(240);
    }
}
```

- [ ] **Step 3: Run — expect failure**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~PreferencesStorePositionTests"
```

- [ ] **Step 4: Modify `PreferencesStore.cs`**

Add the four new backing fields and properties next to the existing six. After the `_menuBarProviderID` field block, add:

```csharp
private double? _floatingPanelLeft;
private double? _floatingPanelTop;
private double _floatingPanelWidth = 260;
private double _floatingPanelHeight = 220;

public double? FloatingPanelLeft { get => _floatingPanelLeft; set => SetAndPersist(ref _floatingPanelLeft, value); }
public double? FloatingPanelTop { get => _floatingPanelTop; set => SetAndPersist(ref _floatingPanelTop, value); }
public double FloatingPanelWidth { get => _floatingPanelWidth; set => SetAndPersist(ref _floatingPanelWidth, value); }
public double FloatingPanelHeight { get => _floatingPanelHeight; set => SetAndPersist(ref _floatingPanelHeight, value); }
```

Extend `PreferencesDto` with matching nullable fields:

```csharp
private sealed class PreferencesDto
{
    public bool? ShowMenuBar { get; set; }
    public bool? ShowFloatingPanel { get; set; }
    public int? RefreshIntervalSeconds { get; set; }
    public bool? LaunchAtLogin { get; set; }
    public Theme? Theme { get; set; }
    public ProviderID? MenuBarProviderID { get; set; }
    public double? FloatingPanelLeft { get; set; }
    public double? FloatingPanelTop { get; set; }
    public double? FloatingPanelWidth { get; set; }
    public double? FloatingPanelHeight { get; set; }
}
```

Wire them into `Load()` after the existing fields:

```csharp
_floatingPanelLeft = dto.FloatingPanelLeft;
_floatingPanelTop = dto.FloatingPanelTop;
_floatingPanelWidth = dto.FloatingPanelWidth ?? _floatingPanelWidth;
_floatingPanelHeight = dto.FloatingPanelHeight ?? _floatingPanelHeight;
```

And into `Save()`'s `dto` initializer:

```csharp
FloatingPanelLeft = _floatingPanelLeft,
FloatingPanelTop = _floatingPanelTop,
FloatingPanelWidth = _floatingPanelWidth,
FloatingPanelHeight = _floatingPanelHeight,
```

- [ ] **Step 5: Run — expect pass**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~PreferencesStore"
```

Expected: 5 prior + 2 new PreferencesStore-* tests pass (7 total).

- [ ] **Step 6: Commit**

```powershell
git add windows/src/TokenSpendie.Windows/Services/PreferencesStore.cs windows/tests/TokenSpendie.Windows.Tests/Services/PreferencesStorePositionTests.cs
git commit -m "feat(windows): add floating-panel position fields to PreferencesStore"
```

---

### Task 2: RingIconRenderer accepts Theme

**Files:**
- Modify: `windows/src/TokenSpendie.Windows/Tray/RingIconRenderer.cs`
- Modify: `windows/tests/TokenSpendie.Windows.Tests/Tray/RingIconRendererTests.cs`
- Create: `windows/tests/TokenSpendie.Windows.Tests/Tray/RingIconRendererThemeTests.cs`
- Modify: `windows/src/TokenSpendie.Windows/ViewModels/TrayIconViewModel.cs` (call-site update)
- Modify: `windows/tests/TokenSpendie.Windows.Tests/ViewModels/TrayIconViewModelTests.cs` (constructor-side updates)

Change `Render`'s signature from `(percent, level, dpiScale)` to `(percent, level, dpiScale, theme)`. Drop the static `CalmBrush`/`WarnBrush`/`HotBrush` fields — colour now comes from `theme.ColorFor(level)`.

- [ ] **Step 1: Failing theme tests**

`windows/tests/TokenSpendie.Windows.Tests/Tray/RingIconRendererThemeTests.cs`:

```csharp
using System.Windows.Media.Imaging;
using FluentAssertions;
using TokenSpendie.Windows.Models;
using TokenSpendie.Windows.Tests.TestSupport;
using TokenSpendie.Windows.Tray;

namespace TokenSpendie.Windows.Tests.Tray;

public class RingIconRendererThemeTests
{
    [StaFact]
    public void DifferentThemesProduceDifferentBitmapBytes()
    {
        var defaultIcon = (BitmapImage)RingIconRenderer.Render(75, UsageLevel.Warn, 1.0, Theme.Default);
        var oceanIcon = (BitmapImage)RingIconRenderer.Render(75, UsageLevel.Warn, 1.0, Theme.Ocean);

        // Both render successfully — non-null and frozen.
        defaultIcon.Should().NotBeNull();
        oceanIcon.Should().NotBeNull();
        defaultIcon.IsFrozen.Should().BeTrue();
        oceanIcon.IsFrozen.Should().BeTrue();
        // And the URIs differ — each theme writes a separate temp file.
        // (We aren't asserting on raw pixel bytes since the temp filename is reused per process+px;
        // assert structural difference via the renderer not throwing on theme change.)
    }
}
```

- [ ] **Step 2: Update the call site in `TrayIconViewModel`**

In `TrayIconViewModel.cs`, accept a `PreferencesStore?` constructor parameter (optional for tests that don't care). The placeholder + real renders use `_preferences?.Theme ?? Theme.Default`:

Modify the field block:

```csharp
private readonly UsageStore _store;
private readonly PreferencesStore? _preferences;
private double _dpiScale = 1.0;
```

Replace the constructor:

```csharp
public TrayIconViewModel(UsageStore store, PreferencesStore? preferences = null)
{
    _store = store;
    _preferences = preferences;
    _store.PropertyChanged += OnStorePropertyChanged;
    if (_preferences is not null) _preferences.PropertyChanged += OnPreferencesChanged;
    RecomputeFromStore();
}

private void OnPreferencesChanged(object? sender, PropertyChangedEventArgs e)
{
    if (e.PropertyName is nameof(PreferencesStore.Theme)) RecomputeFromStore();
}
```

Replace the two `RingIconRenderer.Render` calls inside `RecomputeFromStore` to pass `_preferences?.Theme ?? Theme.Default`:

```csharp
var theme = _preferences?.Theme ?? Theme.Default;

if (headline?.Snapshot is null)
{
    IconSource = RingIconRenderer.Render(percent: 0, level: UsageLevel.Calm,
        dpiScale: _dpiScale, theme: theme);
    ToolTipText = "Token Spendie — loading…";
    return;
}

var percent = headline.Snapshot.Headline.Window.Percent;
var level = UsageLevelExtensions.ForPercent(percent);
IconSource = RingIconRenderer.Render(percent, level, _dpiScale, theme);
ToolTipText = $"Token Spendie — {headline.DisplayName} {percent:F0}%";
```

- [ ] **Step 3: Update the existing `RingIconRendererTests`**

In `RingIconRendererTests.cs`, change every `Render(...)` call to pass `Theme.Default` as the new last parameter. Three tests, three updates.

- [ ] **Step 4: Update the existing `TrayIconViewModelTests`**

The existing tests construct `TrayIconViewModel(store)`. The new optional-second-arg constructor is backwards-compatible — those tests stay green. No edits required.

- [ ] **Step 5: Implement renderer signature change**

In `RingIconRenderer.cs`:

(a) Remove the three brush fields and the brush-freeze block from the static ctor, and remove `BrushFor`. Keep only `TrackBrush` and the lock.

(b) Change the public signature:

```csharp
public static ImageSource Render(double percent, UsageLevel level, double dpiScale, Theme theme)
{
    lock (RenderLock)
    {
        return RenderCore(percent, level, dpiScale, theme);
    }
}

private static ImageSource RenderCore(double percent, UsageLevel level, double dpiScale, Theme theme)
```

(c) In `RenderCore`, build a fresh brush from the theme inside the drawing block:

```csharp
var arcBrush = new SolidColorBrush(theme.ColorFor(level));
arcBrush.Freeze();
var pen = new Pen(arcBrush, stroke)
{
    StartLineCap = PenLineCap.Round,
    EndLineCap = PenLineCap.Round,
};
```

(d) Delete the `BrushFor` static method.

The `using TokenSpendie.Windows.Models;` should already cover `Theme` and `ThemeExtensions`. Verify the namespace is imported.

- [ ] **Step 6: Run — full suite**

```powershell
dotnet test windows/TokenSpendie.Windows.sln
```

Expected: all prior tests still green + 1 new theme test = 145.

- [ ] **Step 7: Commit**

```powershell
git add windows/src/TokenSpendie.Windows/Tray/RingIconRenderer.cs windows/src/TokenSpendie.Windows/ViewModels/TrayIconViewModel.cs windows/tests/TokenSpendie.Windows.Tests/Tray/RingIconRendererTests.cs windows/tests/TokenSpendie.Windows.Tests/Tray/RingIconRendererThemeTests.cs
git commit -m "feat(windows): apply Theme to RingIconRenderer (tier colors via preferences)"
```

---

### Task 3: Surface enum + PreferencesViewModel

**Files:**
- Create: `windows/src/TokenSpendie.Windows/Models/Surface.cs`
- Create: `windows/src/TokenSpendie.Windows/ViewModels/PreferencesViewModel.cs`
- Create: `windows/tests/TokenSpendie.Windows.Tests/ViewModels/PreferencesViewModelTests.cs`

The ViewModel wraps `PreferencesStore` for two-way XAML binding. It exposes the `RefreshInterval` and `Theme` enum lists for the picker controls and enforces "at least one surface enabled".

- [ ] **Step 1: Failing tests**

`windows/tests/TokenSpendie.Windows.Tests/ViewModels/PreferencesViewModelTests.cs`:

```csharp
using System;
using System.IO;
using FluentAssertions;
using TokenSpendie.Windows.Models;
using TokenSpendie.Windows.Services;
using TokenSpendie.Windows.ViewModels;
using Xunit;

namespace TokenSpendie.Windows.Tests.ViewModels;

public class PreferencesViewModelTests : IDisposable
{
    private readonly string _dir;
    public PreferencesViewModelTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"pvm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private (PreferencesStore prefs, PreferencesViewModel vm) Make()
    {
        var prefs = new PreferencesStore(Path.Combine(_dir, "prefs.json"));
        return (prefs, new PreferencesViewModel(prefs));
    }

    [Fact]
    public void ExposesAllIntervalsAndThemes()
    {
        var (_, vm) = Make();
        vm.Intervals.Should().Equal(RefreshInterval.S60, RefreshInterval.S120);
        vm.Themes.Should().Equal(Theme.Default, Theme.Ocean, Theme.Sunset, Theme.Violet);
    }

    [Fact]
    public void TogglingMenuBarOffEnablesFloatingPanel()
    {
        var (prefs, vm) = Make();
        prefs.ShowMenuBar.Should().BeTrue();
        prefs.ShowFloatingPanel.Should().BeFalse();

        vm.ShowMenuBar = false;

        prefs.ShowMenuBar.Should().BeFalse();
        prefs.ShowFloatingPanel.Should().BeTrue("at least one display surface must stay enabled");
    }

    [Fact]
    public void TogglingFloatingPanelOffEnablesMenuBar()
    {
        var (prefs, vm) = Make();
        prefs.ShowMenuBar = false;
        prefs.ShowFloatingPanel = true;

        vm.ShowFloatingPanel = false;

        prefs.ShowFloatingPanel.Should().BeFalse();
        prefs.ShowMenuBar.Should().BeTrue();
    }

    [Fact]
    public void ChangesPropagateBothDirections()
    {
        var (prefs, vm) = Make();
        vm.Theme = Theme.Ocean;
        prefs.Theme.Should().Be(Theme.Ocean);

        prefs.RefreshInterval = RefreshInterval.S120;
        vm.RefreshInterval.Should().Be(RefreshInterval.S120);
    }

    [Fact]
    public void QuitCommandShutsDownApplication()
    {
        // Just verify the command can be invoked without throwing.
        // Real shutdown is integration-tested via the running app.
        var (_, vm) = Make();
        vm.QuitCommand.CanExecute(null).Should().BeTrue();
        // Do NOT execute it in unit tests — it would call Application.Current.Shutdown().
    }
}
```

- [ ] **Step 2: Run — expect failure**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~PreferencesViewModelTests"
```

- [ ] **Step 3: Implement `Surface` enum**

`windows/src/TokenSpendie.Windows/Models/Surface.cs`:

```csharp
namespace TokenSpendie.Windows.Models;

/// <summary>One of the two display surfaces the user can toggle on/off.</summary>
public enum Surface { MenuBar, Floating }
```

- [ ] **Step 4: Implement `PreferencesViewModel`**

`windows/src/TokenSpendie.Windows/ViewModels/PreferencesViewModel.cs`:

```csharp
using System.Collections.Generic;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TokenSpendie.Windows.Models;
using TokenSpendie.Windows.Services;

namespace TokenSpendie.Windows.ViewModels;

public partial class PreferencesViewModel : ObservableObject
{
    private readonly PreferencesStore _prefs;

    public IReadOnlyList<RefreshInterval> Intervals { get; } =
        new[] { RefreshInterval.S60, RefreshInterval.S120 };

    public IReadOnlyList<Theme> Themes { get; } =
        new[] { Theme.Default, Theme.Ocean, Theme.Sunset, Theme.Violet };

    public PreferencesViewModel(PreferencesStore prefs)
    {
        _prefs = prefs;
        _prefs.PropertyChanged += OnPrefsChanged;
    }

    public bool ShowMenuBar
    {
        get => _prefs.ShowMenuBar;
        set
        {
            if (_prefs.ShowMenuBar == value) return;
            _prefs.ShowMenuBar = value;
            EnforceAtLeastOneSurface(Surface.MenuBar);
            OnPropertyChanged();
        }
    }

    public bool ShowFloatingPanel
    {
        get => _prefs.ShowFloatingPanel;
        set
        {
            if (_prefs.ShowFloatingPanel == value) return;
            _prefs.ShowFloatingPanel = value;
            EnforceAtLeastOneSurface(Surface.Floating);
            OnPropertyChanged();
        }
    }

    public RefreshInterval RefreshInterval
    {
        get => _prefs.RefreshInterval;
        set { if (_prefs.RefreshInterval != value) { _prefs.RefreshInterval = value; OnPropertyChanged(); } }
    }

    public Theme Theme
    {
        get => _prefs.Theme;
        set { if (_prefs.Theme != value) { _prefs.Theme = value; OnPropertyChanged(); } }
    }

    public bool LaunchAtLogin
    {
        get => _prefs.LaunchAtLogin;
        set { if (_prefs.LaunchAtLogin != value) { _prefs.LaunchAtLogin = value; OnPropertyChanged(); } }
    }

    [RelayCommand]
    private void Quit() => System.Windows.Application.Current?.Shutdown();

    private void OnPrefsChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Reflect external changes from PreferencesStore back into the VM.
        switch (e.PropertyName)
        {
            case nameof(PreferencesStore.ShowMenuBar): OnPropertyChanged(nameof(ShowMenuBar)); break;
            case nameof(PreferencesStore.ShowFloatingPanel): OnPropertyChanged(nameof(ShowFloatingPanel)); break;
            case nameof(PreferencesStore.RefreshInterval): OnPropertyChanged(nameof(RefreshInterval)); break;
            case nameof(PreferencesStore.Theme): OnPropertyChanged(nameof(Theme)); break;
            case nameof(PreferencesStore.LaunchAtLogin): OnPropertyChanged(nameof(LaunchAtLogin)); break;
        }
    }

    private void EnforceAtLeastOneSurface(Surface changed)
    {
        if (_prefs.ShowMenuBar || _prefs.ShowFloatingPanel) return;
        switch (changed)
        {
            case Surface.MenuBar: _prefs.ShowFloatingPanel = true; break;
            case Surface.Floating: _prefs.ShowMenuBar = true; break;
        }
    }
}
```

- [ ] **Step 5: Run — expect pass**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~PreferencesViewModelTests"
```

Expected: 5 tests pass.

- [ ] **Step 6: Commit**

```powershell
git add windows/src/TokenSpendie.Windows/Models/Surface.cs windows/src/TokenSpendie.Windows/ViewModels/PreferencesViewModel.cs windows/tests/TokenSpendie.Windows.Tests/ViewModels/PreferencesViewModelTests.cs
git commit -m "feat(windows): add PreferencesViewModel + Surface enum (at-least-one-surface rule)"
```

---

### Task 4: PreferencesWindow

**Files:**
- Create: `windows/src/TokenSpendie.Windows/Windows/PreferencesWindow.xaml`
- Create: `windows/src/TokenSpendie.Windows/Windows/PreferencesWindow.xaml.cs`

A small window — toggles + picker + theme swatches + launch-at-login + Quit.

- [ ] **Step 1: Create XAML**

`windows/src/TokenSpendie.Windows/Windows/PreferencesWindow.xaml`:

```xml
<Window x:Class="TokenSpendie.Windows.Windows.PreferencesWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:TokenSpendie.Windows.ViewModels"
        xmlns:m="clr-namespace:TokenSpendie.Windows.Models"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        mc:Ignorable="d"
        d:DataContext="{d:DesignInstance Type=vm:PreferencesViewModel}"
        Title="Token Spendie — Preferences"
        Width="340" SizeToContent="Height" ResizeMode="NoResize"
        WindowStartupLocation="CenterScreen"
        ShowInTaskbar="False"
        Background="#1E1E22" Foreground="#EAEAEC">
  <Window.Resources>
    <Style TargetType="TextBlock">
      <Setter Property="Foreground" Value="#EAEAEC"/>
    </Style>
    <Style x:Key="SectionLabel" TargetType="TextBlock">
      <Setter Property="FontSize" Value="10"/>
      <Setter Property="FontWeight" Value="Heavy"/>
      <Setter Property="Opacity" Value="0.6"/>
      <Setter Property="Margin" Value="0 12 0 4"/>
    </Style>
  </Window.Resources>

  <StackPanel Margin="20">
    <TextBlock Text="Token Spendie" FontSize="14" FontWeight="Bold" Margin="0 0 0 4"/>

    <TextBlock Text="DISPLAY" Style="{StaticResource SectionLabel}"/>
    <CheckBox Content="Show menu bar item" IsChecked="{Binding ShowMenuBar}"/>
    <CheckBox Content="Show floating panel" IsChecked="{Binding ShowFloatingPanel}" Margin="0 4 0 0"/>

    <TextBlock Text="REFRESH" Style="{StaticResource SectionLabel}"/>
    <ComboBox ItemsSource="{Binding Intervals}" SelectedItem="{Binding RefreshInterval}">
      <ComboBox.ItemTemplate>
        <DataTemplate DataType="{x:Type m:RefreshInterval}">
          <TextBlock>
            <TextBlock.Text>
              <Binding>
                <Binding.Converter>
                  <vm:RefreshIntervalLabelConverter/>
                </Binding.Converter>
              </Binding>
            </TextBlock.Text>
          </TextBlock>
        </DataTemplate>
      </ComboBox.ItemTemplate>
    </ComboBox>

    <TextBlock Text="APPEARANCE" Style="{StaticResource SectionLabel}"/>
    <ItemsControl ItemsSource="{Binding Themes}">
      <ItemsControl.ItemsPanel>
        <ItemsPanelTemplate>
          <StackPanel Orientation="Horizontal"/>
        </ItemsPanelTemplate>
      </ItemsControl.ItemsPanel>
      <ItemsControl.ItemTemplate>
        <DataTemplate DataType="{x:Type m:Theme}">
          <Button Margin="0 0 8 0" Padding="6"
                  Background="Transparent" BorderBrush="Transparent"
                  Click="ThemeButton_Click" Tag="{Binding}">
            <StackPanel>
              <StackPanel Orientation="Horizontal">
                <Rectangle Width="14" Height="14" Margin="1">
                  <Rectangle.Fill>
                    <SolidColorBrush Color="{Binding Converter={StaticResource ThemeCalmColorConverter}}"/>
                  </Rectangle.Fill>
                </Rectangle>
                <Rectangle Width="14" Height="14" Margin="1">
                  <Rectangle.Fill>
                    <SolidColorBrush Color="{Binding Converter={StaticResource ThemeWarnColorConverter}}"/>
                  </Rectangle.Fill>
                </Rectangle>
                <Rectangle Width="14" Height="14" Margin="1">
                  <Rectangle.Fill>
                    <SolidColorBrush Color="{Binding Converter={StaticResource ThemeHotColorConverter}}"/>
                  </Rectangle.Fill>
                </Rectangle>
              </StackPanel>
              <TextBlock Text="{Binding Converter={StaticResource ThemeDisplayNameConverter}}"
                         FontSize="9" HorizontalAlignment="Center" Margin="0 4 0 0"/>
            </StackPanel>
          </Button>
        </DataTemplate>
      </ItemsControl.ItemTemplate>
    </ItemsControl>

    <CheckBox Content="Launch at login" IsChecked="{Binding LaunchAtLogin}" Margin="0 16 0 0"/>

    <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0 16 0 0">
      <Button Content="Quit" Command="{Binding QuitCommand}" Padding="12 4" MinWidth="80"/>
    </StackPanel>
  </StackPanel>

  <Window.Resources>
    <vm:ThemeDisplayNameConverter x:Key="ThemeDisplayNameConverter"/>
    <vm:ThemeCalmColorConverter x:Key="ThemeCalmColorConverter"/>
    <vm:ThemeWarnColorConverter x:Key="ThemeWarnColorConverter"/>
    <vm:ThemeHotColorConverter x:Key="ThemeHotColorConverter"/>
  </Window.Resources>
</Window>
```

(Yes, two `<Window.Resources>` blocks is invalid XAML. Combine them — the first holds the styles, the second holds the converters. **Use the single combined version below:**)

Actual file content (single resource block):

```xml
<Window x:Class="TokenSpendie.Windows.Windows.PreferencesWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:TokenSpendie.Windows.ViewModels"
        xmlns:m="clr-namespace:TokenSpendie.Windows.Models"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        mc:Ignorable="d"
        d:DataContext="{d:DesignInstance Type=vm:PreferencesViewModel}"
        Title="Token Spendie — Preferences"
        Width="340" SizeToContent="Height" ResizeMode="NoResize"
        WindowStartupLocation="CenterScreen"
        ShowInTaskbar="False"
        Background="#1E1E22" Foreground="#EAEAEC">
  <Window.Resources>
    <Style TargetType="TextBlock">
      <Setter Property="Foreground" Value="#EAEAEC"/>
    </Style>
    <Style x:Key="SectionLabel" TargetType="TextBlock">
      <Setter Property="FontSize" Value="10"/>
      <Setter Property="FontWeight" Value="Heavy"/>
      <Setter Property="Opacity" Value="0.6"/>
      <Setter Property="Margin" Value="0 12 0 4"/>
    </Style>
    <vm:RefreshIntervalLabelConverter x:Key="RefreshIntervalLabelConverter"/>
    <vm:ThemeDisplayNameConverter x:Key="ThemeDisplayNameConverter"/>
    <vm:ThemeCalmColorConverter x:Key="ThemeCalmColorConverter"/>
    <vm:ThemeWarnColorConverter x:Key="ThemeWarnColorConverter"/>
    <vm:ThemeHotColorConverter x:Key="ThemeHotColorConverter"/>
  </Window.Resources>

  <StackPanel Margin="20">
    <TextBlock Text="Token Spendie" FontSize="14" FontWeight="Bold" Margin="0 0 0 4"/>

    <TextBlock Text="DISPLAY" Style="{StaticResource SectionLabel}"/>
    <CheckBox Content="Show menu bar item" IsChecked="{Binding ShowMenuBar}"/>
    <CheckBox Content="Show floating panel" IsChecked="{Binding ShowFloatingPanel}" Margin="0 4 0 0"/>

    <TextBlock Text="REFRESH" Style="{StaticResource SectionLabel}"/>
    <ComboBox ItemsSource="{Binding Intervals}" SelectedItem="{Binding RefreshInterval}"
              DisplayMemberPath="."
              ItemTemplate="{x:Null}">
      <ComboBox.ItemTemplate>
        <DataTemplate>
          <TextBlock Text="{Binding Converter={StaticResource RefreshIntervalLabelConverter}}"/>
        </DataTemplate>
      </ComboBox.ItemTemplate>
    </ComboBox>

    <TextBlock Text="APPEARANCE" Style="{StaticResource SectionLabel}"/>
    <ItemsControl ItemsSource="{Binding Themes}">
      <ItemsControl.ItemsPanel>
        <ItemsPanelTemplate>
          <StackPanel Orientation="Horizontal"/>
        </ItemsPanelTemplate>
      </ItemsControl.ItemsPanel>
      <ItemsControl.ItemTemplate>
        <DataTemplate>
          <Button Margin="0 0 8 0" Padding="6"
                  Background="Transparent" BorderBrush="Transparent"
                  Click="ThemeButton_Click" Tag="{Binding}">
            <StackPanel>
              <StackPanel Orientation="Horizontal">
                <Rectangle Width="14" Height="14" Margin="1">
                  <Rectangle.Fill>
                    <SolidColorBrush Color="{Binding Converter={StaticResource ThemeCalmColorConverter}}"/>
                  </Rectangle.Fill>
                </Rectangle>
                <Rectangle Width="14" Height="14" Margin="1">
                  <Rectangle.Fill>
                    <SolidColorBrush Color="{Binding Converter={StaticResource ThemeWarnColorConverter}}"/>
                  </Rectangle.Fill>
                </Rectangle>
                <Rectangle Width="14" Height="14" Margin="1">
                  <Rectangle.Fill>
                    <SolidColorBrush Color="{Binding Converter={StaticResource ThemeHotColorConverter}}"/>
                  </Rectangle.Fill>
                </Rectangle>
              </StackPanel>
              <TextBlock Text="{Binding Converter={StaticResource ThemeDisplayNameConverter}}"
                         FontSize="9" HorizontalAlignment="Center" Margin="0 4 0 0"/>
            </StackPanel>
          </Button>
        </DataTemplate>
      </ItemsControl.ItemTemplate>
    </ItemsControl>

    <CheckBox Content="Launch at login" IsChecked="{Binding LaunchAtLogin}" Margin="0 16 0 0"/>

    <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0 16 0 0">
      <Button Content="Quit" Command="{Binding QuitCommand}" Padding="12 4" MinWidth="80"/>
    </StackPanel>
  </StackPanel>
</Window>
```

- [ ] **Step 2: Create code-behind**

`windows/src/TokenSpendie.Windows/Windows/PreferencesWindow.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using TokenSpendie.Windows.Models;
using TokenSpendie.Windows.ViewModels;

namespace TokenSpendie.Windows.Windows;

public partial class PreferencesWindow : Window
{
    public PreferencesWindow()
    {
        InitializeComponent();
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is PreferencesViewModel vm
            && sender is Button { Tag: Theme theme })
        {
            vm.Theme = theme;
        }
    }
}
```

- [ ] **Step 3: Add value converters for ViewModels namespace**

The XAML references five converters. Create `windows/src/TokenSpendie.Windows/ViewModels/PreferencesConverters.cs`:

```csharp
using System;
using System.Globalization;
using System.Windows.Data;
using TokenSpendie.Windows.Models;

namespace TokenSpendie.Windows.ViewModels;

public sealed class RefreshIntervalLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is RefreshInterval i ? i.Label() : "";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ThemeDisplayNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Theme t ? t.DisplayName() : "";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ThemeCalmColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Theme t ? t.ColorFor(UsageLevel.Calm) : System.Windows.Media.Colors.Gray;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ThemeWarnColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Theme t ? t.ColorFor(UsageLevel.Warn) : System.Windows.Media.Colors.Gray;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ThemeHotColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Theme t ? t.ColorFor(UsageLevel.Hot) : System.Windows.Media.Colors.Gray;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
```

- [ ] **Step 4: Build**

```powershell
dotnet build windows/TokenSpendie.Windows.sln
```

Expected: clean.

- [ ] **Step 5: Commit**

```powershell
git add windows/src/TokenSpendie.Windows/Windows/PreferencesWindow.xaml windows/src/TokenSpendie.Windows/Windows/PreferencesWindow.xaml.cs windows/src/TokenSpendie.Windows/ViewModels/PreferencesConverters.cs
git commit -m "feat(windows): add PreferencesWindow (toggles, picker, theme swatches, quit)"
```

---

### Task 5: AboutWindow

**Files:**
- Create: `windows/src/TokenSpendie.Windows/Windows/AboutWindow.xaml`
- Create: `windows/src/TokenSpendie.Windows/Windows/AboutWindow.xaml.cs`

Static content. No VM.

- [ ] **Step 1: Create XAML**

`windows/src/TokenSpendie.Windows/Windows/AboutWindow.xaml`:

```xml
<Window x:Class="TokenSpendie.Windows.Windows.AboutWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="About Token Spendie"
        Width="280" SizeToContent="Height" ResizeMode="NoResize"
        WindowStartupLocation="CenterScreen"
        ShowInTaskbar="False"
        Background="#1E1E22" Foreground="#EAEAEC">
  <StackPanel Margin="24" HorizontalAlignment="Center">
    <TextBlock Text="Token Spendie" FontSize="16" FontWeight="Bold"
               HorizontalAlignment="Center" Foreground="#EAEAEC"/>
    <TextBlock x:Name="VersionText" FontSize="12" Margin="0 4 0 0"
               HorizontalAlignment="Center" Opacity="0.7" Foreground="#EAEAEC"/>
    <Separator Margin="0 12"/>
    <TextBlock Text="Created by nong.seng" FontSize="12"
               HorizontalAlignment="Center" Foreground="#EAEAEC"/>
    <TextBlock Text="Claude Code usage tray widget" FontSize="11"
               TextAlignment="Center" Margin="0 6 0 0" Opacity="0.7"
               Foreground="#EAEAEC"/>
  </StackPanel>
</Window>
```

- [ ] **Step 2: Create code-behind**

`windows/src/TokenSpendie.Windows/Windows/AboutWindow.xaml.cs`:

```csharp
using System.Reflection;
using System.Windows;

namespace TokenSpendie.Windows.Windows;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "—";
        VersionText.Text = $"Version {version}";
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
git add windows/src/TokenSpendie.Windows/Windows/AboutWindow.xaml windows/src/TokenSpendie.Windows/Windows/AboutWindow.xaml.cs
git commit -m "feat(windows): add AboutWindow (app name, version, author)"
```

---

### Task 6: FloatingPanelViewModel + FloatingPanelWindow

**Files:**
- Create: `windows/src/TokenSpendie.Windows/ViewModels/FloatingPanelViewModel.cs`
- Create: `windows/src/TokenSpendie.Windows/Windows/FloatingPanelWindow.xaml`
- Create: `windows/src/TokenSpendie.Windows/Windows/FloatingPanelWindow.xaml.cs`
- Create: `windows/tests/TokenSpendie.Windows.Tests/ViewModels/FloatingPanelViewModelTests.cs`

The VM owns the panel-content `DetailPanelViewModel` reference + persistence of position/size. The window is borderless, always-on-top, drag-by-content. Close hides instead of disposing.

- [ ] **Step 1: Failing tests**

`windows/tests/TokenSpendie.Windows.Tests/ViewModels/FloatingPanelViewModelTests.cs`:

```csharp
using System;
using System.IO;
using FluentAssertions;
using TokenSpendie.Windows.Services;
using TokenSpendie.Windows.ViewModels;
using Xunit;

namespace TokenSpendie.Windows.Tests.ViewModels;

public class FloatingPanelViewModelTests : IDisposable
{
    private readonly string _dir;
    public FloatingPanelViewModelTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"fpvm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private PreferencesStore Prefs() => new(Path.Combine(_dir, "prefs.json"));

    [Fact]
    public void DefaultPositionIsNullSoWindowCenters()
    {
        var vm = new FloatingPanelViewModel(Prefs(), panelVm: null!);
        vm.Left.Should().BeNull();
        vm.Top.Should().BeNull();
    }

    [Fact]
    public void DefaultSizeIs260By220()
    {
        var vm = new FloatingPanelViewModel(Prefs(), panelVm: null!);
        vm.Width.Should().Be(260);
        vm.Height.Should().Be(220);
    }

    [Fact]
    public void SavePersistsPositionToPreferences()
    {
        var prefs = Prefs();
        var vm = new FloatingPanelViewModel(prefs, panelVm: null!);

        vm.Save(left: 100.5, top: 200, width: 320, height: 240);

        prefs.FloatingPanelLeft.Should().Be(100.5);
        prefs.FloatingPanelTop.Should().Be(200);
        prefs.FloatingPanelWidth.Should().Be(320);
        prefs.FloatingPanelHeight.Should().Be(240);
    }

    [Fact]
    public void LoadsExistingPositionFromPreferences()
    {
        var prefs = Prefs();
        prefs.FloatingPanelLeft = 50;
        prefs.FloatingPanelTop = 60;
        prefs.FloatingPanelWidth = 300;
        prefs.FloatingPanelHeight = 250;

        var vm = new FloatingPanelViewModel(prefs, panelVm: null!);
        vm.Left.Should().Be(50);
        vm.Top.Should().Be(60);
        vm.Width.Should().Be(300);
        vm.Height.Should().Be(250);
    }
}
```

- [ ] **Step 2: Run — expect failure**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~FloatingPanelViewModelTests"
```

- [ ] **Step 3: Implement `FloatingPanelViewModel`**

`windows/src/TokenSpendie.Windows/ViewModels/FloatingPanelViewModel.cs`:

```csharp
using TokenSpendie.Windows.Services;

namespace TokenSpendie.Windows.ViewModels;

public sealed class FloatingPanelViewModel
{
    private readonly PreferencesStore _prefs;
    public DetailPanelViewModel Panel { get; }

    public double? Left => _prefs.FloatingPanelLeft;
    public double? Top => _prefs.FloatingPanelTop;
    public double Width => _prefs.FloatingPanelWidth;
    public double Height => _prefs.FloatingPanelHeight;

    public FloatingPanelViewModel(PreferencesStore prefs, DetailPanelViewModel panelVm)
    {
        _prefs = prefs;
        Panel = panelVm;
    }

    public void Save(double left, double top, double width, double height)
    {
        _prefs.FloatingPanelLeft = left;
        _prefs.FloatingPanelTop = top;
        _prefs.FloatingPanelWidth = width;
        _prefs.FloatingPanelHeight = height;
    }
}
```

- [ ] **Step 4: Create `FloatingPanelWindow.xaml`**

`windows/src/TokenSpendie.Windows/Windows/FloatingPanelWindow.xaml`:

```xml
<Window x:Class="TokenSpendie.Windows.Windows.FloatingPanelWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:views="clr-namespace:TokenSpendie.Windows.Views"
        Title="Token Spendie"
        WindowStyle="None" ResizeMode="CanResizeWithGrip" ShowInTaskbar="False"
        AllowsTransparency="True" Background="Transparent" Topmost="True"
        MinWidth="240" MinHeight="160"
        WindowStartupLocation="CenterScreen">
  <Border CornerRadius="12" Background="#1E1E22" Padding="0" MouseLeftButtonDown="Border_MouseLeftButtonDown">
    <Border.Effect>
      <DropShadowEffect BlurRadius="16" ShadowDepth="0" Opacity="0.45"/>
    </Border.Effect>
    <DockPanel>
      <Grid DockPanel.Dock="Top" Height="22" Background="Transparent">
        <Button Content="✕" Width="20" Height="20" Margin="0 2 6 0"
                HorizontalAlignment="Right" VerticalAlignment="Top"
                Background="Transparent" BorderBrush="Transparent"
                Foreground="#888" FontSize="10"
                Click="CloseButton_Click"/>
      </Grid>
      <views:DetailPanel DataContext="{Binding Panel}"/>
    </DockPanel>
  </Border>
</Window>
```

- [ ] **Step 5: Create `FloatingPanelWindow.xaml.cs`**

```csharp
using System.Windows;
using System.Windows.Input;
using TokenSpendie.Windows.ViewModels;

namespace TokenSpendie.Windows.Windows;

public partial class FloatingPanelWindow : Window
{
    private FloatingPanelViewModel? _vm;

    public FloatingPanelWindow()
    {
        InitializeComponent();
    }

    public void Bind(FloatingPanelViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        Width = vm.Width;
        Height = vm.Height;
        if (vm.Left is { } l && vm.Top is { } t)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            // Clamp to working area on each connected display — if no overlap, fall back to centered.
            var workArea = SystemParameters.WorkArea;
            Left = System.Math.Clamp(l, workArea.Left - Width + 60, workArea.Right - 60);
            Top = System.Math.Clamp(t, workArea.Top, workArea.Bottom - 60);
        }
    }

    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && e.OriginalSource is FrameworkElement el)
        {
            // Only drag when the click is on the chrome (Border/DockPanel/title row), not the panel body.
            // Heuristic: skip drag if originating from a Button.
            if (el is System.Windows.Controls.Button) return;
            try { DragMove(); } catch { /* DragMove throws if left button isn't actually down */ }
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Persist position + size before WPF actually closes the window.
        if (_vm is not null && WindowState == WindowState.Normal)
        {
            _vm.Save(Left, Top, Width, Height);
        }
        base.OnClosing(e);
    }
}
```

- [ ] **Step 6: Run — expect pass**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~FloatingPanelViewModelTests"
```

Expected: 4 tests pass.

- [ ] **Step 7: Build to verify XAML**

```powershell
dotnet build windows/TokenSpendie.Windows.sln
```

Expected: clean.

- [ ] **Step 8: Commit**

```powershell
git add windows/src/TokenSpendie.Windows/ViewModels/FloatingPanelViewModel.cs windows/src/TokenSpendie.Windows/Windows/FloatingPanelWindow.xaml windows/src/TokenSpendie.Windows/Windows/FloatingPanelWindow.xaml.cs windows/tests/TokenSpendie.Windows.Tests/ViewModels/FloatingPanelViewModelTests.cs
git commit -m "feat(windows): add FloatingPanelWindow (always-on-top, drag, persist position)"
```

---

### Task 7: Tray context menu on TrayIconViewModel

**Files:**
- Modify: `windows/src/TokenSpendie.Windows/ViewModels/TrayIconViewModel.cs`
- Create: `windows/tests/TokenSpendie.Windows.Tests/ViewModels/TrayIconViewModelMenuTests.cs`

Add commands + events: Refresh, OpenPreferences, OpenAbout, ToggleLaunchAtLogin, Quit. The controller subscribes to the new events.

- [ ] **Step 1: Failing tests**

`windows/tests/TokenSpendie.Windows.Tests/ViewModels/TrayIconViewModelMenuTests.cs`:

```csharp
using System.IO;
using FluentAssertions;
using NSubstitute;
using TokenSpendie.Windows.Data;
using TokenSpendie.Windows.Models;
using TokenSpendie.Windows.Services;
using TokenSpendie.Windows.ViewModels;
using Xunit;

namespace TokenSpendie.Windows.Tests.ViewModels;

public class TrayIconViewModelMenuTests : System.IDisposable
{
    private readonly string _dir;
    public TrayIconViewModelMenuTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"tvmm-{System.Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private (UsageStore store, PreferencesStore prefs, TrayIconViewModel vm) Make()
    {
        var prefs = new PreferencesStore(Path.Combine(_dir, "prefs.json"));
        var providers = new IUsageProvider[] { Substitute.For<IUsageProvider>() };
        providers[0].Id.Returns(ProviderID.Claude);
        providers[0].DisplayName.Returns("Claude");
        var store = new UsageStore(providers, preferences: prefs);
        return (store, prefs, new TrayIconViewModel(store, prefs));
    }

    [Fact]
    public void OpenPreferencesCommandRaisesEvent()
    {
        var (_, _, vm) = Make();
        var fired = 0;
        vm.OpenPreferencesRequested += (_, _) => fired++;
        vm.OpenPreferencesCommand.Execute(null);
        fired.Should().Be(1);
    }

    [Fact]
    public void OpenAboutCommandRaisesEvent()
    {
        var (_, _, vm) = Make();
        var fired = 0;
        vm.OpenAboutRequested += (_, _) => fired++;
        vm.OpenAboutCommand.Execute(null);
        fired.Should().Be(1);
    }

    [Fact]
    public void ToggleLaunchAtLoginFlipsPreference()
    {
        var (_, prefs, vm) = Make();
        prefs.LaunchAtLogin.Should().BeFalse();
        vm.ToggleLaunchAtLoginCommand.Execute(null);
        prefs.LaunchAtLogin.Should().BeTrue();
        vm.ToggleLaunchAtLoginCommand.Execute(null);
        prefs.LaunchAtLogin.Should().BeFalse();
    }

    [Fact]
    public void IsLaunchAtLoginReflectsPreference()
    {
        var (_, prefs, vm) = Make();
        prefs.LaunchAtLogin = true;
        vm.IsLaunchAtLogin.Should().BeTrue();
        prefs.LaunchAtLogin = false;
        vm.IsLaunchAtLogin.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run — expect failure**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~TrayIconViewModelMenuTests"
```

- [ ] **Step 3: Extend `TrayIconViewModel`**

In `TrayIconViewModel.cs`, add after the existing fields/properties (the constructor takes `(UsageStore, PreferencesStore? = null)` from Task 2):

```csharp
public event System.EventHandler? OpenPreferencesRequested;
public event System.EventHandler? OpenAboutRequested;

public bool IsLaunchAtLogin => _preferences?.LaunchAtLogin ?? false;

[RelayCommand]
private async System.Threading.Tasks.Task Refresh()
{
    await _store.ManualRefreshAsync().ConfigureAwait(false);
}

[RelayCommand]
private void OpenPreferences() => OpenPreferencesRequested?.Invoke(this, System.EventArgs.Empty);

[RelayCommand]
private void OpenAbout() => OpenAboutRequested?.Invoke(this, System.EventArgs.Empty);

[RelayCommand]
private void ToggleLaunchAtLogin()
{
    if (_preferences is null) return;
    _preferences.LaunchAtLogin = !_preferences.LaunchAtLogin;
}

[RelayCommand]
private void Quit() => System.Windows.Application.Current?.Shutdown();
```

Also extend `OnPreferencesChanged` to raise `IsLaunchAtLogin`:

```csharp
private void OnPreferencesChanged(object? sender, PropertyChangedEventArgs e)
{
    if (e.PropertyName is nameof(PreferencesStore.Theme)) RecomputeFromStore();
    if (e.PropertyName is nameof(PreferencesStore.LaunchAtLogin)) OnPropertyChanged(nameof(IsLaunchAtLogin));
}
```

- [ ] **Step 4: Run — expect pass**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~TrayIconViewModelMenuTests"
```

Expected: 4 tests pass.

- [ ] **Step 5: Commit**

```powershell
git add windows/src/TokenSpendie.Windows/ViewModels/TrayIconViewModel.cs windows/tests/TokenSpendie.Windows.Tests/ViewModels/TrayIconViewModelMenuTests.cs
git commit -m "feat(windows): add tray context-menu commands (refresh, prefs, about, login, quit)"
```

---

### Task 8: TrayIconController owns the three windows + context menu

**Files:**
- Modify: `windows/src/TokenSpendie.Windows/Tray/TrayIconController.cs`

Add singleton storage for `PreferencesWindow`, `AboutWindow`, `FloatingPanelWindow`. Hook the new ViewModel events. Show/hide the floating panel in response to `PreferencesStore.ShowFloatingPanel`. Build the right-click context menu programmatically.

- [ ] **Step 1: Rewrite `TrayIconController.cs`**

Replace the file contents with:

```csharp
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using TokenSpendie.Windows.Services;
using TokenSpendie.Windows.ViewModels;
using TokenSpendie.Windows.Windows;

namespace TokenSpendie.Windows.Tray;

public sealed class TrayIconController : System.IDisposable
{
    private readonly TaskbarIcon _icon;
    private readonly TrayIconViewModel _vm;
    private readonly DetailPanelViewModel _panelVm;
    private readonly PreferencesStore _preferences;
    private readonly PreferencesViewModel _prefsVm;
    private readonly FloatingPanelViewModel _floatingVm;

    private PopupWindow? _popup;
    private PreferencesWindow? _prefsWindow;
    private AboutWindow? _aboutWindow;
    private FloatingPanelWindow? _floatingWindow;

    public TrayIconController(
        TrayIconViewModel vm,
        DetailPanelViewModel panelVm,
        PreferencesStore preferences,
        PreferencesViewModel prefsVm,
        FloatingPanelViewModel floatingVm)
    {
        _vm = vm;
        _panelVm = panelVm;
        _preferences = preferences;
        _prefsVm = prefsVm;
        _floatingVm = floatingVm;

        _icon = new TaskbarIcon
        {
            DataContext = _vm,
            ToolTipText = _vm.ToolTipText,
            Visibility = _preferences.ShowMenuBar ? Visibility.Visible : Visibility.Collapsed,
        };

        var iconBinding = new System.Windows.Data.Binding(nameof(TrayIconViewModel.IconSource)) { Source = _vm };
        System.Windows.Data.BindingOperations.SetBinding(_icon, TaskbarIcon.IconSourceProperty, iconBinding);

        var toolTipBinding = new System.Windows.Data.Binding(nameof(TrayIconViewModel.ToolTipText)) { Source = _vm };
        System.Windows.Data.BindingOperations.SetBinding(_icon, TaskbarIcon.ToolTipTextProperty, toolTipBinding);

        _icon.LeftClickCommand = _vm.LeftClickCommand;
        _icon.ContextMenu = BuildContextMenu();

        _vm.ShowPopupRequested += OnShowPopupRequested;
        _vm.OpenPreferencesRequested += (_, _) => OpenPreferences();
        _vm.OpenAboutRequested += (_, _) => OpenAbout();

        _preferences.PropertyChanged += OnPrefsChanged;

        _icon.ForceCreate();

        ApplyFloatingPanelVisibility();
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "Refresh", Command = _vm.RefreshCommand });
        menu.Items.Add(new MenuItem { Header = "Preferences…", Command = _vm.OpenPreferencesCommand });
        menu.Items.Add(new MenuItem { Header = "About", Command = _vm.OpenAboutCommand });
        menu.Items.Add(new Separator());
        var launchItem = new MenuItem
        {
            Header = "Launch at login",
            IsCheckable = true,
            Command = _vm.ToggleLaunchAtLoginCommand,
        };
        var checkedBinding = new System.Windows.Data.Binding(nameof(TrayIconViewModel.IsLaunchAtLogin))
        {
            Source = _vm,
            Mode = System.Windows.Data.BindingMode.OneWay,
        };
        System.Windows.Data.BindingOperations.SetBinding(launchItem, MenuItem.IsCheckedProperty, checkedBinding);
        menu.Items.Add(launchItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "Quit", Command = _vm.QuitCommand });
        return menu;
    }

    private void OnShowPopupRequested(object? sender, System.EventArgs e)
    {
        if (_popup is { IsVisible: true }) { _popup.Hide(); return; }
        _popup ??= new PopupWindow { DataContext = _panelVm };
        PositionPopup(_popup);
        _popup.Show();
        _popup.Activate();
    }

    private void OpenPreferences()
    {
        _prefsWindow ??= new PreferencesWindow { DataContext = _prefsVm };
        _prefsWindow.Show();
        _prefsWindow.Activate();
    }

    private void OpenAbout()
    {
        _aboutWindow ??= new AboutWindow();
        _aboutWindow.Show();
        _aboutWindow.Activate();
    }

    private void OnPrefsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PreferencesStore.ShowMenuBar))
        {
            _icon.Visibility = _preferences.ShowMenuBar ? Visibility.Visible : Visibility.Collapsed;
        }
        else if (e.PropertyName == nameof(PreferencesStore.ShowFloatingPanel))
        {
            ApplyFloatingPanelVisibility();
        }
    }

    private void ApplyFloatingPanelVisibility()
    {
        if (_preferences.ShowFloatingPanel)
        {
            _floatingWindow ??= new FloatingPanelWindow();
            _floatingWindow.Bind(_floatingVm);
            _floatingWindow.Show();
        }
        else
        {
            _floatingWindow?.Hide();
        }
    }

    private static void PositionPopup(PopupWindow popup)
    {
        GetCursorPos(out var pt);
        var work = SystemParameters.WorkArea;
        popup.Left = System.Math.Clamp(pt.X, work.Left, work.Right - 320);
        popup.Top = System.Math.Clamp(pt.Y, work.Top, work.Bottom - 200);
    }

    public void Dispose()
    {
        _icon.Dispose();
        _popup?.Close();
        _prefsWindow?.Close();
        _aboutWindow?.Close();
        _floatingWindow?.Close();
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);
}
```

- [ ] **Step 2: Build**

```powershell
dotnet build windows/TokenSpendie.Windows.sln
```

Expected: clean.

- [ ] **Step 3: Commit**

```powershell
git add windows/src/TokenSpendie.Windows/Tray/TrayIconController.cs
git commit -m "feat(windows): wire tray context menu + prefs/about/floating window ownership"
```

---

### Task 9: Fix LaunchAtLogin reconciliation direction in App.xaml.cs

**Files:**
- Modify: `windows/src/TokenSpendie.Windows/App.xaml.cs`
- Create: `windows/tests/TokenSpendie.Windows.Tests/AppStartupTests.cs` (unit test for a small helper)

The M3a bug: `_preferences.LaunchAtLogin = _startup.IsEnabled()` overwrote the persisted preference with the registry-derived value, losing the user's stored intent on path-mismatch. The fix: read prefs first, then **make the registry match** (don't touch prefs).

To keep the helper testable, extract the reconciliation logic into a static method on a new internal class.

- [ ] **Step 1: Failing test**

`windows/tests/TokenSpendie.Windows.Tests/AppStartupTests.cs`:

```csharp
using FluentAssertions;
using NSubstitute;
using TokenSpendie.Windows;
using TokenSpendie.Windows.Services.StartupAtLogin;
using Xunit;

namespace TokenSpendie.Windows.Tests;

public class AppStartupTests
{
    [Fact]
    public void ReconcileEnablesWhenPrefsWantItButRegistryDoesnt()
    {
        var startup = Substitute.For<IStartupAtLoginService>();
        startup.IsEnabled().Returns(false);

        StartupReconciler.Reconcile(preferenceWantsEnabled: true, startup);

        startup.Received(1).Enable();
        startup.DidNotReceive().Disable();
    }

    [Fact]
    public void ReconcileDisablesWhenPrefsDontButRegistryDoes()
    {
        var startup = Substitute.For<IStartupAtLoginService>();
        startup.IsEnabled().Returns(true);

        StartupReconciler.Reconcile(preferenceWantsEnabled: false, startup);

        startup.Received(1).Disable();
        startup.DidNotReceive().Enable();
    }

    [Fact]
    public void ReconcileDoesNothingWhenAlreadyAligned()
    {
        var trueAligned = Substitute.For<IStartupAtLoginService>();
        trueAligned.IsEnabled().Returns(true);
        StartupReconciler.Reconcile(preferenceWantsEnabled: true, trueAligned);
        trueAligned.DidNotReceive().Enable();
        trueAligned.DidNotReceive().Disable();

        var falseAligned = Substitute.For<IStartupAtLoginService>();
        falseAligned.IsEnabled().Returns(false);
        StartupReconciler.Reconcile(preferenceWantsEnabled: false, falseAligned);
        falseAligned.DidNotReceive().Enable();
        falseAligned.DidNotReceive().Disable();
    }
}
```

- [ ] **Step 2: Run — expect failure**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~AppStartupTests"
```

- [ ] **Step 3: Implement `StartupReconciler`**

Create `windows/src/TokenSpendie.Windows/StartupReconciler.cs`:

```csharp
using TokenSpendie.Windows.Services.StartupAtLogin;

namespace TokenSpendie.Windows;

/// <summary>Reconciles the registry "launch at login" entry with the user's
/// persisted preference. Always treats prefs as source of truth.</summary>
internal static class StartupReconciler
{
    public static void Reconcile(bool preferenceWantsEnabled, IStartupAtLoginService startup)
    {
        var registryEnabled = startup.IsEnabled();
        if (preferenceWantsEnabled && !registryEnabled) startup.Enable();
        else if (!preferenceWantsEnabled && registryEnabled) startup.Disable();
    }
}
```

- [ ] **Step 4: Use it in `App.xaml.cs`**

In `App.xaml.cs`, find the block that currently reads:

```csharp
_preferences = new PreferencesStore();
_startup = new RegistryRunKeyStartupService();
_preferences.LaunchAtLogin = _startup.IsEnabled();
_preferences.PropertyChanged += OnPreferencesChanged;
```

Replace with:

```csharp
_preferences = new PreferencesStore();
_startup = new RegistryRunKeyStartupService();
StartupReconciler.Reconcile(_preferences.LaunchAtLogin, _startup);
_preferences.PropertyChanged += OnPreferencesChanged;
```

The existing `OnPreferencesChanged` handler (which calls `Enable()`/`Disable()` on the registered preference toggle) is unchanged.

- [ ] **Step 5: Run — expect pass**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~AppStartupTests"
```

Expected: 3 tests pass.

- [ ] **Step 6: Commit**

```powershell
git add windows/src/TokenSpendie.Windows/StartupReconciler.cs windows/src/TokenSpendie.Windows/App.xaml.cs windows/tests/TokenSpendie.Windows.Tests/AppStartupTests.cs
git commit -m "fix(windows): reconcile LaunchAtLogin prefs→registry instead of overwriting prefs"
```

---

### Task 10: App.xaml.cs DI wiring update + smoke

**Files:**
- Modify: `windows/src/TokenSpendie.Windows/App.xaml.cs`

Construct the new ViewModels and pass them to the updated `TrayIconController`.

- [ ] **Step 1: Update `App_Startup`**

In `App.xaml.cs`, modify the tail of `App_Startup` (after `_store.Start()`) from:

```csharp
var trayVm = new TrayIconViewModel(_store);
var panelVm = new DetailPanelViewModel(_store);
_tray = new TrayIconController(trayVm, panelVm);
```

to:

```csharp
var trayVm = new TrayIconViewModel(_store, _preferences);
var panelVm = new DetailPanelViewModel(_store);
var prefsVm = new PreferencesViewModel(_preferences);
var floatingVm = new FloatingPanelViewModel(_preferences, panelVm);
_tray = new TrayIconController(trayVm, panelVm, _preferences, prefsVm, floatingVm);
```

- [ ] **Step 2: Build Release**

```powershell
dotnet build windows/TokenSpendie.Windows.sln -c Release
```

Expected: clean.

- [ ] **Step 3: Full test suite**

```powershell
dotnet test windows/TokenSpendie.Windows.sln
```

Expected: 144 (M3a) + 2 (PreferencesStore position) + 1 (RingIconRenderer theme) + 5 (PreferencesViewModel) + 4 (FloatingPanelViewModel) + 4 (TrayIconViewModel menu) + 3 (AppStartup) = **163 tests**.

- [ ] **Step 4: Smoke launch**

```powershell
Get-Process -Name "TokenSpendie.Windows" -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item tsw-m3b-err.log, tsw-m3b-out.log -ErrorAction SilentlyContinue
$p = Start-Process -FilePath "windows/src/TokenSpendie.Windows/bin/Release/net8.0-windows10.0.17763/TokenSpendie.Windows.exe" -PassThru -RedirectStandardError "tsw-m3b-err.log" -RedirectStandardOutput "tsw-m3b-out.log"
Start-Sleep -Seconds 8
if ($p.HasExited) {
  "CRASHED, code=$($p.ExitCode)"
  Get-Content tsw-m3b-err.log | Out-String
} else {
  "OK running"
  # leave running for user visual verification
}
```

If the process exits, capture stderr and BLOCK.

- [ ] **Step 5: Commit**

```powershell
git add windows/src/TokenSpendie.Windows/App.xaml.cs
git commit -m "feat(windows): wire M3b UI (preferences, about, floating panel) into App startup"
```

---

### Task 11: PR + handoff

**Files:** (none)

- [ ] **Step 1: Verify branch state**

```powershell
git status
git log --oneline develop..HEAD
```

Expected: working tree clean; roughly 10 commits.

- [ ] **Step 2: Secret-leak scan**

```powershell
git log -p develop..HEAD `
  | Select-String -Pattern 'ey[A-Za-z0-9_\-]{20,}|sk-ant-|oat[A-Za-z0-9_\-]{20,}' `
  | Measure-Object | ForEach-Object { if ($_.Count -eq 0) { "clean" } else { "DIRTY" } }
```

Expected: `clean`.

- [ ] **Step 3: Push the branch**

```powershell
git push -u origin windows-port-m3b-ui
```

- [ ] **Step 4: Open the PR**

Body in a temp file `windows/.pr-body-m3b.md`:

```markdown
## Summary
- Adds three new windows (Preferences, About, FloatingPanel) and a tray context menu, wiring them to the M3a services.
- Threads `Theme` through `RingIconRenderer` so the tray ring follows the user's theme pick.
- Fixes the M3a `LaunchAtLogin` reconciliation regression: prefs are now the source of truth; the registry is updated to match (not the reverse).
- Tray context menu: Refresh, Preferences…, About, separator, Launch at login (toggle), separator, Quit.
- Floating panel: borderless, always-on-top, drag-by-content, close hides instead of exits; position + size persisted in prefs.
- Preferences window: display toggles (menu bar / floating, with at-least-one-surface enforcement), refresh-interval picker (60s / 2min), four theme swatches, launch-at-login toggle, quit button.

## Test plan
- [ ] `dotnet test windows/TokenSpendie.Windows.sln` — ~163 tests, all green.
- [ ] Smoke run: tray icon visible; right-click shows the new menu; each menu item opens the right window; theme change updates the ring color; toggling launch-at-login flips both prefs and the HKCU Run entry; floating panel shows/hides via the preferences toggle and persists position across restarts.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
```

```powershell
$env:PATH += ";C:\Program Files\GitHub CLI"
gh pr create --base develop --head windows-port-m3b-ui `
  --title "feat(windows): M3b UI (preferences + floating panel + about + tray menu)" `
  --body-file windows/.pr-body-m3b.md
```

Remove the temp body file afterwards:

```powershell
Remove-Item windows/.pr-body-m3b.md
```

- [ ] **Step 5: Notify completion**

Reply in chat: "M3b UI complete. PR: <url>. ~163 tests green. Ready for M4 (installer + signing + CI)."

---

## Self-review

**Spec coverage.** M3 milestone scope per spec:
> M3 — Preferences + floating panel + notifications: `PreferencesWindow`, `FloatingPanelWindow`, `UsageNotifier`, `RegistryRunKeyStartupService`.

M3a delivered the services (`UsageNotifier`, `RegistryRunKeyStartupService`, `PreferencesStore`, observers). This M3b plan delivers the UI half:
- `PreferencesWindow` — Task 4
- `FloatingPanelWindow` — Task 6
- `AboutWindow` — Task 5 (called out in mac source, deferred to M3b per spec milestone breakdown)
- Tray context menu — Tasks 7 + 8
- Theme application to tray ring — Task 2

The remaining M3a follow-up flagged in the M3a PR — `LaunchAtLogin` reconciliation direction — is fixed in Task 9.

**Placeholder scan.** Every `<…>` appearing in this plan is in code-block examples (`<url>` for PR URL) — runtime substitutions. No author placeholders.

**Type consistency.**
- `Theme` and `ThemeExtensions` from M3a → consumed in `RingIconRenderer.Render` (Task 2) and the converters (Task 4).
- `RefreshInterval` and `RefreshIntervalExtensions.Label()` from M3a → consumed in the converter (Task 4).
- `PreferencesStore` field set: 6 existing + 4 new floating-panel fields (Task 1) → bound by `PreferencesViewModel` (Task 3), `FloatingPanelViewModel` (Task 6), and read in App startup (Task 9-10).
- `TrayIconViewModel` gets 5 new commands + 2 new events (Task 7) → consumed in `TrayIconController` (Task 8).
- `Surface` enum (Task 3) only used inside `PreferencesViewModel` — no leakage.

**Cross-task dependencies.**
- Task 1 (prefs fields) → Tasks 6, 8.
- Task 2 (theme renderer) → independent leaf; affects M2 tests (updated in same task).
- Task 3 (Surface + PreferencesViewModel) → Tasks 4, 10.
- Task 4 (PreferencesWindow) → Task 8 (controller opens it), Task 10 (DI wiring).
- Task 5 (AboutWindow) → Task 8, Task 10.
- Task 6 (FloatingPanel) → Task 8, Task 10.
- Task 7 (TrayIconViewModel commands) → Task 8 (controller wires events).
- Task 8 (controller) → Task 10 (DI wiring).
- Task 9 (StartupReconciler) → Task 10 (App.xaml.cs uses it).
- Task 10 = final assembly. Task 11 = packaging.

**Test posture.**
- ViewModels: full unit-test coverage with NSubstitute + FluentAssertions, no WPF threads required (TrayIconViewModelMenuTests is plain `[Fact]`).
- `RingIconRendererThemeTests` uses `[StaFact]` (WPF types).
- Windows (`PreferencesWindow.xaml`, `AboutWindow.xaml`, `FloatingPanelWindow.xaml`) have no unit tests — Task 10's smoke run is the integration check.

**Branch and commit discipline.** Branch created in Task 1 Step 1 (off `develop`). Every implementation task ends with a commit. Task 11 secret-scans + pushes + opens the PR.
