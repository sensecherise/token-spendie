# Windows Port — M1 Core (Data Layer) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Port the macOS Swift data layer (models + providers + credential reader + Gemini reader + headless smoke binary) to a Windows .NET 8 project, with full xUnit coverage. Produces a `TokenSpendie.Windows.exe` console binary that prints a snapshot for each provider it detects — proving the data path end-to-end before the WPF UI lands in M2.

**Architecture:** One .NET project (`TokenSpendie.Windows.csproj`) targeting `net8.0-windows`, `OutputType=Exe` for M1 (changes to `WinExe` when WPF lands in M2). One xUnit test project. Each Swift type maps to a C# record (immutable, value-equality). Each Swift `protocol` maps to a C# `interface`. The Swift `throws` error model maps to a small exception hierarchy. JSON parsing uses `System.Text.Json` with untyped `JsonDocument` for the credential and endpoint payloads (matches the Swift dictionary-walking style and tolerates unknown fields surfaced by the M0 spike). HTTP transport is `HttpClient` injected behind an `HttpMessageHandler` for tests.

**Tech Stack:** .NET 8, C# 12 (nullable + warnings-as-errors), xUnit, FluentAssertions, NSubstitute, `System.Text.Json`, `HttpClient` with `SocketsHttpHandler { PooledConnectionLifetime = 5min }`.

**Spec:** [`docs/superpowers/specs/2026-05-26-windows-port-design.md`](../specs/2026-05-26-windows-port-design.md). M0 spike findings: [`docs/superpowers/findings/2026-05-26-windows-creds-spike.md`](../findings/2026-05-26-windows-creds-spike.md) — load-bearing for Task 6 (Claude credentials path) and Tasks 10/11 (Gemini JSONL log format).

**Prerequisites:** Windows 11 host with .NET 8 SDK or newer (`dotnet --version` ≥ `8.0`). Claude Code installed and logged in (`%USERPROFILE%\.claude\.credentials.json` exists) for the Task 13 end-to-end smoke run. Gemini CLI install is optional but lets you verify the Gemini path.

**Branch:** create `windows-port-m1-core` off `windows-port-m0-spike` before the first commit. (Spike branch carries the findings + spec edits this plan depends on; merge order to `develop` is spike → m1.)

**Out of scope (deferred):**
- WPF UI, tray icon, popup, floating panel — M2.
- Preferences store, snapshot cache, usage notifier, launch-at-login — M3.
- Installer, code signing, CI — M4.

---

## File structure

| File | Purpose | Created in |
|---|---|---|
| `windows/Directory.Build.props` | Solution-wide build settings: `nullable`, warnings-as-errors, language version, common refs. | Task 1 |
| `windows/TokenSpendie.Windows.sln` | Solution file referencing the main + test projects. | Task 1 |
| `windows/src/TokenSpendie.Windows/TokenSpendie.Windows.csproj` | Main project. `net8.0-windows`, `OutputType=Exe` in M1. | Task 1 |
| `windows/src/TokenSpendie.Windows/Program.cs` | Headless entry point (stub in Task 1, filled in Task 12). | Task 1, 12 |
| `windows/tests/TokenSpendie.Windows.Tests/TokenSpendie.Windows.Tests.csproj` | xUnit project referencing the main project + FluentAssertions + NSubstitute. | Task 1 |
| `windows/tests/TokenSpendie.Windows.Tests/SmokeTests.cs` | Trivial passing test that proves the test runner works. | Task 1 |
| `windows/src/TokenSpendie.Windows/Models/UsageWindow.cs` | Single usage window (percent + resetsAt). | Task 2 |
| `windows/src/TokenSpendie.Windows/Models/ModelWeekly.cs` | Per-model weekly window. | Task 2 |
| `windows/src/TokenSpendie.Windows/Models/UsageSnapshot.cs` | Full reading (session + weekly + model weeklies + fetchedAt). | Task 2 |
| `windows/src/TokenSpendie.Windows/Models/ProviderID.cs` | Enum: `Claude`, `Gemini`. JSON-encodes as lowercase. | Task 3 |
| `windows/src/TokenSpendie.Windows/Models/ResetStyle.cs` | Enum: `Countdown`, `Date`. | Task 3 |
| `windows/src/TokenSpendie.Windows/Models/LabeledWindow.cs` | A `UsageWindow` plus row label/detail. | Task 3 |
| `windows/src/TokenSpendie.Windows/Models/ProviderSnapshot.cs` | Per-provider normalized snapshot. | Task 3 |
| `windows/src/TokenSpendie.Windows/Models/LoadState.cs` | UI state: Loading / Ok / Stale / Error. | Task 4 |
| `windows/src/TokenSpendie.Windows/Models/ProviderUsage.cs` | One panel-row's state. | Task 4 |
| `windows/src/TokenSpendie.Windows/Models/Errors.cs` | Exception hierarchies: `CredentialException`, `ProviderException`, `UsageException`. | Task 4 |
| `windows/src/TokenSpendie.Windows/Data/OAuthCredentials.cs` | OAuth-token record + `IsExpired`. | Task 5 |
| `windows/src/TokenSpendie.Windows/Data/OAuthCredentialsParser.cs` | Parses the `claudeAiOauth` JSON blob with the seconds-vs-milliseconds heuristic. | Task 5 |
| `windows/src/TokenSpendie.Windows/Data/ICredentialReader.cs` | Async credential-reader interface. | Task 6 |
| `windows/src/TokenSpendie.Windows/Data/ClaudeJsonFileReader.cs` | Reads `%USERPROFILE%\.claude\.credentials.json`. Concrete reader chosen by the M0 spike. Handles concurrent rewrite via `FileShare.ReadWrite` + retry. | Task 6 |
| `windows/src/TokenSpendie.Windows/Data/UsageDecoder.cs` | Decodes the `/api/oauth/usage` JSON payload. | Task 7 |
| `windows/src/TokenSpendie.Windows/Data/IClaudeUsageEndpoint.cs` | Endpoint interface for DI. | Task 8 |
| `windows/src/TokenSpendie.Windows/Data/EndpointUsageProvider.cs` | `HttpClient`-backed endpoint impl. | Task 8 |
| `windows/src/TokenSpendie.Windows/Data/IUsageProvider.cs` | Provider interface. | Task 9 |
| `windows/src/TokenSpendie.Windows/Data/ClaudeProvider.cs` | Composes credential reader + endpoint + retry-on-401. | Task 9 |
| `windows/src/TokenSpendie.Windows/Data/GeminiUsageReader.cs` | Reads `~/.gemini/tmp/<project>/chats/session-*.jsonl` (Windows format from M0 spike). | Task 10 |
| `windows/src/TokenSpendie.Windows/Data/GeminiProvider.cs` | Maps `requestsToday` to a `Daily` `ProviderSnapshot`. | Task 11 |
| `windows/src/TokenSpendie.Windows/Program.cs` | Replaces Task-1 stub with the snapshot-printing CLI. | Task 12 |

Tests live one-for-one alongside the modules they cover, under `windows/tests/TokenSpendie.Windows.Tests/`, mirroring the `src/` folder layout.

---

## Conventions used in this plan

- Every step is a single command or a single edit; expected output is included so you know whether it worked.
- Tests come before implementations. Run each new test once to confirm it fails (red), then implement, then run again (green).
- Build command for the whole solution: `dotnet build windows/TokenSpendie.Windows.sln`. Test command: `dotnet test windows/TokenSpendie.Windows.sln`. Pin a single test with `dotnet test --filter "FullyQualifiedName~<TestName>"`.
- Commit at the end of each task. Commit messages use the project's existing convention (`feat:`, `test:`, `chore:`, `spec:`, `spike:` — pick the one that fits).
- C# records use positional syntax for value types (`public record UsageWindow(double Percent, DateTimeOffset? ResetsAt)`) — concise, immutable, value-equality for free, JSON-friendly via `System.Text.Json`.
- Dates: C# `DateTimeOffset` (not `DateTime`) for all timestamps. It carries timezone info and round-trips through ISO-8601 cleanly.
- The Swift `Calendar` injection becomes a `TimeZoneInfo` parameter in C# (`TimeZoneInfo.Utc` in tests, `TimeZoneInfo.Local` by default).
- All JSON serialization uses one shared `JsonSerializerOptions` instance configured for camelCase property names and `JsonStringEnumConverter` with `JsonNamingPolicy.CamelCase`, so the on-disk JSON shape matches the Swift `Codable` output byte-for-byte.

---

### Task 1: Project scaffolding

**Files:**
- Create: `windows/Directory.Build.props`
- Create: `windows/TokenSpendie.Windows.sln`
- Create: `windows/src/TokenSpendie.Windows/TokenSpendie.Windows.csproj`
- Create: `windows/src/TokenSpendie.Windows/Program.cs`
- Create: `windows/tests/TokenSpendie.Windows.Tests/TokenSpendie.Windows.Tests.csproj`
- Create: `windows/tests/TokenSpendie.Windows.Tests/SmokeTests.cs`

- [ ] **Step 1: Create the branch**

```powershell
git fetch origin
git checkout -b windows-port-m1-core windows-port-m0-spike
```

Expected: `Switched to a new branch 'windows-port-m1-core'`.

- [ ] **Step 2: Create `windows/Directory.Build.props`**

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <LangVersion>12</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <NoWarn>$(NoWarn);CA1416</NoWarn>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  </PropertyGroup>
</Project>
```

(`CA1416` is suppressed because `net8.0-windows` triggers it on members the data layer doesn't actually call — the suppression is removed in M2 when the WPF code that legitimately uses Windows-only APIs lands.)

- [ ] **Step 3: Create `windows/src/TokenSpendie.Windows/TokenSpendie.Windows.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <RootNamespace>TokenSpendie.Windows</RootNamespace>
    <AssemblyName>TokenSpendie.Windows</AssemblyName>
  </PropertyGroup>
</Project>
```

- [ ] **Step 4: Create `windows/src/TokenSpendie.Windows/Program.cs`** (stub — filled in Task 12)

```csharp
namespace TokenSpendie.Windows;

internal static class Program
{
    public static int Main(string[] args)
    {
        Console.WriteLine("TokenSpendie.Windows — M1 scaffold OK");
        return 0;
    }
}
```

- [ ] **Step 5: Create `windows/tests/TokenSpendie.Windows.Tests/TokenSpendie.Windows.Tests.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <RootNamespace>TokenSpendie.Windows.Tests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.10.0" />
    <PackageReference Include="xunit" Version="2.9.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
    <PackageReference Include="NSubstitute" Version="5.1.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\TokenSpendie.Windows\TokenSpendie.Windows.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 6: Create `windows/tests/TokenSpendie.Windows.Tests/SmokeTests.cs`**

```csharp
using FluentAssertions;
using Xunit;

namespace TokenSpendie.Windows.Tests;

public class SmokeTests
{
    [Fact]
    public void TestRunnerWorks()
    {
        (2 + 2).Should().Be(4);
    }
}
```

- [ ] **Step 7: Create the solution and add the two projects**

```powershell
dotnet new sln --name TokenSpendie.Windows --output windows
dotnet sln windows/TokenSpendie.Windows.sln add `
  windows/src/TokenSpendie.Windows/TokenSpendie.Windows.csproj `
  windows/tests/TokenSpendie.Windows.Tests/TokenSpendie.Windows.Tests.csproj
```

Expected: two "Project ... added to the solution." lines.

- [ ] **Step 8: Build + test**

```powershell
dotnet build windows/TokenSpendie.Windows.sln
dotnet test  windows/TokenSpendie.Windows.sln
```

Expected: `Build succeeded`, then test output `Passed! - Failed: 0, Passed: 1`.

- [ ] **Step 9: Commit**

```powershell
git add windows .gitignore 2>$null; git add windows
git commit -m "feat(windows): scaffold solution, main project, test project (M1 Task 1)"
```

(The first `git add windows` may fail if `windows/` is gitignored; if so, use `git add -f windows`. Confirm `git status` shows the new files staged.)

Expected: one commit, ~7 new files.

---

### Task 2: Models — UsageWindow, ModelWeekly, UsageSnapshot

**Files:**
- Create: `windows/src/TokenSpendie.Windows/Models/UsageWindow.cs`
- Create: `windows/src/TokenSpendie.Windows/Models/ModelWeekly.cs`
- Create: `windows/src/TokenSpendie.Windows/Models/UsageSnapshot.cs`
- Create: `windows/tests/TokenSpendie.Windows.Tests/Models/UsageSnapshotTests.cs`

- [ ] **Step 1: Write the failing tests**

