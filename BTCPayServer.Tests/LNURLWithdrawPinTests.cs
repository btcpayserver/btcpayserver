using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using Microsoft.AspNetCore.Http;
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

        using var fakeServer = new FakeServer();
        await fakeServer.Start();
        var lnurl = LNURL.LNURL.EncodeBech32(fakeServer.ServerUri);
        var callback = new Uri(fakeServer.ServerUri, "cb");
        var httpClient = tester.PayTester.HttpClient;

        Task<HttpResponseMessage> Submit(object payload) =>
            httpClient.PostAsync("plugins/NFC/SubmitLNURLWithdrawForInvoice",
                new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json"));

        async Task<HttpContext> NextRequest()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            return await fakeServer.GetNextRequest(cts.Token);
        }

        static async Task Respond(HttpContext ctx, JObject body)
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(body.ToString());
        }

        JObject WithdrawRequest(string k1, long pinLimit) => new()
        {
            ["tag"] = "withdrawRequest",
            ["callback"] = callback.ToString(),
            ["k1"] = k1,
            ["defaultDescription"] = "PIN withdraw test",
            ["minWithdrawable"] = 1,
            ["maxWithdrawable"] = 100_000_000,
            ["pinLimit"] = pinLimit
        };

        // --- Amount (1000 sats) is at/above the pinLimit (500 sats): a PIN must be requested,
        //     and the callback must NOT be contacted yet.
        var initiate = Submit(new { lnurl, invoiceId = invoice.Id });
        var withdrawFetch = await NextRequest();
        await Respond(withdrawFetch, WithdrawRequest("k1-pin", 500_000));
        fakeServer.Done();

        var initiateResponse = await initiate;
        Assert.Equal(HttpStatusCode.OK, initiateResponse.StatusCode);
        var initiateBody = JObject.Parse(await initiateResponse.Content.ReadAsStringAsync());
        Assert.True(initiateBody["requiresPin"].Value<bool>());
        var token = initiateBody["token"].Value<string>();
        Assert.False(string.IsNullOrEmpty(token));

        // --- A wrong PIN is forwarded, rejected, and the session is kept so the customer can retry.
        var wrongAttempt = Submit(new { invoiceId = invoice.Id, token, pin = "0000" });
        var wrongCallback = await NextRequest();
        Assert.Equal("0000", wrongCallback.Request.Query["pin"].ToString());
        Assert.False(string.IsNullOrEmpty(wrongCallback.Request.Query["pr"].ToString()));
        await Respond(wrongCallback, new JObject { ["status"] = "ERROR", ["reason"] = "Invalid PIN" });
        fakeServer.Done();

        var wrongResponse = await wrongAttempt;
        Assert.Equal(HttpStatusCode.BadRequest, wrongResponse.StatusCode);
        Assert.Contains("Invalid PIN", await wrongResponse.Content.ReadAsStringAsync());

        // --- Retrying with the correct PIN (same token/session, same k1) succeeds.
        var rightAttempt = Submit(new { invoiceId = invoice.Id, token, pin = "1234" });
        var rightCallback = await NextRequest();
        Assert.Equal("1234", rightCallback.Request.Query["pin"].ToString());
        await Respond(rightCallback, new JObject { ["status"] = "OK" });
        fakeServer.Done();

        var rightResponse = await rightAttempt;
        Assert.Equal(HttpStatusCode.OK, rightResponse.StatusCode);

        // --- When the amount is below the pinLimit, behaviour is unchanged: no PIN, the callback is
        //     contacted straight away with no `pin` parameter.
        var noPin = Submit(new { lnurl, invoiceId = invoice.Id });
        var noPinFetch = await NextRequest();
        await Respond(noPinFetch, WithdrawRequest("k1-nopin", 100_000_000));
        fakeServer.Done();

        var noPinCallback = await NextRequest();
        Assert.True(string.IsNullOrEmpty(noPinCallback.Request.Query["pin"].ToString()));
        await Respond(noPinCallback, new JObject { ["status"] = "OK" });
        fakeServer.Done();

        var noPinResponse = await noPin;
        Assert.Equal(HttpStatusCode.OK, noPinResponse.StatusCode);
    }
}
