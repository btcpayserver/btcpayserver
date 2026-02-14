using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Lightning;
using BTCPayServer.NTag424;
using BTCPayServer.Payments;
using BTCPayServer.Views.Stores;
using Dapper;
using LNURL;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using NBitcoin;
using NBitcoin.DataEncoders;
using Xunit;
using Xunit.Abstractions;
using static Microsoft.Playwright.Assertions;
using PayoutData = BTCPayServer.Data.PayoutData;

namespace BTCPayServer.Tests;

[Collection(nameof(NonParallelizableCollectionDefinition))]
public class PullPaymentsTests(ITestOutputHelper helper) : UnitTestBase(helper)
{
    [Fact]
    [Trait("Playwright", "Playwright-2")]
    [Trait("Lightning", "Lightning")]
    public async Task CanUsePullPaymentsViaUI()
    {
        await using var s = CreatePlaywrightTester();
        async Task<PayoutData> ClickClaimAmount()
        {
            return (await s.Server.WaitForEvent<PayoutEvent>(async () =>
            {
                await s.Page.PressAsync("#ClaimedAmount", "Enter");
            }, e => e.Type == PayoutEvent.PayoutEventType.Created)).Payout;
        }
        s.Server.DeleteStore = false;
        s.Server.ActivateLightning(LightningConnectionType.LndREST);
        await s.StartAsync();
        await s.Server.EnsureChannelsSetup();
        await s.RegisterNewUser(true);
        await s.CreateNewStore();
        await s.GenerateWallet("BTC", "", true, true);

        await s.Server.ExplorerNode.GenerateAsync(1);
        await s.FundStoreWallet(denomination: 50.0m);
        await s.GoToStore(s.StoreId, StoreNavPages.PullPayments);
        await s.ClickPagePrimary();
        await s.Page.FillAsync("#Name", "PP1");
        await s.Page.FillAsync("#Amount", "99.0");
        await s.ClickPagePrimary();

        await using (_ = await s.SwitchPage(async () =>
                     {
                         await s.Page.Locator(".actions-col a:has-text('View')").First.ClickAsync();
                     }))
        {
            await Expect(s.Page.Locator("body")).ToContainTextAsync("PP1");
        }

        await s.GoToStore(s.StoreId, StoreNavPages.PullPayments);

        await s.ClickPagePrimary();
        await s.Page.FillAsync("#Name", "PP2");
        await s.Page.FillAsync("#Amount", "100.0");
        await s.ClickPagePrimary();

        string viewPullPaymentUrl;
        // This should select the first View, ie, the last one PP2
        await using (await s.SwitchPage(async () =>
                     {
                         await s.Page.Locator(".actions-col a:has-text('View')").First.ClickAsync();
                     }))
        {
            var address = await s.Server.ExplorerNode.GetNewAddressAsync();
            await s.Page.FillAsync("#Destination", address.ToString());
            await s.Page.FillAsync("#ClaimedAmount", "15");
            await ClickClaimAmount();
            await s.FindAlertMessage();

            // We should not be able to use an address already used
            await s.Page.FillAsync("#Destination", address.ToString());
            await s.Page.FillAsync("#ClaimedAmount", "20");
            await s.Page.PressAsync("#ClaimedAmount", "Enter");
            await s.FindAlertMessage(StatusMessageModel.StatusSeverity.Error);

            address = await s.Server.ExplorerNode.GetNewAddressAsync();
            await s.Page.FillAsync("#Destination", address.ToString());
            await s.Page.FillAsync("#ClaimedAmount", "20");
            await ClickClaimAmount();
            await s.FindAlertMessage();
            await Expect(s.Page.Locator("body")).ToContainTextAsync("Awaiting Approval");

            viewPullPaymentUrl = s.Page.Url;
        }

        // This one should have nothing
        await s.GoToStore(s.StoreId, StoreNavPages.PullPayments);
        var payouts = s.Page.Locator(".pp-payout");
        await Expect(payouts).ToHaveCountAsync(2);
        await payouts.Nth(1).ClickAsync();
        await Expect(s.Page.Locator(".payout")).ToHaveCountAsync(0);
        // PP2 should have payouts
        await s.GoToStore(s.StoreId, StoreNavPages.PullPayments);
        payouts = s.Page.Locator(".pp-payout");
        await payouts.First.ClickAsync();
        await Expect(s.Page.Locator(".payout")).ToHaveCountAsync(2);

        await s.Page.CheckAsync(".mass-action-select-all");
        await s.Page.ClickAsync($"#{PayoutState.AwaitingApproval}-approve-pay");

        await s.Page.ClickAsync("#SignTransaction");
        await s.Page.ClickAsync("button[value='broadcast']");
        await s.FindAlertMessage();

        var pmo = await s.GoToWalletTransactions();
        await Expect(s.Page.Locator(".transaction-label")).ToHaveCountAsync(2);
        await pmo.AssertHasLabels("payout");
        await pmo.AssertHasLabels("pull-payment");

        await s.GoToStore(s.StoreId, StoreNavPages.Payouts);
        await s.Page.ClickAsync($"#{PayoutState.InProgress}-view");

        await Expect(s.Page.Locator(".transaction-link")).ToHaveCountAsync(2);

        await s.GoToUrl(viewPullPaymentUrl);
        await Expect(s.Page.Locator(".transaction-link")).ToHaveCountAsync(2);
        await Expect(s.Page.Locator("body")).ToContainTextAsync(PayoutState.InProgress.GetStateString());

        await s.Server.ExplorerNode.GenerateAsync(1);

        await TestUtils.EventuallyAsync(async () =>
        {
            await s.Page.ReloadAsync();
            Assert.Contains(PayoutState.Completed.GetStateString(), await s.Page.ContentAsync());
        });
        await s.Server.ExplorerNode.GenerateAsync(10);
        var pullPaymentId = viewPullPaymentUrl.Split('/').Last();

        await TestUtils.EventuallyAsync(async () =>
        {
            await using var ctx = s.Server.PayTester.GetService<ApplicationDbContextFactory>().CreateContext();
            var payoutsData = await ctx.Payouts.Where(p => p.PullPaymentDataId == pullPaymentId).ToListAsync();
            Assert.True(payoutsData.All(p => p.State == PayoutState.Completed));
        });
        await s.GoToHome();
        //offline/external payout test

        await s.CreateNewStore();
        await s.GenerateWallet("BTC", "", true, true);
        await s.GoToStore(s.StoreId, StoreNavPages.PullPayments);

        await s.ClickPagePrimary();
        await s.Page.FillAsync("#Name", "External Test");
        await s.Page.FillAsync("#Amount", "0.001");
        await s.Page.FillAsync("#Currency", "BTC");
        await s.ClickPagePrimary();

        await using (await s.SwitchPage(async () =>
                     {
                         await s.Page.Locator(".actions-col a:has-text('View')").First.ClickAsync();
                     }))
        {
            var address = await s.Server.ExplorerNode.GetNewAddressAsync();
            await s.Page.FillAsync("#Destination", address.ToString());
            await ClickClaimAmount();
            await s.FindAlertMessage();

            await Expect(s.Page.Locator("body")).ToContainTextAsync(PayoutState.AwaitingApproval.GetStateString());
            await s.Page.Context.Pages.First().BringToFrontAsync();
        }

        await s.GoToStore(s.StoreId, StoreNavPages.Payouts);
        await s.Page.ClickAsync($"#{PayoutState.AwaitingApproval}-view");
        await s.Page.CheckAsync(".mass-action-select-all");
        await s.Page.ClickAsync($"#{PayoutState.AwaitingApproval}-approve");
        await s.FindAlertMessage();
        var onchainAddress = await s.Server.ExplorerNode.GetNewAddressAsync();
        await s.Server.ExplorerNode.SendToAddressAsync(onchainAddress, Money.FromUnit(0.001m, MoneyUnit.BTC));
        await s.Page.Context.Pages.First().BringToFrontAsync();

        await s.GoToStore(s.StoreId, StoreNavPages.Payouts);

        await s.Page.ClickAsync($"#{PayoutState.AwaitingPayment}-view");
        await Expect(s.Page.Locator("body")).ToContainTextAsync(PayoutState.AwaitingPayment.GetStateString());
        await s.Page.CheckAsync(".mass-action-select-all");
        await s.Page.ClickAsync($"#{PayoutState.AwaitingPayment}-mark-paid");
        await s.FindAlertMessage();

        await s.Page.ClickAsync($"#{PayoutState.InProgress}-view");
        await Expect(s.Page.Locator(".payout")).ToHaveCountAsync(0);
        await s.Page.ClickAsync($"#{PayoutState.Completed}-view");
        await Expect(s.Page.Locator(".payout").Filter(new() { HasText = "External Test" })).ToHaveCountAsync(1);


        // lightning tests,
        // Since the merchant is sending on lightning, it needs some liquidity from the client
        var payoutAmount = LightMoney.Satoshis(1000);
        var minimumReserve = LightMoney.Satoshis(167773m);
        var inv = await s.Server.MerchantLnd.Client.CreateInvoice(minimumReserve + payoutAmount, "Donation to merchant", TimeSpan.FromHours(1),
            CancellationToken.None);
        var resp = await s.Server.CustomerLightningD.Pay(inv.BOLT11);
        Assert.Equal(PayResult.Ok, resp.Result);

        var newStore = await s.CreateNewStore();
        await s.AddLightningNode();

        //Currently an onchain wallet is required to use the Lightning payouts feature…
        await s.GenerateWallet("BTC", "", true, true);
        await s.GoToStore(newStore.storeId, StoreNavPages.PullPayments);
        await s.ClickPagePrimary();

        var paymentMethodOptions = s.Page.Locator("input[name='PayoutMethods']");
        await Expect(paymentMethodOptions).ToHaveCountAsync(2);

        await s.Page.FillAsync("#Name", "Lightning Test");
        await s.Page.FillAsync("#Amount", payoutAmount.ToString());
        await s.Page.FillAsync("#Currency", "BTC");
        await s.ClickPagePrimary();
        await s.Page.Locator(".actions-col a:has-text('View')").First.ClickAsync();
        string bolt;
        PayoutData payout;
        await using (await s.SwitchPage(async () =>
                     {
                         await s.Page.Locator(".actions-col a:has-text('View')").First.ClickAsync();
                     }))
        {
            // Bitcoin-only, SelectedPaymentMethod should not be displayed
            await Expect(s.Page.Locator("#SelectedPayoutMethod")).ToHaveCountAsync(0);

            bolt = (await s.Server.CustomerLightningD.CreateInvoice(
                payoutAmount,
                $"LN payout test {DateTime.UtcNow.Ticks}",
                TimeSpan.FromHours(1), CancellationToken.None)).BOLT11;
            await s.Page.FillAsync("#Destination", bolt);
            await s.Page.PressAsync("#ClaimedAmount", "Enter");
            //we do not allow short-life bolts.
            await s.FindAlertMessage(StatusMessageModel.StatusSeverity.Error);

            bolt = (await s.Server.CustomerLightningD.CreateInvoice(
                payoutAmount,
                $"LN payout test {DateTime.UtcNow.Ticks}",
                TimeSpan.FromDays(31), CancellationToken.None)).BOLT11;
            await s.Page.FillAsync("#Destination", bolt);
            payout = await ClickClaimAmount();
            await s.FindAlertMessage();

            await Expect(s.Page.Locator("body")).ToContainTextAsync(PayoutState.AwaitingApproval.GetStateString());
        }

        await s.GoToStore(newStore.storeId, StoreNavPages.Payouts);
        await s.Page.ClickAsync($"#{PaymentTypes.LN.GetPaymentMethodId("BTC")}-view");
        await s.Page.ClickAsync($"#{PayoutState.AwaitingApproval}-view");
        await s.Page.CheckAsync(".mass-action-select-all");
        await s.Server.WaitForEvent<PayoutEvent>(async () =>
        {
            await s.Page.ClickAsync($"#{PayoutState.AwaitingApproval}-approve-pay");
        }, e => e.Type == PayoutEvent.PayoutEventType.Approved && e.Payout.Id == payout.Id);

        await Expect(s.Page.Locator("body")).ToContainTextAsync(bolt);
        await Expect(s.Page.Locator("body")).ToContainTextAsync($"{payoutAmount} BTC");

        await s.Server.WaitForEvent<PayoutEvent>(async () =>
        {
            await s.Page.ClickAsync("#Pay");
        }, e => e.Type == PayoutEvent.PayoutEventType.Updated && e.Payout.Id == payout.Id);

        await s.FindAlertMessage();
        await s.GoToStore(newStore.storeId, StoreNavPages.Payouts);
        await s.Page.ClickAsync($"#{PaymentTypes.LN.GetPaymentMethodId("BTC")}-view");

        await s.Page.ClickAsync($"#{PayoutState.Completed}-view");
        await Expect(s.Page.Locator("body")).ToContainTextAsync(bolt);
        if (!(await s.Page.ContentAsync()).Contains(bolt))
        {
            await s.Page.ClickAsync($"#{PayoutState.AwaitingPayment}-view");


            await s.Page.CheckAsync(".mass-action-select-all");
            await s.Page.ClickAsync($"#{PayoutState.AwaitingPayment}-mark-paid");
            await s.Page.ClickAsync($"#{PaymentTypes.LN.GetPaymentMethodId("BTC")}-view");

            await s.Page.ClickAsync($"#{PayoutState.Completed}-view");
            await Expect(s.Page.Locator("body")).ToContainTextAsync(bolt);
        }

        //auto-approve pull payments
        await s.GoToStore(StoreNavPages.PullPayments);
        await s.ClickPagePrimary();
        await s.Page.FillAsync("#Name", "PP1");
        await s.Page.CheckAsync("#AutoApproveClaims");
        await s.Page.FillAsync("#Amount", "99.0");
        await s.Page.PressAsync("#Amount", "Enter");
        await s.FindAlertMessage();

        string lnurlStr;
        await using (await s.SwitchPage(async () =>
                     {
                         await s.Page.Locator(".actions-col a:has-text('View')").First.ClickAsync();
                     }))
        {
            var address = await s.Server.ExplorerNode.GetNewAddressAsync();
            await s.Page.FillAsync("#Destination", address.ToString());
            await s.Page.FillAsync("#ClaimedAmount", "20");
            await ClickClaimAmount();
            await s.FindAlertMessage();

            await Expect(s.Page.Locator("body")).ToContainTextAsync(PayoutState.AwaitingPayment.GetStateString());
        }

        // LNURL Withdraw support check with BTC denomination
        await s.GoToStore(s.StoreId, StoreNavPages.PullPayments);
        await s.ClickPagePrimary();
        await s.Page.FillAsync("#Name", "PP1");
        await s.Page.CheckAsync("#AutoApproveClaims");
        await s.Page.FillAsync("#Amount", "0.0000001");
        await s.Page.FillAsync("#Currency", "BTC");
        await s.Page.PressAsync("#Currency", "Enter");
        await s.FindAlertMessage();

        await using (await s.SwitchPage(async () =>
                     {
                         await s.Page.Locator(".actions-col a:has-text('View')").First.ClickAsync();
                     }))
        {
            await Expect(s.Page.Locator("#lnurlwithdraw-button")).ToBeVisibleAsync();
            await s.Page.ClickAsync("#lnurlwithdraw-button");
            await s.Page.WaitForFunctionAsync("() => document.querySelector('#qr-code-data-input')?.value?.length > 0");

            // Try to use lnurlw via the QR Code
            lnurlStr = await s.Page.Locator("#qr-code-data-input").InputValueAsync();
            var lnurl = new Uri(LNURL.LNURL.Parse(lnurlStr, out _).ToString().Replace("https", "http"));
            await s.Page.ClickAsync("button[data-bs-dismiss='modal']");
            var info = Assert.IsType<LNURLWithdrawRequest>(await LNURL.LNURL.FetchInformation(lnurl, s.Server.PayTester.HttpClient));
            Assert.Equal(info.MaxWithdrawable, new LightMoney(0.0000001m, LightMoneyUnit.BTC));
            Assert.Equal(info.CurrentBalance, new LightMoney(0.0000001m, LightMoneyUnit.BTC));
            info = Assert.IsType<LNURLWithdrawRequest>(await LNURL.LNURL.FetchInformation(info.BalanceCheck, s.Server.PayTester.HttpClient));
            Assert.Equal(info.MaxWithdrawable, new LightMoney(0.0000001m, LightMoneyUnit.BTC));
            Assert.Equal(info.CurrentBalance, new LightMoney(0.0000001m, LightMoneyUnit.BTC));

            var bolt2 = (await s.Server.CustomerLightningD.CreateInvoice(
                new LightMoney(0.00000005m, LightMoneyUnit.BTC),
                $"LNurl w payout test {DateTime.UtcNow.Ticks}",
                TimeSpan.FromHours(1), CancellationToken.None));
            var response = await info.SendRequest(bolt2.BOLT11, s.Server.PayTester.HttpClient, null, null);
            // Oops!
            Assert.Equal(
                "The request has been approved. The sender needs to send the payment manually. (Or activate the lightning automated payment processor)",
                response.Reason);
            var account = await s.AsTestAccount().CreateClient();
            await account.UpdateStoreLightningAutomatedPayoutProcessors(s.StoreId, "BTC-LN", new()
            {
                ProcessNewPayoutsInstantly = true,
                IntervalSeconds = TimeSpan.FromSeconds(60)
            });
            // Now it should process to complete
            await TestUtils.EventuallyAsync(async () =>
            {
                await s.Page.ReloadAsync();
                var content = await s.Page.ContentAsync();
                Assert.Contains(bolt2.BOLT11, content);
                Assert.Contains(PayoutState.Completed.GetStateString(), content);
                Assert.Equal(LightningInvoiceStatus.Paid, (await s.Server.CustomerLightningD.GetInvoice(bolt2.Id)).Status);
            });
        }

        // Simulate a boltcard
        Assert.False(string.IsNullOrEmpty(lnurlStr), "LNURL string should have been captured from the previous flow");
        {
            var db = s.Server.PayTester.GetService<ApplicationDbContextFactory>();
            var ppid = new Uri(LNURL.LNURL.Parse(lnurlStr, out _).ToString().Replace("https", "http")).AbsoluteUri.Split('/').Last();
            var issuerKey = new IssuerKey(SettingsRepositoryExtensions.FixedKey());
            var uid = RandomNumberGenerator.GetBytes(7);
            var cardKey = issuerKey.CreatePullPaymentCardKey(uid, 0, ppid);
            var keys = cardKey.DeriveBoltcardKeys(issuerKey);
            await db.LinkBoltcardToPullPayment(ppid, issuerKey, uid);
            var piccData = new byte[] { 0xc7 }.Concat(uid).Concat(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }).ToArray();
            var p = keys.EncryptionKey.Encrypt(piccData);
            var c = keys.AuthenticationKey.GetSunMac(uid, 1);
            var boltcardUrl = new Uri(s.Server.PayTester.ServerUri.AbsoluteUri +
                                      $"boltcard?p={Encoders.Hex.EncodeData(p).ToUpperInvariant()}&c={Encoders.Hex.EncodeData(c).ToUpperInvariant()}");
            await LNURL.LNURL.FetchInformation(boltcardUrl, s.Server.PayTester.HttpClient);
            var info2 = (LNURLWithdrawRequest)await LNURL.LNURL.FetchInformation(boltcardUrl, s.Server.PayTester.HttpClient);
            var fakeBoltcardUrl = new Uri(Regex.Replace(boltcardUrl.AbsoluteUri, "p=([A-F0-9]{32})", $"p={RandomBytes(16)}"));
            await Assert.ThrowsAsync<LNUrlException>(() => LNURL.LNURL.FetchInformation(fakeBoltcardUrl, s.Server.PayTester.HttpClient));
            fakeBoltcardUrl = new Uri(Regex.Replace(boltcardUrl.AbsoluteUri, "c=([A-F0-9]{16})", $"c={RandomBytes(8)}"));
            await Assert.ThrowsAsync<LNUrlException>(() => LNURL.LNURL.FetchInformation(fakeBoltcardUrl, s.Server.PayTester.HttpClient));

            var bolt3 = (await s.Server.CustomerLightningD.CreateInvoice(
                new LightMoney(0.00000005m, LightMoneyUnit.BTC),
                $"LNurl w payout test2 {DateTime.UtcNow.Ticks}",
                TimeSpan.FromHours(1), CancellationToken.None));
            var response2 = await info2.SendRequest(bolt3.BOLT11, s.Server.PayTester.HttpClient, null, null);
            Assert.Equal("OK", response2.Status);
            await Assert.ThrowsAsync<LNUrlException>(() => LNURL.LNURL.FetchInformation(boltcardUrl, s.Server.PayTester.HttpClient));
            response2 = await info2.SendRequest(bolt3.BOLT11, s.Server.PayTester.HttpClient, null, null);
            Assert.Equal("ERROR", response2.Status);
            Assert.Contains("Replayed", response2.Reason);

            var reg = await db.GetBoltcardRegistration(issuerKey, uid);
            Assert.Equal((ppid, 1, 0), (reg!.PullPaymentId, reg.Counter, reg.Version));
            await db.SetBoltcardResetState(issuerKey, uid);
            reg = await db.GetBoltcardRegistration(issuerKey, uid);
            Assert.Equal((null, 0, 0), (reg!.PullPaymentId, reg.Counter, reg.Version));
            await db.LinkBoltcardToPullPayment(ppid, issuerKey, uid);
            reg = await db.GetBoltcardRegistration(issuerKey, uid);
            Assert.Equal((ppid, 0, 1), (reg!.PullPaymentId, reg.Counter, reg.Version));

            await db.LinkBoltcardToPullPayment(ppid, issuerKey, uid);
            reg = await db.GetBoltcardRegistration(issuerKey, uid);
            Assert.Equal((ppid, 0, 2), (reg!.PullPaymentId, reg.Counter, reg.Version));
        }