`windows/tests/TokenSpendie.Windows.Tests/Models/UsageSnapshotTests.cs`:

```csharp
using System;
using System.Text.Json;
using FluentAssertions;
using TokenSpendie.Windows.Models;
using Xunit;

namespace TokenSpendie.Windows.Tests.Models;

public class UsageSnapshotTests
{
    private static JsonSerializerOptions Options() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void UsageWindowRoundTrips()
    {
        var resetsAt = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var window = new UsageWindow(Percent: 47.5, ResetsAt: resetsAt);

        var json = JsonSerializer.Serialize(window, Options());
        var deserialized = JsonSerializer.Deserialize<UsageWindow>(json, Options());

        deserialized.Should().Be(window);
    }

    [Fact]
    public void UsageWindowAllowsNullResetsAt()
    {
        var window = new UsageWindow(Percent: 10, ResetsAt: null);
        var json = JsonSerializer.Serialize(window, Options());
        JsonSerializer.Deserialize<UsageWindow>(json, Options()).Should().Be(window);
    }

    [Fact]
    public void UsageSnapshotRoundTrips()
    {
        var snapshot = new UsageSnapshot(
            Session: new UsageWindow(47, DateTimeOffset.FromUnixTimeSeconds(100)),
            Weekly: new UsageWindow(31, DateTimeOffset.FromUnixTimeSeconds(200)),
            ModelWeeklies: new[]
            {
                new ModelWeekly("Opus", new UsageWindow(62, null)),
            },
            FetchedAt: DateTimeOffset.FromUnixTimeSeconds(999));

        var json = JsonSerializer.Serialize(snapshot, Options());
        JsonSerializer.Deserialize<UsageSnapshot>(json, Options()).Should().Be(snapshot);
    }

    [Fact]
    public void UsageSnapshotValueEqualityComparesByContents()
    {
        var a = new UsageSnapshot(
            new UsageWindow(1, null), new UsageWindow(2, null),
            Array.Empty<ModelWeekly>(), DateTimeOffset.FromUnixTimeSeconds(0));
        var b = a with { };
        a.Should().Be(b);
    }
}
```

- [ ] **Step 2: Run the test — expect failure**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~UsageSnapshotTests"
```

Expected: build failure (`UsageWindow`, `ModelWeekly`, `UsageSnapshot` not found in `TokenSpendie.Windows.Models`).

- [ ] **Step 3: Implement `UsageWindow`**

`windows/src/TokenSpendie.Windows/Models/UsageWindow.cs`:

```csharp
namespace TokenSpendie.Windows.Models;

/// <summary>A single rate-limit window (session or weekly).</summary>
public record UsageWindow(double Percent, DateTimeOffset? ResetsAt);
```

- [ ] **Step 4: Implement `ModelWeekly`**

`windows/src/TokenSpendie.Windows/Models/ModelWeekly.cs`:

```csharp
namespace TokenSpendie.Windows.Models;

public record ModelWeekly(string Model, UsageWindow Window);
```

- [ ] **Step 5: Implement `UsageSnapshot`**

`windows/src/TokenSpendie.Windows/Models/UsageSnapshot.cs`:

```csharp
namespace TokenSpendie.Windows.Models;

public record UsageSnapshot(
    UsageWindow Session,
    UsageWindow Weekly,
    IReadOnlyList<ModelWeekly> ModelWeeklies,
    DateTimeOffset FetchedAt)
{
    public virtual bool Equals(UsageSnapshot? other) =>
        other is not null
        && Session == other.Session
        && Weekly == other.Weekly
        && ModelWeeklies.SequenceEqual(other.ModelWeeklies)
        && FetchedAt == other.FetchedAt;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Session);
        hash.Add(Weekly);
        foreach (var model in ModelWeeklies) hash.Add(model);
        hash.Add(FetchedAt);
        return hash.ToHashCode();
    }
}
```

(The custom `Equals`/`GetHashCode` overrides default record value-equality for the `IReadOnlyList<>` member — default record equality on a list would compare references, so two structurally-equal snapshots from JSON round-trips would be unequal.)

- [ ] **Step 6: Run the tests — expect pass**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~UsageSnapshotTests"
```

Expected: `Passed! - Failed: 0, Passed: 4`.

- [ ] **Step 7: Commit**

```powershell
git add windows/src/TokenSpendie.Windows/Models windows/tests/TokenSpendie.Windows.Tests/Models
git commit -m "feat(windows): add UsageWindow, ModelWeekly, UsageSnapshot models"
```

---

### Task 3: Models — ProviderID, ResetStyle, LabeledWindow, ProviderSnapshot

**Files:**
- Create: `windows/src/TokenSpendie.Windows/Models/ProviderID.cs`
- Create: `windows/src/TokenSpendie.Windows/Models/ResetStyle.cs`
- Create: `windows/src/TokenSpendie.Windows/Models/LabeledWindow.cs`
- Create: `windows/src/TokenSpendie.Windows/Models/ProviderSnapshot.cs`
- Create: `windows/tests/TokenSpendie.Windows.Tests/Models/ProviderSnapshotTests.cs`

- [ ] **Step 1: Write the failing tests**

`windows/tests/TokenSpendie.Windows.Tests/Models/ProviderSnapshotTests.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using TokenSpendie.Windows.Models;
using Xunit;

namespace TokenSpendie.Windows.Tests.Models;

public class ProviderSnapshotTests
{
    private static JsonSerializerOptions Options()
    {
        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        opts.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return opts;
    }

    [Fact]
    public void ProviderIDEncodesAsCamelCaseString()
    {
        JsonSerializer.Serialize(ProviderID.Claude, Options()).Should().Be("\"claude\"");
        JsonSerializer.Serialize(ProviderID.Gemini, Options()).Should().Be("\"gemini\"");
    }

    [Fact]
    public void ProviderIDDecodesFromCamelCase()
    {
        JsonSerializer.Deserialize<ProviderID>("\"claude\"", Options()).Should().Be(ProviderID.Claude);
        JsonSerializer.Deserialize<ProviderID>("\"gemini\"", Options()).Should().Be(ProviderID.Gemini);
    }

    [Fact]
    public void ResetStyleEncodesAsCamelCase()
    {
        JsonSerializer.Serialize(ResetStyle.Countdown, Options()).Should().Be("\"countdown\"");
        JsonSerializer.Serialize(ResetStyle.Date, Options()).Should().Be("\"date\"");
    }

    [Fact]
    public void ProviderSnapshotRoundTripsWithModelWeekly()
    {
        var headline = new LabeledWindow(
            Label: "Session", Detail: "5-hour window",
            ResetStyle: ResetStyle.Countdown,
            Window: new UsageWindow(47, DateTimeOffset.FromUnixTimeSeconds(100)));
        var weekly = new LabeledWindow(
            Label: "Weekly", Detail: "all models",
            ResetStyle: ResetStyle.Date,
            Window: new UsageWindow(31, DateTimeOffset.FromUnixTimeSeconds(200)));

        var snapshot = new ProviderSnapshot(
            Id: ProviderID.Claude, Plan: "Max",
            Headline: headline, Windows: new[] { headline, weekly },
            FetchedAt: DateTimeOffset.FromUnixTimeSeconds(999), Note: null);

        var json = JsonSerializer.Serialize(snapshot, Options());
        JsonSerializer.Deserialize<ProviderSnapshot>(json, Options()).Should().Be(snapshot);
    }

    [Fact]
    public void ProviderSnapshotRoundTripsWithNoteAndNullPlan()
    {
        var headline = new LabeledWindow(
            Label: "Daily", Detail: "≈3 of 1000 requests",
            ResetStyle: ResetStyle.Countdown,
            Window: new UsageWindow(0.3, DateTimeOffset.FromUnixTimeSeconds(1)));
        var snapshot = new ProviderSnapshot(
            Id: ProviderID.Gemini, Plan: null, Headline: headline,
            Windows: new[] { headline },
            FetchedAt: DateTimeOffset.FromUnixTimeSeconds(0),
            Note: "estimate · counted from local logs");

        var json = JsonSerializer.Serialize(snapshot, Options());
        JsonSerializer.Deserialize<ProviderSnapshot>(json, Options()).Should().Be(snapshot);
    }
}
```

- [ ] **Step 2: Run — expect failure**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~ProviderSnapshotTests"
```

Expected: build failure (types not found).

- [ ] **Step 3: Implement `ProviderID`**

`windows/src/TokenSpendie.Windows/Models/ProviderID.cs`:

```csharp
namespace TokenSpendie.Windows.Models;

public enum ProviderID
{
    Claude,
    Gemini,
}
```

- [ ] **Step 4: Implement `ResetStyle`**

`windows/src/TokenSpendie.Windows/Models/ResetStyle.cs`:

```csharp
namespace TokenSpendie.Windows.Models;

public enum ResetStyle
{
    Countdown,
    Date,
}
```

- [ ] **Step 5: Implement `LabeledWindow`**

`windows/src/TokenSpendie.Windows/Models/LabeledWindow.cs`:

```csharp
namespace TokenSpendie.Windows.Models;

public record LabeledWindow(
    string Label,
    string Detail,
    ResetStyle ResetStyle,
    UsageWindow Window);
```

- [ ] **Step 6: Implement `ProviderSnapshot`** (override list equality the same way as `UsageSnapshot`)

`windows/src/TokenSpendie.Windows/Models/ProviderSnapshot.cs`:

```csharp
namespace TokenSpendie.Windows.Models;

public record ProviderSnapshot(
    ProviderID Id,
    string? Plan,
    LabeledWindow Headline,
    IReadOnlyList<LabeledWindow> Windows,
    DateTimeOffset FetchedAt,
    string? Note = null)
{
    public virtual bool Equals(ProviderSnapshot? other) =>
        other is not null
        && Id == other.Id
        && Plan == other.Plan
        && Headline == other.Headline
        && Windows.SequenceEqual(other.Windows)
        && FetchedAt == other.FetchedAt
        && Note == other.Note;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id); hash.Add(Plan); hash.Add(Headline);
        foreach (var window in Windows) hash.Add(window);
        hash.Add(FetchedAt); hash.Add(Note);
        return hash.ToHashCode();
    }
}
```

- [ ] **Step 7: Run — expect pass**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~ProviderSnapshotTests"
```

Expected: `Passed! - Failed: 0, Passed: 5`.

- [ ] **Step 8: Commit**

```powershell
git add windows/src/TokenSpendie.Windows/Models windows/tests/TokenSpendie.Windows.Tests/Models
git commit -m "feat(windows): add ProviderID, ResetStyle, LabeledWindow, ProviderSnapshot"
```

---

### Task 4: Models — LoadState, ProviderUsage, errors

**Files:**
- Create: `windows/src/TokenSpendie.Windows/Models/LoadState.cs`
- Create: `windows/src/TokenSpendie.Windows/Models/ProviderUsage.cs`
- Create: `windows/src/TokenSpendie.Windows/Models/Errors.cs`
- Create: `windows/tests/TokenSpendie.Windows.Tests/Models/LoadStateTests.cs`
- Create: `windows/tests/TokenSpendie.Windows.Tests/Models/ErrorsTests.cs`

- [ ] **Step 1: Write the failing tests**

`windows/tests/TokenSpendie.Windows.Tests/Models/LoadStateTests.cs`:

```csharp
using FluentAssertions;
using TokenSpendie.Windows.Models;
using Xunit;

namespace TokenSpendie.Windows.Tests.Models;

public class LoadStateTests
{
    [Fact]
    public void LoadStateValueEquality()
    {
        LoadState.Loading.Should().Be(LoadState.Loading);
        LoadState.Ok.Should().Be(LoadState.Ok);
        LoadState.Stale.Should().Be(LoadState.Stale);
        LoadState.Error(UsageErrorKind.Network).Should().Be(LoadState.Error(UsageErrorKind.Network));
        LoadState.Error(UsageErrorKind.Network).Should().NotBe(LoadState.Error(UsageErrorKind.BadResponse));
        LoadState.Loading.Should().NotBe(LoadState.Ok);
    }

    [Fact]
    public void ProviderUsageStateIsMutable()
    {
        var usage = new ProviderUsage(ProviderID.Claude, "Claude")
        {
            State = LoadState.Loading,
        };
        usage.State = LoadState.Ok;
        usage.State.Should().Be(LoadState.Ok);
    }

    [Fact]
    public void ProviderUsageDistinguishesByState()
    {
        var a = new ProviderUsage(ProviderID.Claude, "Claude") { State = LoadState.Loading };
        var b = new ProviderUsage(ProviderID.Claude, "Claude") { State = LoadState.Ok };
        a.Equals(b).Should().BeFalse();
    }
}
```

