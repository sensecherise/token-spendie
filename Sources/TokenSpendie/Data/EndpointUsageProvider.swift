import Foundation

/// Fetches usage from the dedicated `/api/oauth/usage` endpoint.
struct EndpointUsageProvider: UsageProvider {
    private let transport: HTTPTransport
    private let now: () -> Date
    private let url = URL(string: "https://api.anthropic.com/api/oauth/usage")!

    init(transport: @escaping HTTPTransport = DefaultTransport.shared,
         now: @escaping () -> Date = Date.init) {
        self.transport = transport
        self.now = now
    }

    func fetchUsage(accessToken: String) async throws -> UsageSnapshot {
        // The endpoint returns 429 (not 401) for a missing/empty bearer, which
        // would be mislabeled as a rate limit. Never send an empty token.
        guard !accessToken.isEmpty else { throw ProviderError.unauthorized }
        var request = URLRequest(url: url)
        request.httpMethod = "GET"
        request.setValue("Bearer \(accessToken)", forHTTPHeaderField: "Authorization")
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        request.setValue("TokenSpendie/1.0", forHTTPHeaderField: "User-Agent")

        let (data, response) = try await transport(request)
        switch response.statusCode {
        case 200:
            return try UsageDecoder.decode(data, fetchedAt: now())
        case 401:
            throw ProviderError.unauthorized
        case 429:
            // Honor `Retry-After` (integer seconds) when present.
            let retryAfter = response.value(forHTTPHeaderField: "Retry-After")
                .flatMap { TimeInterval($0) }
            throw ProviderError.rateLimited(retryAfter: retryAfter)
        default:
            throw ProviderError.badResponse
        }
    }
}
