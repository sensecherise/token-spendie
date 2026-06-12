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

/// The raw bytes of a successful quota response plus which RPC produced them
/// (the two calls have different top-level shapes).
struct AntigravityQuotaData: Equatable {
    enum Source: Equatable { case userStatus, commandModelConfigs }
    let source: Source
    let body: Data
}

/// Abstracts the probe so `AntigravityProvider` is testable without processes
/// or sockets.
protocol AntigravityProbing {
    /// True when an Antigravity IDE / `agy` language-server process is running.
    /// Cheap-ish (one `ps` scan); never prompts.
    func isProcessPresent() -> Bool
    /// Full pipeline: process → ports → reachable endpoint → `GetUserStatus`
    /// (fallback `GetCommandModelConfigs`). Throws `ProviderError.notRunning`
    /// when no process is found, `.network`/`.badResponse` otherwise.
    func fetchQuotaData() async throws -> AntigravityQuotaData
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
