using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Lightning;
using BTCPayServer.Plugins.NFC;
using Newtonsoft.Json;
using Xunit;

namespace BTCPayServer.Tests;

[Collection(nameof(NonParallelizableCollectionDefinition))]
public class NFCTests(ITestOutputHelper testOutputHelper) : UnitTestBase(testOutputHelper)
{
    // Pure-config assert: the handler used for clearnet LNURL fetches must
    // never follow HTTP redirects, so a 302 Location: <private-network> can
    // not sneak past the pre-fetch host validation. Guards against a future
    // regression toggling AllowAutoRedirect back to true.
    [Fact]
    [Trait("Fast", "Fast")]
    public void ClearnetLnurlHandlerDisablesAutoRedirect()
    {
        var pinned = new[] { IPAddress.Parse("8.8.8.8") };
        using var handler = NFCController.BuildPinnedHandler(pinned);
        Assert.False(handler.AllowAutoRedirect);
    }

    // Verifies the address-family filter that backs ResolveAndValidateAsync.
    // Covers every branch of IsNonPublicAddress so a future change to the
    // predicate can't silently drop a family from the reject-set without a
    // test flipping. Also pins IPv6 coverage that the endpoint round-trip
    // exercises indirectly.
    [Theory]
    [Trait("Fast", "Fast")]
    // Non-public families that MUST be rejected.
    [InlineData("127.0.0.1", true)]           // IPv4 loopback
    [InlineData("10.0.0.1", true)]            // RFC1918 10.0.0.0/8
    [InlineData("172.16.0.1", true)]          // RFC1918 172.16.0.0/12
    [InlineData("192.168.1.1", true)]         // RFC1918 192.168.0.0/16
    [InlineData("169.254.169.254", true)]     // link-local (cloud metadata)
    [InlineData("100.64.0.1", true)]          // CGNAT 100.64.0.0/10
    [InlineData("100.100.100.100", true)]     // CGNAT interior
    [InlineData("0.1.2.3", true)]             // 0.0.0.0/8 "this network"
    [InlineData("224.0.0.1", true)]           // multicast 224.0.0.0/4
    [InlineData("240.0.0.1", true)]           // reserved 240.0.0.0/4
    [InlineData("::1", true)]                 // IPv6 loopback
    [InlineData("fe80::1", true)]             // IPv6 link-local
    [InlineData("fd00::1", true)]             // IPv6 unique-local
    [InlineData("ff02::1", true)]             // IPv6 multicast
    [InlineData("::", true)]                  // IPv6 unspecified (may fall back to loopback)
    [InlineData("0.0.0.0", true)]             // IPv4 unspecified (already 0/8 but explicit)
    // Public addresses that MUST pass.
    [InlineData("8.8.8.8", false)]            // public IPv4 (Google DNS)
    [InlineData("1.1.1.1", false)]            // public IPv4 (Cloudflare)
    [InlineData("99.63.255.255", false)]      // just below CGNAT boundary
    [InlineData("100.128.0.1", false)]        // just above CGNAT boundary
    [InlineData("2606:4700:4700::1111", false)] // public IPv6 (Cloudflare)
    public void IsNonPublicAddressFiltersPrivateFamilies(string ipString, bool expectRejected)
    {
        var ip = IPAddress.Parse(ipString);
        Assert.Equal(expectRejected, NFCController.IsNonPublicAddress(ip));
    }

    // Verifies the resolve-and-validate helper directly rejects a Uri whose
    // literal-IP host is loopback, without hitting DNS. Covers the callback
    // guard's decision path without needing to boot the full endpoint stack.
    [Fact]
    [Trait("Fast", "Fast")]
    public async Task ResolveAndValidateRejectsLoopbackCallbackLiteral()
    {
        var (safe, addresses, reason) = await NFCController.ResolveAndValidateAsync(
            new Uri("http://127.0.0.1:9999/pay-request"), CancellationToken.None);
        Assert.False(safe);
        Assert.Null(addresses);
        Assert.Contains("local or private network", reason, StringComparison.OrdinalIgnoreCase);
    }

    // IPv6 literal-IP host that hits the IPAddress.TryParse short-circuit.
    // Ensures the IPv6 loopback branch of IsNonPublicAddress is wired all
    // the way through the helper's control flow, not just the predicate.
    [Fact]
    [Trait("Fast", "Fast")]
    public async Task ResolveAndValidateRejectsIPv6LoopbackLiteral()
    {
        var (safe, _, reason) = await NFCController.ResolveAndValidateAsync(
            new Uri("http://[::1]:9999/pay-request"), CancellationToken.None);
        Assert.False(safe);
        Assert.Contains("local or private network", reason, StringComparison.OrdinalIgnoreCase);
    }