        await s.GoToStore(s.StoreId, StoreNavPages.PullPayments);
        await s.ClickPagePrimary();
        await s.Page.FillAsync("#Name", "PP1");
        await s.Page.UncheckAsync("#AutoApproveClaims");
        await s.Page.FillAsync("#Amount", "0.0000001");
        await s.Page.FillAsync("#Currency", "BTC");
        await s.Page.PressAsync("#Currency", "Enter");
        await s.FindAlertMessage();

        await using (await s.SwitchPage(async () =>
                     {
                         await s.Page.Locator(".actions-col a:has-text('View')").First.ClickAsync();
                     }))
        {
            await Expect(s.Page.Locator("#lnurlwithdraw-button")).ToBeVisibleAsync();
            await s.Page.ClickAsync("#lnurlwithdraw-button");
            await s.Page.WaitForFunctionAsync("() => document.querySelector('#qr-code-data-input')?.value?.length > 0");
            var lnurlStr2 = await s.Page.Locator("#qr-code-data-input").InputValueAsync();
            await s.Page.ClickAsync("button[data-bs-dismiss='modal']");
            var info = Assert.IsType<LNURLWithdrawRequest>(
                await LNURL.LNURL.FetchInformation(new Uri(LNURL.LNURL.Parse(lnurlStr2, out _).ToString().Replace("https", "http")),
                    s.Server.PayTester.HttpClient));
            Assert.Equal(info.MaxWithdrawable, new LightMoney(0.0000001m, LightMoneyUnit.BTC));
            Assert.Equal(info.CurrentBalance, new LightMoney(0.0000001m, LightMoneyUnit.BTC));
            info = Assert.IsType<LNURLWithdrawRequest>(await LNURL.LNURL.FetchInformation(info.BalanceCheck, s.Server.PayTester.HttpClient));
            Assert.Equal(info.MaxWithdrawable, new LightMoney(0.0000001m, LightMoneyUnit.BTC));
            Assert.Equal(info.CurrentBalance, new LightMoney(0.0000001m, LightMoneyUnit.BTC));

            var bolt2 = (await s.Server.CustomerLightningD.CreateInvoice(
                new LightMoney(0.0000001m, LightMoneyUnit.BTC),
                $"LNurl w payout test {DateTime.UtcNow.Ticks}",
                TimeSpan.FromHours(1), CancellationToken.None));
            var response = await info.SendRequest(bolt2.BOLT11, s.Server.PayTester.HttpClient, null, null);
            // Nope, you need to approve the claim automatically
            Assert.Equal("The request has been recorded, but still need to be approved before execution.", response.Reason);
            await TestUtils.EventuallyAsync(async () =>
            {
                await s.Page.ReloadAsync();
                var content = await s.Page.ContentAsync();
                Assert.Contains(bolt2.BOLT11, content);
                Assert.Contains(PayoutState.AwaitingApproval.GetStateString(), content);
            });
        }

