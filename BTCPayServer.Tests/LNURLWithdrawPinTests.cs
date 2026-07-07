using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace BTCPayServer.Tests;

[Collection(nameof(NonParallelizableCollectionDefinition))]
public class LNURLWithdrawPinTests(ITestOutputHelper helper) : UnitTestBase(helper)
{
    // LUD-290: when a customer pays a checkout invoice with an LNURL-Withdraw that advertises a
    // `pinLimit`, BTCPay (acting as the wallet) must collect a PIN and forward it on the callback.
    [Fact]
    [Trait("Integration", "Integration")]
    [Trait("Lightning", "Lightning")]
    public async Task CanUseLNURLWithdrawWithPin()
    {
        using var tester = CreateServerTester();
        tester.ActivateLightning();
        await tester.StartAsync();
        var user = tester.NewAccount();
        user.GrantAccess(true);
        user.RegisterDerivationScheme("BTC");
        await user.RegisterLightningNodeAsync("BTC");

        var client = await user.CreateClient();
        // 1000 sats invoice, so a pinLimit of 500 sats triggers the PIN requirement.
        var invoice = await client.CreateInvoice(user.StoreId,
            new CreateInvoiceRequest { Amount = 0.00001m, Currency = "BTC" });

        using var withdrawService = new FakeWithdrawService();
        var lnurl = LNURL.LNURL.EncodeBech32(withdrawService.ServerUri);
        var httpClient = tester.PayTester.HttpClient;

        // The NFC controller is routed at [Route("plugins/NFC")] with a single (template-less) action,
        // so the endpoint is "plugins/NFC" itself - the same URL the checkout gets from Url.Action.
        async Task<(HttpStatusCode Status, string Body)> Submit(object payload)
        {
            var response = await httpClient.PostAsync("plugins/NFC",
                new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json"));
            return (response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        // --- Amount (1000 sats) is at/above the pinLimit (500 sats): a PIN must be requested, and the
        //     callback must NOT be contacted yet.
        withdrawService.PinLimitMilliSats = 500_000;
        withdrawService.ExpectedPin = "1234";
        var initiate = await Submit(new { lnurl, invoiceId = invoice.Id });
        Assert.True(initiate.Status == HttpStatusCode.OK, $"initiate: {initiate.Status} {initiate.Body}");
        var initiateBody = JObject.Parse(initiate.Body);
        Assert.True(initiateBody["requiresPin"]?.Value<bool>() == true, initiate.Body);
        var token = initiateBody["token"]?.Value<string>();
        Assert.False(string.IsNullOrEmpty(token));
        Assert.Empty(withdrawService.Callbacks);

        // --- A wrong PIN is forwarded, rejected, and the session is kept so the customer can retry.
        var wrong = await Submit(new { invoiceId = invoice.Id, token, pin = "0000" });
        Assert.True(wrong.Status == HttpStatusCode.BadRequest, $"wrong pin: {wrong.Status} {wrong.Body}");
        Assert.Contains("Invalid PIN", wrong.Body);
        Assert.True(withdrawService.Callbacks.TryDequeue(out var wrongCallback));
        Assert.Equal("0000", wrongCallback.Pin);
        Assert.False(string.IsNullOrEmpty(wrongCallback.Pr));

        // --- Retrying with the correct PIN (same token/session, same k1) succeeds.
        var right = await Submit(new { invoiceId = invoice.Id, token, pin = "1234" });
        Assert.True(right.Status == HttpStatusCode.OK, $"right pin: {right.Status} {right.Body}");
        Assert.True(withdrawService.Callbacks.TryDequeue(out var rightCallback));
        Assert.Equal("1234", rightCallback.Pin);

        // --- When the amount is below the pinLimit, behaviour is unchanged: no PIN, the callback is
        //     contacted straight away with no `pin` parameter.
        withdrawService.PinLimitMilliSats = 100_000_000; // 100k sats, above the 1000 sat amount
        withdrawService.ExpectedPin = null;
        var noPin = await Submit(new { lnurl, invoiceId = invoice.Id });
        Assert.True(noPin.Status == HttpStatusCode.OK, $"no pin: {noPin.Status} {noPin.Body}");
        Assert.True(withdrawService.Callbacks.TryDequeue(out var noPinCallback));
        Assert.True(string.IsNullOrEmpty(noPinCallback.Pin));
    }

    // Minimal external LNURL-Withdraw service: serves a withdrawRequest (optionally advertising a
    // pinLimit) and records the callback it receives so the test can assert the forwarded PIN.
    private class FakeWithdrawService : IDisposable
    {
        private readonly IHost _host;

        public FakeWithdrawService()
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureLogging(l => l.ClearProviders())
                .ConfigureWebHostDefaults(web => web
                    .UseKestrel()
                    .UseUrls("http://127.0.0.1:0")
                    .Configure(app => app.Run(Handle)))
                .Build();
            _host.Start();
            ServerUri = new Uri(_host.GetServerFeatures<IServerAddressesFeature>().Addresses.First());
        }

        public Uri ServerUri { get; }
        public long? PinLimitMilliSats { get; set; }
        public string ExpectedPin { get; set; }
        public ConcurrentQueue<(string Pin, string Pr)> Callbacks { get; } = new();

        private async Task Handle(HttpContext ctx)
        {
            ctx.Response.ContentType = "application/json";
            var pr = ctx.Request.Query["pr"].ToString();
            if (string.IsNullOrEmpty(pr))
            {
                var withdrawRequest = new JObject
                {
                    ["tag"] = "withdrawRequest",
                    ["callback"] = new Uri(ServerUri, "cb").ToString(),
                    ["k1"] = "test-k1",
                    ["defaultDescription"] = "PIN withdraw test",
                    ["minWithdrawable"] = 1,
                    ["maxWithdrawable"] = 100_000_000
                };
                if (PinLimitMilliSats is not null)
                    withdrawRequest["pinLimit"] = PinLimitMilliSats.Value;
                await ctx.Response.WriteAsync(withdrawRequest.ToString());
            }
            else
            {
                var pin = ctx.Request.Query["pin"].ToString();
                Callbacks.Enqueue((pin, pr));
                var ok = ExpectedPin is null || pin == ExpectedPin;
                var result = ok
                    ? new JObject { ["status"] = "OK" }
                    : new JObject { ["status"] = "ERROR", ["reason"] = "Invalid PIN" };
                await ctx.Response.WriteAsync(result.ToString());
            }
        }

        public void Dispose() => _host.Dispose();
    }
}
