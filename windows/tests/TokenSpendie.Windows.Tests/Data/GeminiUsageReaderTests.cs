using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using TokenSpendie.Windows.Data;
using Xunit;

namespace TokenSpendie.Windows.Tests.Data;

public class GeminiUsageReaderTests : IDisposable
{
    private readonly string _home;
    private readonly TimeZoneInfo _utc = TimeZoneInfo.Utc;
    /// <summary>2025-05-22 12:00 UTC.</summary>
    private static readonly DateTimeOffset Noon =
        DateTimeOffset.FromUnixTimeSeconds(1_747_915_200);
    /// <summary>2025-05-23 00:00 UTC.</summary>
    private static readonly DateTimeOffset NextLocalMidnight =
        DateTimeOffset.FromUnixTimeSeconds(1_747_958_400);

    public GeminiUsageReaderTests()
    {
        _home = Path.Combine(Path.GetTempPath(), $"gem-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_home);
    }

    public void Dispose()
    {
        try { Directory.Delete(_home, recursive: true); } catch { }
    }

    private GeminiUsageReader Reader(DateTimeOffset? now = null) =>
        new(geminiHome: _home, now: () => now ?? Noon, timeZone: _utc);

    private void StubOAuth() =>
        File.WriteAllText(Path.Combine(_home, "oauth_creds.json"), "{}");

    /// <summary>Write a JSONL session file containing the given lines.</summary>
    private void WriteSession(string project, string sessionId, params string[] lines)
    {
        var dir = Path.Combine(_home, "tmp", project, "chats");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"session-2025-05-22T01-00-{sessionId}.jsonl");
        File.WriteAllLines(path, lines);
    }

    private static string UserLine(string text, string iso) =>
        $$$"""{"id":"u","timestamp":"{{{iso}}}","type":"user","content":[{"text":"{{{text}}}"}]}""";

    private static string GeminiLine(string text, string iso) =>
        $$$"""{"id":"g","timestamp":"{{{iso}}}","type":"gemini","content":"{{{text}}}"}""";

    private static string SessionHeader() =>
        """{"sessionId":"s","projectHash":"h","startTime":"2025-05-22T00:00:00.000Z","lastUpdated":"2025-05-22T00:00:00.000Z","kind":"main"}""";

    private static string SetSentinel() =>
        """{"$set":{"lastUpdated":"2025-05-22T01:00:00.000Z"}}""";

    [Fact]
    public void DetectCredentialsTrueWhenOAuthFileExists()
    {
        StubOAuth();
        Reader().DetectCredentials().Should().BeTrue();
    }

    [Fact]
    public void DetectCredentialsFalseWhenNoOAuthFile()
    {
        Reader().DetectCredentials().Should().BeFalse();
    }

    [Fact]
    public void NextLocalMidnightIsStartOfTomorrow()
    {
        Reader().NextLocalMidnight().Should().Be(NextLocalMidnight);
    }

    [Fact]
    public void CountsTodaysPrompts()
    {
        WriteSession("p", "a",
            SessionHeader(),
            UserLine("hello", "2025-05-22T01:00:00.000Z"),
            SetSentinel(),
            GeminiLine("hi", "2025-05-22T01:00:01.000Z"),
            UserLine("again", "2025-05-22T11:59:00.000Z"));
        Reader().RequestsToday().Should().Be(2);
    }

    [Fact]
    public void IgnoresYesterdaysPrompts()
    {
        WriteSession("p", "a",
            SessionHeader(),
            UserLine("old", "2025-05-21T23:59:00.000Z"),
            UserLine("new", "2025-05-22T00:00:00.000Z"));
        Reader().RequestsToday().Should().Be(1);
    }

    [Fact]
    public void IgnoresSlashCommands()
    {
        WriteSession("p", "a",
            SessionHeader(),
            UserLine("/stats", "2025-05-22T01:00:00.000Z"),
            UserLine("real prompt", "2025-05-22T02:00:00.000Z"));
        Reader().RequestsToday().Should().Be(1);
    }

    [Fact]
    public void IgnoresNonUserRecordTypes()
    {
        WriteSession("p", "a",
            SessionHeader(),
            GeminiLine("response", "2025-05-22T01:00:00.000Z"),
            UserLine("prompt", "2025-05-22T02:00:00.000Z"));
        Reader().RequestsToday().Should().Be(1);
    }

    [Fact]
    public void ParsesTimestampsWithoutFractionalSeconds()
    {
        WriteSession("p", "a",
            SessionHeader(),
            UserLine("plain", "2025-05-22T03:00:00Z"));
        Reader().RequestsToday().Should().Be(1);
    }

    [Fact]
    public void SumsAcrossProjectsAndSessions()
    {
        WriteSession("p1", "a",
            SessionHeader(),
            UserLine("a", "2025-05-22T01:00:00.000Z"));
        WriteSession("p2", "b",
            SessionHeader(),
            UserLine("b", "2025-05-22T02:00:00.000Z"),
            UserLine("c", "2025-05-22T03:00:00.000Z"));
        WriteSession("p2", "c",
            SessionHeader(),
            UserLine("d", "2025-05-22T04:00:00.000Z"));
        Reader().RequestsToday().Should().Be(4);
    }

    [Fact]
    public void CorruptSessionFileSkippedOthersStillCounted()
    {
        WriteSession("good", "a",
            SessionHeader(),
            UserLine("a", "2025-05-22T01:00:00.000Z"));
        // Add a session file whose contents are not valid JSON.
        var badDir = Path.Combine(_home, "tmp", "bad", "chats");
        Directory.CreateDirectory(badDir);
        File.WriteAllText(Path.Combine(badDir, "session-x.jsonl"),
            "not json\nstill not json\n");
        Reader().RequestsToday().Should().Be(1);
    }

    [Fact]
    public void MissingTmpDirectoryReturnsZero()
    {
        StubOAuth(); // no tmp/ created
        Reader().RequestsToday().Should().Be(0);
    }

    [Fact]
    public void IgnoresPlaceholderEmptyLogsJson()
    {
        // M0 spike: Gemini v0.43.0 writes an empty `[]` logs.json at first login.
        // The reader must not be confused by it — it reads JSONL sessions, not logs.json.
        var dir = Path.Combine(_home, "tmp", "cherise");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "logs.json"), "[]");
        Reader().RequestsToday().Should().Be(0);
    }

    [Fact]
    public void ParsesM0SanitizedFixture()
    {
        // The M0 fixture wraps two real session-jsonl files. Lay them out
        // under tmp/<project>/chats/ so the reader picks them up.
        var fixturePath = Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..",
            "docs", "superpowers", "findings", "fixtures",
            "gemini-logs-sanitized.json");
        fixturePath = Path.GetFullPath(fixturePath);
        File.Exists(fixturePath).Should().BeTrue();

        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(fixturePath));
        var lines = doc.RootElement.GetProperty("_jsonl_lines")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();

        var dir = Path.Combine(_home, "tmp", "token-spendie", "chats");
        Directory.CreateDirectory(dir);
        File.WriteAllLines(Path.Combine(dir, "session-fixture.jsonl"), lines);

        // The fixture's two user prompts are timestamped 2026-05-26T18:13Z and
        // 2026-05-26T18:16Z. Set "now" to noon of that day so both fall within today.
        var fixtureNoon = new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero);
        var reader = new GeminiUsageReader(_home, () => fixtureNoon, _utc);
        reader.RequestsToday().Should().Be(2);
    }
}
