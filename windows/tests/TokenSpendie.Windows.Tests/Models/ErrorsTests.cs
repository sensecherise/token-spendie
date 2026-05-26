using FluentAssertions;
using TokenSpendie.Windows.Models;
using Xunit;

namespace TokenSpendie.Windows.Tests.Models;

public class ErrorsTests
{
    [Fact]
    public void CredentialExceptionsCarryKind()
    {
        new CredentialNotFoundException().Kind.Should().Be(CredentialErrorKind.NotFound);
        new CredentialAccessDeniedException().Kind.Should().Be(CredentialErrorKind.AccessDenied);
        new CredentialMalformedException("bad json").Kind.Should().Be(CredentialErrorKind.Malformed);
    }

    [Fact]
    public void ProviderExceptionsCarryKindAndRetryAfter()
    {
        new ProviderUnauthorizedException().Kind.Should().Be(ProviderErrorKind.Unauthorized);
        new ProviderNetworkException(new System.Net.Http.HttpRequestException("offline"))
            .Kind.Should().Be(ProviderErrorKind.Network);
        new ProviderBadResponseException("bad").Kind.Should().Be(ProviderErrorKind.BadResponse);

        var rateLimited = new ProviderRateLimitedException(retryAfter: System.TimeSpan.FromSeconds(30));
        rateLimited.Kind.Should().Be(ProviderErrorKind.RateLimited);
        rateLimited.RetryAfter.Should().Be(System.TimeSpan.FromSeconds(30));
    }
}
