import Foundation

/// Persists the most recent `ProviderSnapshot` for one provider to a JSON file.
struct SnapshotCache {
    let fileURL: URL

    /// Per-provider location:
    /// ~/Library/Application Support/TokenSpendie/snapshot-<id>.json
    static func defaultURL(for provider: ProviderID) -> URL {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("TokenSpendie", isDirectory: true)
        try? FileManager.default.createDirectory(at: base, withIntermediateDirectories: true)
        return base.appendingPathComponent("snapshot-\(provider.rawValue).json")
    }

    func load() -> ProviderSnapshot? {
        guard let data = try? Data(contentsOf: fileURL) else { return nil }
        return try? JSONDecoder().decode(ProviderSnapshot.self, from: data)
    }

    func save(_ snapshot: ProviderSnapshot) {
        guard let data = try? JSONEncoder().encode(snapshot) else { return }
        try? data.write(to: fileURL, options: .atomic)
    }
}
