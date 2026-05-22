import Foundation

/// Injected HTTP transport: performs a request and returns the body + HTTP response.
typealias HTTPTransport = (URLRequest) async throws -> (Data, HTTPURLResponse)

/// Default transport backed by `URLSession`.
enum DefaultTransport {
    static let shared: HTTPTransport = { request in
        do {
            let (data, response) = try await URLSession.shared.data(for: request)
            guard let http = response as? HTTPURLResponse else { throw ProviderError.network }
            return (data, http)
        } catch let error as ProviderError {
            throw error
        } catch {
            throw ProviderError.network
        }
    }
}

/// The raw Claude usage HTTP call: given a valid access token, returns a
/// decoded `UsageSnapshot`. Implemented by `EndpointUsageProvider`.
protocol ClaudeUsageEndpoint {
    func fetchUsage(accessToken: String) async throws -> UsageSnapshot
}

/// One trackable AI CLI. Each conformer owns its own credential discovery and
/// fetch, and returns a normalized `ProviderSnapshot`.
protocol UsageProvider {
    var id: ProviderID { get }
    var displayName: String { get }
    /// Are this CLI's credentials present? Must be cheap and must not trigger
    /// a credential-consent prompt.
    func detectCredentials() -> Bool
    func fetchUsage() async throws -> ProviderSnapshot
}