`windows/tests/TokenSpendie.Windows.Tests/Models/ErrorsTests.cs`:

```csharp
using FluentAssertions;
using TokenSpendie.Windows.Models;
using Xunit;

namespace TokenSpendie.Windows.Tests.Models;

public class ErrorsTests
{
    [Fact]
    public void CredentialExceptionsCarryKind()
    {
        new CredentialNotFoundException().Kind.Should().Be(CredentialErrorKind.NotFound);
        new CredentialAccessDeniedException().Kind.Should().Be(CredentialErrorKind.AccessDenied);
        new CredentialMalformedException("bad json").Kind.Should().Be(CredentialErrorKind.Malformed);
    }

    [Fact]
    public void ProviderExceptionsCarryKindAndRetryAfter()
    {
        new ProviderUnauthorizedException().Kind.Should().Be(ProviderErrorKind.Unauthorized);
        new ProviderNetworkException(new System.Net.Http.HttpRequestException("offline"))
            .Kind.Should().Be(ProviderErrorKind.Network);
        new ProviderBadResponseException("bad").Kind.Should().Be(ProviderErrorKind.BadResponse);

        var rateLimited = new ProviderRateLimitedException(retryAfter: System.TimeSpan.FromSeconds(30));
        rateLimited.Kind.Should().Be(ProviderErrorKind.RateLimited);
        rateLimited.RetryAfter.Should().Be(System.TimeSpan.FromSeconds(30));
    }
}
```

- [ ] **Step 2: Run — expect failure**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~LoadStateTests|FullyQualifiedName~ErrorsTests"
```

Expected: build failure.

- [ ] **Step 3: Implement the error layer**

`windows/src/TokenSpendie.Windows/Models/Errors.cs`:

```csharp
namespace TokenSpendie.Windows.Models;

// --- Credential errors --------------------------------------------------------

public enum CredentialErrorKind { NotFound, AccessDenied, Malformed }

public abstract class CredentialException : Exception
{
    public abstract CredentialErrorKind Kind { get; }
    protected CredentialException(string message, Exception? inner = null)
        : base(message, inner) { }
}

public sealed class CredentialNotFoundException : CredentialException
{
    public override CredentialErrorKind Kind => CredentialErrorKind.NotFound;
    public CredentialNotFoundException() : base("Claude credentials not found.") { }
}

public sealed class CredentialAccessDeniedException : CredentialException
{
    public override CredentialErrorKind Kind => CredentialErrorKind.AccessDenied;
    public CredentialAccessDeniedException(Exception? inner = null)
        : base("Credential file could not be read (ACL).", inner) { }
}

public sealed class CredentialMalformedException : CredentialException
{
    public override CredentialErrorKind Kind => CredentialErrorKind.Malformed;
    public CredentialMalformedException(string detail, Exception? inner = null)
        : base($"Credential JSON is malformed: {detail}", inner) { }
}

// --- Provider errors ----------------------------------------------------------

public enum ProviderErrorKind { Unauthorized, Network, BadResponse, RateLimited }

public abstract class ProviderException : Exception
{
    public abstract ProviderErrorKind Kind { get; }
    protected ProviderException(string message, Exception? inner = null)
        : base(message, inner) { }
}

public sealed class ProviderUnauthorizedException : ProviderException
{
    public override ProviderErrorKind Kind => ProviderErrorKind.Unauthorized;
    public ProviderUnauthorizedException() : base("Endpoint returned 401.") { }
}

public sealed class ProviderNetworkException : ProviderException
{
    public override ProviderErrorKind Kind => ProviderErrorKind.Network;
    public ProviderNetworkException(Exception inner)
        : base("Network transport failure.", inner) { }
}

public sealed class ProviderBadResponseException : ProviderException
{
    public override ProviderErrorKind Kind => ProviderErrorKind.BadResponse;
    public ProviderBadResponseException(string detail, Exception? inner = null)
        : base($"Bad response: {detail}", inner) { }
}

public sealed class ProviderRateLimitedException : ProviderException
{
    public override ProviderErrorKind Kind => ProviderErrorKind.RateLimited;
    public TimeSpan? RetryAfter { get; }
    public ProviderRateLimitedException(TimeSpan? retryAfter)
        : base("Endpoint returned 429.") => RetryAfter = retryAfter;
}

// --- User-facing usage errors (consumed by LoadState) -------------------------

public enum UsageErrorKind
{
    ClaudeCodeNotFound,
    CredentialAccessDenied,
    LoginExpired,
    Network,
    BadResponse,
}
```

- [ ] **Step 4: Implement `LoadState`**

`windows/src/TokenSpendie.Windows/Models/LoadState.cs`:

```csharp
namespace TokenSpendie.Windows.Models;

public abstract record LoadState
{
    public sealed record LoadingState : LoadState;
    public sealed record OkState : LoadState;
    public sealed record StaleState : LoadState;
    public sealed record ErrorState(UsageErrorKind Kind) : LoadState;

    public static readonly LoadState Loading = new LoadingState();
    public static readonly LoadState Ok = new OkState();
    public static readonly LoadState Stale = new StaleState();
    public static LoadState Error(UsageErrorKind kind) => new ErrorState(kind);
}
```

- [ ] **Step 5: Implement `ProviderUsage`**

`windows/src/TokenSpendie.Windows/Models/ProviderUsage.cs`:

```csharp
namespace TokenSpendie.Windows.Models;

public sealed record ProviderUsage(ProviderID Id, string DisplayName)
{
    public LoadState State { get; set; } = LoadState.Loading;
    public ProviderSnapshot? Snapshot { get; set; }
}
```

(Mutable `State`/`Snapshot` matches the Swift `var` semantics. The constructor-supplied `Id` + `DisplayName` are immutable.)

- [ ] **Step 6: Run — expect pass**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~LoadStateTests|FullyQualifiedName~ErrorsTests"
```

Expected: `Passed! - Failed: 0, Passed: 5`.

- [ ] **Step 7: Commit**

```powershell
git add windows/src/TokenSpendie.Windows/Models windows/tests/TokenSpendie.Windows.Tests/Models
git commit -m "feat(windows): add LoadState, ProviderUsage, error hierarchies"
```

---

### Task 5: OAuthCredentials + OAuthCredentialsParser

**Files:**
- Create: `windows/src/TokenSpendie.Windows/Data/OAuthCredentials.cs`
- Create: `windows/src/TokenSpendie.Windows/Data/OAuthCredentialsParser.cs`
- Create: `windows/tests/TokenSpendie.Windows.Tests/Data/OAuthCredentialsParserTests.cs`

The M0 spike confirmed the Windows JSON shape matches mac with three extra advisory fields (`scopes`, `subscriptionType`, `rateLimitTier`) that the parser ignores. `expiresAt` is in milliseconds; the same seconds-vs-milliseconds heuristic the Swift parser uses works without change.

- [ ] **Step 1: Write the failing tests**

`windows/tests/TokenSpendie.Windows.Tests/Data/OAuthCredentialsParserTests.cs`:

```csharp
using System;
using System.IO;
using System.Text;
using FluentAssertions;
using TokenSpendie.Windows.Data;
using TokenSpendie.Windows.Models;
using Xunit;

namespace TokenSpendie.Windows.Tests.Data;

public class OAuthCredentialsParserTests
{
    [Fact]
    public void ParsesSecondsExpiry()
    {
        var json = """{"claudeAiOauth":{"accessToken":"abc","refreshToken":"ref","expiresAt":1700000000}}""";
        var creds = OAuthCredentialsParser.Parse(Encoding.UTF8.GetBytes(json));
        creds.AccessToken.Should().Be("abc");
        creds.RefreshToken.Should().Be("ref");
        creds.ExpiresAt.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));
    }

    [Fact]
    public void ParsesMillisecondsExpiry()
    {
        var json = """{"claudeAiOauth":{"accessToken":"abc","expiresAt":1700000000000}}""";
        var creds = OAuthCredentialsParser.Parse(Encoding.UTF8.GetBytes(json));
        creds.ExpiresAt.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));
        creds.RefreshToken.Should().BeNull();
    }

    [Fact]
    public void ToleratesExtraWindowsFields()
    {
        // From the M0 spike: scopes, subscriptionType, rateLimitTier appear on Windows.
        var json = """
        {"claudeAiOauth":{
          "accessToken":"abc","refreshToken":"ref","expiresAt":1779844385628,
          "scopes":["user:profile","user:inference"],
          "subscriptionType":"max","rateLimitTier":"some-tier-string"
        }}
        """;
        var creds = OAuthCredentialsParser.Parse(Encoding.UTF8.GetBytes(json));
        creds.AccessToken.Should().Be("abc");
        creds.RefreshToken.Should().Be("ref");
    }

    [Fact]
    public void MissingAccessTokenThrowsMalformed()
    {
        var json = """{"claudeAiOauth":{"refreshToken":"ref"}}""";
        Action act = () => OAuthCredentialsParser.Parse(Encoding.UTF8.GetBytes(json));
        act.Should().Throw<CredentialMalformedException>();
    }

    [Fact]
    public void EmptyAccessTokenThrowsMalformed()
    {
        var json = """{"claudeAiOauth":{"accessToken":""}}""";
        Action act = () => OAuthCredentialsParser.Parse(Encoding.UTF8.GetBytes(json));
        act.Should().Throw<CredentialMalformedException>();
    }

    [Fact]
    public void GarbageThrowsMalformed()
    {
        Action act = () => OAuthCredentialsParser.Parse(Encoding.UTF8.GetBytes("not json"));
        act.Should().Throw<CredentialMalformedException>();
    }

    [Fact]
    public void IsExpiredComparesAgainstNow()
    {
        var past = new OAuthCredentials("a", null, DateTimeOffset.FromUnixTimeSeconds(100));
        past.IsExpired(DateTimeOffset.FromUnixTimeSeconds(200)).Should().BeTrue();
        past.IsExpired(DateTimeOffset.FromUnixTimeSeconds(50)).Should().BeFalse();

        var noExpiry = new OAuthCredentials("a", null, null);
        noExpiry.IsExpired(DateTimeOffset.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void ParsesM0SanitizedFixture()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..",
            "docs", "superpowers", "findings", "fixtures",
            "claude-credentials-sanitized.json");
        fixturePath = Path.GetFullPath(fixturePath);
        File.Exists(fixturePath).Should().BeTrue($"fixture must exist at {fixturePath}");
        var bytes = File.ReadAllBytes(fixturePath);
        var creds = OAuthCredentialsParser.Parse(bytes);
        // Sanitized fixture has "<redacted>" as access token — non-empty, so parse succeeds.
        creds.AccessToken.Should().Be("<redacted>");
        creds.RefreshToken.Should().Be("<redacted>");
        // expiresAt = 1779844385628 → milliseconds → 2026-05-30 UTC.
        creds.ExpiresAt!.Value.Year.Should().Be(2026);
    }
}
```

- [ ] **Step 2: Run — expect failure**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~OAuthCredentialsParserTests"
```

Expected: build failure (`OAuthCredentials`, `OAuthCredentialsParser` not found).

- [ ] **Step 3: Implement `OAuthCredentials`**

`windows/src/TokenSpendie.Windows/Data/OAuthCredentials.cs`:

```csharp
namespace TokenSpendie.Windows.Data;

public record OAuthCredentials(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset? ExpiresAt)
{
    public bool IsExpired(DateTimeOffset now) =>
        ExpiresAt is { } expires && now >= expires;
}
```

- [ ] **Step 4: Implement `OAuthCredentialsParser`**

`windows/src/TokenSpendie.Windows/Data/OAuthCredentialsParser.cs`:

```csharp
using System.Text.Json;
using TokenSpendie.Windows.Models;

namespace TokenSpendie.Windows.Data;

public static class OAuthCredentialsParser
{
    public static OAuthCredentials Parse(ReadOnlySpan<byte> utf8Json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(utf8Json.ToArray());
        }
        catch (JsonException ex)
        {
            throw new CredentialMalformedException("not JSON", ex);
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth)
                || oauth.ValueKind != JsonValueKind.Object)
            {
                throw new CredentialMalformedException("claudeAiOauth field missing");
            }

            if (!oauth.TryGetProperty("accessToken", out var atProp)
                || atProp.ValueKind != JsonValueKind.String
                || string.IsNullOrEmpty(atProp.GetString()))
            {
                throw new CredentialMalformedException("accessToken missing or empty");
            }

            var accessToken = atProp.GetString()!;
            var refreshToken = oauth.TryGetProperty("refreshToken", out var rt)
                && rt.ValueKind == JsonValueKind.String
                    ? rt.GetString()
                    : null;

            DateTimeOffset? expiresAt = null;
            if (oauth.TryGetProperty("expiresAt", out var exp)
                && exp.ValueKind == JsonValueKind.Number
                && exp.TryGetDouble(out var raw))
            {
                // Heuristic: values past year ~2001 in ms are > 1e12; treat those as milliseconds.
                var seconds = raw > 1_000_000_000_000 ? raw / 1000.0 : raw;
                expiresAt = DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds * 1000));
            }

            return new OAuthCredentials(accessToken, refreshToken, expiresAt);
        }
    }
}
```

- [ ] **Step 5: Run — expect pass**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~OAuthCredentialsParserTests"
```

