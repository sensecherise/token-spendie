using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using TokenSpendie.Windows.Models;

namespace TokenSpendie.Windows.Data;

public static class UsageDecoder
{
    public static UsageSnapshot Decode(ReadOnlySpan<byte> utf8Json, DateTimeOffset fetchedAt)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(utf8Json.ToArray()); }
        catch (JsonException ex) { throw new ProviderBadResponseException("not JSON", ex); }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new ProviderBadResponseException("root is not an object");

            var session = ReadWindow(root, "five_hour");
            var weekly = ReadWindow(root, "seven_day");
            if (session is null || weekly is null)
                throw new ProviderBadResponseException("required window missing");

            var modelWeeklies = new List<ModelWeekly>(capacity: 2);
            if (ReadWindow(root, "seven_day_opus") is { } opus)
                modelWeeklies.Add(new ModelWeekly("Opus", opus));
            if (ReadWindow(root, "seven_day_sonnet") is { } sonnet)
                modelWeeklies.Add(new ModelWeekly("Sonnet", sonnet));

            return new UsageSnapshot(session, weekly, modelWeeklies, fetchedAt);
        }
    }

    private static UsageWindow? ReadWindow(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var el) || el.ValueKind != JsonValueKind.Object)
            return null;
        if (!el.TryGetProperty("utilization", out var util) || util.ValueKind != JsonValueKind.Number)
            return null;
        var percent = util.GetDouble();
        DateTimeOffset? resetsAt = null;
        if (el.TryGetProperty("resets_at", out var ra) && ra.ValueKind == JsonValueKind.String)
            resetsAt = ParseDate(ra.GetString()!);
        return new UsageWindow(percent, resetsAt);
    }

    private static readonly Regex FractionalSeconds = new(@"\.\d+", RegexOptions.Compiled);

    /// <summary>The endpoint emits microsecond precision; .NET's parser handles up to 7 digits
    /// (ticks). To stay safe across .NET versions, we strip the fractional component (mac
    /// parser does the same).</summary>
    internal static DateTimeOffset? ParseDate(string s)
    {
        var withoutFraction = FractionalSeconds.Replace(s, "");
        return DateTimeOffset.TryParse(
            withoutFraction, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }
}
