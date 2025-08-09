using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Lightning;
using BTCPayServer.NTag424;
using BTCPayServer.Payments;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Wallets;
using BTCPayServer.Views.Manage;
using BTCPayServer.Views.Server;
using BTCPayServer.Views.Stores;
using BTCPayServer.Views.Wallets;
using ExchangeSharp;
using LNURL;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBitcoin.Payment;
using NBXplorer.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.Extensions;
using OpenQA.Selenium.Support.UI;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests
{
    [Trait("Selenium", "Selenium")]
    [Collection(nameof(NonParallelizableCollectionDefinition))]
    public class ChromeTests : UnitTestBase
    {
        private const int TestTimeout = TestUtils.TestTimeout;

        public ChromeTests(ITestOutputHelper helper) : base(helper)
        {
        }


        [Fact]
        [Trait("Selenium", "Selenium")]
        [Trait("Lightning", "Lightning")]
        public async Task CanUsePullPaymentsViaUI()
        {
            using var s = CreateSeleniumTester();
			s.Server.DeleteStore = false;
            s.Server.ActivateLightning(LightningConnectionType.LndREST);
            await s.StartAsync();
            await s.Server.EnsureChannelsSetup();
            s.RegisterNewUser(true);
            s.CreateNewStore();
            s.GenerateWallet("BTC", "", true, true);

            await s.Server.ExplorerNode.GenerateAsync(1);
            await s.FundStoreWallet(denomination: 50.0m);
            s.GoToStore(s.StoreId, StoreNavPages.PullPayments);
            s.ClickPagePrimary();
            s.Driver.FindElement(By.Id("Name")).SendKeys("PP1");
            s.Driver.FindElement(By.Id("Amount")).Clear();
            s.Driver.FindElement(By.Id("Amount")).SendKeys("99.0");
            s.ClickPagePrimary();

            s.Driver.FindElement(By.LinkText("View")).Click();
            s.Driver.SwitchTo().Window(s.Driver.WindowHandles.Last());
            Assert.Contains("PP1", s.Driver.PageSource);
            s.Driver.Close();
            s.Driver.SwitchTo().Window(s.Driver.WindowHandles.First());

            s.GoToStore(s.StoreId, StoreNavPages.PullPayments);

            s.ClickPagePrimary();
            s.Driver.FindElement(By.Id("Name")).SendKeys("PP2");
            s.Driver.FindElement(By.Id("Amount")).Clear();
            s.Driver.FindElement(By.Id("Amount")).SendKeys("100.0");
            s.ClickPagePrimary();

            // This should select the first View, ie, the last one PP2
            s.Driver.FindElement(By.LinkText("View")).Click();
            s.Driver.SwitchTo().Window(s.Driver.WindowHandles.Last());
            var address = await s.Server.ExplorerNode.GetNewAddressAsync();
            s.Driver.FindElement(By.Id("Destination")).SendKeys(address.ToString());
            s.Driver.FindElement(By.Id("ClaimedAmount")).Clear();
            s.Driver.FindElement(By.Id("ClaimedAmount")).SendKeys("15" + Keys.Enter);
            s.FindAlertMessage();

            // We should not be able to use an address already used
            s.Driver.FindElement(By.Id("Destination")).SendKeys(address.ToString());
            s.Driver.FindElement(By.Id("ClaimedAmount")).Clear();
            s.Driver.FindElement(By.Id("ClaimedAmount")).SendKeys("20" + Keys.Enter);
            s.FindAlertMessage(StatusMessageModel.StatusSeverity.Error);

            address = await s.Server.ExplorerNode.GetNewAddressAsync();
            s.Driver.FindElement(By.Id("Destination")).Clear();
            s.Driver.FindElement(By.Id("Destination")).SendKeys(address.ToString());
            s.Driver.FindElement(By.Id("ClaimedAmount")).Clear();
            s.Driver.FindElement(By.Id("ClaimedAmount")).SendKeys("20" + Keys.Enter);
            s.FindAlertMessage();
            Assert.Contains("Awaiting Approval", s.Driver.PageSource);

            var viewPullPaymentUrl = s.Driver.Url;
            s.Driver.Close();
            s.Driver.SwitchTo().Window(s.Driver.WindowHandles.First());

            // This one should have nothing
            s.GoToStore(s.StoreId, StoreNavPages.PullPayments);
            var payouts = s.Driver.FindElements(By.ClassName("pp-payout"));
            Assert.Equal(2, payouts.Count);
            payouts[1].Click();
            Assert.Empty(s.Driver.FindElements(By.ClassName("payout")));
            // PP2 should have payouts
            s.GoToStore(s.StoreId, StoreNavPages.PullPayments);
            payouts = s.Driver.FindElements(By.ClassName("pp-payout"));
            payouts[0].Click();

            Assert.NotEmpty(s.Driver.FindElements(By.ClassName("payout")));
            s.Driver.FindElement(By.ClassName("mass-action-select-all")).Click();
            s.Driver.FindElement(By.Id($"{PayoutState.AwaitingApproval}-approve-pay")).Click();

            s.Driver.FindElement(By.Id("SignTransaction")).Click();
            s.Driver.FindElement(By.CssSelector("button[value=broadcast]")).Click();
            s.FindAlertMessage();

            s.GoToWallet(null, WalletsNavPages.Transactions);
            TestUtils.Eventually(() =>
            {
                s.Driver.Navigate().Refresh();
                s.Driver.WaitWalletTransactionsLoaded();
                Assert.Contains("transaction-label", s.Driver.PageSource);
                var labels = s.Driver.FindElements(By.CssSelector("#WalletTransactionsList tr:first-child div.transaction-label"));
                Assert.Equal(2, labels.Count);
                Assert.Contains(labels, element => element.Text == "payout");
                Assert.Contains(labels, element => element.Text == "pull-payment");
            });

            s.GoToStore(s.StoreId, StoreNavPages.Payouts);
            s.Driver.FindElement(By.Id($"{PayoutState.InProgress}-view")).Click();
            ReadOnlyCollection<IWebElement> txs;
            TestUtils.Eventually(() =>
            {
                s.Driver.Navigate().Refresh();

                txs = s.Driver.FindElements(By.ClassName("transaction-link"));
                Assert.Equal(2, txs.Count);
            });

            s.Driver.Navigate().GoToUrl(viewPullPaymentUrl);
            txs = s.Driver.FindElements(By.ClassName("transaction-link"));
            Assert.Equal(2, txs.Count);
            Assert.Contains(PayoutState.InProgress.GetStateString(), s.Driver.PageSource);

            await s.Server.ExplorerNode.GenerateAsync(1);

            TestUtils.Eventually(() =>
            {
                s.Driver.Navigate().Refresh();
                Assert.Contains(PayoutState.Completed.GetStateString(), s.Driver.PageSource);
            });
            await s.Server.ExplorerNode.GenerateAsync(10);
            var pullPaymentId = viewPullPaymentUrl.Split('/').Last();

            await TestUtils.EventuallyAsync(async () =>
            {
                using var ctx = s.Server.PayTester.GetService<ApplicationDbContextFactory>().CreateContext();
                var payoutsData = await ctx.Payouts.Where(p => p.PullPaymentDataId == pullPaymentId).ToListAsync();
                Assert.True(payoutsData.All(p => p.State == PayoutState.Completed));
            });
            s.GoToHome();
            //offline/external payout test

            var newStore = s.CreateNewStore();
            s.GenerateWallet("BTC", "", true, true);
            s.GoToStore(s.StoreId, StoreNavPages.PullPayments);

            s.ClickPagePrimary();
            s.Driver.FindElement(By.Id("Name")).SendKeys("External Test");
            s.Driver.FindElement(By.Id("Amount")).Clear();
            s.Driver.FindElement(By.Id("Amount")).SendKeys("0.001");
            s.Driver.FindElement(By.Id("Currency")).Clear();
            s.Driver.FindElement(By.Id("Currency")).SendKeys("BTC");
            s.ClickPagePrimary();

            s.Driver.FindElement(By.LinkText("View")).Click();
            s.Driver.SwitchTo().Window(s.Driver.WindowHandles.Last());

            address = await s.Server.ExplorerNode.GetNewAddressAsync();
            s.Driver.FindElement(By.Id("Destination")).SendKeys(address.ToString());
            s.Driver.FindElement(By.Id("ClaimedAmount")).SendKeys(Keys.Enter);
            s.FindAlertMessage();

            Assert.Contains(PayoutState.AwaitingApproval.GetStateString(), s.Driver.PageSource);
            s.GoToStore(s.StoreId, StoreNavPages.Payouts);
            s.Driver.FindElement(By.Id($"{PayoutState.AwaitingApproval}-view")).Click();
            s.Driver.FindElement(By.ClassName("mass-action-select-all")).Click();
            s.Driver.FindElement(By.Id($"{PayoutState.AwaitingApproval}-approve")).Click();
            s.FindAlertMessage();
            var tx = await s.Server.ExplorerNode.SendToAddressAsync(address, Money.FromUnit(0.001m, MoneyUnit.BTC));
            s.Driver.Close();
            s.Driver.SwitchTo().Window(s.Driver.WindowHandles.First());

            s.GoToStore(s.StoreId, StoreNavPages.Payouts);

            s.Driver.FindElement(By.Id($"{PayoutState.AwaitingPayment}-view")).Click();
            Assert.Contains(PayoutState.AwaitingPayment.GetStateString(), s.Driver.PageSource);
            s.Driver.FindElement(By.ClassName("mass-action-select-all")).Click();
            s.Driver.FindElement(By.Id($"{PayoutState.AwaitingPayment}-mark-paid")).Click();
            s.FindAlertMessage();

            s.Driver.FindElement(By.Id($"{PayoutState.InProgress}-view")).Click();
            Assert.Contains(tx.ToString(), s.Driver.PageSource);

            //lightning tests
            // Since the merchant is sending on lightning, it needs some liquidity from the client
            var payoutAmount = LightMoney.Satoshis(1000);
            var minimumReserve = LightMoney.Satoshis(167773m);
            var inv = await s.Server.MerchantLnd.Client.CreateInvoice(minimumReserve + payoutAmount, "Donation to merchant", TimeSpan.FromHours(1), default);
            var resp = await s.Server.CustomerLightningD.Pay(inv.BOLT11);
            Assert.Equal(PayResult.Ok, resp.Result);

            newStore = s.CreateNewStore();
            s.AddLightningNode();

            //Currently an onchain wallet is required to use the Lightning payouts feature..
            s.GenerateWallet("BTC", "", true, true);
            s.GoToStore(newStore.storeId, StoreNavPages.PullPayments);
            s.ClickPagePrimary();

            var paymentMethodOptions = s.Driver.FindElements(By.CssSelector("input[name='PayoutMethods']"));
            Assert.Equal(2, paymentMethodOptions.Count);

            s.Driver.FindElement(By.Id("Name")).SendKeys("Lightning Test");
            s.Driver.FindElement(By.Id("Amount")).Clear();
            s.Driver.FindElement(By.Id("Amount")).SendKeys(payoutAmount.ToString());
            s.Driver.FindElement(By.Id("Currency")).Clear();
            s.Driver.FindElement(By.Id("Currency")).SendKeys("BTC");
            s.ClickPagePrimary();
            s.Driver.FindElement(By.LinkText("View")).Click();
            s.Driver.SwitchTo().Window(s.Driver.WindowHandles.Last());

            // Bitcoin-only, SelectedPaymentMethod should not be displayed
            s.Driver.ElementDoesNotExist(By.Id("SelectedPayoutMethod"));

            var bolt = (await s.Server.CustomerLightningD.CreateInvoice(
                payoutAmount,
                $"LN payout test {DateTime.UtcNow.Ticks}",
                TimeSpan.FromHours(1), CancellationToken.None)).BOLT11;
            s.Driver.FindElement(By.Id("Destination")).SendKeys(bolt);
            s.Driver.FindElement(By.Id("ClaimedAmount")).SendKeys(Keys.Enter);
            //we do not allow short-life bolts.
            s.FindAlertMessage(StatusMessageModel.StatusSeverity.Error);

            bolt = (await s.Server.CustomerLightningD.CreateInvoice(
                payoutAmount,
                $"LN payout test {DateTime.UtcNow.Ticks}",
                TimeSpan.FromDays(31), CancellationToken.None)).BOLT11;
            s.Driver.FindElement(By.Id("Destination")).Clear();
            s.Driver.FindElement(By.Id("Destination")).SendKeys(bolt);
            s.Driver.FindElement(By.Id("ClaimedAmount")).SendKeys(Keys.Enter);
            s.FindAlertMessage();

            Assert.Contains(PayoutState.AwaitingApproval.GetStateString(), s.Driver.PageSource);
            s.Driver.Close();
            s.Driver.SwitchTo().Window(s.Driver.WindowHandles.First());

            s.GoToStore(newStore.storeId, StoreNavPages.Payouts);
            s.Driver.FindElement(By.Id($"{PaymentTypes.LN.GetPaymentMethodId("BTC")}-view")).Click();
            s.Driver.FindElement(By.Id($"{PayoutState.AwaitingApproval}-view")).Click();
            s.Driver.FindElement(By.ClassName("mass-action-select-all")).Click();
            s.Driver.FindElement(By.Id($"{PayoutState.AwaitingApproval}-approve-pay")).Click();
            Assert.Contains(bolt, s.Driver.PageSource);
            Assert.Contains($"{payoutAmount} BTC", s.Driver.PageSource);
            s.Driver.FindElement(By.CssSelector("#pay-invoices-form")).Submit();

            s.FindAlertMessage();
            s.GoToStore(newStore.storeId, StoreNavPages.Payouts);
            s.Driver.FindElement(By.Id($"{PaymentTypes.LN.GetPaymentMethodId("BTC")}-view")).Click();

            s.Driver.FindElement(By.Id($"{PayoutState.Completed}-view")).Click();
            if (!s.Driver.PageSource.Contains(bolt))
            {
                s.Driver.FindElement(By.Id($"{PayoutState.AwaitingPayment}-view")).Click();
                Assert.Contains(bolt, s.Driver.PageSource);

                s.Driver.FindElement(By.ClassName("mass-action-select-all")).Click();
                s.Driver.FindElement(By.Id($"{PayoutState.AwaitingPayment}-mark-paid")).Click();
                s.Driver.FindElement(By.Id($"{PaymentTypes.LN.GetPaymentMethodId("BTC")}-view")).Click();

                s.Driver.FindElement(By.Id($"{PayoutState.Completed}-view")).Click();
                Assert.Contains(bolt, s.Driver.PageSource);
            }

            //auto-approve pull payments
            s.GoToStore(StoreNavPages.PullPayments);
            s.ClickPagePrimary();
            s.Driver.FindElement(By.Id("Name")).SendKeys("PP1");
            s.Driver.SetCheckbox(By.Id("AutoApproveClaims"), true);
            s.Driver.FindElement(By.Id("Amount")).Clear();
            s.Driver.FindElement(By.Id("Amount")).SendKeys("99.0" + Keys.Enter);
            s.FindAlertMessage();

            s.Driver.FindElement(By.LinkText("View")).Click();
            s.Driver.SwitchTo().Window(s.Driver.WindowHandles.Last());

            address = await s.Server.ExplorerNode.GetNewAddressAsync();
            s.Driver.FindElement(By.Id("Destination")).Clear();
            s.Driver.FindElement(By.Id("Destination")).SendKeys(address.ToString());
            s.Driver.FindElement(By.Id("ClaimedAmount")).Clear();
            s.Driver.FindElement(By.Id("ClaimedAmount")).SendKeys("20" + Keys.Enter);
            s.FindAlertMessage();

            Assert.Contains(PayoutState.AwaitingPayment.GetStateString(), s.Driver.PageSource);
            s.Driver.Close();
            s.Driver.SwitchTo().Window(s.Driver.WindowHandles.First());

            // LNURL Withdraw support check with BTC denomination
            s.GoToStore(s.StoreId, StoreNavPages.PullPayments);
            s.ClickPagePrimary();
            s.Driver.FindElement(By.Id("Name")).SendKeys("PP1");
            s.Driver.SetCheckbox(By.Id("AutoApproveClaims"), true);
            s.Driver.FindElement(By.Id("Amount")).Clear();
            s.Driver.FindElement(By.Id("Amount")).SendKeys("0.0000001");
            s.Driver.FindElement(By.Id("Currency")).Clear();
            s.Driver.FindElement(By.Id("Currency")).SendKeys("BTC" + Keys.Enter);
            s.FindAlertMessage();

            s.Driver.FindElement(By.LinkText("View")).Click();
            s.Driver.SwitchTo().Window(s.Driver.WindowHandles.Last());

            s.Driver.FindElement(By.CssSelector("#lnurlwithdraw-button")).Click();
            s.Driver.WaitForElement(By.Id("qr-code-data-input"));

            // Try to use lnurlw via the QR Code
            var lnurl = new Uri(LNURL.LNURL.Parse(s.Driver.FindElement(By.Id("qr-code-data-input")).GetAttribute("value"), out _).ToString().Replace("https", "http"));
            s.Driver.FindElement(By.CssSelector("button[data-bs-dismiss='modal']")).Click();
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
            var response = await info.SendRequest(bolt2.BOLT11, s.Server.PayTester.HttpClient, null,null);
            // Oops!
            Assert.Equal("The request has been approved. The sender needs to send the payment manually. (Or activate the lightning automated payment processor)", response.Reason);
			var account = await s.AsTestAccount().CreateClient();
			await account.UpdateStoreLightningAutomatedPayoutProcessors(s.StoreId, "BTC-LN", new()
			{
				ProcessNewPayoutsInstantly = true,
				IntervalSeconds = TimeSpan.FromSeconds(60)
			});
			// Now it should process to complete
			await TestUtils.EventuallyAsync(async () =>
            {
                s.Driver.Navigate().Refresh();
                Assert.Contains(bolt2.BOLT11, s.Driver.PageSource);

                Assert.Contains(PayoutState.Completed.GetStateString(), s.Driver.PageSource);
                Assert.Equal(LightningInvoiceStatus.Paid, (await s.Server.CustomerLightningD.GetInvoice(bolt2.Id)).Status);
            });
            s.Driver.Close();
            s.Driver.SwitchTo().Window(s.Driver.WindowHandles.First());

            // Simulate a boltcard
            {
                var db = s.Server.PayTester.GetService<ApplicationDbContextFactory>();
                var ppid = lnurl.AbsoluteUri.Split("/").Last();
                var issuerKey = new IssuerKey(SettingsRepositoryExtensions.FixedKey());
                var uid = RandomNumberGenerator.GetBytes(7);
                var cardKey = issuerKey.CreatePullPaymentCardKey(uid, 0, ppid);
                var keys = cardKey.DeriveBoltcardKeys(issuerKey);
                await db.LinkBoltcardToPullPayment(ppid, issuerKey, uid);
                var piccData = new byte[] { 0xc7 }.Concat(uid).Concat(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }).ToArray();
                var p = keys.EncryptionKey.Encrypt(piccData);
                var c = keys.AuthenticationKey.GetSunMac(uid, 1);
                var boltcardUrl = new Uri(s.Server.PayTester.ServerUri.AbsoluteUri + $"boltcard?p={Encoders.Hex.EncodeData(p).ToStringUpperInvariant()}&c={Encoders.Hex.EncodeData(c).ToStringUpperInvariant()}");
                // p and c should work so long as no bolt11 has been submitted
                info = (LNURLWithdrawRequest)await LNURL.LNURL.FetchInformation(boltcardUrl, s.Server.PayTester.HttpClient);
                info = (LNURLWithdrawRequest)await LNURL.LNURL.FetchInformation(boltcardUrl, s.Server.PayTester.HttpClient);
                var fakeBoltcardUrl = new Uri(Regex.Replace(boltcardUrl.AbsoluteUri, "p=([A-F0-9]{32})", $"p={RandomBytes(16)}"));
                await Assert.ThrowsAsync<LNUrlException>(() => LNURL.LNURL.FetchInformation(fakeBoltcardUrl, s.Server.PayTester.HttpClient));
                fakeBoltcardUrl = new Uri(Regex.Replace(boltcardUrl.AbsoluteUri, "c=([A-F0-9]{16})", $"c={RandomBytes(8)}"));
                await Assert.ThrowsAsync<LNUrlException>(() => LNURL.LNURL.FetchInformation(fakeBoltcardUrl, s.Server.PayTester.HttpClient));

                bolt2 = (await s.Server.CustomerLightningD.CreateInvoice(
                    new LightMoney(0.00000005m, LightMoneyUnit.BTC),
                    $"LNurl w payout test2 {DateTime.UtcNow.Ticks}",
                    TimeSpan.FromHours(1), CancellationToken.None));
                response = await info.SendRequest(bolt2.BOLT11, s.Server.PayTester.HttpClient, null, null);
                Assert.Equal("OK", response.Status);
                // No replay should be possible
                await Assert.ThrowsAsync<LNUrlException>(() => LNURL.LNURL.FetchInformation(boltcardUrl, s.Server.PayTester.HttpClient));
                response = await info.SendRequest(bolt2.BOLT11, s.Server.PayTester.HttpClient, null, null);
                Assert.Equal("ERROR", response.Status);
                Assert.Contains("Replayed", response.Reason);

                // Check the state of the registration, counter should have increased
                var reg = await db.GetBoltcardRegistration(issuerKey, uid);
                Assert.Equal((ppid, 1, 0), (reg.PullPaymentId, reg.Counter, reg.Version));
                await db.SetBoltcardResetState(issuerKey, uid);
                // After reset, counter is 0, version unchanged and ppId null
                reg = await db.GetBoltcardRegistration(issuerKey, uid);
                Assert.Equal((null, 0, 0), (reg.PullPaymentId, reg.Counter, reg.Version));
                await db.LinkBoltcardToPullPayment(ppid, issuerKey, uid);
                // Relink should bump Version
                reg = await db.GetBoltcardRegistration(issuerKey, uid);
                Assert.Equal((ppid, 0, 1), (reg.PullPaymentId, reg.Counter, reg.Version));

                await db.LinkBoltcardToPullPayment(ppid, issuerKey, uid);
                reg = await db.GetBoltcardRegistration(issuerKey, uid);
                Assert.Equal((ppid, 0, 2), (reg.PullPaymentId, reg.Counter, reg.Version));
            }

            s.GoToStore(s.StoreId, StoreNavPages.PullPayments);
            s.ClickPagePrimary();
            s.Driver.FindElement(By.Id("Name")).SendKeys("PP1");
            s.Driver.SetCheckbox(By.Id("AutoApproveClaims"), false);
            s.Driver.FindElement(By.Id("Amount")).Clear();
            s.Driver.FindElement(By.Id("Amount")).SendKeys("0.0000001");
            s.Driver.FindElement(By.Id("Currency")).Clear();
            s.Driver.FindElement(By.Id("Currency")).SendKeys("BTC" + Keys.Enter);
            s.FindAlertMessage();

            s.Driver.FindElement(By.LinkText("View")).Click();
            s.Driver.SwitchTo().Window(s.Driver.WindowHandles.Last());

            s.Driver.FindElement(By.CssSelector("#lnurlwithdraw-button")).Click();
            lnurl = new Uri(LNURL.LNURL.Parse(s.Driver.FindElement(By.Id("qr-code-data-input")).GetAttribute("value"), out _).ToString().Replace("https", "http"));

            s.Driver.FindElement(By.CssSelector("button[data-bs-dismiss='modal']")).Click();
            info = Assert.IsType<LNURLWithdrawRequest>(await LNURL.LNURL.FetchInformation(lnurl, s.Server.PayTester.HttpClient));
            Assert.Equal(info.MaxWithdrawable, new LightMoney(0.0000001m, LightMoneyUnit.BTC));
            Assert.Equal(info.CurrentBalance, new LightMoney(0.0000001m, LightMoneyUnit.BTC));
            info = Assert.IsType<LNURLWithdrawRequest>(await LNURL.LNURL.FetchInformation(info.BalanceCheck, s.Server.PayTester.HttpClient));
            Assert.Equal(info.MaxWithdrawable, new LightMoney(0.0000001m, LightMoneyUnit.BTC));
            Assert.Equal(info.CurrentBalance, new LightMoney(0.0000001m, LightMoneyUnit.BTC));

            bolt2 = (await s.Server.CustomerLightningD.CreateInvoice(
                new LightMoney(0.0000001m, LightMoneyUnit.BTC),
                $"LNurl w payout test {DateTime.UtcNow.Ticks}",
                TimeSpan.FromHours(1), CancellationToken.None));
            response = await info.SendRequest(bolt2.BOLT11, s.Server.PayTester.HttpClient, null,null);
			// Nope, you need to approve the claim automatically
			Assert.Equal("The request has been recorded, but still need to be approved before execution.", response.Reason);
			TestUtils.Eventually(() =>
            {
                s.Driver.Navigate().Refresh();
                Assert.Contains(bolt2.BOLT11, s.Driver.PageSource);

                Assert.Contains(PayoutState.AwaitingApproval.GetStateString(), s.Driver.PageSource);
            });
            s.Driver.Close();
            s.Driver.SwitchTo().Window(s.Driver.WindowHandles.First());

            // LNURL Withdraw support check with SATS denomination
            s.GoToStore(s.StoreId, StoreNavPages.PullPayments);
            s.ClickPagePrimary();
            s.Driver.FindElement(By.Id("Name")).SendKeys("PP SATS");
            s.Driver.SetCheckbox(By.Id("AutoApproveClaims"), true);
            s.Driver.FindElement(By.Id("Amount")).Clear();
            s.Driver.FindElement(By.Id("Amount")).SendKeys("21021");
            s.Driver.FindElement(By.Id("Currency")).Clear();
            s.Driver.FindElement(By.Id("Currency")).SendKeys("SATS" + Keys.Enter);
            s.FindAlertMessage();

            s.Driver.FindElement(By.LinkText("View")).Click();
            s.Driver.SwitchTo().Window(s.Driver.WindowHandles.Last());

            s.Driver.FindElement(By.CssSelector("#lnurlwithdraw-button")).Click();
            lnurl = new Uri(LNURL.LNURL.Parse(s.Driver.FindElement(By.Id("qr-code-data-input")).GetAttribute("value"), out _).ToString().Replace("https", "http"));
            s.Driver.FindElement(By.CssSelector("button[data-bs-dismiss='modal']")).Click();
            var amount = new LightMoney(21021, LightMoneyUnit.Satoshi);
            info = Assert.IsType<LNURLWithdrawRequest>(await LNURL.LNURL.FetchInformation(lnurl, s.Server.PayTester.HttpClient));
            Assert.Equal(amount, info.MaxWithdrawable);
            Assert.Equal(amount, info.CurrentBalance);
            info = Assert.IsType<LNURLWithdrawRequest>(await LNURL.LNURL.FetchInformation(info.BalanceCheck, s.Server.PayTester.HttpClient));
            Assert.Equal(amount, info.MaxWithdrawable);
            Assert.Equal(amount, info.CurrentBalance);

            bolt2 = (await s.Server.CustomerLightningD.CreateInvoice(
                amount,
                $"LNurl w payout test {DateTime.UtcNow.Ticks}",
                TimeSpan.FromHours(1), CancellationToken.None));
            response = await info.SendRequest(bolt2.BOLT11, s.Server.PayTester.HttpClient, null,null);
            await TestUtils.EventuallyAsync(async () =>
            {
                s.Driver.Navigate().Refresh();
                Assert.Contains(bolt2.BOLT11, s.Driver.PageSource);

                Assert.Contains(PayoutState.Completed.GetStateString(), s.Driver.PageSource);
                Assert.Equal(LightningInvoiceStatus.Paid, (await s.Server.CustomerLightningD.GetInvoice(bolt2.Id)).Status);
            });
            s.Driver.Close();
        }

        private string RandomBytes(int count)
        {
            var c = RandomNumberGenerator.GetBytes(count);
            return Encoders.Hex.EncodeData(c);
        }

        // For god know why, selenium have problems clicking on the save button, resulting in ultimate hacks
        // to make it works.
        private void SudoForceSaveLightningSettingsRightNowAndFast(SeleniumTester s, string cryptoCode)
        {
            int maxAttempts = 5;
retry:
            s.ClickPagePrimary();
            try
            {
                Assert.Contains($"{cryptoCode} Lightning settings successfully updated", s.FindAlertMessage().Text);
            }
            catch (NoSuchElementException) when (maxAttempts > 0)
            {
                maxAttempts--;
                goto retry;
            }
        }

        private static string AssertUrlHasPairingCode(SeleniumTester s)
        {
            var regex = Regex.Match(new Uri(s.Driver.Url, UriKind.Absolute).Query, "pairingCode=([^&]*)");
            Assert.True(regex.Success, $"{s.Driver.Url} does not match expected regex");
            var pairingCode = regex.Groups[1].Value;
            return pairingCode;
        }

        private void SetTransactionOutput(SeleniumTester s, int index, BitcoinAddress dest, decimal amount, bool subtract = false)
        {
            s.Driver.FindElement(By.Id($"Outputs_{index}__DestinationAddress")).SendKeys(dest.ToString());
            var amountElement = s.Driver.FindElement(By.Id($"Outputs_{index}__Amount"));
            amountElement.Clear();
            amountElement.SendKeys(amount.ToString(CultureInfo.InvariantCulture));
            var checkboxElement = s.Driver.FindElement(By.Id($"Outputs_{index}__SubtractFeesFromOutput"));
            if (checkboxElement.Selected != subtract)
            {
                checkboxElement.Click();
            }
        }
    }
}
