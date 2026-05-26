import UserNotifications
import Foundation

/// Fires macOS notifications when session or weekly usage crosses 50 / 70 / 90 / 99 %.
/// Each threshold fires at most once per window cycle.
@MainActor
final class UsageNotifier {

    // MARK: - Config

    private static let thresholds: [Double] = [50, 70, 90, 99]

    private struct Joke {
        let title: String
        let body: String
    }

    private static let sessionJokes: [Double: Joke] = [
        50: Joke(
            title: "Session 50% gone 👀",
            body: "Still going? Bold. Very bold."
        ),
        70: Joke(
            title: "Session 70% cooked 🍳",
            body: "At this point Claude knows more about your life than your therapist."
        ),
        90: Joke(
            title: "Session 90%... okay wow 😰",
            body: "What are you even building in there. Go touch some grass."
        ),
        99: Joke(
            title: "Session basically done. Go take a break ☕",
            body: "Tokens ended. Skill issue. Grab a coffee, you've earned it."
        ),
    ]

    private static let weeklyJokes: [Double: Joke] = [
        50: Joke(
            title: "Weekly 50% gone already 🙃",
            body: "It's only midweek. Totally fine. Everything is fine."
        ),
        70: Joke(
            title: "Weekly 70% used 😬",
            body: "Claude is starting to recognise your typing pattern. This is your fault."
        ),
        90: Joke(
            title: "Weekly 90% — almost speedrunning this 🏃",
            body: "New record? Probably a new record. Who needs tokens anyway."
        ),
        99: Joke(
            title: "Weekly tokens ended. Seeya next week 👋",
            body: "Go outside. Touch grass. Tell your friends you exist. Tokens reset soon™."
        ),
    ]

    // MARK: - State (persisted across restarts)

    /// Fired thresholds per window key. Stored as JSON in UserDefaults.
    private var firedThresholds: [String: Set<Double>] {
        get {
            guard let data = UserDefaults.standard.data(forKey: "UsageNotifier.firedThresholds"),
                  let decoded = try? JSONDecoder().decode([String: [Double]].self, from: data)
            else { return [:] }
            return decoded.mapValues { Set($0) }
        }
        set {
            let encodable = newValue.mapValues { Array($0) }
            if let data = try? JSONEncoder().encode(encodable) {
                UserDefaults.standard.set(data, forKey: "UsageNotifier.firedThresholds")
            }
        }
    }

    /// Last seen `resetsAt` per window key. Used to detect window rollovers.
    private var lastResetDates: [String: Date] {
        get {
            guard let data = UserDefaults.standard.data(forKey: "UsageNotifier.lastResetDates"),
                  let decoded = try? JSONDecoder().decode([String: Date].self, from: data)
            else { return [:] }
            return decoded
        }
        set {
            if let data = try? JSONEncoder().encode(newValue) {
                UserDefaults.standard.set(data, forKey: "UsageNotifier.lastResetDates")
            }
        }
    }

    // MARK: - Public

    func check(snapshot: UsageSnapshot) {
        checkWindow(snapshot.session,
                    key: "session",
                    jokes: Self.sessionJokes)
        checkWindow(snapshot.weekly,
                    key: "weekly",
                    jokes: Self.weeklyJokes)
    }

    // MARK: - Private

    private func checkWindow(_ window: UsageWindow, key: String, jokes: [Double: Joke]) {
        var fired = firedThresholds
        var resets = lastResetDates

        // Detect window rollover: if resetsAt changed, clear fired thresholds.
        if let newReset = window.resetsAt {
            if let lastReset = resets[key], lastReset != newReset {
                fired[key] = []
            }
            resets[key] = newReset
        }

        let alreadyFired = fired[key] ?? []
        var newFired = alreadyFired

        for threshold in Self.thresholds {
            guard window.percent >= threshold, !alreadyFired.contains(threshold) else { continue }
            newFired.insert(threshold)
            if let joke = jokes[threshold] {
                sendNotification(id: "\(key).\(Int(threshold))", joke: joke)
            }
        }

        fired[key] = newFired
        firedThresholds = fired
        lastResetDates = resets
    }

    private func sendNotification(id: String, joke: Joke) {
        let content = UNMutableNotificationContent()
        content.title = joke.title
        content.body = joke.body
        content.sound = .default

        let request = UNNotificationRequest(identifier: id, content: content, trigger: nil)
        UNUserNotificationCenter.current().add(request)
    }
}
