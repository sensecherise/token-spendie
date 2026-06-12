# Antigravity Provider Implementation Plan (Phase 2)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a best-effort Google Antigravity usage row (per-model quota windows) to Token Spendie on macOS and Windows by probing the language server that the Antigravity IDE / `agy` CLI runs locally — no credentials, no login flow.

**Architecture:** `AntigravityProvider` conforms to `UsageProvider` / `IUsageProvider`. A separate `AntigravityProbe` unit does the platform-specific work: find the language-server process (command-line scan), find its listening ports, then POST Connect-RPC requests to `127.0.0.1`. The provider decodes `GetUserStatus` into per-model windows. Row semantics: detected = process running OR cached snapshot < 7 days; probe failure degrades to the cached snapshot (`stale`). New `ProviderError.notRunning` → `UsageError.antigravityNotRunning`. Spec: `docs/superpowers/specs/2026-06-12-codex-antigravity-providers-design.md`.

**Tech Stack:** Swift (Foundation `Process` for `ps`/`lsof`, `URLSession` with a localhost-scoped TLS delegate); C# .NET 8 (`System.Management` WMI for command lines, `GetExtendedTcpTable` P/Invoke for ports, `HttpClientHandler` with a localhost-scoped certificate callback). Tests: XCTest / xUnit — all parsing and mapping is pure functions; the process/network edges sit behind small protocols/interfaces.

**Branch:** `feature/antigravity-provider` off `develop` (after the Codex PR merges — this plan assumes `ProviderID.codex` etc. already exist). Never `git add` the TEMP-debug working-tree files.

**Reference protocol facts** (verified 2026-06-12 against CodexBar, MIT — `docs/antigravity.md` + `AntigravityStatusProbe.swift`):

