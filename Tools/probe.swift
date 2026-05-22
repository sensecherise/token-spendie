// Tools/probe.swift — run with: swift Tools/probe.swift
// Verifies the Keychain item and the /api/oauth/usage endpoint. Prints no token values.
import Foundation
import Security

func keychainBlob() -> Data? {
    let query: [String: Any] = [
        kSecClass as String: kSecClassGenericPassword,
        kSecAttrService as String: "Claude Code-credentials",
        kSecReturnData as String: true,
        kSecMatchLimit as String: kSecMatchLimitOne,
    ]
    var out: AnyObject?
    let status = SecItemCopyMatching(query as CFDictionary, &out)
    guard status == errSecSuccess else {
        FileHandle.standardError.write(Data("SecItemCopyMatching status: \(status)\n".utf8))
        return nil
    }
    return out as? Data
}

guard let blob = keychainBlob() else {
    print("No Keychain item — is Claude Code installed and logged in?")
    exit(1)
}
guard let root = try? JSONSerialization.jsonObject(with: blob) as? [String: Any],
      let oauth = root["claudeAiOauth"] as? [String: Any],
      let token = oauth["accessToken"] as? String else {
    print("Could not parse claudeAiOauth.accessToken from the Keychain blob")
    exit(1)
}
print("Keychain oauth keys: \(oauth.keys.sorted())")
print("expiresAt raw value: \(oauth["expiresAt"] ?? "nil")")
print("accessToken length: \(token.count)")

var request = URLRequest(url: URL(string: "https://api.anthropic.com/api/oauth/usage")!)
request.httpMethod = "GET"
request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
request.setValue("application/json", forHTTPHeaderField: "Accept")
request.setValue("TokenSpendie/probe", forHTTPHeaderField: "User-Agent")

let semaphore = DispatchSemaphore(value: 0)
URLSession.shared.dataTask(with: request) { data, response, error in
    defer { semaphore.signal() }
    if let error = error { print("Network error: \(error)"); return }
    let http = response as! HTTPURLResponse
    print("HTTP status: \(http.statusCode)")
    print("anthropic-ratelimit-unified-status: \(http.value(forHTTPHeaderField: "anthropic-ratelimit-unified-status") ?? "—")")
    print("anthropic-ratelimit-unified-reset: \(http.value(forHTTPHeaderField: "anthropic-ratelimit-unified-reset") ?? "—")")
    if let data = data, let body = String(data: data, encoding: .utf8) {
        print("Response body:\n\(body)")
    }
}.resume()
semaphore.wait()
