using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TokenSpendie.Windows.Models;

namespace TokenSpendie.Windows.Services;

/// <summary>
/// Fires toast notifications when session/weekly usage crosses 50/70/90/99 %.
/// Each threshold fires at most once per window cycle. Window rollover is
/// detected via <c>resetsAt</c> change.
/// </summary>
public sealed class UsageNotifier
{
    private static readonly double[] Thresholds = { 50, 70, 90, 99 };

    private static readonly Dictionary<double, Joke> SessionJokes = new()
    {
        [50] = new Joke("Session 50% gone \U0001f440", "Still going? Bold. Very bold."),
        [70] = new Joke("Session 70% cooked \U0001f373",
            "At this point Claude knows more about your life than your therapist."),
        [90] = new Joke("Session 90%... okay wow \U0001f630",
            "What are you even building in there. Go touch some grass."),
        [99] = new Joke("Session basically done. Go take a break ☕",
            "Tokens ended. Skill issue. Grab a coffee, you've earned it."),
    };

    private static readonly Dictionary<double, Joke> WeeklyJokes = new()
    {
        [50] = new Joke("Weekly 50% gone already \U0001f643",
            "It's only midweek. Totally fine. Everything is fine."),
        [70] = new Joke("Weekly 70% used \U0001f62c",
            "Claude is starting to recognise your typing pattern. This is your fault."),
        [90] = new Joke("Weekly 90% — almost speedrunning this \U0001f3c3",
            "New record? Probably a new record. Who needs tokens anyway."),
        [99] = new Joke("Weekly tokens ended. Seeya next week \U0001f44b",
            "Go outside. Touch grass. Tell your friends you exist. Tokens reset soon™."),
    };

    private readonly IToastSender _toasts;
    private readonly string _statePath;

    public UsageNotifier(IToastSender toasts) : this(toasts, DefaultStatePath()) { }

    public UsageNotifier(IToastSender toasts, string statePath)
    {
        _toasts = toasts;
        _statePath = statePath;
    }

    public static string DefaultStatePath() =>
        Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
            "TokenSpendie", "notifier-state.json");

    public void Check(IEnumerable<ProviderUsage> providers)
    {
        var state = Load();
        foreach (var usage in providers)
        {
            if (usage.Snapshot is null) continue;
            foreach (var window in usage.Snapshot.Windows)
            {
                var labelLower = window.Label.ToLowerInvariant();
                var jokes = labelLower.Contains("session") ? SessionJokes : WeeklyJokes;
                var key = $"{usage.Id.ToString().ToLowerInvariant()}.{LabelKey(window.Label)}";
                CheckOne(window.Window, key, jokes, state);
            }
        }
        Save(state);
    }

    private static string LabelKey(string label) =>
        label.ToLowerInvariant()
            .Replace(" · ", ".")
            .Replace(" ", "");

    private void CheckOne(UsageWindow window, string key, Dictionary<double, Joke> jokes, NotifierState state)
    {
        if (!state.FiredThresholds.TryGetValue(key, out var fired)) fired = new();
        var firedSet = new HashSet<double>(fired);

        if (window.ResetsAt is { } newReset)
        {
            if (state.LastResetDates.TryGetValue(key, out var lastReset) && lastReset != newReset)
                firedSet.Clear();
            state.LastResetDates[key] = newReset;
        }

        foreach (var threshold in Thresholds)
        {
            if (window.Percent < threshold) continue;
            if (firedSet.Contains(threshold)) continue;
            firedSet.Add(threshold);
            if (jokes.TryGetValue(threshold, out var joke))
            {
                var tag = $"{key}.{(int)threshold}";
                _toasts.Send(joke, tag);
            }
        }

        state.FiredThresholds[key] = firedSet.ToList();
    }

    private NotifierState Load()
    {
        try
        {
            if (!File.Exists(_statePath)) return new NotifierState();
            using var stream = File.OpenRead(_statePath);
            return JsonSerializer.Deserialize<NotifierState>(stream) ?? new NotifierState();
        }
        catch { return new NotifierState(); }
    }

    private void Save(NotifierState state)
    {
        try
        {
            var parent = Path.GetDirectoryName(_statePath);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            using var stream = File.Create(_statePath);
            JsonSerializer.Serialize(stream, state);
        }
        catch { /* best-effort */ }
    }
}
