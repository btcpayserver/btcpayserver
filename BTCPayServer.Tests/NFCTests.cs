using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Lightning;
using Newtonsoft.Json;
using Xunit;

namespace BTCPayServer.Tests;

[Collection(nameof(NonParallelizableCollectionDefinition))]
public class NFCTests(ITestOutputHelper testOutputHelper) : UnitTestBase(testOutputHelper)
{
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

        // Loopback: an attacker pointing at 127.0.0.1 could probe local admin panels.
        var resp = await Submit("http://127.0.0.1:1/withdraw");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("local or private network", body, StringComparison.OrdinalIgnoreCase);

        // Cloud metadata link-local: the canonical AWS/GCP metadata service oracle.
        resp = await Submit("http://169.254.169.254/latest/meta-data/");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("local or private network", body, StringComparison.OrdinalIgnoreCase);

        // RFC1918 host: adjacent-container / docker-bridge peer.
        resp = await Submit("http://10.0.0.1/withdraw");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("local or private network", body, StringComparison.OrdinalIgnoreCase);

        // Single-label host: routes through /etc/hosts to another service on the box.
        resp = await Submit("http://localhost/withdraw");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("local or private network", body, StringComparison.OrdinalIgnoreCase);

        // Non-http(s) scheme LNURL.Parse may return: file/ftp/gopher.
        resp = await Submit("http://example.com/withdraw".Replace("http://", "ftp://"));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("scheme must be http or https", body, StringComparison.OrdinalIgnoreCase);
    }
}
