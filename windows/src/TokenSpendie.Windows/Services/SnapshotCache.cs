using System.Text.Json;
using System.Text.Json.Serialization;
using TokenSpendie.Windows.Models;

namespace TokenSpendie.Windows.Services;

/// <summary>Persists one <see cref="ProviderSnapshot"/> for one provider as JSON.</summary>
public sealed class SnapshotCache
{
    public string FileUrl { get; }

    private static readonly JsonSerializerOptions Options;

    static SnapshotCache()
    {
        Options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        Options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    public SnapshotCache(string fileUrl)
    {
        FileUrl = fileUrl;
    }

    public static string DefaultPathFor(ProviderID id)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TokenSpendie");
        return Path.Combine(dir, $"snapshot-{id.ToString().ToLowerInvariant()}.json");
    }

    public ProviderSnapshot? Load()
    {
        try
        {
            if (!File.Exists(FileUrl)) return null;
            using var stream = File.OpenRead(FileUrl);
            return JsonSerializer.Deserialize<ProviderSnapshot>(stream, Options);
        }
        catch { return null; }
    }

    public void Save(ProviderSnapshot snapshot)
    {
        try
        {
            var parent = Path.GetDirectoryName(FileUrl);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            using var stream = File.Create(FileUrl);
            JsonSerializer.Serialize(stream, snapshot, Options);
        }
        catch { /* best-effort */ }
    }
}