Expected: `Passed! - Failed: 0, Passed: 8`. If the fixture-file test fails on the path, double-check the `..` count — six `..` from `bin/Debug/net8.0-windows/` should reach repo root.

- [ ] **Step 6: Commit**

```powershell
git add windows/src/TokenSpendie.Windows/Data windows/tests/TokenSpendie.Windows.Tests/Data
git commit -m "feat(windows): port OAuthCredentials + parser with ms expiry heuristic"
```

---

### Task 6: ICredentialReader + ClaudeJsonFileReader

**Files:**
- Create: `windows/src/TokenSpendie.Windows/Data/ICredentialReader.cs`
- Create: `windows/src/TokenSpendie.Windows/Data/ClaudeJsonFileReader.cs`
- Create: `windows/tests/TokenSpendie.Windows.Tests/Data/ClaudeJsonFileReaderTests.cs`

Per M0 spike: credentials are at `%USERPROFILE%\.claude\.credentials.json`, plain JSON, no DPAPI, no Credential Manager. Spec G9 requires `FileShare.ReadWrite` + retry on `JsonException` (Claude Code may rewrite the file mid-read). Three attempts, 50ms backoff between.

- [ ] **Step 1: Write the failing tests**

`windows/tests/TokenSpendie.Windows.Tests/Data/ClaudeJsonFileReaderTests.cs`:

```csharp
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using TokenSpendie.Windows.Data;
using TokenSpendie.Windows.Models;
using Xunit;

namespace TokenSpendie.Windows.Tests.Data;

public class ClaudeJsonFileReaderTests : IDisposable
{
    private readonly string _tempHome;
    private readonly string _credsPath;

    public ClaudeJsonFileReaderTests()
    {
        _tempHome = Path.Combine(Path.GetTempPath(), $"tsw-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_tempHome, ".claude"));
        _credsPath = Path.Combine(_tempHome, ".claude", ".credentials.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempHome, recursive: true); } catch { }
    }

    private ClaudeJsonFileReader Reader() => new(_tempHome);

    private void WriteValidCreds(string accessToken = "tok", long expiresAtMs = 1779844385628)
    {
        File.WriteAllText(_credsPath,
            $$"""{"claudeAiOauth":{"accessToken":"{{accessToken}}","refreshToken":"r","expiresAt":{{expiresAtMs}}}}""");
    }

    [Fact]
    public void CredentialsExistFalseWhenFileMissing()
    {
        Reader().CredentialsExist().Should().BeFalse();
    }

    [Fact]
    public void CredentialsExistTrueWhenFilePresent()
    {
        WriteValidCreds();
        Reader().CredentialsExist().Should().BeTrue();
    }

    [Fact]
    public async Task LoadCredentialsAsyncReturnsParsed()
    {
        WriteValidCreds(accessToken: "abc");
        var creds = await Reader().LoadCredentialsAsync();
        creds.AccessToken.Should().Be("abc");
    }

    [Fact]
    public async Task LoadCredentialsAsyncThrowsNotFoundWhenAbsent()
    {
        Func<Task> act = () => Reader().LoadCredentialsAsync();
        await act.Should().ThrowAsync<CredentialNotFoundException>();
    }

    [Fact]
    public async Task LoadCredentialsAsyncThrowsMalformedOnGarbage()
    {
        File.WriteAllText(_credsPath, "not json");
        Func<Task> act = () => Reader().LoadCredentialsAsync();
        await act.Should().ThrowAsync<CredentialMalformedException>();
    }

    [Fact]
    public async Task LoadCredentialsAsyncRetriesOnConcurrentRewrite()
    {
        // First two reads see partial JSON; third sees a complete file.
        // The reader retries up to 3 times with a 50ms backoff.
        File.WriteAllText(_credsPath, """{"claudeAiOauth":{"accessTok"""); // truncated
        var readsBeforeComplete = 0;
        var fixerTask = Task.Run(async () =>
        {
            await Task.Delay(60);            // after first retry
            File.WriteAllText(_credsPath, """{"claudeAiOauth":{"accessToken":"final"}}""");
            Interlocked.Increment(ref readsBeforeComplete);
        });

        var creds = await Reader().LoadCredentialsAsync();
        await fixerTask;
        creds.AccessToken.Should().Be("final");
    }

    [Fact]
    public async Task LoadCredentialsAsyncGivesUpAfterThreeAttempts()
    {
        File.WriteAllText(_credsPath, "{garbage");
        var reader = Reader();
        Func<Task> act = () => reader.LoadCredentialsAsync();
        await act.Should().ThrowAsync<CredentialMalformedException>();
    }
}
```

- [ ] **Step 2: Run — expect failure**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~ClaudeJsonFileReaderTests"
```

Expected: build failure (`ICredentialReader`, `ClaudeJsonFileReader` not found).

- [ ] **Step 3: Implement `ICredentialReader`**

`windows/src/TokenSpendie.Windows/Data/ICredentialReader.cs`:

```csharp
namespace TokenSpendie.Windows.Data;

public interface ICredentialReader
{
    bool CredentialsExist();
    Task<OAuthCredentials> LoadCredentialsAsync(CancellationToken ct = default);
}
```

- [ ] **Step 4: Implement `ClaudeJsonFileReader`**

`windows/src/TokenSpendie.Windows/Data/ClaudeJsonFileReader.cs`:

```csharp
using TokenSpendie.Windows.Models;

namespace TokenSpendie.Windows.Data;

/// <summary>
/// Reads Claude Code OAuth credentials from a plain-JSON file at
/// <c>%USERPROFILE%\.claude\.credentials.json</c>. Storage location confirmed by
/// the M0 spike (see <c>docs/superpowers/findings/2026-05-26-windows-creds-spike.md</c>).
/// </summary>
public sealed class ClaudeJsonFileReader : ICredentialReader
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan BackoffBetweenAttempts = TimeSpan.FromMilliseconds(50);

    private readonly string _path;

    public ClaudeJsonFileReader()
        : this(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) { }

    /// <summary>Test-friendly constructor. <paramref name="userProfile"/> stands in for <c>%USERPROFILE%</c>.</summary>
    public ClaudeJsonFileReader(string userProfile)
    {
        _path = Path.Combine(userProfile, ".claude", ".credentials.json");
    }

    public bool CredentialsExist() => File.Exists(_path);

    public async Task<OAuthCredentials> LoadCredentialsAsync(CancellationToken ct = default)
    {
        Exception? lastMalformed = null;
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            byte[] bytes;
            try
            {
                // FileShare.ReadWrite lets Claude Code rewrite the file while we read it (G9).
                using var stream = new FileStream(
                    _path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 4096, useAsync: true);
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
                bytes = ms.ToArray();
            }
            catch (FileNotFoundException) { throw new CredentialNotFoundException(); }
            catch (DirectoryNotFoundException) { throw new CredentialNotFoundException(); }
            catch (UnauthorizedAccessException ex) { throw new CredentialAccessDeniedException(ex); }
            catch (IOException ex) when (attempt < MaxAttempts - 1)
            {
                // Sharing violation or partial write — back off and retry.
                await Task.Delay(BackoffBetweenAttempts, ct).ConfigureAwait(false);
                lastMalformed = ex;
                continue;
            }

            try
            {
                return OAuthCredentialsParser.Parse(bytes);
            }
            catch (CredentialMalformedException ex) when (attempt < MaxAttempts - 1)
            {
                // Partial JSON during a concurrent rewrite — back off and retry.
                await Task.Delay(BackoffBetweenAttempts, ct).ConfigureAwait(false);
                lastMalformed = ex;
            }
        }

        throw new CredentialMalformedException(
            "credentials file remained unreadable after retries",
            lastMalformed);
    }
}
```

- [ ] **Step 5: Run — expect pass**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~ClaudeJsonFileReaderTests"
```

Expected: `Passed! - Failed: 0, Passed: 7`.

- [ ] **Step 6: Commit**

```powershell
git add windows/src/TokenSpendie.Windows/Data windows/tests/TokenSpendie.Windows.Tests/Data
git commit -m "feat(windows): add ClaudeJsonFileReader with concurrent-write retry (G9)"
```

---

### Task 7: UsageDecoder

**Files:**
- Create: `windows/src/TokenSpendie.Windows/Data/UsageDecoder.cs`
- Create: `windows/tests/TokenSpendie.Windows.Tests/Data/UsageDecoderTests.cs`

- [ ] **Step 1: Write the failing tests**

`windows/tests/TokenSpendie.Windows.Tests/Data/UsageDecoderTests.cs`:

```csharp
using System;
using System.Text;
using FluentAssertions;
using TokenSpendie.Windows.Data;
using TokenSpendie.Windows.Models;
using Xunit;

namespace TokenSpendie.Windows.Tests.Data;

public class UsageDecoderTests
{
    private static readonly DateTimeOffset FetchedAt =
        DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    [Fact]
    public void DecodesPercentUtilization()
    {
        var json = """
        {
          "five_hour":      {"utilization": 44.0, "resets_at": "2026-05-21T10:20:00.431249+00:00"},
          "seven_day":      {"utilization": 8.0,  "resets_at": "2026-05-26T08:00:00.431273+00:00"},
          "seven_day_opus": {"utilization": 91.0, "resets_at": "2026-05-26T08:00:00.431280+00:00"}
        }
        """;
        var snapshot = UsageDecoder.Decode(Encoding.UTF8.GetBytes(json), FetchedAt);
        snapshot.Session.Percent.Should().BeApproximately(44, 0.001);
        snapshot.Weekly.Percent.Should().BeApproximately(8, 0.001);
        snapshot.ModelWeeklies.Should().ContainSingle();
        snapshot.ModelWeeklies[0].Model.Should().Be("Opus");
        snapshot.ModelWeeklies[0].Window.Percent.Should().BeApproximately(91, 0.001);
        snapshot.FetchedAt.Should().Be(FetchedAt);
    }

    [Fact]
    public void ParsesMicrosecondResetTime()
    {
        var json = """{"five_hour":{"utilization":1,"resets_at":"2026-05-21T10:20:00.431249+00:00"},"seven_day":{"utilization":1}}""";
        var snapshot = UsageDecoder.Decode(Encoding.UTF8.GetBytes(json), FetchedAt);
        var resetsAt = snapshot.Session.ResetsAt!.Value.ToUniversalTime();
        resetsAt.Year.Should().Be(2026);
        resetsAt.Month.Should().Be(5);
        resetsAt.Day.Should().Be(21);
        resetsAt.Hour.Should().Be(10);
        resetsAt.Minute.Should().Be(20);
    }

    [Fact]
    public void NullWindowIsOmitted()
    {
        var json = """{"five_hour":{"utilization":5},"seven_day":{"utilization":6},"seven_day_opus":null,"seven_day_sonnet":{"utilization":7}}""";
        var snapshot = UsageDecoder.Decode(Encoding.UTF8.GetBytes(json), FetchedAt);
        snapshot.ModelWeeklies.Select(m => m.Model).Should().Equal("Sonnet");
    }

    [Fact]
    public void DecodesOpusAndSonnetWeekly()
    {
        var json = """
        {"five_hour":{"utilization":1},"seven_day":{"utilization":2},"seven_day_opus":{"utilization":3},"seven_day_sonnet":{"utilization":4}}
        """;
        var snapshot = UsageDecoder.Decode(Encoding.UTF8.GetBytes(json), FetchedAt);
        snapshot.ModelWeeklies.Select(m => m.Model).Should().Equal("Opus", "Sonnet");
    }

    [Fact]
    public void MissingRequiredWindowThrowsBadResponse()
    {
        var json = """{"five_hour": {"utilization": 5}}""";
        Action act = () => UsageDecoder.Decode(Encoding.UTF8.GetBytes(json), FetchedAt);
        act.Should().Throw<ProviderBadResponseException>();
    }

    [Fact]
    public void GarbageThrowsBadResponse()
    {
        Action act = () => UsageDecoder.Decode(Encoding.UTF8.GetBytes("nonsense"), FetchedAt);
        act.Should().Throw<ProviderBadResponseException>();
    }
}
```

