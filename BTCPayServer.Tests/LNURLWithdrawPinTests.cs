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
    // LUD-290: paying a checkout invoice with a pinLimit LNURL-Withdraw prompts for a PIN and forwards it.
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

        // NFCController is routed at "plugins/NFC" (single template-less action).
        async Task<(HttpStatusCode Status, string Body)> Submit(object payload)
        {
            var response = await httpClient.PostAsync("plugins/NFC",
                new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json"));
            return (response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        // Amount (1000 sats) >= pinLimit (500 sats): PIN required, callback not contacted yet.
        withdrawService.PinLimitMilliSats = 500_000;
        withdrawService.ExpectedPin = "1234";
        var initiate = await Submit(new { lnurl, invoiceId = invoice.Id });
        Assert.True(initiate.Status == HttpStatusCode.OK, $"initiate: {initiate.Status} {initiate.Body}");
        var initiateBody = JObject.Parse(initiate.Body);
        Assert.True(initiateBody["requiresPin"]?.Value<bool>() == true, initiate.Body);
        var token = initiateBody["token"]?.Value<string>();
        Assert.False(string.IsNullOrEmpty(token));
        Assert.Empty(withdrawService.Callbacks);

        // Wrong PIN is forwarded, rejected, and the session kept for retry.
        var wrong = await Submit(new { invoiceId = invoice.Id, token, pin = "0000" });
        Assert.True(wrong.Status == HttpStatusCode.BadRequest, $"wrong pin: {wrong.Status} {wrong.Body}");
        Assert.Contains("Invalid PIN", wrong.Body);
        Assert.True(withdrawService.Callbacks.TryDequeue(out var wrongCallback));
        Assert.Equal("0000", wrongCallback.Pin);
        Assert.False(string.IsNullOrEmpty(wrongCallback.Pr));

        // Correct PIN on the same session/k1 succeeds (2xx; a reasonless OK maps to 204).
        var right = await Submit(new { invoiceId = invoice.Id, token, pin = "1234" });
        Assert.True((int)right.Status is >= 200 and < 300, $"right pin: {right.Status} {right.Body}");
        Assert.True(withdrawService.Callbacks.TryDequeue(out var rightCallback));
        Assert.Equal("1234", rightCallback.Pin);

        // Amount below pinLimit: no PIN, callback contacted directly with no pin.
        withdrawService.PinLimitMilliSats = 100_000_000; // 100k sats, above the 1000 sat amount
        withdrawService.ExpectedPin = null;
        var noPin = await Submit(new { lnurl, invoiceId = invoice.Id });
        Assert.True((int)noPin.Status is >= 200 and < 300, $"no pin: {noPin.Status} {noPin.Body}");
        Assert.True(withdrawService.Callbacks.TryDequeue(out var noPinCallback));
        Assert.True(string.IsNullOrEmpty(noPinCallback.Pin));
    }

    // Minimal withdraw service: serves a withdrawRequest (optional pinLimit) and records callbacks.
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
