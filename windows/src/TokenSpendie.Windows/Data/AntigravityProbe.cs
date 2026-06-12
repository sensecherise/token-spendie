using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TokenSpendie.Windows.Models;

namespace TokenSpendie.Windows.Data;

/// <summary>What the probe needs from a matched Antigravity language-server
/// process.</summary>
/// <remarks>IDE servers carry <c>--csrf_token</c>; the <c>agy</c> CLI server
/// needs none ("").</remarks>
public readonly record struct AntigravityProcessMatch(int Pid, string CsrfToken, int? ExtensionPort);

/// <summary>One localhost endpoint candidate to try, in order. <c>Scheme</c> is
/// "https" (language server) or "http" (extension port).</summary>
public readonly record struct AntigravityEndpoint(string Scheme, int Port, string CsrfToken);

/// <summary>Which RPC produced a successful quota response (the two calls have
/// different top-level shapes).</summary>
public enum AntigravityQuotaSource { UserStatus, CommandModelConfigs }

/// <summary>The raw bytes of a successful quota response plus its source.</summary>
public sealed record AntigravityQuotaData(AntigravityQuotaSource Source, byte[] Body);

/// <summary>Abstracts the probe so <see cref="AntigravityProvider"/> is testable
/// without processes or sockets.</summary>
public interface IAntigravityProbe
{
    /// <summary>True when an Antigravity IDE / <c>agy</c> language-server process
    /// is running. Cheap-ish (one WMI scan); never prompts.</summary>
    bool IsProcessPresent();

    /// <summary>Full pipeline: process → ports → reachable endpoint →
    /// <c>GetUserStatus</c> (fallback <c>GetCommandModelConfigs</c>). Throws
    /// <see cref="ProviderNotRunningException"/> when no process is found,
    /// network/bad-response otherwise.</summary>
    Task<AntigravityQuotaData> FetchQuotaDataAsync(CancellationToken ct = default);
}

/// <summary>
/// Finds the local Antigravity language server and speaks just enough
/// Connect-RPC to read per-model quota. Protocol facts verified against
/// CodexBar (MIT) 2026-06-12 — internal protocol, fields may change; every
/// failure here is best-effort by design. Never logs token values.
/// </summary>
public sealed class AntigravityProbe : IAntigravityProbe
{
    private const string BasePath = "/exa.language_server_pb.LanguageServerService";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(4);

    // MARK: - Pure parsing (unit-tested)

    /// <summary>Scans process command lines for the first usable match. IDE
    /// matches without a CSRF token are skipped (the server rejects tokenless
    /// calls); <c>agy</c>/<c>antigravity-cli</c> matches use an empty token.</summary>
    public static AntigravityProcessMatch? FirstMatch(IEnumerable<(int Pid, string CommandLine)> processes)
    {
        foreach (var (pid, commandLine) in processes)
        {
            if (string.IsNullOrEmpty(commandLine)) continue;
            var kind = ProcessKind(commandLine);
            if (kind is null) continue;
            var token = ExtractFlag("--csrf_token", commandLine);
            switch (kind)
            {
                case Kind.Ide:
                    if (token is null) continue; // tokenless IDE → skip
                    var port = ExtractFlag("--extension_server_port", commandLine);
                    int? extensionPort = port is not null && int.TryParse(port, out var p) ? p : null;
                    return new AntigravityProcessMatch(pid, token, extensionPort);
                case Kind.Cli:
                    return new AntigravityProcessMatch(pid, token ?? "", null);
            }
        }
        return null;
    }

    private enum Kind { Ide, Cli }