- [ ] **Step 2: Run — expect failure**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~UsageDecoderTests"
```

Expected: build failure (`UsageDecoder` not found).

- [ ] **Step 3: Implement `UsageDecoder`**

`windows/src/TokenSpendie.Windows/Data/UsageDecoder.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using TokenSpendie.Windows.Models;

namespace TokenSpendie.Windows.Data;

public static class UsageDecoder
{
    public static UsageSnapshot Decode(ReadOnlySpan<byte> utf8Json, DateTimeOffset fetchedAt)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(utf8Json.ToArray()); }
        catch (JsonException ex) { throw new ProviderBadResponseException("not JSON", ex); }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new ProviderBadResponseException("root is not an object");

            var session = ReadWindow(root, "five_hour");
            var weekly = ReadWindow(root, "seven_day");
            if (session is null || weekly is null)
                throw new ProviderBadResponseException("required window missing");

            var modelWeeklies = new List<ModelWeekly>(capacity: 2);
            if (ReadWindow(root, "seven_day_opus") is { } opus)
                modelWeeklies.Add(new ModelWeekly("Opus", opus));
            if (ReadWindow(root, "seven_day_sonnet") is { } sonnet)
                modelWeeklies.Add(new ModelWeekly("Sonnet", sonnet));

            return new UsageSnapshot(session, weekly, modelWeeklies, fetchedAt);
        }
    }

    private static UsageWindow? ReadWindow(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var el) || el.ValueKind != JsonValueKind.Object)
            return null;
        if (!el.TryGetProperty("utilization", out var util) || util.ValueKind != JsonValueKind.Number)
            return null;
        var percent = util.GetDouble();
        DateTimeOffset? resetsAt = null;
        if (el.TryGetProperty("resets_at", out var ra) && ra.ValueKind == JsonValueKind.String)
            resetsAt = ParseDate(ra.GetString()!);
        return new UsageWindow(percent, resetsAt);
    }

    private static readonly Regex FractionalSeconds = new(@"\.\d+", RegexOptions.Compiled);

    /// <summary>The endpoint emits microsecond precision; .NET's parser handles up to 7 digits
    /// (ticks). To stay safe across .NET versions, we strip the fractional component (mac
    /// parser does the same).</summary>
    internal static DateTimeOffset? ParseDate(string s)
    {
        var withoutFraction = FractionalSeconds.Replace(s, "");
        return DateTimeOffset.TryParse(
            withoutFraction, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }
}
```

- [ ] **Step 4: Run — expect pass**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~UsageDecoderTests"
```

Expected: `Passed! - Failed: 0, Passed: 6`.

- [ ] **Step 5: Commit**

```powershell
git add windows/src/TokenSpendie.Windows/Data windows/tests/TokenSpendie.Windows.Tests/Data
git commit -m "feat(windows): port UsageDecoder (microsecond ISO parsing)"
```

---

### Task 8: IClaudeUsageEndpoint + EndpointUsageProvider

**Files:**
- Create: `windows/src/TokenSpendie.Windows/Data/IClaudeUsageEndpoint.cs`
- Create: `windows/src/TokenSpendie.Windows/Data/EndpointUsageProvider.cs`
- Create: `windows/tests/TokenSpendie.Windows.Tests/Data/EndpointUsageProviderTests.cs`

Spec requires `HttpClient` with `SocketsHttpHandler { PooledConnectionLifetime = 5min }` (G8) and a thin abstraction for test injection. Tests substitute the `HttpMessageHandler` directly so we don't need NSubstitute here.

- [ ] **Step 1: Write the failing tests**

`windows/tests/TokenSpendie.Windows.Tests/Data/EndpointUsageProviderTests.cs`:

```csharp
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using TokenSpendie.Windows.Data;
using TokenSpendie.Windows.Models;
using Xunit;

namespace TokenSpendie.Windows.Tests.Data;

public class EndpointUsageProviderTests
{
    /// <summary>HttpMessageHandler that returns a canned response and records the request.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public HttpRequestMessage? Captured { get; private set; }
        public int CallCount { get; private set; }

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Captured = request;
            CallCount++;
            return Task.FromResult(_respond(request));
        }
    }

    private static EndpointUsageProvider Make(StubHandler handler, DateTimeOffset? now = null) =>
        new(new HttpClient(handler), now: () => now ?? DateTimeOffset.FromUnixTimeSeconds(42));

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };

    [Fact]
    public async Task Decodes200()
    {
        var body = """{"five_hour":{"utilization":50},"seven_day":{"utilization":60}}""";
        var provider = Make(new StubHandler(_ => Json(HttpStatusCode.OK, body)));
        var snapshot = await provider.FetchUsageAsync("tok");
        snapshot.Session.Percent.Should().BeApproximately(50, 0.001);
        snapshot.Weekly.Percent.Should().BeApproximately(60, 0.001);
        snapshot.FetchedAt.Should().Be(DateTimeOffset.FromUnixTimeSeconds(42));
    }

    [Fact]
    public async Task EmptyTokenThrowsUnauthorizedWithoutCallingTransport()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, ""));
        var provider = Make(handler);

        Func<Task> act = () => provider.FetchUsageAsync("");
        await act.Should().ThrowAsync<ProviderUnauthorizedException>();
        handler.CallCount.Should().Be(0, "transport must not be invoked for an empty token");
    }

    [Fact]
    public async Task Status401ThrowsUnauthorized()
    {
        var provider = Make(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        Func<Task> act = () => provider.FetchUsageAsync("tok");
        await act.Should().ThrowAsync<ProviderUnauthorizedException>();
    }

    [Fact]
    public async Task Status429ThrowsRateLimitedWithRetryAfter()
    {
        var provider = Make(new StubHandler(_ =>
        {
            var resp = new HttpResponseMessage((HttpStatusCode)429);
            resp.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return resp;
        }));
        Func<Task> act = () => provider.FetchUsageAsync("tok");
        var exception = await act.Should().ThrowAsync<ProviderRateLimitedException>();
        exception.Which.RetryAfter.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Status500ThrowsBadResponse()
    {
        var provider = Make(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        Func<Task> act = () => provider.FetchUsageAsync("tok");
        await act.Should().ThrowAsync<ProviderBadResponseException>();
    }

    [Fact]
    public async Task SendsBearerAndAcceptHeaders()
    {
        var body = """{"five_hour":{"utilization":10},"seven_day":{"utilization":10}}""";
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, body));
        var provider = Make(handler);
        _ = await provider.FetchUsageAsync("secret-token");

        handler.Captured.Should().NotBeNull();
        handler.Captured!.Headers.Authorization.Should().NotBeNull();
        handler.Captured.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.Captured.Headers.Authorization.Parameter.Should().Be("secret-token");
        handler.Captured.Headers.Accept.ToString().Should().Contain("application/json");
        handler.Captured.RequestUri.Should().Be("https://api.anthropic.com/api/oauth/usage");
    }
}
```

- [ ] **Step 2: Run — expect failure**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~EndpointUsageProviderTests"
```

Expected: build failure.

- [ ] **Step 3: Implement `IClaudeUsageEndpoint`**

`windows/src/TokenSpendie.Windows/Data/IClaudeUsageEndpoint.cs`:

```csharp
using TokenSpendie.Windows.Models;

namespace TokenSpendie.Windows.Data;

public interface IClaudeUsageEndpoint
{
    Task<UsageSnapshot> FetchUsageAsync(string accessToken, CancellationToken ct = default);
}
```

- [ ] **Step 4: Implement `EndpointUsageProvider`**

`windows/src/TokenSpendie.Windows/Data/EndpointUsageProvider.cs`:

```csharp
using System.Net.Http.Headers;
using TokenSpendie.Windows.Models;

namespace TokenSpendie.Windows.Data;

public sealed class EndpointUsageProvider : IClaudeUsageEndpoint
{
    private static readonly Uri UsageUrl = new("https://api.anthropic.com/api/oauth/usage");

    private readonly HttpClient _http;
    private readonly Func<DateTimeOffset> _now;

    public EndpointUsageProvider(HttpClient http, Func<DateTimeOffset>? now = null)
    {
        _http = http;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Default constructor — production wiring. <see cref="BuildHttpClient"/>
    /// applies the spec G8 connection pooling.</summary>
    public EndpointUsageProvider() : this(BuildHttpClient()) { }

    public static HttpClient BuildHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        return new HttpClient(handler);
    }

    public async Task<UsageSnapshot> FetchUsageAsync(string accessToken, CancellationToken ct = default)
    {
        // The endpoint returns 429 (not 401) for a missing/empty bearer, which would be
        // mislabeled as a rate limit. Never send an empty token.
        if (string.IsNullOrEmpty(accessToken))
            throw new ProviderUnauthorizedException();

        using var request = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("TokenSpendie/1.0");

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new ProviderNetworkException(ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            return (int)response.StatusCode switch
            {
                200 => UsageDecoder.Decode(body, _now()),
                401 => throw new ProviderUnauthorizedException(),
                429 => throw new ProviderRateLimitedException(response.Headers.RetryAfter?.Delta),
                _ => throw new ProviderBadResponseException($"status {(int)response.StatusCode}"),
            };
        }
    }
}
```

- [ ] **Step 5: Run — expect pass**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~EndpointUsageProviderTests"
```

Expected: `Passed! - Failed: 0, Passed: 6`.

- [ ] **Step 6: Commit**

```powershell
git add windows/src/TokenSpendie.Windows/Data windows/tests/TokenSpendie.Windows.Tests/Data
git commit -m "feat(windows): add EndpointUsageProvider (HttpClient, retry-after, pooled handler)"
```

---

### Task 9: IUsageProvider + ClaudeProvider

**Files:**
- Create: `windows/src/TokenSpendie.Windows/Data/IUsageProvider.cs`
- Create: `windows/src/TokenSpendie.Windows/Data/ClaudeProvider.cs`
- Create: `windows/tests/TokenSpendie.Windows.Tests/Data/ClaudeProviderTests.cs`

- [ ] **Step 1: Write the failing tests**

