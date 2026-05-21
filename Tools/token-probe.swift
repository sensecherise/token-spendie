#!/usr/bin/swift
// Tools/token-probe.swift — run with: swift Tools/token-probe.swift <token>
// Verifies a Claude OAuth token works against the usage endpoint.
import Foundation

guard CommandLine.arguments.count == 2 else {
    print("usage: swift Tools/token-probe.swift <token>")
    exit(2)
}
let token = CommandLine.arguments[1]
var request = URLRequest(url: URL(string: "https://api.anthropic.com/api/oauth/usage")!)
request.httpMethod = "GET"
request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
request.setValue("application/json", forHTTPHeaderField: "Accept")
request.setValue("ClaudeUsageWidget/token-probe", forHTTPHeaderField: "User-Agent")

let semaphore = DispatchSemaphore(value: 0)
URLSession.shared.dataTask(with: request) { data, response, error in
    defer { semaphore.signal() }
    if let error = error { print("error: \(error)"); return }
    let http = response as! HTTPURLResponse
    print("HTTP \(http.statusCode)")
    if let data = data, let body = String(data: data, encoding: .utf8) { print(body) }
}.resume()
semaphore.wait()
