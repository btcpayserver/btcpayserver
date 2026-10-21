using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Data.Payouts.LightningLike;
using BTCPayServer.Logging;
using BTCPayServer.Payments.PayJoin.Sender;
using BTCPayServer.Plugins.Bitpay;
using BTCPayServer.Plugins.Webhooks;
using BTCPayServer.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BTCPayServer.Tests;

public class SSRFProtectionTests
{
    [Fact]
    public void ParsesAndMatchesSSRFExceptions()
    {
        var exceptions = SSRFAllowedDestination.ParseList(
            " NAS.Home.Arpa.:8080, bücher.example ; 10.0.0.0/8:443; [fd00::/8]:8443; ::ffff:192.168.1.2; nas.home.arpa:8080");

        Assert.Equal(5, exceptions.Count);
        Assert.Contains(exceptions, exception => exception.MatchesHostname("nas.home.arpa", 8080));
        Assert.DoesNotContain(exceptions, exception => exception.MatchesHostname("nas.home.arpa", 8081));
        Assert.Contains(exceptions, exception => exception.MatchesHostname("xn--bcher-kva.example.", 80));
        Assert.Contains(exceptions, exception => exception.MatchesAddress(IPAddress.Parse("10.2.3.4"), 443));
        Assert.DoesNotContain(exceptions, exception => exception.MatchesAddress(IPAddress.Parse("10.2.3.4"), 80));
        Assert.Contains(exceptions, exception => exception.MatchesAddress(IPAddress.Parse("fd00::1234"), 8443));
        Assert.Contains(exceptions, exception => exception.MatchesAddress(IPAddress.Parse("192.168.1.2"), 1234));
    }

    [Theory]
    [InlineData("http://nas.home.arpa")]
    [InlineData("*.home.arpa")]
    [InlineData("10.0.0.1/8")]
    [InlineData("0.0.0.0/0")]
    [InlineData("::/0")]
    [InlineData("::ffff:0.0.0.0/96")]
    [InlineData("fe80::1%1")]
    [InlineData("nas.home.arpa:0")]
    [InlineData("nas.home.arpa:65536")]
    [InlineData("nas.home.arpa:8000-8100")]
    public void RejectsInvalidSSRFExceptions(string value)
    {
        Assert.Throws<FormatException>(() => SSRFAllowedDestination.Parse(value));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidSSRFExceptionsFailConfiguration(bool disableProtection)
    {
        var values = new Dictionary<string, string>
        {
            ["network"] = "regtest",
            ["disablessrfprotection"] = disableProtection.ToString(),
            ["ssrfexceptions"] = "10.0.0.1/8"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var exception = Assert.Throws<ConfigException>(() => new BTCPayServerOptions().LoadArgs(configuration, new Logs()));

        Assert.Contains("10.0.0.1/8", exception.Message);
    }

    [Fact]
    public async Task AppliesSSRFExceptionsToConnections()
    {
        using var server = new FakeServer();
        await server.Start();
        var port = server.ServerUri.Port;
        var localhostUri = new UriBuilder(server.ServerUri) { Host = "localhost" }.Uri;

        await using (var provider = CreateProvider())
        {
            var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("ssrf-test");
            var exception = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(localhostUri));
            Assert.Contains("does not resolve to an allowed network address", exception.Message);
        }

        await using (var provider = CreateProvider($"localhost:{port + 1}"))
        {
            var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("ssrf-test");
            var exception = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(localhostUri));
            Assert.Contains("does not resolve to an allowed network address", exception.Message);
        }

        await using (var provider = CreateProvider($"localhost:{port}"))
        {
            var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("ssrf-test");
            using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var responseTask = client.GetAsync(localhostUri, cancellationTokenSource.Token);
            var request = await server.GetNextRequest(cancellationTokenSource.Token);
            request.Response.StatusCode = (int)HttpStatusCode.Redirect;
            request.Response.Headers.Location = server.ServerUri.AbsoluteUri;
            server.Done();
            var exception = await Assert.ThrowsAsync<HttpRequestException>(() => responseTask);
            Assert.Contains("does not resolve to an allowed network address", exception.Message);
        }

        await AssertConnects(server, localhostUri, $"localhost:{port}");
        await AssertConnects(server, server.ServerUri, $"127.0.0.0/8:{port}");
        await AssertConnects(server, server.ServerUri, disableProtection: true);
    }

    private static async Task AssertConnects(FakeServer server, Uri uri, string exceptions = null,
        bool disableProtection = false)
    {
        await using var provider = CreateProvider(exceptions, disableProtection);
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("ssrf-test");
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var responseTask = client.GetAsync(uri, cancellationTokenSource.Token);
        var request = await server.GetNextRequest(cancellationTokenSource.Token);
        request.Response.StatusCode = 200;
        server.Done();
        using var response = await responseTask;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static ServiceProvider CreateProvider(string exceptions = null, bool disableProtection = false)
    {
        var values = new Dictionary<string, string>
        {
            ["network"] = "regtest",
            ["disablessrfprotection"] = disableProtection.ToString()
        };
        if (exceptions is not null)
            values["ssrfexceptions"] = exceptions;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var options = new BTCPayServerOptions();
        options.LoadArgs(configuration, new Logs());

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(options);
        services.AddHttpClient("ssrf-test").UseSSRFProtection();
        return services.BuildServiceProvider();
    }
}

[Collection(nameof(NonParallelizableCollectionDefinition))]
public class SSRFProtectedNamedClientTests(ITestOutputHelper testOutputHelper) : UnitTestBase(testOutputHelper)
{
    [Fact(Timeout = 60_000)]
    [Trait("Integration", "Integration")]
    public async Task ProtectedNamedClientsRejectInternalServer()
    {
        using var tester = CreateServerTester();
        await tester.StartAsync();
        var httpClientFactory = tester.PayTester.GetService<IHttpClientFactory>();
        var localUri = new UriBuilder(tester.PayTester.ServerUriWithIP) { Host = "localhost" }.Uri;
        var clientNames = new[]
        {
            LightningClientFactoryService.SafeNamedClient,
            LightningLikePayoutHandler.LightningLikePayoutHandlerClearnetNamedClient,
            PayjoinServerCommunicator.PayjoinClearnetNamedClient,
            WebhookSender.ClearnetNamedClient,
            BitpayIPNSender.NamedClient
        };

        foreach (var clientName in clientNames)
        {
            var client = httpClientFactory.CreateClient(clientName);
            var exception = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(localUri));
            Assert.Contains("does not resolve to an allowed network address", exception.Message);
        }
    }
}
