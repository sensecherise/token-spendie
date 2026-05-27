namespace TokenSpendie.Windows.Data;

public interface ICredentialReader
{
    bool CredentialsExist();
    Task<OAuthCredentials> LoadCredentialsAsync(CancellationToken ct = default);
}