        // LNURL Withdraw support check with SATS denomination
        await s.GoToStore(s.StoreId, StoreNavPages.PullPayments);
        await s.ClickPagePrimary();
        await s.Page.FillAsync("#Name", "PP SATS");
        await s.Page.CheckAsync("#AutoApproveClaims");
        await s.Page.FillAsync("#Amount", "21021");
        await s.Page.FillAsync("#Currency", "SATS");
        await s.Page.PressAsync("#Currency", "Enter");
        await s.FindAlertMessage();

        await using (await s.SwitchPage(async () =>
                     {
                         await s.Page.Locator(".actions-col a:has-text('View')").First.ClickAsync();
                     }))
        {
            await Expect(s.Page.Locator("#lnurlwithdraw-button")).ToBeVisibleAsync();
            await s.Page.ClickAsync("#lnurlwithdraw-button");
            await s.Page.WaitForFunctionAsync("() => document.querySelector('#qr-code-data-input')?.value?.length > 0");
            var lnurlStr3 = await s.Page.Locator("#qr-code-data-input").InputValueAsync();
            await s.Page.ClickAsync("button[data-bs-dismiss='modal']");
            var amount = new LightMoney(21021, LightMoneyUnit.Satoshi);
            var info = Assert.IsType<LNURLWithdrawRequest>(
                await LNURL.LNURL.FetchInformation(new Uri(LNURL.LNURL.Parse(lnurlStr3, out _).ToString().Replace("https", "http")),
                    s.Server.PayTester.HttpClient));
            Assert.Equal(amount, info.MaxWithdrawable);
            Assert.Equal(amount, info.CurrentBalance);
            info = Assert.IsType<LNURLWithdrawRequest>(await LNURL.LNURL.FetchInformation(info.BalanceCheck, s.Server.PayTester.HttpClient));
            Assert.Equal(amount, info.MaxWithdrawable);
            Assert.Equal(amount, info.CurrentBalance);

            var bolt2 = (await s.Server.CustomerLightningD.CreateInvoice(
                amount,
                $"LNurl w payout test {DateTime.UtcNow.Ticks}",
                TimeSpan.FromHours(1), CancellationToken.None));
            await info.SendRequest(bolt2.BOLT11, s.Server.PayTester.HttpClient, null, null);
            await TestUtils.EventuallyAsync(async () =>
            {
                await s.Page.ReloadAsync();
                var content = await s.Page.ContentAsync();
                Assert.Contains(bolt2.BOLT11, content);
                Assert.Contains(PayoutState.Completed.GetStateString(), content);
                Assert.Equal(LightningInvoiceStatus.Paid, (await s.Server.CustomerLightningD.GetInvoice(bolt2.Id)).Status);
            });
        }

