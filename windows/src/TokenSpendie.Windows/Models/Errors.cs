namespace TokenSpendie.Windows.Models;

// --- Credential errors --------------------------------------------------------

public enum CredentialErrorKind { NotFound, AccessDenied, Malformed }

public abstract class CredentialException : Exception
{
    public abstract CredentialErrorKind Kind { get; }
    protected CredentialException(string message, Exception? inner = null)
        : base(message, inner) { }
}

public sealed class CredentialNotFoundException : CredentialException
{
    public override CredentialErrorKind Kind => CredentialErrorKind.NotFound;
    public CredentialNotFoundException() : base("Claude credentials not found.") { }
}

public sealed class CredentialAccessDeniedException : CredentialException
{
    public override CredentialErrorKind Kind => CredentialErrorKind.AccessDenied;
    public CredentialAccessDeniedException(Exception? inner = null)
        : base("Credential file could not be read (ACL).", inner) { }
}

public sealed class CredentialMalformedException : CredentialException
{
    public override CredentialErrorKind Kind => CredentialErrorKind.Malformed;
    public CredentialMalformedException(string detail, Exception? inner = null)
        : base($"Credential JSON is malformed: {detail}", inner) { }
}

// --- Provider errors ----------------------------------------------------------

public enum ProviderErrorKind { Unauthorized, Network, BadResponse, RateLimited }

public abstract class ProviderException : Exception
{
    public abstract ProviderErrorKind Kind { get; }
    protected ProviderException(string message, Exception? inner = null)
        : base(message, inner) { }
}

public sealed class ProviderUnauthorizedException : ProviderException
{
    public override ProviderErrorKind Kind => ProviderErrorKind.Unauthorized;
    public ProviderUnauthorizedException() : base("Endpoint returned 401.") { }
}

public sealed class ProviderNetworkException : ProviderException
{
    public override ProviderErrorKind Kind => ProviderErrorKind.Network;
    public ProviderNetworkException(Exception inner)
        : base("Network transport failure.", inner) { }
}

public sealed class ProviderBadResponseException : ProviderException
{
    public override ProviderErrorKind Kind => ProviderErrorKind.BadResponse;
    public ProviderBadResponseException(string detail, Exception? inner = null)
        : base($"Bad response: {detail}", inner) { }
}

public sealed class ProviderRateLimitedException : ProviderException
{
    public override ProviderErrorKind Kind => ProviderErrorKind.RateLimited;
    public TimeSpan? RetryAfter { get; }
    public ProviderRateLimitedException(TimeSpan? retryAfter)
        : base("Endpoint returned 429.") => RetryAfter = retryAfter;
}

// --- User-facing usage errors (consumed by LoadState) -------------------------

public enum UsageErrorKind
{
    ClaudeCodeNotFound,
    CredentialAccessDenied,
    LoginExpired,
    Network,
    BadResponse,
}