    // Public host passes and returns at least one resolved address for the
    // caller to socket-pin. Uses a stable well-known DNS name.
    [Fact]
    [Trait("Fast", "Fast")]
    public async Task ResolveAndValidateAcceptsPublicHost()
    {
        var (safe, addresses, reason) = await NFCController.ResolveAndValidateAsync(
            new Uri("https://one.one.one.one/"), CancellationToken.None);
        Assert.True(safe, $"expected public 1.1.1.1 to pass, got reason: {reason}");
        Assert.NotNull(addresses);
        Assert.NotEmpty(addresses);
        foreach (var addr in addresses)
        {
            Assert.False(NFCController.IsNonPublicAddress(addr));
        }
    }

    // Documents the SSRF surface on the [AllowAnonymous] NFC LNURL-Withdraw
    // endpoint. Without the guard the endpoint would issue an outbound fetch
    // against a caller-supplied URL, which an unauthenticated remote could use
    // as a reachability oracle for internal services (cloud metadata, localhost
    // admin panels, adjacent-container services on shared docker networks).
    [Fact(Timeout = 60 * 20 * 1000)]
    [Trait("Integration", "Integration")]
    [Trait("Lightning", "Lightning")]
    public async Task NfcLnurlWithdrawRejectsSsrfTargets()
    {
        using var tester = CreateServerTester();
        tester.ActivateLightning();
        await tester.StartAsync();
        await tester.EnsureChannelsSetup();
        var user = tester.NewAccount();
        await user.GrantAccessAsync(true);
        user.RegisterLightningNode("BTC", LightningTestImplementation.CoreLightning);

        var client = await user.CreateClient(Policies.Unrestricted);
        var invoice = await client.CreateInvoice(user.StoreId, new CreateInvoiceRequest
        {
            Currency = "USD",
            Amount = 0.1m,
            Checkout = new CreateInvoiceRequest.CheckoutOptions
            {
                PaymentMethods = new[] { "BTC-LN" },
                DefaultPaymentMethod = "BTC-LN"
            }
        });
        Assert.NotNull(invoice);

        async Task<HttpResponseMessage> Submit(string lnurl)
        {
            var body = JsonConvert.SerializeObject(new
            {
                Lnurl = lnurl,
                InvoiceId = invoice.Id,
                Amount = (long?)null
            });
            var req = new HttpRequestMessage(HttpMethod.Post,
                new Uri(tester.PayTester.ServerUri, "plugins/NFC"))
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            return await tester.PayTester.HttpClient.SendAsync(req);
        }

        // LNURL.LNURL.EncodeUri validates the URL must be onion / https / on the
        // "local network" (LNURL's own definition, narrower than our guard's).
        // We use https:// to satisfy that validation transparently - the SSRF
        // guard in the endpoint runs on the DECODED uri from Parse, and its
        // scheme/host filters treat https + a private IP the same as http + a
        // private IP. The bech32 wrapper is purely transport.
        static string ToBech32Lnurl(string rawUrl) =>
            LNURL.LNURL.EncodeUri(new Uri(rawUrl), "withdrawRequest", bech32: true).ToString();

        // Loopback: an attacker pointing at 127.0.0.1 could probe local admin panels.
        var resp = await Submit(ToBech32Lnurl("https://127.0.0.1:1/withdraw"));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("local or private network", body, StringComparison.OrdinalIgnoreCase);

        // Cloud metadata link-local: the canonical AWS/GCP metadata service oracle.
        resp = await Submit(ToBech32Lnurl("https://169.254.169.254/latest/meta-data/"));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("local or private network", body, StringComparison.OrdinalIgnoreCase);

        // RFC1918 host: adjacent-container / docker-bridge peer.
        resp = await Submit(ToBech32Lnurl("https://10.0.0.1/withdraw"));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("local or private network", body, StringComparison.OrdinalIgnoreCase);

        // Single-label host: routes through /etc/hosts to another service on the box.
        resp = await Submit(ToBech32Lnurl("https://localhost/withdraw"));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("local or private network", body, StringComparison.OrdinalIgnoreCase);

        // IPv6 loopback literal - exercises the [::1] branch of IsNonPublicAddress
        // through the endpoint round-trip.
        resp = await Submit(ToBech32Lnurl("https://[::1]:1/withdraw"));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("local or private network", body, StringComparison.OrdinalIgnoreCase);

        // IPv6 link-local literal - the fe80::/10 branch. Deliberately no zone-id
        // suffix because .NET Uri accepts bare fe80::1 and IPAddress.TryParse
        // succeeds without a scope.
        resp = await Submit(ToBech32Lnurl("https://[fe80::1]:1/withdraw"));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("local or private network", body, StringComparison.OrdinalIgnoreCase);
    }
}