        static string RandomBytes(int count)
        {
            var c = RandomNumberGenerator.GetBytes(count);
            return Encoders.Hex.EncodeData(c);
        }
    }

    [Fact]
    [Trait("Integration", "Integration")]
    public async Task CanTopUpPullPayment()
    {
        using var tester = CreateServerTester();
        await tester.StartAsync();
        var user = tester.NewAccount();
        await user.GrantAccessAsync(true);
        await user.RegisterDerivationSchemeAsync("BTC");
        var client = await user.CreateClient();
        var pp = await client.CreatePullPayment(user.StoreId, new()
        {
            Currency = "BTC",
            Amount = 1.0m,
            PayoutMethods = ["BTC-CHAIN"]
        });
        var controller = user.GetController<UIInvoiceController>();
        var invoice = await controller.CreateInvoiceCoreRaw(new()
        {
            Amount = 0.5m,
            Currency = "BTC",
        }, controller.HttpContext.GetStoreData(), controller.Url.Link(null, null)!, [PullPaymentHostedService.GetInternalTag(pp.Id)]);
        await client.MarkInvoiceStatus(user.StoreId, invoice.Id, new() { Status = InvoiceStatus.Settled });

        await TestUtils.EventuallyAsync(async () =>
        {
            var payouts = await client.GetPayouts(pp.Id);
            var payout = Assert.Single(payouts);
            Assert.Equal("TOPUP", payout.PayoutMethodId);
            Assert.Equal(invoice.Id, payout.Destination);
            Assert.Equal(-0.5m, payout.OriginalAmount);
        });
    }

    [Fact]
    [Trait("Integration", "Integration")]
    public async Task CanMigratePayoutsAndPullPayments()
    {
        var tester = CreateDBTester();
        await tester.MigrateUntil("20240827034505_migratepayouts");

        await using var ctx = tester.CreateContext();
        var conn = ctx.Database.GetDbConnection();
        await conn.ExecuteAsync("INSERT INTO \"Stores\"(\"Id\", \"SpeedPolicy\") VALUES (@store, 0)", new { store = "store1" });
        var param = new
        {
            Id = "pp1",
            StoreId = "store1",
            Blob =
                "{\"Name\": \"CoinLottery\", \"View\": {\"Email\": null, \"Title\": \"\", \"Description\": \"\", \"EmbeddedCSS\": null, \"CustomCSSLink\": null}, \"Limit\": \"10.00\", \"Period\": null, \"Currency\": \"GBP\", \"Description\": \"\", \"Divisibility\": 0, \"MinimumClaim\": \"0\", \"AutoApproveClaims\": false, \"SupportedPaymentMethods\": [\"BTC\", \"BTC_LightningLike\"]}"
        };
        await conn.ExecuteAsync(
            "INSERT INTO \"PullPayments\"(\"Id\", \"StoreId\", \"Blob\", \"StartDate\", \"Archived\") VALUES (@Id, @StoreId, @Blob::JSONB, NOW(), 'f')", param);
        var parameters = new[]
        {
            new
            {
                Id = "p1",
                StoreId = "store1",
                PullPaymentDataId = "pp1",
                PaymentMethodId = "BTC",
                Blob = "{\"Amount\": \"10.0\", \"Revision\": 0, \"Destination\": \"address\", \"CryptoAmount\": \"0.00012225\", \"MinimumConfirmation\": 1}"
            },
            new
            {
                Id = "p2",
                StoreId = "store1",
                PullPaymentDataId = "pp1",
                PaymentMethodId = "BTC_LightningLike",
                Blob = "{\"Amount\": \"10.0\", \"Revision\": 0, \"Destination\": \"address\", \"CryptoAmount\": null, \"MinimumConfirmation\": 1}"
            },
            new
            {
                Id = "p3",
                StoreId = "store1",
                PullPaymentDataId = null as string,
                PaymentMethodId = "BTC_LightningLike",
                Blob = "{\"Amount\": \"10.0\", \"Revision\": 0, \"Destination\": \"address\", \"CryptoAmount\": null, \"MinimumConfirmation\": 1}"
            },
            new
            {
                Id = "p4",
                StoreId = "store1",
                PullPaymentDataId = null as string,
                PaymentMethodId = "BTC_LightningLike",
                Blob = "{\"Amount\": \"-10.0\", \"Revision\": 0, \"Destination\": \"address\", \"CryptoAmount\": null, \"MinimumConfirmation\": 1}"
            }
        };
        await conn.ExecuteAsync(
            "INSERT INTO \"Payouts\"(\"Id\", \"StoreDataId\", \"PullPaymentDataId\", \"PaymentMethodId\", \"Blob\", \"State\", \"Date\") VALUES (@Id, @StoreId, @PullPaymentDataId, @PaymentMethodId, @Blob::JSONB, 'state', NOW())",
            parameters);
        await tester.CompleteMigrations();

        var migrated = await conn.ExecuteScalarAsync<bool>(
            "SELECT 't'::BOOLEAN FROM \"PullPayments\" WHERE \"Id\"='pp1' AND \"Limit\"=10.0 AND \"Currency\"='GBP' AND \"Blob\"->>'SupportedPayoutMethods'='[\"BTC-CHAIN\", \"BTC-LN\"]'");
        Assert.True(migrated);

        migrated = await conn.ExecuteScalarAsync<bool>(
            "SELECT 't'::BOOLEAN FROM \"Payouts\" WHERE \"Id\"='p1' AND \"Amount\"= 0.00012225 AND \"OriginalAmount\"=10.0 AND \"OriginalCurrency\"='GBP' AND \"PayoutMethodId\"='BTC-CHAIN'");
        Assert.True(migrated);

        migrated = await conn.ExecuteScalarAsync<bool>(
            "SELECT 't'::BOOLEAN FROM \"Payouts\" WHERE \"Id\"='p2' AND \"Amount\" IS NULL AND \"OriginalAmount\"=10.0 AND \"OriginalCurrency\"='GBP' AND \"PayoutMethodId\"='BTC-LN'");
        Assert.True(migrated);

        migrated = await conn.ExecuteScalarAsync<bool>(
            "SELECT 't'::BOOLEAN FROM \"Payouts\" WHERE \"Id\"='p3' AND \"Amount\" IS NULL AND \"OriginalAmount\"=10.0 AND \"OriginalCurrency\"='BTC'");
        Assert.True(migrated);

        migrated = await conn.ExecuteScalarAsync<bool>(
            "SELECT 't'::BOOLEAN FROM \"Payouts\" WHERE \"Id\"='p4' AND \"Amount\" IS NULL AND \"OriginalAmount\"=-10.0 AND \"OriginalCurrency\"='BTC' AND \"PayoutMethodId\"='TOPUP'");
        Assert.True(migrated);
    }

    [Fact]
    [Trait("Integration", "Integration")]
    public async Task CanUsePullPaymentViaAPI()
    {
        using var tester = CreateServerTester();
        tester.ActivateLightning();
        await tester.StartAsync();
        await tester.EnsureChannelsSetup();
        var acc = tester.NewAccount();
        await acc.GrantAccessAsync(true);
        acc.RegisterLightningNode("BTC", LightningConnectionType.CLightning, false);
        var storeId = (await acc.RegisterDerivationSchemeAsync("BTC", importKeysToNBX: true)).StoreId;
        var client = await acc.CreateClient();
        var result = await client.CreatePullPayment(storeId, new CreatePullPaymentRequest()
        {
            Name = "Test",
            Description = "Test description",
            Amount = 12.3m,
            Currency = "BTC",
            PayoutMethods = new[] { "BTC" }
        });

        void VerifyResult()
        {
            Assert.Equal("Test", result.Name);
            Assert.Equal("Test description", result.Description);
            // If it contains ? it means that we are resolving an unknown route with the link generator
            Assert.DoesNotContain("?", result.ViewLink);
            Assert.False(result.Archived);
            Assert.Equal("BTC", result.Currency);
            Assert.Equal(12.3m, result.Amount);
        }

        VerifyResult();

        var unauthenticated = new BTCPayServerClient(tester.PayTester.ServerUri);
        result = await unauthenticated.GetPullPayment(result.Id);
        VerifyResult();
        await AssertEx.AssertHttpError(404, async () => await unauthenticated.GetPullPayment("lol"));
        // Can't list pull payments unauthenticated
        await AssertEx.AssertHttpError(401, async () => await unauthenticated.GetPullPayments(storeId));

        var pullPayments = await client.GetPullPayments(storeId);
        result = Assert.Single(pullPayments);
        VerifyResult();

        var test2 = await client.CreatePullPayment(storeId, new CreatePullPaymentRequest()
        {
            Name = "Test 2",
            Amount = 12.3m,
            Currency = "BTC",
            PayoutMethods = new[] { "BTC" },
            BOLT11Expiration = TimeSpan.FromDays(31.0)
        });
        Assert.Equal(TimeSpan.FromDays(31.0), test2.BOLT11Expiration);

        TestLogs.LogInformation("Can't archive without knowing the walletId");
        var ex = await AssertEx.AssertApiError("missing-permission", async () => await client.ArchivePullPayment("lol", result.Id));
        Assert.Equal("btcpay.store.canarchivepullpayments", ((GreenfieldPermissionAPIError)ex.APIError).MissingPermission);
        TestLogs.LogInformation("Can't archive without permission");
        await AssertEx.AssertApiError("unauthenticated", async () => await unauthenticated.ArchivePullPayment(storeId, result.Id));
        await client.ArchivePullPayment(storeId, result.Id);
        result = await unauthenticated.GetPullPayment(result.Id);
        Assert.Equal(TimeSpan.FromDays(30.0), result.BOLT11Expiration);
        Assert.True(result.Archived);
        var pps = await client.GetPullPayments(storeId);
        result = Assert.Single(pps);
        Assert.Equal("Test 2", result.Name);
        pps = await client.GetPullPayments(storeId, true);
        Assert.Equal(2, pps.Length);
        Assert.Equal("Test 2", pps[0].Name);
        Assert.Equal("Test", pps[1].Name);

        var payouts = await unauthenticated.GetPayouts(pps[0].Id);
        Assert.Empty(payouts);

        var destination = (await tester.ExplorerNode.GetNewAddressAsync()).ToString();
        await AssertEx.AssertApiError("overdraft", async () => await unauthenticated.CreatePayout(pps[0].Id, new CreatePayoutRequest()
        {
            Destination = destination,
            Amount = 1_000_000m,
            PayoutMethodId = "BTC",
        }));

        await AssertEx.AssertApiError("archived", async () => await unauthenticated.CreatePayout(pps[1].Id, new CreatePayoutRequest()
        {
            Destination = destination,
            PayoutMethodId = "BTC"
        }));

        var payout = await unauthenticated.CreatePayout(pps[0].Id, new CreatePayoutRequest()
        {
            Destination = destination,
            PayoutMethodId = "BTC"
        });

        payouts = await unauthenticated.GetPayouts(pps[0].Id);
        var payout2 = Assert.Single(payouts);
        Assert.Equal(payout.OriginalAmount, payout2.OriginalAmount);
        Assert.Equal(payout.Id, payout2.Id);
        Assert.Equal(destination, payout2.Destination);
        Assert.Equal(PayoutState.AwaitingApproval, payout.State);
        Assert.Equal("BTC-CHAIN", payout2.PayoutMethodId);
        Assert.Equal("BTC", payout2.PayoutCurrency);
        Assert.Null(payout.PayoutAmount);

        TestLogs.LogInformation("Can't overdraft");

        var destination2 = (await tester.ExplorerNode.GetNewAddressAsync()).ToString();
        await AssertEx.AssertApiError("overdraft", async () => await unauthenticated.CreatePayout(pps[0].Id, new CreatePayoutRequest()
        {
            Destination = destination2,
            Amount = 0.00001m,
            PayoutMethodId = "BTC"
        }));

        TestLogs.LogInformation("Can't create too low payout");
        await AssertEx.AssertApiError("amount-too-low", async () => await unauthenticated.CreatePayout(pps[0].Id, new CreatePayoutRequest()
        {
            Destination = destination2,
            PayoutMethodId = "BTC"
        }));

        TestLogs.LogInformation("Can archive payout");
        await client.CancelPayout(storeId, payout.Id);
        payouts = await unauthenticated.GetPayouts(pps[0].Id);
        Assert.Empty(payouts);

        payouts = await client.GetPayouts(pps[0].Id, true);
        payout = Assert.Single(payouts);
        Assert.Equal(PayoutState.Cancelled, payout.State);

        TestLogs.LogInformation("Can create payout after cancelling");
        await unauthenticated.CreatePayout(pps[0].Id, new CreatePayoutRequest()
        {
            Destination = destination,
            PayoutMethodId = "BTC"
        });

        var start = TestUtils.RoundSeconds(DateTimeOffset.Now + TimeSpan.FromDays(7.0));
        var inFuture = await client.CreatePullPayment(storeId, new CreatePullPaymentRequest()
        {
            Name = "Starts in the future",
            Amount = 12.3m,
            StartsAt = start,
            Currency = "BTC",
            PayoutMethods = new[] { "BTC" }
        });
        Assert.Equal(start, inFuture.StartsAt);
        Assert.Null(inFuture.ExpiresAt);
        await AssertEx.AssertApiError("not-started", async () => await unauthenticated.CreatePayout(inFuture.Id, new CreatePayoutRequest()
        {
            Amount = 1.0m,
            Destination = destination,
            PayoutMethodId = "BTC"
        }));

        var expires = TestUtils.RoundSeconds(DateTimeOffset.Now - TimeSpan.FromDays(7.0));
        var inPast = await client.CreatePullPayment(storeId, new CreatePullPaymentRequest()
        {
            Name = "Will expires",
            Amount = 12.3m,
            ExpiresAt = expires,
            Currency = "BTC",
            PayoutMethods = new[] { "BTC" }
        });
        await AssertEx.AssertApiError("expired", async () => await unauthenticated.CreatePayout(inPast.Id, new CreatePayoutRequest()
        {
            Amount = 1.0m,
            Destination = destination,
            PayoutMethodId = "BTC"
        }));

        await AssertEx.AssertValidationError(new[] { "ExpiresAt" }, async () => await client.CreatePullPayment(storeId, new CreatePullPaymentRequest()
        {
            Name = "Test 2",
            Amount = 12.3m,
            StartsAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow - TimeSpan.FromDays(1)
        }));


        TestLogs.LogInformation("Create a pull payment with USD");
        var pp = await client.CreatePullPayment(storeId, new CreatePullPaymentRequest()
        {
            Name = "Test USD",
            Amount = 5000m,
            Currency = "USD",
            PayoutMethods = new[] { "BTC" }
        });

        await AssertEx.AssertApiError("lnurl-not-supported", async () => await unauthenticated.GetPullPaymentLNURL(pp.Id));

        destination = (await tester.ExplorerNode.GetNewAddressAsync()).ToString();
        TestLogs.LogInformation("Try to pay it in BTC");
        payout = await unauthenticated.CreatePayout(pp.Id, new CreatePayoutRequest()
        {
            Destination = destination,
            PayoutMethodId = "BTC"
        });
        await AssertEx.AssertApiError("old-revision", async () => await client.ApprovePayout(storeId, payout.Id, new ApprovePayoutRequest()
        {
            Revision = -1
        }));
        await AssertEx.AssertApiError("rate-unavailable", async () => await client.ApprovePayout(storeId, payout.Id, new ApprovePayoutRequest()
        {
            RateRule = "DONOTEXIST(BTC_USD)"
        }));
        payout = await client.ApprovePayout(storeId, payout.Id, new ApprovePayoutRequest()
        {
            Revision = payout.Revision
        });
        Assert.Equal(PayoutState.AwaitingPayment, payout.State);
        Assert.NotNull(payout.PayoutAmount);
        Assert.Equal(1.0m, payout.PayoutAmount); // 1 BTC == 5000 USD in tests
        await AssertEx.AssertApiError("invalid-state", async () => await client.ApprovePayout(storeId, payout.Id, new ApprovePayoutRequest()
        {
            Revision = payout.Revision
        }));

        // Create one pull payment with an amount of 9 decimals
        var test3 = await client.CreatePullPayment(storeId, new CreatePullPaymentRequest()
        {
            Name = "Test 2",
            Amount = 12.303228134m,
            Currency = "BTC",
            PayoutMethods = new[] { "BTC" }
        });
        destination = (await tester.ExplorerNode.GetNewAddressAsync()).ToString();
        payout = await unauthenticated.CreatePayout(test3.Id, new CreatePayoutRequest()
        {
            Destination = destination,
            PayoutMethodId = "BTC"
        });
        payout = await client.ApprovePayout(storeId, payout.Id, new ApprovePayoutRequest());
        // The payout should round the value of the payment down to the network of the payment method
        Assert.Equal(12.30322814m, payout.PayoutAmount);
        Assert.Equal(12.303228134m, payout.OriginalAmount);

        await client.MarkPayoutPaid(storeId, payout.Id);
        payout = (await client.GetPayouts(payout.PullPaymentId)).First(data => data.Id == payout.Id);
        Assert.Equal(PayoutState.Completed, payout.State);
        await AssertEx.AssertApiError("invalid-state", async () => await client.MarkPayoutPaid(storeId, payout.Id));

        // Test LNURL values
        var test4 = await client.CreatePullPayment(storeId, new CreatePullPaymentRequest()
        {
            Name = "Test 3",
            Amount = 12.303228134m,
            Currency = "BTC",
            PayoutMethods = new[] { "BTC", "BTC-LightningNetwork", "BTC_LightningLike" }
        });
        var lnrUrLs = await unauthenticated.GetPullPaymentLNURL(test4.Id);
        Assert.IsType<string>(lnrUrLs.LNURLBech32);
        Assert.IsType<string>(lnrUrLs.LNURLUri);
        Assert.Equal(12.303228134m, test4.Amount);
        Assert.Equal("BTC", test4.Currency);

        // Check we can register Boltcard
        var uid = new byte[7];
        RandomNumberGenerator.Fill(uid);
        var card = await client.RegisterBoltcard(test4.Id, new RegisterBoltcardRequest()
        {
            UID = uid
        });
        Assert.Equal(0, card.Version);
        var card1Keys = new[] { card.K0, card.K1, card.K2, card.K3, card.K4 };
        Assert.DoesNotContain(null, card1Keys);

        var card2 = await client.RegisterBoltcard(test4.Id, new RegisterBoltcardRequest()
        {
            UID = uid
        });
        Assert.Equal(0, card2.Version);
        card2 = await client.RegisterBoltcard(test4.Id, new RegisterBoltcardRequest()
        {
            UID = uid,
            OnExisting = OnExistingBehavior.UpdateVersion
        });
        Assert.Equal(1, card2.Version);
        Assert.StartsWith("lnurlw://", card2.LNURLW);
        Assert.EndsWith("/boltcard", card2.LNURLW);
        var card2Keys = new[] { card2.K0, card2.K1, card2.K2, card2.K3, card2.K4 };
        Assert.DoesNotContain(null, card2Keys);
        for (var i = 0; i < card1Keys.Length; i++)
        {
            if (i == 1)
                Assert.Contains(card1Keys[i], card2Keys);
            else
                Assert.DoesNotContain(card1Keys[i], card2Keys);
        }

        var card3 = await client.RegisterBoltcard(test4.Id, new RegisterBoltcardRequest()
        {
            UID = uid,
            OnExisting = OnExistingBehavior.KeepVersion
        });
        Assert.Equal(card2.Version, card3.Version);
        var p = new byte[] { 0xc7 }.Concat(uid).Concat(new byte[8]).ToArray();
        var card4 = await client.RegisterBoltcard(test4.Id, new RegisterBoltcardRequest()
        {
            OnExisting = OnExistingBehavior.KeepVersion,
            LNURLW = card2.LNURLW + $"?p={Encoders.Hex.EncodeData(AESKey.Parse(card2.K1).Encrypt(p))}"
        });
        Assert.Equal(card2.Version, card4.Version);
        Assert.Equal(card2.K4, card4.K4);
        // Can't define both properties
        await AssertEx.AssertValidationError(["LNURLW"], () => client.RegisterBoltcard(test4.Id, new RegisterBoltcardRequest()
        {
            OnExisting = OnExistingBehavior.KeepVersion,
            UID = uid,
            LNURLW = card2.LNURLW + $"?p={Encoders.Hex.EncodeData(AESKey.Parse(card2.K1).Encrypt(p))}"
        }));
        // p is malformed
        await AssertEx.AssertValidationError(["LNURLW"], () => client.RegisterBoltcard(test4.Id, new RegisterBoltcardRequest()
        {
            OnExisting = OnExistingBehavior.KeepVersion,
            UID = uid,
            LNURLW = card2.LNURLW + $"?p=lol"
        }));
        // p is invalid
        p[0] = 0;
        await AssertEx.AssertValidationError(["LNURLW"], () => client.RegisterBoltcard(test4.Id, new RegisterBoltcardRequest()
        {
            OnExisting = OnExistingBehavior.KeepVersion,
            LNURLW = card2.LNURLW + $"?p={Encoders.Hex.EncodeData(AESKey.Parse(card2.K1).Encrypt(p))}"
        }));
        // Test with SATS denomination values
        var testSats = await client.CreatePullPayment(storeId, new CreatePullPaymentRequest()
        {
            Name = "Test SATS",
            Amount = 21000,
            Currency = "SATS",
            PayoutMethods = new[] { "BTC", "BTC-LightningNetwork", "BTC_LightningLike" }
        });
        lnrUrLs = await unauthenticated.GetPullPaymentLNURL(testSats.Id);
        Assert.IsType<string>(lnrUrLs.LNURLBech32);
        Assert.IsType<string>(lnrUrLs.LNURLUri);
        Assert.Equal(21000, testSats.Amount);
        Assert.Equal("SATS", testSats.Currency);

        //permission test around auto approved pps and payouts
        var nonApproved = await acc.CreateClient(Policies.CanCreateNonApprovedPullPayments);
        var approved = await acc.CreateClient(Policies.CanCreatePullPayments);
        await AssertEx.AssertPermissionError(Policies.CanCreatePullPayments, async () =>
        {
            await nonApproved.CreatePullPayment(acc.StoreId, new CreatePullPaymentRequest()
            {
                Amount = 100,
                Currency = "USD",
                Name = "pull payment",
                PayoutMethods = new[] { "BTC" },
                AutoApproveClaims = true
            });
        });
        await AssertEx.AssertPermissionError(Policies.CanCreatePullPayments, async () =>
        {
            await nonApproved.CreatePayout(acc.StoreId, new CreatePayoutThroughStoreRequest()
            {
                Amount = 100,
                PayoutMethodId = "BTC",
                Approved = true,
                Destination = new Key().GetAddress(ScriptPubKeyType.TaprootBIP86, Network.RegTest).ToString()
            });
        });

        await approved.CreatePullPayment(acc.StoreId, new CreatePullPaymentRequest()
        {
            Amount = 100,
            Currency = "USD",
            Name = "pull payment",
            PayoutMethods = new[] { "BTC" },
            AutoApproveClaims = true
        });

        await approved.CreatePayout(acc.StoreId, new CreatePayoutThroughStoreRequest()
        {
            Amount = 100,
            PayoutMethodId = "BTC",
            Approved = true,
            Destination = new Key().GetAddress(ScriptPubKeyType.TaprootBIP86, Network.RegTest).ToString()
        });
    }

    [Fact]
    [Trait("Playwright", "Playwright-2")]
    public async Task CanEditPullPaymentUI()
    {
        await using var s = CreatePlaywrightTester();
        await s.StartAsync();
        await s.RegisterNewUser(true);
        await s.CreateNewStore();
        await s.GenerateWallet("BTC", "", true, true);
        await s.Server.ExplorerNode.GenerateAsync(1);
        await s.FundStoreWallet(denomination: 50.0m);

        await s.GoToStore(s.StoreId, StoreNavPages.PullPayments);

        await s.ClickPagePrimary();
        await s.Page.FillAsync("#Name", "PP1");
        await s.Page.Locator("#Amount").ClearAsync();
        await s.Page.FillAsync("#Amount", "99.0");
        await s.ClickPagePrimary();

        var opening = s.Page.Context.WaitForPageAsync();
        await s.Page.Locator(".actions-col a:has-text('View')").First.ClickAsync();
        var newPage = await opening;
        await Expect(newPage.Locator("body")).ToContainTextAsync("PP1");
        await newPage.CloseAsync();

        await s.GoToStore(s.StoreId, StoreNavPages.PullPayments);

        await s.Page.ClickAsync("text=PP1");
        await s.Page.FillAsync(".note-editable", "Description Edit");
        await s.Page.FillAsync("#Name", "PP1 Edited");
        await s.ClickPagePrimary();

        await s.FindAlertMessage();

        await using (await s.SwitchPage(async () => {
                         await s.Page.Locator(".actions-col a:has-text('View')").First.ClickAsync();
                     }))
        {
            try
            {
                await Expect(s.Page.GetByTestId("description")).ToContainTextAsync("Description Edit");
                await Expect(s.Page.GetByTestId("title")).ToContainTextAsync("PP1 Edited");
            }
            catch
            {
                await s.TakeScreenshot("Flaky-CanEditPullPaymentUI.png");
                throw;
            }
        }
    }
}