`windows/tests/TokenSpendie.Windows.Tests/Data/ClaudeProviderTests.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TokenSpendie.Windows.Data;
using TokenSpendie.Windows.Models;
using Xunit;

namespace TokenSpendie.Windows.Tests.Data;

public class ClaudeProviderTests
{
    private static OAuthCredentials Creds(string token = "tok") =>
        new(token, null, null);

    private static UsageSnapshot Snapshot() => new(
        Session: new UsageWindow(47, DateTimeOffset.FromUnixTimeSeconds(100)),
        Weekly: new UsageWindow(31, DateTimeOffset.FromUnixTimeSeconds(200)),
        ModelWeeklies: new[] { new ModelWeekly("Opus", new UsageWindow(62, null)) },
        FetchedAt: DateTimeOffset.FromUnixTimeSeconds(999));

    [Fact]
    public void IdAndDisplayName()
    {
        var provider = new ClaudeProvider(
            Substitute.For<ICredentialReader>(),
            Substitute.For<IClaudeUsageEndpoint>());
        provider.Id.Should().Be(ProviderID.Claude);
        provider.DisplayName.Should().Be("Claude");
    }

    [Fact]
    public void DetectCredentialsDelegates()
    {
        var reader = Substitute.For<ICredentialReader>();
        reader.CredentialsExist().Returns(true);
        new ClaudeProvider(reader, Substitute.For<IClaudeUsageEndpoint>()).DetectCredentials()
            .Should().BeTrue();
        reader.CredentialsExist().Returns(false);
        new ClaudeProvider(reader, Substitute.For<IClaudeUsageEndpoint>()).DetectCredentials()
            .Should().BeFalse();
    }

    [Fact]
    public void ConvertMapsWindowsWithLabelsAndHeadline()
    {
        var snapshot = ClaudeProvider.Convert(Snapshot());
        snapshot.Id.Should().Be(ProviderID.Claude);
        snapshot.Headline.Label.Should().Be("Session");
        snapshot.Headline.Window.Percent.Should().BeApproximately(47, 0.001);
        snapshot.Windows.Select(w => w.Label).Should().Equal("Session", "Weekly", "Weekly · Opus");
        snapshot.Windows[0].ResetStyle.Should().Be(ResetStyle.Countdown);
        snapshot.Windows[1].ResetStyle.Should().Be(ResetStyle.Date);
        snapshot.Windows[1].Detail.Should().Be("all models");
        snapshot.Windows[2].Detail.Should().Be("Opus only");
        snapshot.FetchedAt.Should().Be(DateTimeOffset.FromUnixTimeSeconds(999));
        snapshot.Plan.Should().BeNull();
    }

    [Fact]
    public async Task FetchUsageReturnsConvertedSnapshot()
    {
        var reader = Substitute.For<ICredentialReader>();
        reader.LoadCredentialsAsync(Arg.Any<CancellationToken>()).Returns(Creds());
        var endpoint = Substitute.For<IClaudeUsageEndpoint>();
        endpoint.FetchUsageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Snapshot());

        var provider = new ClaudeProvider(reader, endpoint);
        var snapshot = await provider.FetchUsageAsync();

        snapshot.Headline.Window.Percent.Should().BeApproximately(47, 0.001);
    }

    [Fact]
    public async Task FetchUsageRetriesOnceByRereadingCredentialsOn401()
    {
        var reader = Substitute.For<ICredentialReader>();
        reader.LoadCredentialsAsync(Arg.Any<CancellationToken>()).Returns(Creds());

        var endpoint = Substitute.For<IClaudeUsageEndpoint>();
        endpoint.FetchUsageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => throw new ProviderUnauthorizedException(),
                _ => Task.FromResult(Snapshot()));

        var provider = new ClaudeProvider(reader, endpoint);
        _ = await provider.FetchUsageAsync();

        await reader.Received(2).LoadCredentialsAsync(Arg.Any<CancellationToken>());
        await endpoint.Received(2).FetchUsageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FetchUsagePropagatesPersistentUnauthorized()
    {
        var reader = Substitute.For<ICredentialReader>();
        reader.LoadCredentialsAsync(Arg.Any<CancellationToken>()).Returns(Creds());
        var endpoint = Substitute.For<IClaudeUsageEndpoint>();
        endpoint.FetchUsageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new ProviderUnauthorizedException());

        var provider = new ClaudeProvider(reader, endpoint);
        Func<Task> act = () => provider.FetchUsageAsync();
        await act.Should().ThrowAsync<ProviderUnauthorizedException>();
    }

    [Fact]
    public async Task FetchUsagePropagatesCredentialError()
    {
        var reader = Substitute.For<ICredentialReader>();
        reader.LoadCredentialsAsync(Arg.Any<CancellationToken>())
            .Throws(new CredentialNotFoundException());
        var endpoint = Substitute.For<IClaudeUsageEndpoint>();

        var provider = new ClaudeProvider(reader, endpoint);
        Func<Task> act = () => provider.FetchUsageAsync();
        await act.Should().ThrowAsync<CredentialNotFoundException>();
        await endpoint.DidNotReceive().FetchUsageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 2: Run — expect failure**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~ClaudeProviderTests"
```

Expected: build failure.

- [ ] **Step 3: Implement `IUsageProvider`**

`windows/src/TokenSpendie.Windows/Data/IUsageProvider.cs`:

```csharp
using TokenSpendie.Windows.Models;

namespace TokenSpendie.Windows.Data;

public interface IUsageProvider
{
    ProviderID Id { get; }
    string DisplayName { get; }
    bool DetectCredentials();
    Task<ProviderSnapshot> FetchUsageAsync(CancellationToken ct = default);
}
```

- [ ] **Step 4: Implement `ClaudeProvider`**

`windows/src/TokenSpendie.Windows/Data/ClaudeProvider.cs`:

```csharp
using TokenSpendie.Windows.Models;

namespace TokenSpendie.Windows.Data;

public sealed class ClaudeProvider : IUsageProvider
{
    public ProviderID Id => ProviderID.Claude;
    public string DisplayName => "Claude";

    private readonly ICredentialReader _credentials;
    private readonly IClaudeUsageEndpoint _endpoint;

    public ClaudeProvider(ICredentialReader credentials, IClaudeUsageEndpoint endpoint)
    {
        _credentials = credentials;
        _endpoint = endpoint;
    }

    public bool DetectCredentials() => _credentials.CredentialsExist();

    public async Task<ProviderSnapshot> FetchUsageAsync(CancellationToken ct = default)
    {
        var creds = await _credentials.LoadCredentialsAsync(ct).ConfigureAwait(false);
        UsageSnapshot usage;
        try
        {
            usage = await _endpoint.FetchUsageAsync(creds.AccessToken, ct).ConfigureAwait(false);
        }
        catch (ProviderUnauthorizedException)
        {
            // Re-read credentials once — Claude Code refreshes the token during normal use.
            var refreshed = await _credentials.LoadCredentialsAsync(ct).ConfigureAwait(false);
            usage = await _endpoint.FetchUsageAsync(refreshed.AccessToken, ct).ConfigureAwait(false);
        }
        return Convert(usage);
    }

    /// <summary>Pure <see cref="UsageSnapshot"/> → <see cref="ProviderSnapshot"/> mapping.
    /// Session is the headline; <c>Windows</c> is <c>[session, weekly, model-weeklies…]</c>.</summary>
    public static ProviderSnapshot Convert(UsageSnapshot usage)
    {
        var session = new LabeledWindow("Session", "5-hour window",
            ResetStyle.Countdown, usage.Session);
        var windows = new List<LabeledWindow> { session };
        windows.Add(new LabeledWindow("Weekly", "all models", ResetStyle.Date, usage.Weekly));
        foreach (var model in usage.ModelWeeklies)
        {
            windows.Add(new LabeledWindow(
                $"Weekly · {model.Model}", $"{model.Model} only",
                ResetStyle.Date, model.Window));
        }
        return new ProviderSnapshot(
            Id: ProviderID.Claude, Plan: null,
            Headline: session, Windows: windows,
            FetchedAt: usage.FetchedAt);
    }
}
```

- [ ] **Step 5: Run — expect pass**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~ClaudeProviderTests"
```

Expected: `Passed! - Failed: 0, Passed: 7`.

- [ ] **Step 6: Commit**

```powershell
git add windows/src/TokenSpendie.Windows/Data windows/tests/TokenSpendie.Windows.Tests/Data
git commit -m "feat(windows): add ClaudeProvider with retry-on-401 (re-reads credentials)"
```

---

### Task 10: GeminiUsageReader (Windows JSONL session format)

**Files:**
- Create: `windows/src/TokenSpendie.Windows/Data/GeminiUsageReader.cs`
- Create: `windows/tests/TokenSpendie.Windows.Tests/Data/GeminiUsageReaderTests.cs`

This reader implements the Windows reality the M0 spike discovered: per-project session files at `~/.gemini/tmp/<project>/chats/session-*.jsonl`, line-delimited, with user-prompt records `{type:"user", content:[{text:string}], timestamp:string}`. The mac reader's `logs.json` shape is NOT used on Windows.

- [ ] **Step 1: Write the failing tests**

`windows/tests/TokenSpendie.Windows.Tests/Data/GeminiUsageReaderTests.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using TokenSpendie.Windows.Data;
using Xunit;

namespace TokenSpendie.Windows.Tests.Data;

public class GeminiUsageReaderTests : IDisposable
{
    private readonly string _home;
    private readonly TimeZoneInfo _utc = TimeZoneInfo.Utc;
    /// <summary>2025-05-22 12:00 UTC.</summary>
    private static readonly DateTimeOffset Noon =
        DateTimeOffset.FromUnixTimeSeconds(1_747_915_200);
    /// <summary>2025-05-23 00:00 UTC.</summary>
    private static readonly DateTimeOffset NextLocalMidnight =
        DateTimeOffset.FromUnixTimeSeconds(1_747_958_400);

    public GeminiUsageReaderTests()
    {
        _home = Path.Combine(Path.GetTempPath(), $"gem-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_home);
    }

    public void Dispose()
    {
        try { Directory.Delete(_home, recursive: true); } catch { }
    }

    private GeminiUsageReader Reader(DateTimeOffset? now = null) =>
        new(geminiHome: _home, now: () => now ?? Noon, timeZone: _utc);

    private void StubOAuth() =>
        File.WriteAllText(Path.Combine(_home, "oauth_creds.json"), "{}");

    /// <summary>Write a JSONL session file containing the given lines.</summary>
    private void WriteSession(string project, string sessionId, params string[] lines)
    {
        var dir = Path.Combine(_home, "tmp", project, "chats");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"session-2025-05-22T01-00-{sessionId}.jsonl");
        File.WriteAllLines(path, lines);
    }

    private static string UserLine(string text, string iso) =>
        $$"""{"id":"u","timestamp":"{{iso}}","type":"user","content":[{"text":"{{text}}"}]}""";

    private static string GeminiLine(string text, string iso) =>
        $$"""{"id":"g","timestamp":"{{iso}}","type":"gemini","content":"{{text}}"}""";

    private static string SessionHeader() =>
        """{"sessionId":"s","projectHash":"h","startTime":"2025-05-22T00:00:00.000Z","lastUpdated":"2025-05-22T00:00:00.000Z","kind":"main"}""";

    private static string SetSentinel() =>
        """{"$set":{"lastUpdated":"2025-05-22T01:00:00.000Z"}}""";

    [Fact]
    public void DetectCredentialsTrueWhenOAuthFileExists()
    {
        StubOAuth();
        Reader().DetectCredentials().Should().BeTrue();
    }

    [Fact]
    public void DetectCredentialsFalseWhenNoOAuthFile()
    {
        Reader().DetectCredentials().Should().BeFalse();
    }

    [Fact]
    public void NextLocalMidnightIsStartOfTomorrow()
    {
        Reader().NextLocalMidnight().Should().Be(NextLocalMidnight);
    }

    [Fact]
    public void CountsTodaysPrompts()
    {
        WriteSession("p", "a",
            SessionHeader(),
            UserLine("hello", "2025-05-22T01:00:00.000Z"),
            SetSentinel(),
            GeminiLine("hi", "2025-05-22T01:00:01.000Z"),
            UserLine("again", "2025-05-22T11:59:00.000Z"));
        Reader().RequestsToday().Should().Be(2);
    }

    [Fact]
    public void IgnoresYesterdaysPrompts()
    {
        WriteSession("p", "a",
            SessionHeader(),
            UserLine("old", "2025-05-21T23:59:00.000Z"),
            UserLine("new", "2025-05-22T00:00:00.000Z"));
        Reader().RequestsToday().Should().Be(1);
    }

    [Fact]
    public void IgnoresSlashCommands()
    {
        WriteSession("p", "a",
            SessionHeader(),
            UserLine("/stats", "2025-05-22T01:00:00.000Z"),
            UserLine("real prompt", "2025-05-22T02:00:00.000Z"));
        Reader().RequestsToday().Should().Be(1);
    }

    [Fact]
    public void IgnoresNonUserRecordTypes()
    {
        WriteSession("p", "a",
            SessionHeader(),
            GeminiLine("response", "2025-05-22T01:00:00.000Z"),
            UserLine("prompt", "2025-05-22T02:00:00.000Z"));
        Reader().RequestsToday().Should().Be(1);
    }

    [Fact]
    public void ParsesTimestampsWithoutFractionalSeconds()
    {
        WriteSession("p", "a",
            SessionHeader(),
            UserLine("plain", "2025-05-22T03:00:00Z"));
        Reader().RequestsToday().Should().Be(1);
    }

    [Fact]
    public void SumsAcrossProjectsAndSessions()
    {
        WriteSession("p1", "a",
            SessionHeader(),
            UserLine("a", "2025-05-22T01:00:00.000Z"));
        WriteSession("p2", "b",
            SessionHeader(),
            UserLine("b", "2025-05-22T02:00:00.000Z"),
            UserLine("c", "2025-05-22T03:00:00.000Z"));
        WriteSession("p2", "c",
            SessionHeader(),
            UserLine("d", "2025-05-22T04:00:00.000Z"));
        Reader().RequestsToday().Should().Be(4);
    }

    [Fact]
    public void CorruptSessionFileSkippedOthersStillCounted()
    {
        WriteSession("good", "a",
            SessionHeader(),
            UserLine("a", "2025-05-22T01:00:00.000Z"));
        // Add a session file whose contents are not valid JSON.
        var badDir = Path.Combine(_home, "tmp", "bad", "chats");
        Directory.CreateDirectory(badDir);
        File.WriteAllText(Path.Combine(badDir, "session-x.jsonl"),
            "not json\nstill not json\n");
        Reader().RequestsToday().Should().Be(1);
    }

    [Fact]
    public void MissingTmpDirectoryReturnsZero()
    {
        StubOAuth(); // no tmp/ created
        Reader().RequestsToday().Should().Be(0);
    }

