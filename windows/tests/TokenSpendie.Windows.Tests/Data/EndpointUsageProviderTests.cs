using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using TokenSpendie.Windows.Data;
using TokenSpendie.Windows.Models;
using Xunit;

namespace TokenSpendie.Windows.Tests.Data;

public class EndpointUsageProviderTests
{
    /// <summary>HttpMessageHandler that returns a canned response and records the request.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public HttpRequestMessage? Captured { get; private set; }
        public int CallCount { get; private set; }

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Captured = request;
            CallCount++;
            return Task.FromResult(_respond(request));
        }
    }

    private static EndpointUsageProvider Make(StubHandler handler, DateTimeOffset? now = null) =>
        new(new HttpClient(handler), now: () => now ?? DateTimeOffset.FromUnixTimeSeconds(42));

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };

    [Fact]
    public async Task Decodes200()
    {
        var body = """{"five_hour":{"utilization":50},"seven_day":{"utilization":60}}""";
        var provider = Make(new StubHandler(_ => Json(HttpStatusCode.OK, body)));
        var snapshot = await provider.FetchUsageAsync("tok");
        snapshot.Session.Percent.Should().BeApproximately(50, 0.001);
        snapshot.Weekly.Percent.Should().BeApproximately(60, 0.001);
        snapshot.FetchedAt.Should().Be(DateTimeOffset.FromUnixTimeSeconds(42));
    }

    [Fact]
    public async Task EmptyTokenThrowsUnauthorizedWithoutCallingTransport()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, ""));
        var provider = Make(handler);

        Func<Task> act = () => provider.FetchUsageAsync("");
        await act.Should().ThrowAsync<ProviderUnauthorizedException>();
        handler.CallCount.Should().Be(0, "transport must not be invoked for an empty token");
    }

    [Fact]
    public async Task Status401ThrowsUnauthorized()
    {
        var provider = Make(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        Func<Task> act = () => provider.FetchUsageAsync("tok");
        await act.Should().ThrowAsync<ProviderUnauthorizedException>();
    }

    [Fact]
    public async Task Status429ThrowsRateLimitedWithRetryAfter()
    {
        var provider = Make(new StubHandler(_ =>
        {
            var resp = new HttpResponseMessage((HttpStatusCode)429);
            resp.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return resp;
        }));
        Func<Task> act = () => provider.FetchUsageAsync("tok");
        var exception = await act.Should().ThrowAsync<ProviderRateLimitedException>();
        exception.Which.RetryAfter.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Status500ThrowsBadResponse()
    {
        var provider = Make(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        Func<Task> act = () => provider.FetchUsageAsync("tok");
        await act.Should().ThrowAsync<ProviderBadResponseException>();
    }

    [Fact]
    public async Task SendsBearerAndAcceptHeaders()
    {
        var body = """{"five_hour":{"utilization":10},"seven_day":{"utilization":10}}""";
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, body));
        var provider = Make(handler);
        _ = await provider.FetchUsageAsync("secret-token");

        handler.Captured.Should().NotBeNull();
        handler.Captured!.Headers.Authorization.Should().NotBeNull();
        handler.Captured.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.Captured.Headers.Authorization.Parameter.Should().Be("secret-token");
        handler.Captured.Headers.Accept.ToString().Should().Contain("application/json");
        handler.Captured.RequestUri.Should().Be("https://api.anthropic.com/api/oauth/usage");
    }
}