    private static Kind? ProcessKind(string command)
    {
        var lower = command.ToLowerInvariant();
        var isLanguageServer = Regex.IsMatch(
            lower, @"(^|[/\\])language_server(_\w+|\.exe)?(\s|$)");
        // NOTE: the IDE marker is "antigravity" — deliberately NOT
        // "/antigravity-cli/" or "\antigravity-cli\", which is the CLI install
        // dir and must fall through to the .cli branch (its server takes no
        // CSRF token). Hence the path markers use the bare-segment form.
        var isAntigravity = (lower.Contains("--app_data_dir") && lower.Contains("antigravity"))
            || lower.Contains("/antigravity/") || lower.Contains(@"\antigravity\");
        if (isLanguageServer && isAntigravity) return Kind.Ide;
        var isCli = Regex.IsMatch(lower, @"(^|[/\\])(antigravity-cli|antigravity_cli)([\s/\\]|$)")
            || Regex.IsMatch(lower, @"(^|[/\\])agy(\.exe)?(\s|$)");
        return isCli ? Kind.Cli : (Kind?)null;
    }

    private static string? ExtractFlag(string flag, string command)
    {
        var match = Regex.Match(command, Regex.Escape(flag) + @"[=\s]+(\S+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>HTTPS on every listening port first, then plain HTTP on the
    /// IDE's extension port.</summary>
    public static IReadOnlyList<AntigravityEndpoint> CandidateEndpoints(
        IReadOnlyList<int> listeningPorts, int? extensionPort, string csrfToken)
    {
        var endpoints = listeningPorts
            .Select(port => new AntigravityEndpoint("https", port, csrfToken))
            .ToList();
        if (extensionPort is { } ep)
            endpoints.Add(new AntigravityEndpoint("http", ep, csrfToken));
        return endpoints;
    }

    /// <summary>The self-signed-cert override applies ONLY to loopback hosts.</summary>
    public static bool ShouldTrustHost(string host)
    {
        var normalized = host.ToLowerInvariant();
        return normalized is "127.0.0.1" or "localhost" or "::1";
    }

    // MARK: - Process scan (best effort)

    public bool IsProcessPresent() => FirstMatch(EnumerateProcesses()) is not null;

    /// <summary>Every process with a non-null command line, via WMI. Any failure
    /// (WMI unavailable, access denied) degrades to an empty sequence.</summary>
    private static IEnumerable<(int Pid, string CommandLine)> EnumerateProcesses()
    {
        var results = new List<(int, string)>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, CommandLine FROM Win32_Process WHERE CommandLine IS NOT NULL");
            foreach (var obj in searcher.Get())
            {
                using (obj)
                {
                    var commandLine = obj["CommandLine"] as string;
                    if (commandLine is null) continue;
                    var pid = obj["ProcessId"] is { } raw ? System.Convert.ToInt32(raw) : 0;
                    results.Add((pid, commandLine));
                }
            }
        }
        catch
        {
            return System.Array.Empty<(int, string)>();
        }
        return results;
    }

    // MARK: - Listening ports (TCP table, filtered by pid)

    private const int AfInet = 2;                  // AF_INET
    private const int TcpTableOwnerPidListener = 3; // TCP_TABLE_OWNER_PID_LISTENER

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;   // big-endian in the low 16 bits
        public uint RemoteAddr;
        public uint RemotePort;
        public int OwningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable, ref int pdwSize, bool bOrder, int ulAf, int tableClass, uint reserved);

    /// <summary>IPv4 TCP ports in LISTEN state owned by <paramref name="pid"/>,
    /// deduplicated. Best effort: any failure yields an empty list.</summary>
    internal static IReadOnlyList<int> ListeningPorts(int pid)
    {
        var ports = new List<int>();
        int size = 0;
        // First call sizes the buffer.
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, AfInet, TcpTableOwnerPidListener, 0);
        if (size == 0) return ports;

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buffer, ref size, false, AfInet, TcpTableOwnerPidListener, 0) != 0)
                return ports;

            var rowCount = Marshal.ReadInt32(buffer);
            var rowPtr = IntPtr.Add(buffer, sizeof(int)); // table follows the dwNumEntries header
            var rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();
            for (var i = 0; i < rowCount; i++)
            {
                var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(rowPtr);
                rowPtr = IntPtr.Add(rowPtr, rowSize);
                if (row.OwningPid != pid) continue;
                // LocalPort is network byte order: low byte then high byte.
                var port = ((int)(row.LocalPort & 0xFF) << 8) | (int)((row.LocalPort >> 8) & 0xFF);
                if (!ports.Contains(port)) ports.Add(port);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
        return ports;
    }

    // MARK: - Transport (manual-tested; seams above are unit-tested)

    public async Task<AntigravityQuotaData> FetchQuotaDataAsync(CancellationToken ct = default)
    {
        if (FirstMatch(EnumerateProcesses()) is not { } match)
            throw new ProviderNotRunningException("Antigravity");

        var ports = ListeningPorts(match.Pid);
        var candidates = CandidateEndpoints(ports, match.ExtensionPort, match.CsrfToken);
        // The process IS running here — an empty port list (no listeners found
        // yet) is "unreachable", not "not running", so the panel doesn't tell
        // the user to launch an app they have open.
        if (candidates.Count == 0)
            throw new ProviderNetworkException(
                new HttpRequestException("no listening ports found for Antigravity language server"));

        var endpoint = await FirstReachableAsync(candidates, ct).ConfigureAwait(false);
        if (endpoint is not { } reachable)
            throw new ProviderNetworkException(
                new HttpRequestException("no reachable Antigravity language server endpoint"));

        var metadataBody = new JsonObject
        {
            ["metadata"] = new JsonObject
            {
                ["ideName"] = "antigravity",
                ["extensionName"] = "antigravity",
                ["ideVersion"] = "unknown",
                ["locale"] = "en",
            },
        }.ToJsonString();

        try
        {
            var body = await PostAsync($"{BasePath}/GetUserStatus", metadataBody, reachable, ct)
                .ConfigureAwait(false);
            return new AntigravityQuotaData(AntigravityQuotaSource.UserStatus, body);
        }
        catch (ProviderException)
        {
            // Older builds expose only GetCommandModelConfigs; let its failure
            // propagate.
            var fallback = await PostAsync(
                $"{BasePath}/GetCommandModelConfigs", metadataBody, reachable, ct).ConfigureAwait(false);
            return new AntigravityQuotaData(AntigravityQuotaSource.CommandModelConfigs, fallback);
        }
    }

    /// <summary>First endpoint that answers <c>GetUnleashData</c> with ANY HTTP
    /// response — even an error status proves the right server is on that
    /// port.</summary>
    private static async Task<AntigravityEndpoint?> FirstReachableAsync(
        IReadOnlyList<AntigravityEndpoint> endpoints, CancellationToken ct)
    {
        var probeBody = new JsonObject
        {
            ["context"] = new JsonObject
            {
                ["properties"] = new JsonObject
                {
                    ["ide"] = "antigravity",
                    ["ideVersion"] = "unknown",
                    ["installationId"] = "tokenspendie",
                    ["os"] = "windows",
                },
            },
        }.ToJsonString();

        foreach (var endpoint in endpoints)
        {
            try
            {
                await PostAsync($"{BasePath}/GetUnleashData", probeBody, endpoint, ct).ConfigureAwait(false);
                return endpoint;
            }
            catch (ProviderBadResponseException)
            {
                return endpoint; // HTTP answered with a non-200 — still our server
            }
            catch
            {
                // connection refused / TLS to a non-HTTP port → next candidate
            }
        }
        return null;
    }

    /// <summary>One Connect-RPC POST. Success → body bytes; other HTTP status →
    /// <see cref="ProviderBadResponseException"/>; transport failure →
    /// <see cref="ProviderNetworkException"/>. The self-signed-cert override is
    /// scoped to loopback hosts only.</summary>
    private static async Task<byte[]> PostAsync(
        string path, string json, AntigravityEndpoint endpoint, CancellationToken ct)
    {
        var url = $"{endpoint.Scheme}://127.0.0.1:{endpoint.Port}{path}";

        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
                errors == SslPolicyErrors.None
                || ShouldTrustHost(message.RequestUri?.Host ?? ""),
        };
        using var http = new HttpClient(handler) { Timeout = RequestTimeout };

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Connect-Protocol-Version", "1");
        request.Headers.Add("X-Codeium-Csrf-Token", endpoint.CsrfToken);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new ProviderNetworkException(ex);
        }
        catch (TaskCanceledException ex)
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