    [Fact]
    public void IgnoresPlaceholderEmptyLogsJson()
    {
        // M0 spike: Gemini v0.43.0 writes an empty `[]` logs.json at first login.
        // The reader must not be confused by it — it reads JSONL sessions, not logs.json.
        var dir = Path.Combine(_home, "tmp", "cherise");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "logs.json"), "[]");
        Reader().RequestsToday().Should().Be(0);
    }

    [Fact]
    public void ParsesM0SanitizedFixture()
    {
        // The M0 fixture wraps two real session-jsonl files. Lay them out
        // under tmp/<project>/chats/ so the reader picks them up.
        var fixturePath = Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..",
            "docs", "superpowers", "findings", "fixtures",
            "gemini-logs-sanitized.json");
        fixturePath = Path.GetFullPath(fixturePath);
        File.Exists(fixturePath).Should().BeTrue();

        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(fixturePath));
        var lines = doc.RootElement.GetProperty("_jsonl_lines")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();

        var dir = Path.Combine(_home, "tmp", "token-spendie", "chats");
        Directory.CreateDirectory(dir);
        File.WriteAllLines(Path.Combine(dir, "session-fixture.jsonl"), lines);

        // The fixture's two user prompts are timestamped 2026-05-26T18:13Z and
        // 2026-05-26T18:16Z. Set "now" to noon of that day so both fall within today.
        var fixtureNoon = new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero);
        var reader = new GeminiUsageReader(_home, () => fixtureNoon, _utc);
        reader.RequestsToday().Should().Be(2);
    }
}
```

- [ ] **Step 2: Run — expect failure**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~GeminiUsageReaderTests"
```

Expected: build failure (`GeminiUsageReader` not found).

- [ ] **Step 3: Implement `GeminiUsageReader`**

`windows/src/TokenSpendie.Windows/Data/GeminiUsageReader.cs`:

```csharp
using System.Globalization;
using System.Text.Json;

namespace TokenSpendie.Windows.Data;

/// <summary>
/// Counts Gemini CLI usage from JSONL session files at
/// <c>%USERPROFILE%\.gemini\tmp\&lt;project&gt;\chats\session-*.jsonl</c>.
/// Format confirmed by the M0 spike: per-line JSON objects, user prompts have
/// <c>type:"user"</c> and <c>content:[{text:string}]</c>. The legacy mac
/// <c>logs.json</c> array is absent on Windows v0.43.0 and is ignored.
/// </summary>
public sealed class GeminiUsageReader
{
    private readonly string _geminiHome;
    private readonly Func<DateTimeOffset> _now;
    private readonly TimeZoneInfo _timeZone;

    public GeminiUsageReader()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini")) { }

    public GeminiUsageReader(string geminiHome,
        Func<DateTimeOffset>? now = null,
        TimeZoneInfo? timeZone = null)
    {
        _geminiHome = geminiHome;
        _now = now ?? (() => DateTimeOffset.Now);
        _timeZone = timeZone ?? TimeZoneInfo.Local;
    }

    public DateTimeOffset Now() => _now();

    public bool DetectCredentials() =>
        File.Exists(Path.Combine(_geminiHome, "oauth_creds.json"));

    public DateTimeOffset NextLocalMidnight()
    {
        var nowLocal = TimeZoneInfo.ConvertTime(_now(), _timeZone);
        var startOfToday = new DateTimeOffset(
            nowLocal.Year, nowLocal.Month, nowLocal.Day, 0, 0, 0, nowLocal.Offset);
        return startOfToday.AddDays(1);
    }

    public int RequestsToday()
    {
        var nowLocal = TimeZoneInfo.ConvertTime(_now(), _timeZone);
        var startOfToday = new DateTimeOffset(
            nowLocal.Year, nowLocal.Month, nowLocal.Day, 0, 0, 0, nowLocal.Offset);

        var tmpDir = Path.Combine(_geminiHome, "tmp");
        if (!Directory.Exists(tmpDir)) return 0;

        var total = 0;
        foreach (var projectDir in Directory.EnumerateDirectories(tmpDir))
        {
            var chatsDir = Path.Combine(projectDir, "chats");
            if (!Directory.Exists(chatsDir)) continue;
            foreach (var session in Directory.EnumerateFiles(chatsDir, "session-*.jsonl"))
            {
                total += CountPromptsInJsonl(session, startOfToday);
            }
        }
        return total;
    }

    private static int CountPromptsInJsonl(string path, DateTimeOffset since)
    {
        int count = 0;
        string[] lines;
        try { lines = File.ReadAllLines(path); }
        catch { return 0; }   // unreadable file — best-effort: 0

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonDocument doc;
            try { doc = JsonDocument.Parse(line); }
            catch { continue; }  // malformed line — skip

            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) continue;
                if (!root.TryGetProperty("type", out var type)
                    || type.ValueKind != JsonValueKind.String
                    || type.GetString() != "user") continue;
                if (!root.TryGetProperty("timestamp", out var ts)
                    || ts.ValueKind != JsonValueKind.String) continue;
                if (!TryParseTimestamp(ts.GetString()!, out var stamp)) continue;
                if (stamp < since) continue;

                // Look up content[0].text and skip if it's a slash command.
                if (root.TryGetProperty("content", out var content)
                    && content.ValueKind == JsonValueKind.Array
                    && content.GetArrayLength() > 0
                    && content[0].TryGetProperty("text", out var textProp)
                    && textProp.ValueKind == JsonValueKind.String
                    && textProp.GetString() is { } text
                    && text.StartsWith('/'))
                {
                    continue;
                }

                count++;
            }
        }
        return count;
    }

    internal static bool TryParseTimestamp(string s, out DateTimeOffset parsed) =>
        DateTimeOffset.TryParse(
            s, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out parsed);
}
```

- [ ] **Step 4: Run — expect pass**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~GeminiUsageReaderTests"
```

Expected: `Passed! - Failed: 0, Passed: 13`.

- [ ] **Step 5: Commit**

```powershell
git add windows/src/TokenSpendie.Windows/Data windows/tests/TokenSpendie.Windows.Tests/Data
git commit -m "feat(windows): add GeminiUsageReader for JSONL sessions (per M0 spike)"
```

---

### Task 11: GeminiProvider

**Files:**
- Create: `windows/src/TokenSpendie.Windows/Data/GeminiProvider.cs`
- Create: `windows/tests/TokenSpendie.Windows.Tests/Data/GeminiProviderTests.cs`

- [ ] **Step 1: Write the failing tests**

`windows/tests/TokenSpendie.Windows.Tests/Data/GeminiProviderTests.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using TokenSpendie.Windows.Data;
using TokenSpendie.Windows.Models;
using Xunit;

namespace TokenSpendie.Windows.Tests.Data;

public class GeminiProviderTests : IDisposable
{
    private readonly string _home;
    private static readonly DateTimeOffset Noon = DateTimeOffset.FromUnixTimeSeconds(1_747_915_200);
    private static readonly DateTimeOffset NextMidnight = DateTimeOffset.FromUnixTimeSeconds(1_747_958_400);

