using System.Globalization;
using System.Text.Json;

namespace TokenSpendie.Windows.Data;

/// <summary>
/// Counts Gemini CLI usage from JSONL session files at
/// <c>%USERPROFILE%\.gemini\tmp\&lt;project&gt;\chats\session-*.jsonl</c>.
/// Format confirmed by the M0 spike: per-line JSON objects, user prompts have
/// <c>type:"user"</c> and <c>content:[{text:string}]</c>. The legacy mac
/// <c>logs.json</c> array is absent on Windows v0.43.0 and is ignored.
/// </summary>
public sealed class GeminiUsageReader
{
    private readonly string _geminiHome;
    private readonly Func<DateTimeOffset> _now;
    private readonly TimeZoneInfo _timeZone;

    public GeminiUsageReader()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini")) { }

    public GeminiUsageReader(string geminiHome,
        Func<DateTimeOffset>? now = null,
        TimeZoneInfo? timeZone = null)
    {
        _geminiHome = geminiHome;
        _now = now ?? (() => DateTimeOffset.Now);
        _timeZone = timeZone ?? TimeZoneInfo.Local;
    }

    public DateTimeOffset Now() => _now();

    public bool DetectCredentials() =>
        File.Exists(Path.Combine(_geminiHome, "oauth_creds.json"));

    public DateTimeOffset NextLocalMidnight()
    {
        var nowLocal = TimeZoneInfo.ConvertTime(_now(), _timeZone);
        var startOfToday = new DateTimeOffset(
            nowLocal.Year, nowLocal.Month, nowLocal.Day, 0, 0, 0, nowLocal.Offset);
        return startOfToday.AddDays(1);
    }

    public int RequestsToday()
    {
        var nowLocal = TimeZoneInfo.ConvertTime(_now(), _timeZone);
        var startOfToday = new DateTimeOffset(
            nowLocal.Year, nowLocal.Month, nowLocal.Day, 0, 0, 0, nowLocal.Offset);

        var tmpDir = Path.Combine(_geminiHome, "tmp");
        if (!Directory.Exists(tmpDir)) return 0;

        var total = 0;
        foreach (var projectDir in Directory.EnumerateDirectories(tmpDir))
        {
            var chatsDir = Path.Combine(projectDir, "chats");
            if (!Directory.Exists(chatsDir)) continue;
            foreach (var session in Directory.EnumerateFiles(chatsDir, "session-*.jsonl"))
            {
                total += CountPromptsInJsonl(session, startOfToday);
            }
        }
        return total;
    }

    private static int CountPromptsInJsonl(string path, DateTimeOffset since)
    {
        int count = 0;
        string[] lines;
        try { lines = File.ReadAllLines(path); }
        catch { return 0; }   // unreadable file — best-effort: 0

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonDocument doc;
            try { doc = JsonDocument.Parse(line); }
            catch { continue; }  // malformed line — skip

            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) continue;
                if (!root.TryGetProperty("type", out var type)
                    || type.ValueKind != JsonValueKind.String
                    || type.GetString() != "user") continue;
                if (!root.TryGetProperty("timestamp", out var ts)
                    || ts.ValueKind != JsonValueKind.String) continue;
                if (!TryParseTimestamp(ts.GetString()!, out var stamp)) continue;
                if (stamp < since) continue;

                // Look up content[0].text and skip if it's a slash command.
                if (root.TryGetProperty("content", out var content)
                    && content.ValueKind == JsonValueKind.Array
                    && content.GetArrayLength() > 0
                    && content[0].TryGetProperty("text", out var textProp)
                    && textProp.ValueKind == JsonValueKind.String
                    && textProp.GetString() is { } text
                    && text.StartsWith('/'))
                {
                    continue;
                }

                count++;
            }
        }
        return count;
    }

    internal static bool TryParseTimestamp(string s, out DateTimeOffset parsed) =>
        DateTimeOffset.TryParse(
            s, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out parsed);
}