- **Process match** (case-insensitive on the full command line):
  - IDE language server: path component `language_server` (optionally suffixed `_macos*` / `.exe`) AND an Antigravity marker (`--app_data_dir … antigravity` or `/antigravity/` / `\antigravity\` in the path). Requires a `--csrf_token <token>` argument — a tokenless IDE match is skipped. May advertise `--extension_server_port <port>`.
  - CLI language server: path component `antigravity-cli` / `antigravity_cli`, or the `agy` binary (path-anchored). No CSRF flag — empty token is sent.
- **Ports:** every listening TCP port of the matched PID. macOS: `lsof -nP -iTCP -sTCP:LISTEN -p <pid>` (lines contain `:<port> (LISTEN)`). Windows: `GetExtendedTcpTable(TCP_TABLE_OWNER_PID_LISTENER)` filtered by PID.
- **Endpoint candidates, in order:** `https://127.0.0.1:<port>` for each lsof/tcp-table port, then `http://127.0.0.1:<extension_server_port>` (IDE only). The server's TLS cert is self-signed → trust override **only when host is 127.0.0.1/localhost**.
- **Requests** (all POST, JSON body, headers `Content-Type: application/json`, `Connect-Protocol-Version: 1`, `X-Codeium-Csrf-Token: <token-or-empty>`):
  - Reachability probe: `/exa.language_server_pb.LanguageServerService/GetUnleashData` — any HTTP response (even non-200) marks the endpoint reachable; first reachable endpoint wins.
  - Quota: `/exa.language_server_pb.LanguageServerService/GetUserStatus`, body `{"metadata": {"ideName": "antigravity", "extensionName": "antigravity", "ideVersion": "unknown", "locale": "en"}}`.
  - Fallback quota: `…/GetCommandModelConfigs`, same body.
- **`GetUserStatus` response** (fields may be absent; a non-zero/`"ok"`-less top-level `code` means error):

```json
{
  "userStatus": {
    "email": "user@example.com",
    "userTier": {"name": "Google AI Pro"},
    "planStatus": {"planInfo": {"planDisplayName": "Pro"}},
    "cascadeModelConfigData": {
      "clientModelConfigs": [
        {"label": "Claude Sonnet 4.5", "modelOrAlias": {"model": "MODEL_CLAUDE_4_5_SONNET"},
         "quotaInfo": {"remainingFraction": 0.82, "resetTime": "2026-06-12T18:00:00Z"}},
        {"label": "Gemini 3 Pro (Low)", "modelOrAlias": {"model": "MODEL_GEMINI_3_PRO_LOW"},
         "quotaInfo": {"remainingFraction": 0.4, "resetTime": "1765562400"}},
        {"label": "Internal", "modelOrAlias": {"model": "MODEL_X"}}
      ]
    }
  }
}
```

  `GetCommandModelConfigs` returns `{"clientModelConfigs": [...]}` at the top level (no plan/email). `resetTime` is ISO-8601 or epoch seconds-as-string. Plan name preference: `userTier.name`, else `planStatus.planInfo.planDisplayName/displayName/productName/planName/planShortName` (first non-empty).

---

## File structure

**macOS (Swift):**

| File | Responsibility |
|---|---|
| `Sources/TokenSpendie/Model/UsageModels.swift` (modify) | `ProviderID.antigravity`, `ProviderError.notRunning`, `UsageError.antigravityNotRunning` |
| `Sources/TokenSpendie/Data/AntigravityProbe.swift` (create) | process scan, port discovery, endpoint resolution, Connect-RPC POSTs, localhost TLS delegate; pure parse functions exposed for tests |
| `Sources/TokenSpendie/Data/AntigravityProvider.swift` (create) | `UsageProvider` conformance, `GetUserStatus`/`GetCommandModelConfigs` decoding, 7-day cache row TTL |
| `Sources/TokenSpendie/Store/UsageStore.swift` (modify) | map `notRunning` via `degrade` |
| `Sources/TokenSpendie/UI/DetailPanelView.swift` (modify) | panel copy for `antigravityNotRunning` |
| `Sources/TokenSpendie/AppDelegate.swift` (modify) | register provider |
| `Tests/TokenSpendieTests/AntigravityProbeTests.swift`, `Tests/TokenSpendieTests/AntigravityProviderTests.swift` (create) | unit tests |

**Windows (C#):** `Models/ProviderID.cs` + `Models/Errors.cs` (cases), new `Data/AntigravityProbe.cs` (WMI command-line scan, `GetExtendedTcpTable` interop, localhost-scoped `HttpClient`), new `Data/AntigravityProvider.cs`, `Services/UsageStore.cs` catch arm, `App.xaml.cs` registration, `windows/src/TokenSpendie.Windows/TokenSpendie.Windows.csproj` (add `System.Management`), tests under `windows/tests/TokenSpendie.Windows.Tests/Data/`. The widget-board `SnapshotFetcher` is NOT touched (spec: no probing in the COM server).

---

### Task 0: Branch

- [ ] **Step 0.1: Create the feature branch**

```bash
git checkout develop && git pull && git checkout -b feature/antigravity-provider
```

---

### Task 1: Model cases (Swift)

**Files:**
- Modify: `Sources/TokenSpendie/Model/UsageModels.swift`
- Modify: `Sources/TokenSpendie/UI/DetailPanelView.swift`
- Modify: `Sources/TokenSpendie/Store/UsageStore.swift`
- Test: `Tests/TokenSpendieTests/ProviderModelsTests.swift`

- [ ] **Step 1.1: Write the failing test**

Append to `Tests/TokenSpendieTests/ProviderModelsTests.swift`:

```swift
func testAntigravityProviderIDRawValueRoundTrips() throws {
    XCTAssertEqual(ProviderID.antigravity.rawValue, "antigravity")
    let decoded = try JSONDecoder().decode(ProviderID.self,
                                           from: Data(#""antigravity""#.utf8))
    XCTAssertEqual(decoded, .antigravity)
}
```

- [ ] **Step 1.2: Run — expect FAIL**

Run: `swift test --filter ProviderModelsTests 2>&1 | tail -5`
Expected: compile error `type 'ProviderID' has no member 'antigravity'`.

- [ ] **Step 1.3: Add the cases**

`Sources/TokenSpendie/Model/UsageModels.swift`:

```swift
enum ProviderID: String, Codable, CaseIterable, Equatable {
    case claude
    case gemini
    case codex
    case antigravity
}
```

`UsageError` gains (after `.codexReauthRequired`):

```swift
    case antigravityNotRunning  // probe found no Antigravity/agy process
```

`ProviderError` gains (after `.reauthRequired`):

```swift
    case notRunning             // local data source's process is not running
```

- [ ] **Step 1.4: Panel copy**

In `Sources/TokenSpendie/UI/DetailPanelView.swift`, `panelMessage(for:)` gains:

```swift
case .antigravityNotRunning:
    return ("🛸", "Antigravity isn't running. Open the Antigravity IDE or run `agy` — usage appears while it's running.")
```

- [ ] **Step 1.5: Store mapping (soft failure — keep stale cache when present)**

In `Sources/TokenSpendie/Store/UsageStore.swift`, inside `refresh(_:ignoringBackoff:)`, after the `catch ProviderError.reauthRequired` arm:

```swift
} catch ProviderError.notRunning {
    degrade(to: .antigravityNotRunning, for: id)
```

- [ ] **Step 1.6: Run full suite — expect PASS, commit**

Run: `swift test 2>&1 | tail -3`

```bash
git add Sources/TokenSpendie/Model/UsageModels.swift Sources/TokenSpendie/Store/UsageStore.swift Sources/TokenSpendie/UI/DetailPanelView.swift Tests/TokenSpendieTests/ProviderModelsTests.swift
git commit -m "feat(antigravity): provider id and not-running error cases"
```

---

### Task 2: Probe parsing — pure functions (Swift)

**Files:**
- Create: `Sources/TokenSpendie/Data/AntigravityProbe.swift` (parsing half)
- Test: `Tests/TokenSpendieTests/AntigravityProbeTests.swift`

- [ ] **Step 2.1: Write the failing tests**

Create `Tests/TokenSpendieTests/AntigravityProbeTests.swift`:

```swift
import XCTest
@testable import TokenSpendie

final class AntigravityProbeTests: XCTestCase {
    // MARK: - Process matching

    func testMatchesIDELanguageServerWithCSRFToken() throws {
        let ps = """
          312 /usr/libexec/syslogd
          845 /Applications/Antigravity.app/Contents/Resources/app/extensions/antigravity/bin/language_server_macos_arm --csrf_token abc123 --app_data_dir /Users/x/antigravity --extension_server_port 42100
        """
        let match = try XCTUnwrap(AntigravityProbe.firstMatch(inProcessList: ps))
        XCTAssertEqual(match.pid, 845)
        XCTAssertEqual(match.csrfToken, "abc123")
        XCTAssertEqual(match.extensionPort, 42100)
    }

    func testSkipsIDEServerWithoutCSRFToken() {
        let ps = "  845 /apps/antigravity/bin/language_server_macos --app_data_dir antigravity"
        XCTAssertNil(AntigravityProbe.firstMatch(inProcessList: ps))
    }

    func testMatchesAgyCLIWithEmptyCSRFToken() throws {
        let ps = "  77 /Users/cherise/.local/bin/agy chat"
        let match = try XCTUnwrap(AntigravityProbe.firstMatch(inProcessList: ps))
        XCTAssertEqual(match.pid, 77)
        XCTAssertEqual(match.csrfToken, "")
        XCTAssertNil(match.extensionPort)
    }

    func testMatchesAntigravityCLIPathSegment() throws {
        let ps = "  91 /Users/x/.gemini/antigravity-cli/bin/language_server --port 1"
        XCTAssertEqual(try XCTUnwrap(AntigravityProbe.firstMatch(inProcessList: ps)).pid, 91)
    }

    func testRejectsLookalikes() {
        // "antigravity" only in an argument; "agy" not path-anchored; unrelated server.
        let ps = """
          11 /usr/bin/vim antigravity-notes.md
          12 /usr/local/bin/biology --mode agy
          13 /opt/other/language_server_macos --csrf_token zzz
        """
        XCTAssertNil(AntigravityProbe.firstMatch(inProcessList: ps))
    }

    // MARK: - lsof parsing

    func testParsesListeningPorts() {
        let lsof = """
        COMMAND   PID USER   FD   TYPE  DEVICE SIZE/OFF NODE NAME
        language  845 user   23u  IPv4  0x0        0t0  TCP 127.0.0.1:42100 (LISTEN)
        language  845 user   24u  IPv6  0x0        0t0  TCP [::1]:42101 (LISTEN)
        language  845 user   25u  IPv4  0x0        0t0  TCP 127.0.0.1:9999->127.0.0.1:1234 (ESTABLISHED)
        """
        XCTAssertEqual(AntigravityProbe.parseListeningPorts(lsof), [42100, 42101])
    }

    // MARK: - Endpoint candidates

    func testCandidateOrderHTTPSPortsThenHTTPExtensionPort() {
        let endpoints = AntigravityProbe.candidateEndpoints(
            listeningPorts: [42100, 42101], extensionPort: 42100, csrfToken: "t")
        XCTAssertEqual(endpoints.map { "\($0.scheme):\($0.port)" },
                       ["https:42100", "https:42101", "http:42100"])
        XCTAssertEqual(endpoints[0].csrfToken, "t")
    }

    // MARK: - Localhost-only TLS policy

    func testTrustPolicyOnlyAcceptsLocalhost() {
        XCTAssertTrue(AntigravityProbe.shouldTrustHost("127.0.0.1"))
        XCTAssertTrue(AntigravityProbe.shouldTrustHost("LOCALHOST"))
        XCTAssertFalse(AntigravityProbe.shouldTrustHost("example.com"))
        XCTAssertFalse(AntigravityProbe.shouldTrustHost("127.0.0.1.evil.com"))
    }
}
```

- [ ] **Step 2.2: Run — expect FAIL**

Run: `swift test --filter AntigravityProbeTests 2>&1 | tail -5`
Expected: compile error `cannot find 'AntigravityProbe' in scope`.

- [ ] **Step 2.3: Implement the parsing half**

Create `Sources/TokenSpendie/Data/AntigravityProbe.swift`:

```swift
import Foundation

/// What the probe needs from a matched Antigravity language-server process.
struct AntigravityProcessMatch: Equatable {
    let pid: Int
    /// IDE servers carry `--csrf_token`; the `agy` CLI server needs none ("").
    let csrfToken: String
    /// The IDE's plain-HTTP fallback port, when advertised.
    let extensionPort: Int?
}

/// One localhost endpoint candidate to try, in order.
struct AntigravityEndpoint: Equatable {
    let scheme: String   // "https" (language server) or "http" (extension port)
    let port: Int
    let csrfToken: String
}

/// Abstracts the probe so `AntigravityProvider` is testable without processes
/// or sockets.
protocol AntigravityProbing {
    /// True when an Antigravity IDE / `agy` language-server process is running.
    /// Cheap-ish (one `ps` scan); never prompts.
    func isProcessPresent() -> Bool
    /// Full pipeline: process → ports → reachable endpoint → `GetUserStatus`
    /// (fallback `GetCommandModelConfigs`). Returns the raw response body and
    /// which call produced it. Throws `ProviderError.notRunning` when no
    /// process is found, `.network`/`.badResponse` otherwise.
    func fetchQuotaData() async throws -> AntigravityQuotaData
}

/// The raw bytes of a successful quota response plus which RPC produced them
/// (the two calls have different top-level shapes).
struct AntigravityQuotaData: Equatable {
    enum Source: Equatable { case userStatus, commandModelConfigs }
    let source: Source
    let body: Data
}

/// Finds the local Antigravity language server and speaks just enough
/// Connect-RPC to read per-model quota. Protocol facts verified against
/// CodexBar (MIT) 2026-06-12 — internal protocol, fields may change; every
/// failure here is best-effort by design.
struct AntigravityProbe: AntigravityProbing {
    // MARK: - Pure parsing (unit-tested)

    /// Scans `ps -ax -o pid=,command=` output for the first usable match.
    /// IDE matches without a CSRF token are skipped (the server rejects
    /// tokenless calls); `agy`/`antigravity-cli` matches use an empty token.
    static func firstMatch(inProcessList output: String) -> AntigravityProcessMatch? {
        for line in output.split(separator: "\n") {
            let trimmed = line.trimmingCharacters(in: .whitespaces)
            let parts = trimmed.split(separator: " ", maxSplits: 1)
            guard parts.count == 2, let pid = Int(parts[0]) else { continue }
            let command = String(parts[1])
            guard let kind = processKind(of: command) else { continue }
            let token = extractFlag("--csrf_token", from: command)
            switch kind {
            case .ide:
                guard let token else { continue }   // tokenless IDE → skip
                return AntigravityProcessMatch(
                    pid: pid, csrfToken: token,
                    extensionPort: extractFlag("--extension_server_port", from: command)
                        .flatMap(Int.init))
            case .cli:
                return AntigravityProcessMatch(pid: pid, csrfToken: token ?? "",
                                               extensionPort: nil)
            }
        }
        return nil
    }

    private enum ProcessKind { case ide, cli }

    private static func processKind(of command: String) -> ProcessKind? {
        let lower = command.lowercased()
        let isLanguageServer = lower.range(
            of: #"(^|[/\\])language_server(_macos\w*|\.exe)?(\s|$)"#,
            options: .regularExpression) != nil
        // NOTE: the IDE marker is "/antigravity/" — deliberately NOT
        // "/antigravity-cli/", which is the CLI install dir and must fall
        // through to the .cli branch (its server takes no CSRF token).
        let isAntigravity = (lower.contains("--app_data_dir") && lower.contains("antigravity"))
            || lower.contains("/antigravity/") || lower.contains("\\antigravity\\")
        if isLanguageServer && isAntigravity { return .ide }
        let isCLI = lower.range(
            of: #"(^|[/\\])(antigravity-cli|antigravity_cli)([\s/\\]|$)"#,
            options: .regularExpression) != nil
            || lower.range(of: #"(^|[/\\])agy(\s|$)"#, options: .regularExpression) != nil
        return isCLI ? .cli : nil
    }

    private static func extractFlag(_ flag: String, from command: String) -> String? {
        let pattern = "\(NSRegularExpression.escapedPattern(for: flag))[=\\s]+([^\\s]+)"
        guard let regex = try? NSRegularExpression(pattern: pattern, options: .caseInsensitive),
              let match = regex.firstMatch(
                in: command, range: NSRange(command.startIndex..., in: command)),
              let range = Range(match.range(at: 1), in: command) else { return nil }
        return String(command[range])
    }

    /// Extracts listening ports from `lsof -nP -iTCP -sTCP:LISTEN -p <pid>`.
    static func parseListeningPorts(_ output: String) -> [Int] {
        var ports: [Int] = []
        for line in output.split(separator: "\n") where line.contains("(LISTEN)") {
            // "… TCP 127.0.0.1:42100 (LISTEN)" / "… TCP [::1]:42101 (LISTEN)"
            guard let addrField = line.split(separator: " ")
                .first(where: { $0.contains(":") && !$0.hasPrefix("TCP") }),
                  let portText = addrField.split(separator: ":").last,
                  let port = Int(portText), !ports.contains(port) else { continue }
            ports.append(port)
        }
        return ports
    }

    /// HTTPS on every listening port first, then plain HTTP on the IDE's
    /// extension port.
    static func candidateEndpoints(listeningPorts: [Int], extensionPort: Int?,
                                   csrfToken: String) -> [AntigravityEndpoint] {
        var endpoints = listeningPorts.map {
            AntigravityEndpoint(scheme: "https", port: $0, csrfToken: csrfToken)
        }
        if let extensionPort {
            endpoints.append(AntigravityEndpoint(scheme: "http", port: extensionPort,
                                                 csrfToken: csrfToken))
        }
        return endpoints
    }

    /// The self-signed-cert override applies ONLY to loopback hosts.
    static func shouldTrustHost(_ host: String) -> Bool {
        let normalized = host.lowercased()
        return normalized == "127.0.0.1" || normalized == "localhost" || normalized == "::1"
    }

    // ... transport half added in Task 3 ...
}
```

(The file does not compile yet as a probe — `isProcessPresent`/`fetchQuotaData` come in Task 3. To keep this task green, add temporary stubs:)

```swift
    func isProcessPresent() -> Bool { Self.firstMatch(inProcessList: Self.runProcessList()) != nil }
    func fetchQuotaData() async throws -> AntigravityQuotaData { throw ProviderError.notRunning }
    static func runProcessList() -> String { "" }   // replaced in Task 3
```

- [ ] **Step 2.4: Run — expect PASS, commit**

Run: `swift test --filter AntigravityProbeTests 2>&1 | tail -3`

```bash
git add Sources/TokenSpendie/Data/AntigravityProbe.swift Tests/TokenSpendieTests/AntigravityProbeTests.swift
git commit -m "feat(antigravity): probe process/port/endpoint parsing"
```

---

### Task 3: Probe transport (Swift)

**Files:**
- Modify: `Sources/TokenSpendie/Data/AntigravityProbe.swift` (replace the Task 2 stubs)

The transport half shells out to `ps`/`lsof` and POSTs to localhost. It is
exercised manually (Task 5) — its parseable seams were unit-tested in Task 2.

- [ ] **Step 3.1: Implement the transport**

Replace the Task 2 stub block in `Sources/TokenSpendie/Data/AntigravityProbe.swift` with:

```swift
    // MARK: - Transport (manual-tested; seams above are unit-tested)

    private static let basePath = "/exa.language_server_pb.LanguageServerService"
    private static let requestTimeout: TimeInterval = 4

    func isProcessPresent() -> Bool {
        Self.firstMatch(inProcessList: Self.runProcessList()) != nil
    }

    func fetchQuotaData() async throws -> AntigravityQuotaData {
        guard let match = Self.firstMatch(inProcessList: Self.runProcessList()) else {
            throw ProviderError.notRunning
        }
        let ports = Self.parseListeningPorts(Self.runLsof(pid: match.pid))
        let candidates = Self.candidateEndpoints(listeningPorts: ports,
                                                 extensionPort: match.extensionPort,
                                                 csrfToken: match.csrfToken)
        guard !candidates.isEmpty else { throw ProviderError.notRunning }

        guard let endpoint = await Self.firstReachable(of: candidates) else {
            throw ProviderError.network
        }
        let metadataBody: [String: Any] = ["metadata": [
            "ideName": "antigravity", "extensionName": "antigravity",
            "ideVersion": "unknown", "locale": "en",
        ]]
        if let body = try? await Self.post(path: "\(Self.basePath)/GetUserStatus",
                                           json: metadataBody, to: endpoint) {
            return AntigravityQuotaData(source: .userStatus, body: body)
        }
        let fallback = try await Self.post(path: "\(Self.basePath)/GetCommandModelConfigs",
                                           json: metadataBody, to: endpoint)
        return AntigravityQuotaData(source: .commandModelConfigs, body: fallback)
    }

    /// `ps -ax -o pid=,command=` — every process with full command line.
    static func runProcessList() -> String {
        runCommand("/bin/ps", ["-ax", "-o", "pid=,command="])
    }

    static func runLsof(pid: Int) -> String {
        let path = ["/usr/sbin/lsof", "/usr/bin/lsof"]
            .first { FileManager.default.isExecutableFile(atPath: $0) }
        guard let path else { return "" }
        return runCommand(path, ["-nP", "-iTCP", "-sTCP:LISTEN", "-p", String(pid)])
    }

    private static func runCommand(_ launchPath: String, _ arguments: [String]) -> String {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: launchPath)
        process.arguments = arguments
        let stdout = Pipe()
        process.standardOutput = stdout
        process.standardError = Pipe()
        do {
            try process.run()
        } catch {
            return ""
        }
        let data = stdout.fileHandleForReading.readDataToEndOfFile()
        process.waitUntilExit()
        return String(data: data, encoding: .utf8) ?? ""
    }

    /// First endpoint that answers `GetUnleashData` with ANY HTTP response —
    /// even an error status proves the right server is on that port.
    private static func firstReachable(of endpoints: [AntigravityEndpoint]) async -> AntigravityEndpoint? {
        let probeBody: [String: Any] = ["context": ["properties": [
            "ide": "antigravity", "ideVersion": "unknown",
            "installationId": "tokenspendie", "os": "macos",
        ]]]
        for endpoint in endpoints {
            do {
                _ = try await post(path: "\(basePath)/GetUnleashData",
                                   json: probeBody, to: endpoint)
                return endpoint
            } catch ProviderError.badResponse {
                return endpoint   // HTTP answered with a non-200 — still our server
            } catch {
                continue          // connection refused / TLS to a non-HTTP port
            }
        }
        return nil
    }

    /// One Connect-RPC POST. 200 → body; other HTTP status → `badResponse`;
    /// transport failure → `network`.
    private static func post(path: String, json: [String: Any],
                             to endpoint: AntigravityEndpoint) async throws -> Data {
        guard let url = URL(string: "\(endpoint.scheme)://127.0.0.1:\(endpoint.port)\(path)") else {
            throw ProviderError.network
        }
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.httpBody = try? JSONSerialization.data(withJSONObject: json)
        request.timeoutInterval = requestTimeout
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.setValue("1", forHTTPHeaderField: "Connect-Protocol-Version")
        request.setValue(endpoint.csrfToken, forHTTPHeaderField: "X-Codeium-Csrf-Token")

        let config = URLSessionConfiguration.ephemeral
        config.timeoutIntervalForRequest = requestTimeout
        config.timeoutIntervalForResource = requestTimeout
        let session = URLSession(configuration: config,
                                 delegate: LocalhostTrustDelegate(), delegateQueue: nil)
        defer { session.finishTasksAndInvalidate() }

        let data: Data
        let response: URLResponse
        do {
            (data, response) = try await session.data(for: request)
        } catch {
            throw ProviderError.network
        }
        guard let http = response as? HTTPURLResponse else { throw ProviderError.network }
        guard http.statusCode == 200 else { throw ProviderError.badResponse }
        return data
    }
}

/// Accepts the language server's self-signed certificate — but only for
/// loopback hosts. Any other host falls through to default TLS validation.
private final class LocalhostTrustDelegate: NSObject, URLSessionDelegate {
    func urlSession(_ session: URLSession,
                    didReceive challenge: URLAuthenticationChallenge,
                    completionHandler: @escaping (URLSession.AuthChallengeDisposition, URLCredential?) -> Void) {
        let space = challenge.protectionSpace
        guard space.authenticationMethod == NSURLAuthenticationMethodServerTrust,
              AntigravityProbe.shouldTrustHost(space.host),
              let trust = space.serverTrust else {
            completionHandler(.performDefaultHandling, nil)
            return
        }
        completionHandler(.useCredential, URLCredential(trust: trust))
    }
}
```

- [ ] **Step 3.2: Build + full suite — expect PASS, commit**

Run: `swift build 2>&1 | tail -3 && swift test 2>&1 | tail -3`

```bash
git add Sources/TokenSpendie/Data/AntigravityProbe.swift
git commit -m "feat(antigravity): localhost Connect-RPC transport with scoped TLS trust"
```

---

### Task 4: AntigravityProvider (Swift)

**Files:**
- Create: `Sources/TokenSpendie/Data/AntigravityProvider.swift`
- Test: `Tests/TokenSpendieTests/AntigravityProviderTests.swift`

- [ ] **Step 4.1: Write the failing tests**

Create `Tests/TokenSpendieTests/AntigravityProviderTests.swift`:

```swift
import XCTest
@testable import TokenSpendie

private struct ProbeStub: AntigravityProbing {
    var present = true
    var result: Result<AntigravityQuotaData, ProviderError> =
        .failure(.notRunning)

    func isProcessPresent() -> Bool { present }
    func fetchQuotaData() async throws -> AntigravityQuotaData {
        try result.get()
    }
}

final class AntigravityProviderTests: XCTestCase {
    private var cacheURL: URL!

    override func setUpWithError() throws {
        cacheURL = FileManager.default.temporaryDirectory
            .appendingPathComponent("ag-cache-\(UUID().uuidString).json")
    }

    override func tearDownWithError() throws {
        try? FileManager.default.removeItem(at: cacheURL)
    }

    private static let userStatusBody = Data(#"""
    {
      "userStatus": {
        "email": "user@example.com",
        "userTier": {"name": "Google AI Pro"},
        "planStatus": {"planInfo": {"planDisplayName": "Pro"}},
        "cascadeModelConfigData": {
          "clientModelConfigs": [
            {"label": "Claude Sonnet 4.5", "modelOrAlias": {"model": "M_CLAUDE"},
             "quotaInfo": {"remainingFraction": 0.82, "resetTime": "2026-06-12T18:00:00Z"}},
            {"label": "Gemini 3 Pro (Low)", "modelOrAlias": {"model": "M_PRO_LOW"},
             "quotaInfo": {"remainingFraction": 0.4, "resetTime": "1765562400"}},
            {"label": "No Quota Model", "modelOrAlias": {"model": "M_X"}}
          ]
        }
      }
    }
    """#.utf8)

    private static let commandConfigsBody = Data(#"""
    {"clientModelConfigs": [
       {"label": "Gemini 3 Flash", "modelOrAlias": {"model": "M_FLASH"},
        "quotaInfo": {"remainingFraction": 0.95}}
    ]}
    """#.utf8)

    private let now = { UsageDecoder.parseDate("2026-06-12T10:00:00Z")! }

    private func provider(_ probe: ProbeStub) -> AntigravityProvider {
        AntigravityProvider(probe: probe,
                            cache: SnapshotCache(fileURL: cacheURL),
                            now: now)
    }

    // MARK: - Decoding

    func testFetchDecodesUserStatusIntoWindows() async throws {
        var probe = ProbeStub()
        probe.result = .success(AntigravityQuotaData(source: .userStatus,
                                                     body: Self.userStatusBody))
        let snapshot = try await provider(probe).fetchUsage()

        XCTAssertEqual(snapshot.id, .antigravity)
        XCTAssertEqual(snapshot.plan, "Google AI Pro")
        XCTAssertEqual(snapshot.windows.map(\.label),
                       ["Claude Sonnet 4.5", "Gemini 3 Pro (Low)"])
        // Headline = highest used: 1 - 0.4 = 60%.
        XCTAssertEqual(snapshot.headline.label, "Gemini 3 Pro (Low)")
        XCTAssertEqual(snapshot.headline.window.percent, 60, accuracy: 0.001)
        XCTAssertEqual(snapshot.windows[0].window.percent, 18, accuracy: 0.001)
        // ISO reset on window 0; epoch-string reset on window 1.
        XCTAssertEqual(snapshot.windows[0].window.resetsAt,
                       UsageDecoder.parseDate("2026-06-12T18:00:00Z"))
        XCTAssertEqual(snapshot.windows[1].window.resetsAt,
                       Date(timeIntervalSince1970: 1765562400))
        XCTAssertEqual(snapshot.windows[0].resetStyle, .countdown)
        XCTAssertEqual(snapshot.fetchedAt, now())
        XCTAssertNotNil(snapshot.note)
    }

    func testFetchDecodesCommandModelConfigsFallback() async throws {
        var probe = ProbeStub()
        probe.result = .success(AntigravityQuotaData(source: .commandModelConfigs,
                                                     body: Self.commandConfigsBody))
        let snapshot = try await provider(probe).fetchUsage()
        XCTAssertEqual(snapshot.windows.map(\.label), ["Gemini 3 Flash"])
        XCTAssertEqual(snapshot.headline.window.percent, 5, accuracy: 0.001)
        XCTAssertNil(snapshot.plan)
    }

    func testErrorCodeOrNoWindowsThrowsBadResponse() async {
        for body in [
            Data(#"{"code": 16, "message": "unauthenticated"}"#.utf8),
            Data(#"{"userStatus": {"cascadeModelConfigData": {"clientModelConfigs": []}}}"#.utf8),
            Data("not json".utf8),
        ] {
            var probe = ProbeStub()
            probe.result = .success(AntigravityQuotaData(source: .userStatus, body: body))
            do {
                _ = try await provider(probe).fetchUsage()
                XCTFail("expected badResponse")
            } catch {
                XCTAssertEqual(error as? ProviderError, .badResponse)
            }
        }
    }

    func testFetchWithoutProcessThrowsNotRunning() async {
        let probe = ProbeStub(present: false, result: .failure(.notRunning))
        do {
            _ = try await provider(probe).fetchUsage()
            XCTFail("expected notRunning")
        } catch {
            XCTAssertEqual(error as? ProviderError, .notRunning)
        }
    }

    // MARK: - Row semantics (detect = process OR cache < 7 days)

    func testDetectTrueWhenProcessRuns() {
        XCTAssertTrue(provider(ProbeStub(present: true)).detectCredentials())
    }

    func testDetectUsesCacheTTLWhenProcessAbsent() {
        let probe = ProbeStub(present: false)
        let window = LabeledWindow(label: "Claude", detail: "quota",
                                   resetStyle: .countdown,
                                   window: UsageWindow(percent: 10, resetsAt: nil))

        // No cache → not detected.
        XCTAssertFalse(provider(probe).detectCredentials())

        // Fresh cache (3 days old) → detected.
        let cache = SnapshotCache(fileURL: cacheURL)
        cache.save(ProviderSnapshot(id: .antigravity, plan: nil, headline: window,
                                    windows: [window],
                                    fetchedAt: now().addingTimeInterval(-3 * 86400)))
        XCTAssertTrue(provider(probe).detectCredentials())

        // Stale cache (8 days old) → not detected.
        cache.save(ProviderSnapshot(id: .antigravity, plan: nil, headline: window,
                                    windows: [window],
                                    fetchedAt: now().addingTimeInterval(-8 * 86400)))
        XCTAssertFalse(provider(probe).detectCredentials())
    }
}
```

- [ ] **Step 4.2: Run — expect FAIL**

Run: `swift test --filter AntigravityProviderTests 2>&1 | tail -5`
Expected: compile error `cannot find 'AntigravityProvider' in scope`.

- [ ] **Step 4.3: Implement the provider**

Create `Sources/TokenSpendie/Data/AntigravityProvider.swift`:

```swift
import Foundation

/// The `UsageProvider` for Google Antigravity. Best-effort: reads per-model
/// quota from the language server the Antigravity IDE / `agy` CLI runs
/// locally (see `AntigravityProbe`). No credentials are read or stored.
///
/// Row semantics: the row exists while the process is running OR a cached
/// snapshot is newer than 7 days, so quitting Antigravity leaves a `stale`
/// row instead of dropping it.
struct AntigravityProvider: UsageProvider {
    let id: ProviderID = .antigravity
    let displayName: String = "Antigravity"

    /// How long a cached snapshot keeps the row alive without a process.
    static let cacheRowTTL: TimeInterval = 7 * 86400

    private let probe: AntigravityProbing
    /// Read-only handle on the same cache file `UsageStore` writes for this
    /// provider — used solely for the row-TTL check above.
    private let cache: SnapshotCache
    private let now: () -> Date

    init(probe: AntigravityProbing = AntigravityProbe(),
         cache: SnapshotCache = SnapshotCache(fileURL: SnapshotCache.defaultURL(for: .antigravity)),
         now: @escaping () -> Date = Date.init) {
        self.probe = probe
        self.cache = cache
        self.now = now
    }

    func detectCredentials() -> Bool {
        if probe.isProcessPresent() { return true }
        guard let cached = cache.load() else { return false }
        return now().timeIntervalSince(cached.fetchedAt) < Self.cacheRowTTL
    }

    func fetchUsage() async throws -> ProviderSnapshot {
        let quota = try await probe.fetchQuotaData()
        return try Self.decode(quota, fetchedAt: now())
    }

    // MARK: - Decoding

    /// Maps a quota payload to one window per model config that reports a
    /// `remainingFraction`. Headline = the highest-used window.
    static func decode(_ quota: AntigravityQuotaData, fetchedAt: Date) throws -> ProviderSnapshot {
        guard let root = (try? JSONSerialization.jsonObject(with: quota.body)) as? [String: Any] else {
            throw ProviderError.badResponse
        }
        // A non-zero / non-"ok" top-level `code` is an RPC error envelope.
        if let code = root["code"] {
            let okCodes: Set<String> = ["0", "ok", "success"]
            let text = String(describing: code).lowercased()
            if !okCodes.contains(text) { throw ProviderError.badResponse }
        }

        let configs: [[String: Any]]
        let plan: String?
        switch quota.source {
        case .userStatus:
            guard let userStatus = root["userStatus"] as? [String: Any] else {
                throw ProviderError.badResponse
            }
            let configData = userStatus["cascadeModelConfigData"] as? [String: Any]
            configs = configData?["clientModelConfigs"] as? [[String: Any]] ?? []
            plan = Self.planName(in: userStatus)
        case .commandModelConfigs:
            configs = root["clientModelConfigs"] as? [[String: Any]] ?? []
            plan = nil
        }

        let windows: [LabeledWindow] = configs.compactMap { config in
            guard let label = config["label"] as? String,
                  let quotaInfo = config["quotaInfo"] as? [String: Any],
                  let remaining = (quotaInfo["remainingFraction"] as? NSNumber)?.doubleValue else {
                return nil
            }
            let percent = min(max((1 - remaining) * 100, 0), 100)
            let resetsAt = (quotaInfo["resetTime"] as? String).flatMap(Self.parseResetTime)
            return LabeledWindow(label: label, detail: "model quota",
                                 resetStyle: .countdown,
                                 window: UsageWindow(percent: percent, resetsAt: resetsAt))
        }
        guard let headline = windows.max(by: { $0.window.percent < $1.window.percent }) else {
            throw ProviderError.badResponse
        }
        return ProviderSnapshot(id: .antigravity, plan: plan, headline: headline,
                                windows: windows, fetchedAt: fetchedAt,
                                note: "live while Antigravity runs")
    }

    /// `userTier.name` is the real subscription tier; `planInfo` names are the
    /// fallback chain (CodexBar-verified preference order).
    private static func planName(in userStatus: [String: Any]) -> String? {
        if let tier = userStatus["userTier"] as? [String: Any],
           let name = (tier["name"] as? String)?.trimmingCharacters(in: .whitespaces),
           !name.isEmpty {
            return name
        }
        guard let planStatus = userStatus["planStatus"] as? [String: Any],
              let planInfo = planStatus["planInfo"] as? [String: Any] else { return nil }
        for key in ["planDisplayName", "displayName", "productName", "planName", "planShortName"] {
            if let value = (planInfo[key] as? String)?.trimmingCharacters(in: .whitespaces),
               !value.isEmpty {
                return value
            }
        }
        return nil
    }

    /// `resetTime` arrives as ISO-8601 or epoch-seconds-as-string.
    private static func parseResetTime(_ value: String) -> Date? {
        if let date = UsageDecoder.parseDate(value) { return date }
        if let seconds = Double(value) { return Date(timeIntervalSince1970: seconds) }
        return nil
    }
}
```

- [ ] **Step 4.4: Run — expect PASS**

Run: `swift test --filter AntigravityProviderTests 2>&1 | tail -3`

- [ ] **Step 4.5: Full suite, commit**

Run: `swift test 2>&1 | tail -3`

```bash
git add Sources/TokenSpendie/Data/AntigravityProvider.swift Tests/TokenSpendieTests/AntigravityProviderTests.swift
git commit -m "feat(antigravity): provider with quota decoding and 7-day cache row TTL"
```

---

### Task 5: Register + manual smoke (macOS)

**Files:**
- Modify: `Sources/TokenSpendie/AppDelegate.swift:20`

- [ ] **Step 5.1: Register**

```swift
providers: [ClaudeProvider(), GeminiProvider(), CodexProvider(), AntigravityProvider()],
```

- [ ] **Step 5.2: Manual smoke with `agy`**

Run: `./build.sh && open build/TokenSpendie.app`
- Start `agy` in a terminal, wait for it to settle, click refresh → an ANTIGRAVITY section appears with per-model bars and (when `GetUserStatus` succeeded) a plan pill.
- Quit `agy`, refresh → the row stays, dimmed `stale`.
- Console check: `log stream --predicate 'process == "TokenSpendie"'` shows no token/secret values.

- [ ] **Step 5.3: Full suite, commit**

Run: `swift test 2>&1 | tail -3`

```bash
git add Sources/TokenSpendie/AppDelegate.swift
git commit -m "feat(antigravity): register AntigravityProvider on macOS"
```

---

### Task 6: Model cases (Windows)

**Files:**
- Modify: `windows/src/TokenSpendie.Windows/Models/ProviderID.cs`
- Modify: `windows/src/TokenSpendie.Windows/Models/Errors.cs`
- Modify: `windows/src/TokenSpendie.Windows/Services/UsageStore.cs`
- Test: `windows/tests/TokenSpendie.Windows.Tests/Models/ProviderIDTests.cs`

> All `dotnet` commands run from `windows/` on a Windows machine or CI.

- [ ] **Step 6.1: Write the failing test**

Append to `windows/tests/TokenSpendie.Windows.Tests/Models/ProviderIDTests.cs`:

```csharp
[Fact]
public void AntigravityCaseExists()
{
    System.Enum.GetNames<ProviderID>().Should().Contain("Antigravity");
    UsageErrorKind.AntigravityNotRunning.Should().BeDefined();
}
```

- [ ] **Step 6.2: Run — expect FAIL**

Run: `dotnet test tests/TokenSpendie.Windows.Tests --filter ProviderIDTests 2>&1 | tail -5`

- [ ] **Step 6.3: Add members, exception, and store mapping**

`Models/ProviderID.cs`:

```csharp
public enum ProviderID
{
    Claude,
    Gemini,
    Codex,
    Antigravity,
}
```

`Models/Errors.cs`:

```csharp
public enum ProviderErrorKind { Unauthorized, ReauthRequired, NotRunning, Network, BadResponse, RateLimited }
```

```csharp
public sealed class ProviderNotRunningException : ProviderException
{
    public override ProviderErrorKind Kind => ProviderErrorKind.NotRunning;
    public ProviderNotRunningException(string what)
        : base($"{what} is not running.") { }
}
```

```csharp
public enum UsageErrorKind
{
    ClaudeCodeNotFound,
    CredentialAccessDenied,
    LoginExpired,
    CodexReauthRequired,
    AntigravityNotRunning,
    Network,
    BadResponse,
}
```

`Services/UsageStore.cs` — after the `catch (ProviderReauthRequiredException)` arm:

```csharp
catch (ProviderNotRunningException)
{
    Degrade(UsageErrorKind.AntigravityNotRunning, id);
}
```

- [ ] **Step 6.4: Run — expect PASS, commit**

Run: `dotnet test tests/TokenSpendie.Windows.Tests --filter ProviderIDTests 2>&1 | tail -3`

```bash
git add windows/src/TokenSpendie.Windows/Models/ProviderID.cs windows/src/TokenSpendie.Windows/Models/Errors.cs windows/src/TokenSpendie.Windows/Services/UsageStore.cs windows/tests/TokenSpendie.Windows.Tests/Models/ProviderIDTests.cs
git commit -m "feat(antigravity,windows): provider id and not-running error plumbing"
```

---

### Task 7: AntigravityProbe (C#)

**Files:**
- Create: `windows/src/TokenSpendie.Windows/Data/AntigravityProbe.cs`
- Modify: `windows/src/TokenSpendie.Windows/TokenSpendie.Windows.csproj` (add `System.Management`)
- Test: `windows/tests/TokenSpendie.Windows.Tests/Data/AntigravityProbeTests.cs`

- [ ] **Step 7.1: Add the WMI package**

In `windows/src/TokenSpendie.Windows/TokenSpendie.Windows.csproj`, inside the existing `<ItemGroup>` with `PackageReference` entries:

```xml
<PackageReference Include="System.Management" Version="8.0.0" />
```

- [ ] **Step 7.2: Write the failing tests (pure parts)**

Create `windows/tests/TokenSpendie.Windows.Tests/Data/AntigravityProbeTests.cs`:

```csharp
using System.Linq;
using FluentAssertions;
using TokenSpendie.Windows.Data;
using Xunit;

namespace TokenSpendie.Windows.Tests.Data;

public class AntigravityProbeTests
{
    [Fact]
    public void MatchesIdeLanguageServerWithCsrfToken()
    {
        var match = AntigravityProbe.FirstMatch(new[]
        {
            (Pid: 312, CommandLine: @"C:\Windows\System32\svchost.exe -k netsvcs"),
            (Pid: 845, CommandLine:
                @"C:\Users\x\AppData\Local\Programs\Antigravity\resources\app\extensions\antigravity\bin\language_server.exe --csrf_token abc123 --app_data_dir C:\Users\x\.antigravity --extension_server_port 42100"),
        });
        match.Should().NotBeNull();
        match!.Value.Pid.Should().Be(845);
        match.Value.CsrfToken.Should().Be("abc123");
        match.Value.ExtensionPort.Should().Be(42100);
    }

    [Fact]
    public void SkipsIdeServerWithoutCsrfToken()
    {
        AntigravityProbe.FirstMatch(new[]
        {
            (Pid: 845, CommandLine: @"C:\apps\antigravity\language_server.exe --app_data_dir antigravity"),
        }).Should().BeNull();
    }

    [Fact]
    public void MatchesAgyCliWithEmptyCsrfToken()
    {
        var match = AntigravityProbe.FirstMatch(new[]
        {
            (Pid: 77, CommandLine: @"C:\Users\x\.local\bin\agy.exe chat"),
        });
        match!.Value.Pid.Should().Be(77);
        match.Value.CsrfToken.Should().Be("");
        match.Value.ExtensionPort.Should().BeNull();
    }

    [Fact]
    public void MatchesAntigravityCliPathSegmentAsCli()
    {
        // The CLI dir hosts a language_server binary too — it must classify
        // as CLI (no CSRF requirement), not as a tokenless IDE to skip.
        var match = AntigravityProbe.FirstMatch(new[]
        {
            (Pid: 91, CommandLine: @"C:\Users\x\.gemini\antigravity-cli\bin\language_server.exe --port 1"),
        });
        match!.Value.Pid.Should().Be(91);
        match.Value.CsrfToken.Should().Be("");
    }

    [Fact]
    public void RejectsLookalikes()
    {
        AntigravityProbe.FirstMatch(new[]
        {
            (Pid: 11, CommandLine: @"C:\tools\vim.exe antigravity-notes.md"),
            (Pid: 12, CommandLine: @"C:\bin\biology.exe --mode agy"),
            (Pid: 13, CommandLine: @"C:\opt\other\language_server.exe --csrf_token zzz"),
        }).Should().BeNull();
    }

    [Fact]
    public void CandidateOrderHttpsPortsThenHttpExtensionPort()
    {
        var endpoints = AntigravityProbe.CandidateEndpoints(
            new[] { 42100, 42101 }, extensionPort: 42100, csrfToken: "t");
        endpoints.Select(e => $"{e.Scheme}:{e.Port}")
            .Should().Equal("https:42100", "https:42101", "http:42100");
    }

    [Fact]
    public void TrustPolicyOnlyAcceptsLocalhost()
    {
        AntigravityProbe.ShouldTrustHost("127.0.0.1").Should().BeTrue();
        AntigravityProbe.ShouldTrustHost("LOCALHOST").Should().BeTrue();
        AntigravityProbe.ShouldTrustHost("example.com").Should().BeFalse();
        AntigravityProbe.ShouldTrustHost("127.0.0.1.evil.com").Should().BeFalse();
    }
}
```

- [ ] **Step 7.3: Run — expect FAIL**

Run: `dotnet test tests/TokenSpendie.Windows.Tests --filter AntigravityProbeTests 2>&1 | tail -5`

- [ ] **Step 7.4: Implement**

Create `windows/src/TokenSpendie.Windows/Data/AntigravityProbe.cs`:

```csharp
using System.Management;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using TokenSpendie.Windows.Models;

namespace TokenSpendie.Windows.Data;

public readonly record struct AntigravityProcessMatch(int Pid, string CsrfToken, int? ExtensionPort);
public readonly record struct AntigravityEndpoint(string Scheme, int Port, string CsrfToken);

/// <summary>Raw bytes of a successful quota response plus which RPC produced
/// them (the two calls have different top-level shapes).</summary>
public sealed record AntigravityQuotaData(AntigravityQuotaSource Source, byte[] Body);
public enum AntigravityQuotaSource { UserStatus, CommandModelConfigs }

public interface IAntigravityProbe
{
    /// <summary>True when an Antigravity IDE / agy language-server process is
    /// running. Cheap-ish (one WMI scan); never prompts.</summary>
    bool IsProcessPresent();
    /// <summary>Full pipeline: process → ports → reachable endpoint →
    /// GetUserStatus (fallback GetCommandModelConfigs). Throws
    /// <see cref="ProviderNotRunningException"/> when no process is found.</summary>
    Task<AntigravityQuotaData> FetchQuotaDataAsync(CancellationToken ct = default);
}

/// <summary>
/// Finds the local Antigravity language server and speaks just enough
/// Connect-RPC to read per-model quota. Protocol facts verified against
/// CodexBar (MIT) 2026-06-12 — internal protocol; every failure is
/// best-effort by design.
/// </summary>
public sealed class AntigravityProbe : IAntigravityProbe
{
    private const string BasePath = "/exa.language_server_pb.LanguageServerService";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(4);

    // MARK: Pure parsing (unit-tested)

    /// <summary>Scans (pid, command line) pairs for the first usable match.
    /// IDE matches without a CSRF token are skipped; agy/antigravity-cli
    /// matches use an empty token.</summary>
    public static AntigravityProcessMatch? FirstMatch(
        IEnumerable<(int Pid, string CommandLine)> processes)
    {
        foreach (var (pid, command) in processes)
        {
            if (string.IsNullOrWhiteSpace(command)) continue;
            var lower = command.ToLowerInvariant();
            var isLanguageServer = Regex.IsMatch(
                lower, @"(^|[/\\])language_server(_\w+|\.exe)?(\s|$)");
            // NOTE: the IDE marker is "\antigravity\" — deliberately NOT
            // "\antigravity-cli\", which is the CLI install dir and must fall
            // through to the CLI branch (its server takes no CSRF token).
            var isAntigravity =
                (lower.Contains("--app_data_dir") && lower.Contains("antigravity"))
                || lower.Contains(@"/antigravity/") || lower.Contains(@"\antigravity\");
            var token = ExtractFlag("--csrf_token", command);

            if (isLanguageServer && isAntigravity)
            {
                if (token is null) continue;   // tokenless IDE → skip
                int? extensionPort = ExtractFlag("--extension_server_port", command) is { } raw
                    && int.TryParse(raw, out var port) ? port : null;
                return new AntigravityProcessMatch(pid, token, extensionPort);
            }

            var isCli = Regex.IsMatch(lower, @"(^|[/\\])(antigravity-cli|antigravity_cli)([\s/\\]|$)")
                || Regex.IsMatch(lower, @"(^|[/\\])agy(\.exe)?(\s|$)");
            if (isCli)
                return new AntigravityProcessMatch(pid, token ?? "", null);
        }
        return null;
    }

    private static string? ExtractFlag(string flag, string command)
    {
        var match = Regex.Match(command, Regex.Escape(flag) + @"[=\s]+(\S+)",
                                RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>HTTPS on every listening port first, then plain HTTP on the
    /// IDE's extension port.</summary>
    public static IReadOnlyList<AntigravityEndpoint> CandidateEndpoints(
        IReadOnlyList<int> listeningPorts, int? extensionPort, string csrfToken)
    {
        var endpoints = listeningPorts
            .Select(p => new AntigravityEndpoint("https", p, csrfToken))
            .ToList();
        if (extensionPort is { } port)
            endpoints.Add(new AntigravityEndpoint("http", port, csrfToken));
        return endpoints;
    }

    /// <summary>The self-signed-cert override applies ONLY to loopback hosts.</summary>
    public static bool ShouldTrustHost(string host)
    {
        var normalized = host.ToLowerInvariant();
        return normalized is "127.0.0.1" or "localhost" or "::1";
    }

    // MARK: Process + port discovery (manual-tested edges)

    public bool IsProcessPresent() => FirstMatch(RunningProcesses()) is not null;

    /// <summary>All processes with command lines, via WMI (the only
    /// non-admin way to read another process's command line).</summary>
    private static IEnumerable<(int Pid, string CommandLine)> RunningProcesses()
    {
        var results = new List<(int, string)>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, CommandLine FROM Win32_Process WHERE CommandLine IS NOT NULL");
            foreach (var item in searcher.Get())
            {
                var pid = Convert.ToInt32(item["ProcessId"]);
                var command = item["CommandLine"] as string ?? "";
                results.Add((pid, command));
            }
        }
        catch
        {
            // WMI unavailable → behave as "not running" (best-effort).
        }
        return results;
    }

    /// <summary>Listening TCP ports owned by <paramref name="pid"/>, via
    /// GetExtendedTcpTable (TCP_TABLE_OWNER_PID_LISTENER).</summary>
    internal static IReadOnlyList<int> ListeningPorts(int pid)
    {
        const int AF_INET = 2;
        const int TCP_TABLE_OWNER_PID_LISTENER = 3;

        var size = 0;
        _ = GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET,
                                TCP_TABLE_OWNER_PID_LISTENER, 0);
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buffer, ref size, false, AF_INET,
                                    TCP_TABLE_OWNER_PID_LISTENER, 0) != 0)
                return Array.Empty<int>();

            var count = Marshal.ReadInt32(buffer);
            var ports = new List<int>();
            var rowPtr = buffer + 4;
            var rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();
            for (var i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(rowPtr + i * rowSize);
                if (row.OwningPid == pid)
                {
                    // dwLocalPort is in network byte order (high byte first).
                    var port = (ushort)IPAddress.NetworkToHostOrder((short)row.LocalPort);
                    if (!ports.Contains(port)) ports.Add(port);
                }
            }
            return ports;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public int OwningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable, ref int pdwSize, bool bOrder, int ulAf,
        int tableClass, uint reserved);

    // MARK: Connect-RPC transport

    public async Task<AntigravityQuotaData> FetchQuotaDataAsync(CancellationToken ct = default)
    {
        var match = FirstMatch(RunningProcesses())
            ?? throw new ProviderNotRunningException("Antigravity");
        var candidates = CandidateEndpoints(
            ListeningPorts(match.Pid), match.ExtensionPort, match.CsrfToken);
        if (candidates.Count == 0) throw new ProviderNotRunningException("Antigravity");

        var endpoint = await FirstReachableAsync(candidates, ct).ConfigureAwait(false)
            ?? throw new ProviderNetworkException(new HttpRequestException("no reachable Antigravity port"));

        var metadataBody = """
            {"metadata": {"ideName": "antigravity", "extensionName": "antigravity", "ideVersion": "unknown", "locale": "en"}}
            """;
        try
        {
            var body = await PostAsync($"{BasePath}/GetUserStatus", metadataBody, endpoint, ct)
                .ConfigureAwait(false);
            return new AntigravityQuotaData(AntigravityQuotaSource.UserStatus, body);
        }
        catch (ProviderException)
        {
            var body = await PostAsync($"{BasePath}/GetCommandModelConfigs", metadataBody, endpoint, ct)
                .ConfigureAwait(false);
            return new AntigravityQuotaData(AntigravityQuotaSource.CommandModelConfigs, body);
        }
    }

    /// <summary>First endpoint that answers GetUnleashData with ANY HTTP
    /// response — even an error status proves the right server is there.</summary>
    private static async Task<AntigravityEndpoint?> FirstReachableAsync(
        IReadOnlyList<AntigravityEndpoint> endpoints, CancellationToken ct)
    {
        var probeBody = """
            {"context": {"properties": {"ide": "antigravity", "ideVersion": "unknown", "installationId": "tokenspendie", "os": "windows"}}}
            """;
        foreach (var endpoint in endpoints)
        {
            try
            {
                _ = await PostAsync($"{BasePath}/GetUnleashData", probeBody, endpoint, ct)
                    .ConfigureAwait(false);
                return endpoint;
            }
            catch (ProviderBadResponseException)
            {
                return endpoint;   // HTTP answered with a non-200 — still our server
            }
            catch
            {
                // connection refused / TLS handshake to a non-HTTP port → next
            }
        }
        return null;
    }

    private static async Task<byte[]> PostAsync(
        string path, string jsonBody, AntigravityEndpoint endpoint, CancellationToken ct)
    {
        using var handler = new HttpClientHandler
        {
            // Self-signed local cert: trust ONLY loopback hosts.
            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
                errors == System.Net.Security.SslPolicyErrors.None
                || ShouldTrustHost(message.RequestUri?.Host ?? ""),
        };
        using var http = new HttpClient(handler) { Timeout = RequestTimeout };
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"{endpoint.Scheme}://127.0.0.1:{endpoint.Port}{path}")
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Connect-Protocol-Version", "1");
        request.Headers.Add("X-Codeium-Csrf-Token", endpoint.CsrfToken);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ProviderNetworkException(ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new ProviderBadResponseException($"status {(int)response.StatusCode}");
            return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        }
    }
}
```

- [ ] **Step 7.5: Run — expect PASS, commit**

Run: `dotnet test tests/TokenSpendie.Windows.Tests --filter AntigravityProbeTests 2>&1 | tail -3`

```bash
git add windows/src/TokenSpendie.Windows/Data/AntigravityProbe.cs windows/src/TokenSpendie.Windows/TokenSpendie.Windows.csproj windows/tests/TokenSpendie.Windows.Tests/Data/AntigravityProbeTests.cs
git commit -m "feat(antigravity,windows): probe with WMI scan, tcp-table ports and scoped TLS trust"
```

---

### Task 8: AntigravityProvider (C#)

**Files:**
- Create: `windows/src/TokenSpendie.Windows/Data/AntigravityProvider.cs`
- Test: `windows/tests/TokenSpendie.Windows.Tests/Data/AntigravityProviderTests.cs`

- [ ] **Step 8.1: Write the failing tests**

Create `windows/tests/TokenSpendie.Windows.Tests/Data/AntigravityProviderTests.cs`:

```csharp
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TokenSpendie.Windows.Data;
using TokenSpendie.Windows.Models;
using TokenSpendie.Windows.Services;
using Xunit;

namespace TokenSpendie.Windows.Tests.Data;

public sealed class AntigravityProviderTests : IDisposable
{
    private readonly string _cachePath =
        Path.Combine(Path.GetTempPath(), "ag-cache-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose() { try { File.Delete(_cachePath); } catch { } }

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-12T10:00:00Z");

    private const string UserStatusBody = """
    {
      "userStatus": {
        "userTier": {"name": "Google AI Pro"},
        "planStatus": {"planInfo": {"planDisplayName": "Pro"}},
        "cascadeModelConfigData": {
          "clientModelConfigs": [
            {"label": "Claude Sonnet 4.5", "modelOrAlias": {"model": "M_CLAUDE"},
             "quotaInfo": {"remainingFraction": 0.82, "resetTime": "2026-06-12T18:00:00Z"}},
            {"label": "Gemini 3 Pro (Low)", "modelOrAlias": {"model": "M_PRO_LOW"},
             "quotaInfo": {"remainingFraction": 0.4, "resetTime": "1765562400"}},
            {"label": "No Quota Model", "modelOrAlias": {"model": "M_X"}}
          ]
        }
      }
    }
    """;

    private AntigravityProvider Provider(IAntigravityProbe probe) =>
        new(probe, new SnapshotCache(_cachePath), () => Now);

    private static AntigravityQuotaData Quota(string body,
        AntigravityQuotaSource source = AntigravityQuotaSource.UserStatus) =>
        new(source, Encoding.UTF8.GetBytes(body));

    [Fact]
    public async Task FetchDecodesUserStatusIntoWindows()
    {
        var probe = Substitute.For<IAntigravityProbe>();
        probe.FetchQuotaDataAsync(Arg.Any<CancellationToken>())
            .Returns(Quota(UserStatusBody));

        var snapshot = await Provider(probe).FetchUsageAsync();

        snapshot.Id.Should().Be(ProviderID.Antigravity);
        snapshot.Plan.Should().Be("Google AI Pro");
        snapshot.Windows.Select(w => w.Label)
            .Should().Equal("Claude Sonnet 4.5", "Gemini 3 Pro (Low)");
        snapshot.Headline.Label.Should().Be("Gemini 3 Pro (Low)");
        snapshot.Headline.Window.Percent.Should().BeApproximately(60, 0.001);
        snapshot.Windows[0].Window.Percent.Should().BeApproximately(18, 0.001);
        snapshot.Windows[0].Window.ResetsAt
            .Should().Be(DateTimeOffset.Parse("2026-06-12T18:00:00Z"));
        snapshot.Windows[1].Window.ResetsAt
            .Should().Be(DateTimeOffset.FromUnixTimeSeconds(1765562400));
        snapshot.FetchedAt.Should().Be(Now);
        snapshot.Note.Should().NotBeNull();
    }

    [Fact]
    public async Task FetchDecodesCommandModelConfigsFallback()
    {
        var probe = Substitute.For<IAntigravityProbe>();
        probe.FetchQuotaDataAsync(Arg.Any<CancellationToken>())
            .Returns(Quota("""
                {"clientModelConfigs": [
                  {"label": "Gemini 3 Flash", "modelOrAlias": {"model": "M_FLASH"},
                   "quotaInfo": {"remainingFraction": 0.95}}
                ]}
                """, AntigravityQuotaSource.CommandModelConfigs));

        var snapshot = await Provider(probe).FetchUsageAsync();
        snapshot.Windows.Should().HaveCount(1);
        snapshot.Headline.Window.Percent.Should().BeApproximately(5, 0.001);
        snapshot.Plan.Should().BeNull();
    }

    [Theory]
    [InlineData("""{"code": 16, "message": "unauthenticated"}""")]
    [InlineData("""{"userStatus": {"cascadeModelConfigData": {"clientModelConfigs": []}}}""")]
    [InlineData("not json")]
    public async Task BadPayloadThrowsBadResponse(string body)
    {
        var probe = Substitute.For<IAntigravityProbe>();
        probe.FetchQuotaDataAsync(Arg.Any<CancellationToken>()).Returns(Quota(body));
        await Provider(probe).Invoking(p => p.FetchUsageAsync())
            .Should().ThrowAsync<ProviderBadResponseException>();
    }

    [Fact]
    public async Task FetchWithoutProcessThrowsNotRunning()
    {
        var probe = Substitute.For<IAntigravityProbe>();
        probe.FetchQuotaDataAsync(Arg.Any<CancellationToken>())
            .Throws(new ProviderNotRunningException("Antigravity"));
        await Provider(probe).Invoking(p => p.FetchUsageAsync())
            .Should().ThrowAsync<ProviderNotRunningException>();
    }

    [Fact]
    public void DetectTrueWhenProcessRuns()
    {
        var probe = Substitute.For<IAntigravityProbe>();
        probe.IsProcessPresent().Returns(true);
        Provider(probe).DetectCredentials().Should().BeTrue();
    }

    [Fact]
    public void DetectUsesCacheTtlWhenProcessAbsent()
    {
        var probe = Substitute.For<IAntigravityProbe>();
        probe.IsProcessPresent().Returns(false);
        var window = new LabeledWindow("Claude", "quota", ResetStyle.Countdown,
                                       new UsageWindow(10, null));

        Provider(probe).DetectCredentials().Should().BeFalse("no cache yet");

        var cache = new SnapshotCache(_cachePath);
        cache.Save(new ProviderSnapshot(ProviderID.Antigravity, null, window,
            new[] { window }, Now.AddDays(-3)));
        Provider(probe).DetectCredentials().Should().BeTrue("cache is 3 days old");

        cache.Save(new ProviderSnapshot(ProviderID.Antigravity, null, window,
            new[] { window }, Now.AddDays(-8)));
        Provider(probe).DetectCredentials().Should().BeFalse("cache is 8 days old");
    }
}
```

- [ ] **Step 8.2: Run — expect FAIL**

Run: `dotnet test tests/TokenSpendie.Windows.Tests --filter AntigravityProviderTests 2>&1 | tail -5`

- [ ] **Step 8.3: Implement**

Create `windows/src/TokenSpendie.Windows/Data/AntigravityProvider.cs`:

```csharp
using System.Text.Json.Nodes;
using TokenSpendie.Windows.Models;
using TokenSpendie.Windows.Services;

namespace TokenSpendie.Windows.Data;

/// <summary>
/// The <see cref="IUsageProvider"/> for Google Antigravity. Best-effort: reads
/// per-model quota from the language server the Antigravity IDE / agy CLI
/// runs locally (see <see cref="AntigravityProbe"/>). No credentials are read
/// or stored. Row semantics: the row exists while the process is running OR a
/// cached snapshot is newer than 7 days.
/// </summary>
public sealed class AntigravityProvider : IUsageProvider
{
    public ProviderID Id => ProviderID.Antigravity;
    public string DisplayName => "Antigravity";

    /// <summary>How long a cached snapshot keeps the row alive without a process.</summary>
    public static readonly TimeSpan CacheRowTtl = TimeSpan.FromDays(7);

    private readonly IAntigravityProbe _probe;
    private readonly SnapshotCache _cache;
    private readonly Func<DateTimeOffset> _now;

    public AntigravityProvider(IAntigravityProbe probe, SnapshotCache cache,
                               Func<DateTimeOffset>? now = null)
    {
        _probe = probe;
        _cache = cache;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Production wiring. The cache handle points at the same file
    /// <see cref="UsageStore"/> writes for this provider — read-only here,
    /// used solely for the row-TTL check.</summary>
    public AntigravityProvider()
        : this(new AntigravityProbe(),
               new SnapshotCache(SnapshotCache.DefaultPathFor(ProviderID.Antigravity))) { }

    public bool DetectCredentials()
    {
        if (_probe.IsProcessPresent()) return true;
        var cached = _cache.Load();
        return cached is not null && (_now() - cached.FetchedAt) < CacheRowTtl;
    }

    public async Task<ProviderSnapshot> FetchUsageAsync(CancellationToken ct = default)
    {
        var quota = await _probe.FetchQuotaDataAsync(ct).ConfigureAwait(false);
        return Decode(quota, _now());
    }

    /// <summary>Maps a quota payload to one window per model config that
    /// reports a remainingFraction. Headline = the highest-used window.</summary>
    public static ProviderSnapshot Decode(AntigravityQuotaData quota, DateTimeOffset fetchedAt)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(quota.Body);
        }
        catch (Exception ex)
        {
            throw new ProviderBadResponseException("unparseable Antigravity payload", ex);
        }

        // A non-zero / non-"ok" top-level `code` is an RPC error envelope.
        if (root?["code"] is { } code)
        {
            var text = code.ToString().ToLowerInvariant();
            if (text is not ("0" or "ok" or "success"))
                throw new ProviderBadResponseException($"Antigravity RPC code {text}");
        }

        JsonNode? configsNode;
        string? plan = null;
        if (quota.Source == AntigravityQuotaSource.UserStatus)
        {
            var userStatus = root?["userStatus"]
                ?? throw new ProviderBadResponseException("missing userStatus");
            configsNode = userStatus["cascadeModelConfigData"]?["clientModelConfigs"];
            plan = PlanName(userStatus);
        }
        else
        {
            configsNode = root?["clientModelConfigs"];
        }

        var windows = new List<LabeledWindow>();
        foreach (var config in configsNode?.AsArray() ?? new JsonArray())
        {
            var label = config?["label"]?.GetValue<string>();
            var quotaInfo = config?["quotaInfo"];
            if (label is null || quotaInfo?["remainingFraction"] is not { } remainingNode) continue;
            var percent = Math.Clamp((1 - remainingNode.GetValue<double>()) * 100, 0, 100);
            DateTimeOffset? resetsAt = quotaInfo["resetTime"]?.GetValue<string>() is { } reset
                ? ParseResetTime(reset)
                : null;
            windows.Add(new LabeledWindow(label, "model quota", ResetStyle.Countdown,
                                          new UsageWindow(percent, resetsAt)));
        }
        if (windows.Count == 0)
            throw new ProviderBadResponseException("no model quotas in Antigravity payload");

        var headline = windows.MaxBy(w => w.Window.Percent)!;
        return new ProviderSnapshot(
            Id: ProviderID.Antigravity, Plan: plan,
            Headline: headline, Windows: windows,
            FetchedAt: fetchedAt,
            Note: "live while Antigravity runs");
    }

    private static string? PlanName(JsonNode userStatus)
    {
        if (userStatus["userTier"]?["name"]?.GetValue<string>()?.Trim() is { Length: > 0 } tier)
            return tier;
        var planInfo = userStatus["planStatus"]?["planInfo"];
        foreach (var key in new[] { "planDisplayName", "displayName", "productName", "planName", "planShortName" })
        {
            if (planInfo?[key]?.GetValue<string>()?.Trim() is { Length: > 0 } value)
                return value;
        }
        return null;
    }

    /// <summary>resetTime arrives as ISO-8601 or epoch-seconds-as-string.</summary>
    private static DateTimeOffset? ParseResetTime(string value)
    {
        if (DateTimeOffset.TryParse(value, out var parsed)) return parsed;
        if (double.TryParse(value, out var seconds))
            return DateTimeOffset.FromUnixTimeSeconds((long)seconds);
        return null;
    }
}
```

- [ ] **Step 8.4: Run — expect PASS, then full suite, commit**

Run: `dotnet test tests/TokenSpendie.Windows.Tests 2>&1 | tail -3`

```bash
git add windows/src/TokenSpendie.Windows/Data/AntigravityProvider.cs windows/tests/TokenSpendie.Windows.Tests/Data/AntigravityProviderTests.cs
git commit -m "feat(antigravity,windows): provider with quota decoding and cache row TTL"
```

---

### Task 9: Register on Windows + finish

**Files:**
- Modify: `windows/src/TokenSpendie.Windows/App.xaml.cs:42-47`

- [ ] **Step 9.1: Register**

```csharp
var providers = new IUsageProvider[]
{
    new ClaudeProvider(new ClaudeJsonFileReader(), new EndpointUsageProvider()),
    new GeminiProvider(),
    new CodexProvider(),
    new AntigravityProvider(),
};
```

(`SnapshotFetcher` in the widget COM server is deliberately NOT extended — spec.)

- [ ] **Step 9.2: Build + full Windows suite — expect PASS, commit**

Run: `dotnet build src/TokenSpendie.Windows && dotnet test tests/TokenSpendie.Windows.Tests 2>&1 | tail -3`

```bash
git add windows/src/TokenSpendie.Windows/App.xaml.cs
git commit -m "feat(antigravity,windows): register AntigravityProvider"
```

- [ ] **Step 9.3: Manual smoke (Windows machine)**

Run the tray app with the Antigravity IDE (or agy) running → ANTIGRAVITY row with per-model bars; quit Antigravity → stale row persists.

- [ ] **Step 9.4: PR**

Use the superpowers:finishing-a-development-branch skill. PR `feature/antigravity-provider` → `develop`.