    public GeminiProviderTests()
    {
        _home = Path.Combine(Path.GetTempPath(), $"gemp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_home);
    }

    public void Dispose()
    {
        try { Directory.Delete(_home, recursive: true); } catch { }
    }

    private void StubOAuth() =>
        File.WriteAllText(Path.Combine(_home, "oauth_creds.json"), "{}");

    private void StubSessionWithPrompts(int promptCount)
    {
        var dir = Path.Combine(_home, "tmp", "p", "chats");
        Directory.CreateDirectory(dir);
        var lines = new[]
        {
            """{"sessionId":"s","projectHash":"h","startTime":"2025-05-22T00:00:00.000Z","lastUpdated":"2025-05-22T00:00:00.000Z","kind":"main"}"""
        }.Concat(Enumerable.Range(0, promptCount).Select(i =>
            $$"""{"id":"u","timestamp":"2025-05-22T0{{i % 9}}:00:00.000Z","type":"user","content":[{"text":"prompt {{i}}"}]}"""));
        File.WriteAllLines(Path.Combine(dir, "session-a.jsonl"), lines);
    }

    private GeminiUsageReader MakeReader() =>
        new(_home, () => Noon, TimeZoneInfo.Utc);

    [Fact]
    public void IdAndDisplayName()
    {
        var provider = new GeminiProvider(MakeReader());
        provider.Id.Should().Be(ProviderID.Gemini);
        provider.DisplayName.Should().Be("Gemini");
    }

    [Fact]
    public void DetectCredentialsDelegatesToReader()
    {
        StubOAuth();
        new GeminiProvider(MakeReader()).DetectCredentials().Should().BeTrue();

        var emptyHome = Path.Combine(Path.GetTempPath(), $"emp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(emptyHome);
        try
        {
            new GeminiProvider(new GeminiUsageReader(emptyHome, () => Noon, TimeZoneInfo.Utc))
                .DetectCredentials().Should().BeFalse();
        }
        finally { Directory.Delete(emptyHome); }
    }

    [Fact]
    public async Task FetchUsageProducesDailySnapshot()
    {
        StubOAuth();
        StubSessionWithPrompts(3);
        var provider = new GeminiProvider(MakeReader());

        var snapshot = await provider.FetchUsageAsync();

        snapshot.Id.Should().Be(ProviderID.Gemini);
        snapshot.Plan.Should().BeNull();
        snapshot.Windows.Should().ContainSingle();
        snapshot.Headline.Label.Should().Be("Daily");
        snapshot.Headline.ResetStyle.Should().Be(ResetStyle.Countdown);
        snapshot.Headline.Window.Percent.Should().BeApproximately(0.3, 0.0001);
        snapshot.Headline.Window.ResetsAt.Should().Be(NextMidnight);
        snapshot.FetchedAt.Should().Be(Noon);
    }

    [Fact]
    public void ConvertPercentMath()
    {
        var reset = DateTimeOffset.FromUnixTimeSeconds(0);
        var now = DateTimeOffset.FromUnixTimeSeconds(0);
        GeminiProvider.Convert(0, reset, now).Headline.Window.Percent
            .Should().BeApproximately(0, 0.0001);
        GeminiProvider.Convert(500, reset, now).Headline.Window.Percent
            .Should().BeApproximately(50, 0.0001);
        GeminiProvider.Convert(1500, reset, now).Headline.Window.Percent
            .Should().BeApproximately(150, 0.0001);
    }

    [Fact]
    public void ConvertDetailString()
    {
        var snapshot = GeminiProvider.Convert(
            420,
            DateTimeOffset.FromUnixTimeSeconds(0),
            DateTimeOffset.FromUnixTimeSeconds(0));
        snapshot.Headline.Detail.Should().Be("≈420 of 1000 requests");
    }

    [Fact]
    public void ConvertMarksSnapshotAsAnEstimate()
    {
        var snapshot = GeminiProvider.Convert(
            1,
            DateTimeOffset.FromUnixTimeSeconds(0),
            DateTimeOffset.FromUnixTimeSeconds(0));
        snapshot.Note.Should().Be("estimate · counted from local logs");
    }
}
```

- [ ] **Step 2: Run — expect failure**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~GeminiProviderTests"
```

Expected: build failure.

- [ ] **Step 3: Implement `GeminiProvider`**

`windows/src/TokenSpendie.Windows/Data/GeminiProvider.cs`:

```csharp
using TokenSpendie.Windows.Models;

namespace TokenSpendie.Windows.Data;

public sealed class GeminiProvider : IUsageProvider
{
    public ProviderID Id => ProviderID.Gemini;
    public string DisplayName => "Gemini";

    public const int DailyQuota = 1000;

    private readonly GeminiUsageReader _reader;

    public GeminiProvider(GeminiUsageReader? reader = null)
    {
        _reader = reader ?? new GeminiUsageReader();
    }

    public bool DetectCredentials() => _reader.DetectCredentials();

    public Task<ProviderSnapshot> FetchUsageAsync(CancellationToken ct = default)
    {
        var count = _reader.RequestsToday();
        var snapshot = Convert(count, _reader.NextLocalMidnight(), _reader.Now());
        return Task.FromResult(snapshot);
    }

    public static ProviderSnapshot Convert(int count, DateTimeOffset resetsAt, DateTimeOffset now)
    {
        var percent = count / (double)DailyQuota * 100;
        var window = new UsageWindow(percent, resetsAt);
        var daily = new LabeledWindow(
            Label: "Daily",
            Detail: $"≈{count} of {DailyQuota} requests",
            ResetStyle: ResetStyle.Countdown,
            Window: window);
        return new ProviderSnapshot(
            Id: ProviderID.Gemini, Plan: null,
            Headline: daily, Windows: new[] { daily },
            FetchedAt: now,
            Note: "estimate · counted from local logs");
    }
}
```

- [ ] **Step 4: Run — expect pass**

```powershell
dotnet test windows/TokenSpendie.Windows.sln --filter "FullyQualifiedName~GeminiProviderTests"
```

Expected: `Passed! - Failed: 0, Passed: 6`.

- [ ] **Step 5: Commit**

```powershell
git add windows/src/TokenSpendie.Windows/Data windows/tests/TokenSpendie.Windows.Tests/Data
git commit -m "feat(windows): add GeminiProvider (count → Daily ProviderSnapshot)"
```

---

### Task 12: Headless snapshot CLI

**Files:**
- Modify: `windows/src/TokenSpendie.Windows/Program.cs`

The headless binary is the milestone's acceptance criterion: "Headless console binary that prints snapshots — proves the data path end to end." It detects whichever providers are present, fetches their usage, prints one section per provider.

- [ ] **Step 1: Replace the Task-1 stub with the real CLI**

`windows/src/TokenSpendie.Windows/Program.cs`:

```csharp
using TokenSpendie.Windows.Data;
using TokenSpendie.Windows.Models;

namespace TokenSpendie.Windows;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var verbose = args.Any(a => a == "--verbose");

        IUsageProvider[] providers =
        {
            new ClaudeProvider(new ClaudeJsonFileReader(), new EndpointUsageProvider()),
            new GeminiProvider(),
        };

        foreach (var provider in providers)
        {
            await PrintProviderAsync(provider, verbose).ConfigureAwait(false);
        }
        return 0;
    }

    private static async Task PrintProviderAsync(IUsageProvider provider, bool verbose)
    {
        Console.WriteLine($"=== {provider.DisplayName} ===");

        if (!provider.DetectCredentials())
        {
            Console.WriteLine("  (not logged in)");
            Console.WriteLine();
            return;
        }

        try
        {
            var snapshot = await provider.FetchUsageAsync().ConfigureAwait(false);
            PrintSnapshot(snapshot);
        }
        catch (CredentialNotFoundException) { Console.WriteLine("  (credential file removed mid-run)"); }
        catch (CredentialMalformedException ex) { Console.WriteLine($"  malformed credentials: {ex.Message}"); }
        catch (ProviderUnauthorizedException) { Console.WriteLine("  401 — login expired"); }
        catch (ProviderRateLimitedException ex)
        {
            Console.WriteLine($"  429 — rate-limited (retry-after: {ex.RetryAfter?.TotalSeconds ?? -1}s)");
        }
        catch (ProviderNetworkException ex) { Console.WriteLine($"  network error: {ex.InnerException?.Message}"); }
        catch (ProviderBadResponseException ex) { Console.WriteLine($"  bad response: {ex.Message}"); }
        catch (Exception ex) when (verbose)
        {
            Console.WriteLine($"  unexpected: {ex}");
        }
        Console.WriteLine();
    }

    private static void PrintSnapshot(ProviderSnapshot snapshot)
    {
        if (snapshot.Plan is { } plan) Console.WriteLine($"  plan: {plan}");
        foreach (var w in snapshot.Windows)
        {
            var reset = w.Window.ResetsAt is { } r
                ? $" (resets {r.ToLocalTime():u})"
                : "";
            Console.WriteLine($"  {w.Label,-20} {w.Window.Percent,6:F1}%  {w.Detail}{reset}");
        }
        if (snapshot.Note is { } note) Console.WriteLine($"  note: {note}");
        Console.WriteLine($"  fetched at {snapshot.FetchedAt.ToLocalTime():u}");
    }
}
```

- [ ] **Step 2: Build the binary**

```powershell
dotnet build windows/TokenSpendie.Windows.sln -c Release
```

Expected: `Build succeeded`. The exe lives at `windows/src/TokenSpendie.Windows/bin/Release/net8.0-windows/TokenSpendie.Windows.exe`.

- [ ] **Step 3: Smoke-run against real credentials**

If Claude Code is logged in (and Gemini CLI is logged in, optionally):

```powershell
windows/src/TokenSpendie.Windows/bin/Release/net8.0-windows/TokenSpendie.Windows.exe
```

Expected output (concrete numbers depend on your real usage):

```
=== Claude ===
  Session                47.3%  5-hour window (resets 2026-05-27 09:00:00Z)
  Weekly                  8.4%  all models (resets 2026-06-02 08:00:00Z)
  fetched at 2026-05-27 02:30:00Z

=== Gemini ===
  Daily                   0.3%  ≈3 of 1000 requests (resets 2026-05-28 00:00:00Z)
  note: estimate · counted from local logs
  fetched at 2026-05-27 02:30:00Z
```

If Claude Code is not logged in, the Claude section prints `(not logged in)`. If Gemini CLI is not installed, the Gemini section prints `(not logged in)` because the `oauth_creds.json` check fails.

Record the actual exit code (`echo $LASTEXITCODE`) — expected `0`.

- [ ] **Step 4: Run the full test suite one final time**

```powershell
dotnet test windows/TokenSpendie.Windows.sln
```

Expected: every test in every file passes. Count: approximately `58` (4 + 5 + 5 + 8 + 7 + 6 + 6 + 7 + 13 + 6 + 1).

- [ ] **Step 5: Commit**

```powershell
git add windows/src/TokenSpendie.Windows/Program.cs
git commit -m "feat(windows): add headless snapshot CLI (M1 end-to-end binary)"
```

---

### Task 13: PR and handoff

**Files:** (none modified — packaging only)

- [ ] **Step 1: Verify the branch state**

```powershell
git status            # expect: working tree clean
git log --oneline windows-port-m0-spike..HEAD
```

Expected: 12 commits — one per Task 1–12.

- [ ] **Step 2: Secret-leak scan**

The data layer does not write secrets to disk, but Task 12's Program.cs prints snapshot data. Confirm no real token leaks were accidentally committed:

```powershell
git log -p windows-port-m0-spike..HEAD `
  | Select-String -Pattern 'ey[A-Za-z0-9_\-]{20,}|sk-ant-|oat[A-Za-z0-9_\-]{20,}' `
  | Measure-Object | ForEach-Object { if ($_.Count -eq 0) { "clean" } else { "DIRTY" } }
```

Expected: `clean`. If `DIRTY`, find the matching commit, redact, amend (or rewrite the commit history with `git rebase -i`), and re-run before pushing.

- [ ] **Step 3: Push the branch**

```powershell
git push -u origin windows-port-m1-core
```

Expected: branch pushed; remote prints a `pull/new/<branch>` URL.

- [ ] **Step 4: Open a PR**

If `gh` is installed:

```powershell
gh pr create --base windows-port-m0-spike --title "feat(windows): M1 core data layer" --body @'
## Summary
- Ports the Swift `Data/` + `Model/` layers to a new `windows/` .NET 8 project.
- One-to-one type port: `UsageWindow`, `ModelWeekly`, `UsageSnapshot`, `LabeledWindow`, `ProviderSnapshot`, `ProviderID`, `ResetStyle`, `LoadState`, `ProviderUsage`, plus credential + provider exception hierarchies.
- `ClaudeJsonFileReader` reads `%USERPROFILE%\.claude\.credentials.json` (path confirmed by the M0 spike) with `FileShare.ReadWrite` + retry-on-`JsonException` to handle Claude Code rewrites mid-read (G9).
- `EndpointUsageProvider` uses `HttpClient` with a `SocketsHttpHandler` whose `PooledConnectionLifetime = 5min` (G8). Retry-after honored on 429.
- `ClaudeProvider` retries once on 401 by re-reading the credentials file.
- `GeminiUsageReader` reads `~/.gemini/tmp/<project>/chats/session-*.jsonl` per the M0 spike (the legacy `logs.json` is empty on Windows v0.43.0 and ignored).
- Headless `TokenSpendie.Windows.exe` prints one section per detected provider — the M1 acceptance binary.
- Full xUnit + FluentAssertions + NSubstitute coverage. ~58 tests, all green.

## Test plan
- [ ] `dotnet test windows/TokenSpendie.Windows.sln` passes locally.
- [ ] `windows/src/TokenSpendie.Windows/bin/Release/net8.0-windows/TokenSpendie.Windows.exe` produces a snapshot section for Claude and (if logged in) Gemini.
- [ ] Reviewer confirms no real tokens in the diff (`grep -E ey[A-Za-z0-9_-]{20,}|sk-ant-|oat[A-Za-z0-9_-]{20,}` is silent over commits in the PR).

🤖 Generated with [Claude Code](https://claude.com/claude-code)
'@
```

If `gh` is not installed, open the URL printed by `git push` in a browser:
- Set base branch to `windows-port-m0-spike`.
- Paste the title and body above.

- [ ] **Step 5: Notify completion**

Reply in chat:

> "M1 core complete. PR: <url>. Headless snapshot binary at `windows/src/TokenSpendie.Windows/bin/Release/net8.0-windows/TokenSpendie.Windows.exe`. All <N> tests green. Ready for M2 (tray + popup)."

---

## Self-review

**Spec coverage.** The M1 milestone line in the spec is "Models, data layer, providers, full test coverage. Headless console binary that prints snapshots — proves the data path end to end." All called-out spec sections are realized in this plan:

- "Module mapping" rows from Swift to C# — Tasks 2–11 cover every row whose Swift origin is in `Model/` or `Data/`.
- "Credential reading" — `ICredentialReader` (Task 6) + `ClaudeJsonFileReader` (Task 6) + `OAuthCredentialsParser` (Task 5) + G9 concurrent-write retry (Task 6 Step 4).
- "Project layout" — Task 1 creates `windows/TokenSpendie.Windows.sln`, `Directory.Build.props`, `src/TokenSpendie.Windows/`, `tests/TokenSpendie.Windows.Tests/`. The WPF-specific files (`App.xaml`, `Themes/`, etc.) are deferred to M2 — the spec's M1 row does not call for them.
- "Testing" — every M1 file listed under "Testing" has a paired tests file in this plan, with concrete test cases.
- "Risks and unknowns" — G8 (HttpClient pooling) is in Task 8; G9 (concurrent rewrite) is in Task 6; U1, U2, U3 already resolved in M0 and applied here (Tasks 5, 6, 10).

**Out-of-scope items.** WPF UI, tray icon, popup, floating panel, preferences store, snapshot cache, usage notifier, launch-at-login service, installer, code signing, CI workflow — all deferred to M2-M4 per the spec's milestone table.

**Placeholder scan.** No `TBD` / `<…>` recording slots in this plan (M0 had those by design; M1 does not). Every code block is complete. Every command is runnable verbatim.

**Type consistency.**
- `ICredentialReader.LoadCredentialsAsync(CancellationToken ct = default)` — same signature in Task 6 (definition) and Task 9 (call site).
- `IClaudeUsageEndpoint.FetchUsageAsync(string accessToken, CancellationToken ct = default)` — same in Task 8 and Task 9.
- `IUsageProvider` (Task 9) → consumed by `Program.cs` (Task 12). Both `ClaudeProvider` and `GeminiProvider` implement it.
- `ClaudeProvider.Convert` is `static` (Task 9 Step 4) and is tested as `static` (Task 9 Step 1).
- `GeminiProvider.Convert(int count, DateTimeOffset resetsAt, DateTimeOffset now)` — same in Task 11 definition and tests.
- Exception types (`CredentialNotFoundException`, `ProviderUnauthorizedException`, etc.) defined in Task 4 and used in Tasks 5, 6, 8, 9, 12 — same names throughout.
- `UsageDecoder.Decode(ReadOnlySpan<byte>, DateTimeOffset)` signature is consistent between Task 7 definition and Task 8 call.

**Cross-task dependencies.**
- Task 1 builds infrastructure; every later task depends on it.
- Task 2–4 (Models) have no external dependencies inside this plan.
- Task 5 depends on Task 4 (`CredentialMalformedException`).
- Task 6 depends on Task 5 (`OAuthCredentialsParser`) and Task 4 (exceptions).
- Task 7 depends on Task 4 (`ProviderBadResponseException`) and Tasks 2–3 (`UsageWindow`, `UsageSnapshot`, `ModelWeekly`).
- Task 8 depends on Task 7 (`UsageDecoder`) and Task 4 (provider exceptions).
- Task 9 depends on Tasks 5, 6, 8 (and indirectly all earlier).
- Tasks 10–11 depend on Tasks 2–3 (models).
- Task 12 depends on every prior implementation task.
- Task 13 is a packaging step with no code dependencies.

**Branch and commit discipline.** Branch created in Task 1 Step 1 (off `windows-port-m0-spike` per the chosen base-branch decision). Every task ends in a commit. Task 13 secret-scans every commit before pushing.
