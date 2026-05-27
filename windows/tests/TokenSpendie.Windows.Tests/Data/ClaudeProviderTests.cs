using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TokenSpendie.Windows.Data;
using TokenSpendie.Windows.Models;
using Xunit;

namespace TokenSpendie.Windows.Tests.Data;

public class ClaudeProviderTests
{
    private static OAuthCredentials Creds(string token = "tok") =>
        new(token, null, null);

    private static UsageSnapshot Snapshot() => new(
        Session: new UsageWindow(47, DateTimeOffset.FromUnixTimeSeconds(100)),
        Weekly: new UsageWindow(31, DateTimeOffset.FromUnixTimeSeconds(200)),
        ModelWeeklies: new[] { new ModelWeekly("Opus", new UsageWindow(62, null)) },
        FetchedAt: DateTimeOffset.FromUnixTimeSeconds(999));

    [Fact]
    public void IdAndDisplayName()
    {
        var provider = new ClaudeProvider(
            Substitute.For<ICredentialReader>(),
            Substitute.For<IClaudeUsageEndpoint>());
        provider.Id.Should().Be(ProviderID.Claude);
        provider.DisplayName.Should().Be("Claude");
    }

    [Fact]
    public void DetectCredentialsDelegates()
    {
        var reader = Substitute.For<ICredentialReader>();
        reader.CredentialsExist().Returns(true);
        new ClaudeProvider(reader, Substitute.For<IClaudeUsageEndpoint>()).DetectCredentials()
            .Should().BeTrue();
        reader.CredentialsExist().Returns(false);
        new ClaudeProvider(reader, Substitute.For<IClaudeUsageEndpoint>()).DetectCredentials()
            .Should().BeFalse();
    }

    [Fact]
    public void ConvertMapsWindowsWithLabelsAndHeadline()
    {
        var snapshot = ClaudeProvider.Convert(Snapshot());
        snapshot.Id.Should().Be(ProviderID.Claude);
        snapshot.Headline.Label.Should().Be("Session");
        snapshot.Headline.Window.Percent.Should().BeApproximately(47, 0.001);
        snapshot.Windows.Select(w => w.Label).Should().Equal("Session", "Weekly", "Weekly · Opus");
        snapshot.Windows[0].ResetStyle.Should().Be(ResetStyle.Countdown);
        snapshot.Windows[1].ResetStyle.Should().Be(ResetStyle.Date);
        snapshot.Windows[1].Detail.Should().Be("all models");
        snapshot.Windows[2].Detail.Should().Be("Opus only");
        snapshot.FetchedAt.Should().Be(DateTimeOffset.FromUnixTimeSeconds(999));
        snapshot.Plan.Should().BeNull();
    }

    [Fact]
    public async Task FetchUsageReturnsConvertedSnapshot()
    {
        var reader = Substitute.For<ICredentialReader>();
        reader.LoadCredentialsAsync(Arg.Any<CancellationToken>()).Returns(Creds());
        var endpoint = Substitute.For<IClaudeUsageEndpoint>();
        endpoint.FetchUsageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Snapshot());

        var provider = new ClaudeProvider(reader, endpoint);
        var snapshot = await provider.FetchUsageAsync();

        snapshot.Headline.Window.Percent.Should().BeApproximately(47, 0.001);
    }

    [Fact]
    public async Task FetchUsageRetriesOnceByRereadingCredentialsOn401()
    {
        var reader = Substitute.For<ICredentialReader>();
        reader.LoadCredentialsAsync(Arg.Any<CancellationToken>()).Returns(Creds());

        var endpoint = Substitute.For<IClaudeUsageEndpoint>();
        endpoint.FetchUsageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => throw new ProviderUnauthorizedException(),
                _ => Task.FromResult(Snapshot()));

        var provider = new ClaudeProvider(reader, endpoint);
        _ = await provider.FetchUsageAsync();

        await reader.Received(2).LoadCredentialsAsync(Arg.Any<CancellationToken>());
        await endpoint.Received(2).FetchUsageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FetchUsagePropagatesPersistentUnauthorized()
    {
        var reader = Substitute.For<ICredentialReader>();
        reader.LoadCredentialsAsync(Arg.Any<CancellationToken>()).Returns(Creds());
        var endpoint = Substitute.For<IClaudeUsageEndpoint>();
        endpoint.FetchUsageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new ProviderUnauthorizedException());

        var provider = new ClaudeProvider(reader, endpoint);
        Func<Task> act = () => provider.FetchUsageAsync();
        await act.Should().ThrowAsync<ProviderUnauthorizedException>();
    }

    [Fact]
    public async Task FetchUsagePropagatesCredentialError()
    {
        var reader = Substitute.For<ICredentialReader>();
        reader.LoadCredentialsAsync(Arg.Any<CancellationToken>())
            .Throws(new CredentialNotFoundException());
        var endpoint = Substitute.For<IClaudeUsageEndpoint>();

        var provider = new ClaudeProvider(reader, endpoint);
        Func<Task> act = () => provider.FetchUsageAsync();
        await act.Should().ThrowAsync<CredentialNotFoundException>();
        await endpoint.DidNotReceive().FetchUsageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
