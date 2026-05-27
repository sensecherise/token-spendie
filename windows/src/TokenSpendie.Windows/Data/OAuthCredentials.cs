namespace TokenSpendie.Windows.Data;

public record OAuthCredentials(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset? ExpiresAt)
{
    public bool IsExpired(DateTimeOffset now) =>
        ExpiresAt is { } expires && now >= expires;
}
