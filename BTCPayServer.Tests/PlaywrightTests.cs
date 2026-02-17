using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Lightning;
using BTCPayServer.Lightning.Tests;
using BTCPayServer.Payments;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Wallets;
using BTCPayServer.Views.Manage;
using BTCPayServer.Views.Server;
using BTCPayServer.Views.Stores;
using BTCPayServer.Views.Wallets;
using Dapper;
using LNURL;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using NBitcoin;
using NBitcoin.Altcoins;
using NBitpayClient;
using NBXplorer;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;
using static Microsoft.Playwright.Assertions;

namespace BTCPayServer.Tests
{
    [Trait("Playwright", "Playwright")]
    [Collection(nameof(NonParallelizableCollectionDefinition))]
    public class PlaywrightTests(ITestOutputHelper helper) : UnitTestBase(helper)
    {

        [Fact]
        public async Task CanNavigateServerSettings()
        {
            await using var s = CreatePlaywrightTester();
            await s.StartAsync();
            await s.RegisterNewUser(true);
            await s.SkipWizard();
            await s.GoToServer();
            await s.Page.AssertNoError();
            await s.ClickOnAllSectionLinks("#mainNavSettings");
            await s.GoToServer(ServerNavPages.Services);
            s.TestLogs.LogInformation("Let's check if we can access the logs");
            await s.Page.GetByRole(AriaRole.Link, new() { Name = "Logs" }).ClickAsync();
            await s.Page.Locator("a:has-text('.log')").First.ClickAsync();
            Assert.Contains("Starting listening NBXplorer", await s.Page.ContentAsync());
        }

        [Fact]
        public async Task CanUseForms()
        {
            await using var s = CreatePlaywrightTester();
            await s.StartAsync();
            await s.InitializeBTCPayServer();
            // Point Of Sale
            var appName = $"PoS-{Guid.NewGuid().ToString()[..21]}";
            await s.CreateApp("PointOfSale", appName);
            await s.Page.SelectOptionAsync("#FormId", "Email");
            await s.ClickPagePrimary();
            await s.FindAlertMessage(partialText: "App updated");
            string invoiceId;
            await using (_ = await POSTests.ViewApp(s))
            {
                await s.Page.Locator("button[type='submit']").First.ClickAsync();
                await s.Page.FillAsync("[name='buyerEmail']", "aa@aa.com");
                await s.Page.ClickAsync("input[type='submit']");
                await s.PayInvoice(true);
                invoiceId = s.Page.Url[(s.Page.Url.LastIndexOf("/", StringComparison.Ordinal) + 1)..];
            }

            await s.Page.Context.Pages.First().BringToFrontAsync();
            await s.GoToUrl($"/invoices/{invoiceId}/");
            await Expect(s.Page.Locator("text=aa@aa.com")).ToBeVisibleAsync();
            // Payment Request
            await s.Page.ClickAsync("#menu-item-PaymentRequests");
            await s.ClickPagePrimary();
            await s.Page.FillAsync("#Title", "Pay123");
            await s.Page.FillAsync("#Amount", "700");
            await s.Page.SelectOptionAsync("#FormId", "Email");
            await s.ClickPagePrimary();
            await s.Page.Locator("a[id^='Edit-']").First.ClickAsync();
            var editUrl = new Uri(s.Page.Url);
            var opening = s.Page.Context.WaitForPageAsync();
            await s.Page.ClickAsync("#ViewPaymentRequest");
            var popOutPage = await opening;
            await popOutPage.ClickAsync("[data-test='form-button']");
            await popOutPage.FillAsync("input[name='buyerEmail']", "aa@aa.com");
            Assert.Contains("Enter your email", await popOutPage.ContentAsync());
            await popOutPage.ClickAsync("#page-primary");
            invoiceId = popOutPage.Url.Split('/').Last();
            await popOutPage.CloseAsync();
            await s.Page.Context.Pages.First().BringToFrontAsync();
            await s.GoToUrl(editUrl.PathAndQuery);

            await Expect(s.Page.Locator("#Email")).ToHaveValueAsync("aa@aa.com");
            var invoice = await s.Server.PayTester.GetService<InvoiceRepository>().GetInvoice(invoiceId);
            Assert.Equal("aa@aa.com", invoice.Metadata.BuyerEmail);

            //Custom Forms
            await s.GoToStore();
            await s.GoToStore(StoreNavPages.Forms);
            await Expect(s.Page.Locator("text=There are no forms yet.")).ToBeVisibleAsync();
            await s.ClickPagePrimary();
            await s.Page.FillAsync("[name='Name']", "Custom Form 1");
            await s.Page.ClickAsync("#ApplyEmailTemplate");
            await s.Page.ClickAsync("#CodeTabButton");
            await s.Page.Locator("#CodeTabPane").WaitForAsync();
            var config = await s.Page.Locator("[name='FormConfig']").InputValueAsync();
            Assert.Contains("buyerEmail", config);
            await s.Page.Locator("[name='FormConfig']").ClearAsync();
            await s.Page.FillAsync("[name='FormConfig']", config.Replace("Enter your email", "CustomFormInputTest"));
            await s.ClickPagePrimary();
            await s.Page.ClickAsync("#ViewForm");
            var formUrl = s.Page.Url;
            Assert.Contains("CustomFormInputTest", await s.Page.ContentAsync());
            await s.Page.FillAsync("[name='buyerEmail']", "aa@aa.com");
            await s.Page.ClickAsync("input[type='submit']");
            await s.PayInvoice(true, 0.001m);
            var result = await s.Server.PayTester.HttpClient.GetAsync(formUrl);
            Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
            await s.GoToHome();
            await s.GoToStore();
            await s.GoToStore(StoreNavPages.Forms);
            await s.Page.WaitForLoadStateAsync();
            await Expect(s.Page.Locator("text=Custom Form 1")).ToBeVisibleAsync();
            await s.Page.GetByRole(AriaRole.Link, new() { Name = "Remove" }).ClickAsync();
            await s.ConfirmDeleteModal();
            await s.Page.WaitForLoadStateAsync();
            Assert.DoesNotContain("Custom Form 1", await s.Page.ContentAsync());
            await s.ClickPagePrimary();
            await s.Page.FillAsync("[name='Name']", "Custom Form 2");
            await s.Page.ClickAsync("#ApplyEmailTemplate");
            await s.Page.ClickAsync("#CodeTabButton");
            await s.Page.Locator("#CodeTabPane").WaitForAsync();
            await s.Page.Locator("input[type='checkbox'][name='Public']").SetCheckedAsync(true);
            await s.Page.Locator("[name='FormConfig']").ClearAsync();
            await s.Page.FillAsync("[name='FormConfig']", config.Replace("Enter your email", "CustomFormInputTest2"));
            await s.ClickPagePrimary();
            await s.Page.ClickAsync("#ViewForm");
            formUrl = s.Page.Url;
            result = await s.Server.PayTester.HttpClient.GetAsync(formUrl);
            Assert.NotEqual(HttpStatusCode.NotFound, result.StatusCode);
            await s.GoToHome();
            await s.GoToStore();
            await s.GoToStore(StoreNavPages.Forms);
            await s.Page.GetByRole(AriaRole.Link, new() { Name = "Custom Form 2" }).ClickAsync();
            await s.Page.Locator("[name='Name']").ClearAsync();
            await s.Page.FillAsync("[name='Name']", "Custom Form 3");
            await s.ClickPagePrimary();
            await s.GoToStore(StoreNavPages.Forms);
            await Expect(s.Page.Locator("text=Custom Form 3")).ToBeVisibleAsync();
            await s.Page.ClickAsync("#menu-item-PaymentRequests");
            await s.ClickPagePrimary();
            var selectOptions = await s.Page.Locator("#FormId >> option").CountAsync();
            Assert.Equal(4, selectOptions);
        }

        [Fact]
        public async Task CanCreatePayRequest()
        {
            await using var s = CreatePlaywrightTester();
            await s.StartAsync();
            await s.RegisterNewUser();
            await s.CreateNewStore();
            await s.GoToUrl($"/stores/{s.StoreId}/payment-requests");

            Task WaitStatusContains(string text)
                => s.Page
                    .Locator(".only-for-js[data-test='status']")
                    .Filter(new() { HasTextString = text })
                    .WaitForAsync(new() { State = WaitForSelectorState.Visible });

            // Should give us an error message if we try to create a payment request before adding a wallet
            await s.ClickPagePrimary();
            await s.FindAlertMessage(partialText: "To create a payment request, you need to", severity: StatusMessageModel.StatusSeverity.Error);

            await s.AddDerivationScheme();
            await s.GoToUrl($"/stores/{s.StoreId}/payment-requests");
            await s.ClickPagePrimary();
            await s.Page.FillAsync("#Title", "Pay123");
            await s.Page.FillAsync("#Amount", ".01");

            var currencyValue = await s.Page.InputValueAsync("#Currency");
            Assert.Equal("USD", currencyValue);
            await s.Page.FillAsync("#Currency", "BTC");

            await s.ClickPagePrimary();
            await s.Page.ClickAsync("a[id^='Edit-']");
            var editUrl = s.Page.Url;

            var opening = s.Page.Context.WaitForPageAsync();
            await s.Page.ClickAsync("#ViewPaymentRequest");
            string viewUrl;
            await using (await s.SwitchPage(opening))
            {
                viewUrl = s.Page.Url;
                Assert.Equal("Pay Invoice", (await s.Page.InnerTextAsync("#PayInvoice")).Trim());
            }

            // expire
            await s.Page.EvaluateAsync("() => document.getElementById('ExpiryDate').value = '2021-01-21T21:00:00.000Z'");
            await s.ClickPagePrimary();
            await s.Page.ClickAsync("a[id^='Edit-']");

            await s.GoToUrl(viewUrl);
            await WaitStatusContains("Expired");

            // unexpire
            await s.GoToUrl(editUrl);
            await s.Page.ClickAsync("#ClearExpiryDate");
            await s.ClickPagePrimary();
            await s.Page.Locator("a[id^='Edit-']").First.ClickAsync();

            // amount and currency should be editable, because no invoice exists
            await s.GoToUrl(editUrl);
            Assert.True(await s.Page.IsEnabledAsync("#Amount"));
            Assert.True(await s.Page.IsEnabledAsync("#Currency"));

            await s.GoToUrl(viewUrl);
            Assert.Equal("Pay Invoice", (await s.Page.InnerTextAsync("#PayInvoice")).Trim());

            // test invoice creation
            await s.Page.ClickAsync("#PayInvoice");
            await s.Page.Locator("iframe[name='btcpay']").WaitForAsync();
            var checkoutFrame = s.Page.FrameLocator("iframe[name='btcpay']");
            await checkoutFrame.Locator("#Checkout").WaitForAsync();

            await checkoutFrame.Locator("#close").WaitForAsync();
            await checkoutFrame.Locator("#close").ClickAsync();
            await s.Page.Locator("iframe[name='btcpay']").WaitForAsync(new() { State = WaitForSelectorState.Detached });

            // amount and currency should not be editable, because an invoice exists
            await s.GoToUrl(editUrl);
            Assert.False(await s.Page.IsEnabledAsync("#Amount"));
            Assert.False(await s.Page.IsEnabledAsync("#Currency"));

            // archive (from details page)
            var payReqId = s.Page.Url.Split('/').Last();
            await s.Page.ClickAsync("#ArchivePaymentRequest");
            await s.FindAlertMessage(partialText: "The payment request has been archived");
            Assert.DoesNotContain("Pay123", await s.Page.ContentAsync());
            await s.Page.ClickAsync("#StatusOptionsToggle");
            await s.Page.ClickAsync("#StatusOptionsIncludeArchived");
            await Expect(s.Page.Locator($"#Edit-{payReqId}")).ToHaveTextAsync("Pay123");

            // unarchive (from list)
            await s.Page.ClickAsync($"#ToggleActions-{payReqId}");
            await s.Page.ClickAsync($"#ToggleArchival-{payReqId}");
            await s.FindAlertMessage(partialText: "The payment request has been unarchived");
            await Expect(s.Page.Locator($"#Edit-{payReqId}")).ToHaveTextAsync("Pay123");

            // payment
            await s.GoToUrl(viewUrl);
            await s.Page.ClickAsync("#PayInvoice");
            await s.Page.Locator("iframe[name='btcpay']").WaitForAsync();
            checkoutFrame = s.Page.FrameLocator("iframe[name='btcpay']");
            await checkoutFrame.Locator("#Checkout").WaitForAsync();

            // Pay full amount
            await checkoutFrame.Locator("#FakePayment").ClickAsync();

            // Processing - verify a payment received message and status
            await Expect(checkoutFrame.Locator("#processing"))
                .ToContainTextAsync("Payment Received");
            await Expect(checkoutFrame.Locator("#processing"))
                .ToContainTextAsync("Your payment has been received and is now processing");

            await Expect(s.Page.Locator(".only-for-js[data-test='status']"))
                .ToContainTextAsync("Processing");


            // Mine
            await checkoutFrame.Locator("#mine-block button").ClickAsync();
            await checkoutFrame.Locator("#CheatSuccessMessage").WaitForAsync();
            Assert.Contains("Mined 1 block", await checkoutFrame.Locator("#CheatSuccessMessage").InnerTextAsync());

            await checkoutFrame.Locator("#close").ClickAsync();
            await s.Page.Locator("iframe[name='btcpay']").WaitForAsync(new() { State = WaitForSelectorState.Detached });

            // One last refresh to ensure UI reflects final state
            await s.Page.ReloadAsync();
            await WaitStatusContains("Settled");
        }


        [Fact]
        public async Task CanChangeUserMail()
        {
            await using var s = CreatePlaywrightTester();
            await s.StartAsync();
            var tester = s.Server;
            var u1 = tester.NewAccount();
            await u1.GrantAccessAsync();
            await u1.MakeAdmin(false);
            var u2 = tester.NewAccount();
            await u2.GrantAccessAsync();
            await u2.MakeAdmin(false);
            await s.GoToLogin();
            await s.LogIn(u1.RegisterDetails.Email, u1.RegisterDetails.Password);
            await s.GoToProfile();
            await s.Page.Locator("#Email").ClearAsync();
            await s.Page.FillAsync("#Email", u2.RegisterDetails.Email);
            await s.ClickPagePrimary();
            await s.FindAlertMessage(StatusMessageModel.StatusSeverity.Error, partialText: "The email address is already in use with an other account.");
            await s.GoToProfile();
            await s.Page.Locator("#Email").ClearAsync();
            var changedEmail = Guid.NewGuid() + "@lol.com";
            await s.Page.FillAsync("#Email", changedEmail);
            await s.ClickPagePrimary();
            await s.FindAlertMessage();
            using var scope = tester.PayTester.ServiceProvider.CreateScope();
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.NotNull(await manager.FindByNameAsync(changedEmail));
            Assert.NotNull(await manager.FindByEmailAsync(changedEmail));
        }

        [Fact]
        [Trait("Playwright", "Playwright")]
        [Trait("Lightning", "Lightning")]
        public async Task CanUseLNURL()
        {
            await using var s = CreatePlaywrightTester();
            s.Server.DeleteStore = false;
            s.Server.ActivateLightning();
            await s.StartAsync();
            await s.Server.EnsureChannelsSetup();
            var cryptoCode = "BTC";
            await ConnectChannels.ConnectAll(s.Server.ExplorerNode,
                new[] { s.Server.MerchantLightningD },
                new[] { s.Server.MerchantLnd.Client });
            await s.RegisterNewUser(true);
            var (_, storeId) = await s.CreateNewStore();
            var network = s.Server.NetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode).NBitcoinNetwork;
            await s.AddLightningNode(LightningConnectionType.CLightning, false);
            await s.GoToLightningSettings();
            // LNURL is true by default
            await Expect(s.Page.Locator("#LNURLEnabled")).ToBeCheckedAsync();
            await s.Page.CheckAsync("#LUD12Enabled");
            await s.ClickPagePrimary();

            // Topup Invoice test
            var i = await s.CreateInvoice(storeId, null, cryptoCode);
            await s.GoToInvoiceCheckout(i);
            var lnurl = await s.Page.Locator("#Lightning_BTC-LNURL .truncate-center").GetAttributeAsync("data-text");
            Assert.NotNull(lnurl);
            var parsed = LNURL.LNURL.Parse(lnurl, out _);
            var fetchedRequest = Assert.IsType<LNURLPayRequest>(await LNURL.LNURL.FetchInformation(parsed, new HttpClient()));
            Assert.Equal(1m, fetchedRequest.MinSendable.ToDecimal(LightMoneyUnit.Satoshi));
            Assert.NotEqual(1m, fetchedRequest.MaxSendable.ToDecimal(LightMoneyUnit.Satoshi));
            var lnurlResponse = await fetchedRequest.SendRequest(new LightMoney(0.000001m, LightMoneyUnit.BTC),
                network, new HttpClient(), comment: "lol");

            Assert.Equal(new LightMoney(0.000001m, LightMoneyUnit.BTC),
                lnurlResponse.GetPaymentRequest(network).MinimumAmount);

            var lnurlResponse2 = await fetchedRequest.SendRequest(new LightMoney(0.000002m, LightMoneyUnit.BTC),
                network, new HttpClient(), comment: "lol2");
            Assert.Equal(new LightMoney(0.000002m, LightMoneyUnit.BTC), lnurlResponse2.GetPaymentRequest(network).MinimumAmount);
            // Initial bolt was cancelled
            var res = await s.Server.CustomerLightningD.Pay(lnurlResponse.Pr);
            Assert.Equal(PayResult.Error, res.Result);

            res = await s.Server.CustomerLightningD.Pay(lnurlResponse2.Pr);
            Assert.Equal(PayResult.Ok, res.Result);
            await TestUtils.EventuallyAsync(async () =>
            {
                var inv = await s.Server.PayTester.InvoiceRepository.GetInvoice(i);
                Assert.Equal(InvoiceStatus.Settled, inv.Status);
            });

            var greenfield = await s.AsTestAccount().CreateClient();
            var paymentMethods = await greenfield.GetInvoicePaymentMethods(s.StoreId, i);
            Assert.Single(paymentMethods, p => p.AdditionalData["providedComment"]!.Value<string>() == "lol2");
            // Standard invoice test
            await s.GoToStore(storeId);
            i = await s.CreateInvoice(storeId, 0.0000001m, cryptoCode);
            await s.GoToInvoiceCheckout(i);
            // BOLT11 is also displayed for standard invoice (not LNURL, even if it is available)
            var bolt11 = await s.Page.Locator("#Lightning_BTC-LN .truncate-center").GetAttributeAsync("data-text");
            BOLT11PaymentRequest.Parse(bolt11!, s.Server.ExplorerNode.Network);
            var invoiceId = s.Page.Url.Split('/').Last();
            using (var resp = await s.Server.PayTester.HttpClient.GetAsync("BTC/lnurl/pay/i/" + invoiceId))
            {
                resp.EnsureSuccessStatusCode();
                fetchedRequest = JsonConvert.DeserializeObject<LNURLPayRequest>(await resp.Content.ReadAsStringAsync());
            }
            Assert.Equal(0.0000001m, fetchedRequest.MaxSendable.ToDecimal(LightMoneyUnit.BTC));
            Assert.Equal(0.0000001m, fetchedRequest.MinSendable.ToDecimal(LightMoneyUnit.BTC));

            await Assert.ThrowsAsync<LNUrlException>(async () =>
            {
                await fetchedRequest.SendRequest(new LightMoney(0.0000002m, LightMoneyUnit.BTC),
                    network, new HttpClient());
            });
            await Assert.ThrowsAsync<LNUrlException>(async () =>
            {
                await fetchedRequest.SendRequest(new LightMoney(0.00000005m, LightMoneyUnit.BTC),
                    network, new HttpClient());
            });

            lnurlResponse = await fetchedRequest.SendRequest(new LightMoney(0.0000001m, LightMoneyUnit.BTC),
                network, new HttpClient());
            lnurlResponse2 = await fetchedRequest.SendRequest(new LightMoney(0.0000001m, LightMoneyUnit.BTC),
                network, new HttpClient());
            //invoice amounts do no change so the payment request is not regenerated
            Assert.Equal(lnurlResponse.Pr, lnurlResponse2.Pr);
            await s.Server.CustomerLightningD.Pay(lnurlResponse.Pr);
            Assert.Equal(new LightMoney(0.0000001m, LightMoneyUnit.BTC),
                lnurlResponse2.GetPaymentRequest(network).MinimumAmount);
            await s.GoToHome();

            i = await s.CreateInvoice(storeId, 0.000001m, cryptoCode);
            await s.GoToInvoiceCheckout(i);

            await s.GoToStore(storeId);
            i = await s.CreateInvoice(storeId, null, cryptoCode);
            await s.GoToInvoiceCheckout(i);

            await s.GoToHome();
            await s.GoToLightningSettings();
            await s.Page.UncheckAsync("#LNURLBech32Mode");
            await s.ClickPagePrimary();
            Assert.Contains($"{cryptoCode} Lightning settings successfully updated", await (await s.FindAlertMessage()).TextContentAsync());

            // Ensure the toggles are set correctly
            await s.GoToLightningSettings();
            await Expect(s.Page.Locator("#LNURLBech32Mode")).Not.ToBeCheckedAsync();

            i = await s.CreateInvoice(storeId, null, cryptoCode);
            await s.GoToInvoiceCheckout(i);
            lnurl = await s.Page.Locator("#Lightning_BTC-LNURL .truncate-center").GetAttributeAsync("data-text");
            Assert.StartsWith("lnurlp", lnurl);
            LNURL.LNURL.Parse(lnurl, out _);

            await s.GoToHome();
            await s.CreateNewStore(false);
            await s.AddLightningNode(LightningConnectionType.LndREST, false);
            await s.GoToLightningSettings();
            await s.Page.CheckAsync("#LNURLEnabled");
            await s.ClickPagePrimary();
            Assert.Contains($"{cryptoCode} Lightning settings successfully updated", await (await s.FindAlertMessage()).TextContentAsync());
            var invForPP = await s.CreateInvoice(null, cryptoCode);
            await s.GoToInvoiceCheckout(invForPP);
            lnurl = await s.Page.Locator("#Lightning_BTC-LNURL .truncate-center").GetAttributeAsync("data-text");
            Assert.NotNull(lnurl);
            LNURL.LNURL.Parse(lnurl, out _);

            // Check that pull payment has lightning option
            await s.GoToStore(s.StoreId, StoreNavPages.PullPayments);
            await s.ClickPagePrimary();
            Assert.Equal(PaymentTypes.LN.GetPaymentMethodId(cryptoCode), PaymentMethodId.Parse(await s.Page.Locator("input[name='PayoutMethods']").InputValueAsync()));
            await s.Page.FillAsync("#Name", "PP1");
            await s.Page.FillAsync("#Amount", "0.0000001");

            var currencyInput = await s.Page.InputValueAsync("#Currency");
            Assert.Equal("USD", currencyInput);
            await s.Page.FillAsync("#Currency", "BTC");

            await s.ClickPagePrimary();

            string pullPaymentId;
            await using (_ = await s.SwitchPage(async () =>
                         {
                             await s.Page.Locator(".actions-col a:has-text('View')").First.ClickAsync();
                         }))
            {
                pullPaymentId = s.Page.Url.Split('/').Last();

                await s.Page.FillAsync("#Destination", lnurl);
                await s.Page.FillAsync("#ClaimedAmount", "0.0000001");
                await s.Page.PressAsync("#ClaimedAmount", "Enter");
                await s.FindAlertMessage();
            }

            await s.Page.Context.Pages.First().BringToFrontAsync();
            await s.GoToStore(s.StoreId, StoreNavPages.PullPayments);
            var payouts = s.Page.Locator(".pp-payout");
            await payouts.First.ClickAsync();
            await s.Page.ClickAsync("#BTC-LN-view");
            Assert.True(await s.Page.Locator(".payout").CountAsync() > 0);
            await s.Page.CheckAsync(".mass-action-select-all");
            await s.Page.ClickAsync($"#{PayoutState.AwaitingApproval}-approve-pay");

            await Expect(s.Page.Locator("body")).ToContainTextAsync(lnurl);

            await s.Page.Locator("#pay-invoices-form").EvaluateAsync("form => form.submit()");

            await TestUtils.EventuallyAsync(async () =>
            {
                var inv = await s.Server.PayTester.InvoiceRepository.GetInvoice(invForPP);
                Assert.Equal(InvoiceStatus.Settled, inv.Status);
                await using var ctx = s.Server.PayTester.GetService<ApplicationDbContextFactory>().CreateContext();
                var payoutsData = await ctx.Payouts.Where(p => p.PullPaymentDataId == pullPaymentId).ToListAsync();
                Assert.True(payoutsData.All(p => p.State == PayoutState.Completed));
            });
        }

        [Fact]
        [Trait("Playwright", "Playwright")]
        [Trait("Lightning", "Lightning")]
        public async Task CanUseLNAddress()
        {
            await using var s = CreatePlaywrightTester();
            s.Server.DeleteStore = false;
            s.Server.ActivateLightning();
            await s.StartAsync();
            await s.Server.EnsureChannelsSetup();
            await s.RegisterNewUser(true);
            //ln address tests
            await s.CreateNewStore();
            //ensure ln address is not available as Lightning is not enable
            Assert.Equal(0, await s.Page.Locator("#menu-item-LightningAddress").CountAsync());

            await s.AddLightningNode(LightningConnectionType.LndREST, false);

            // Navigate to store to refresh the menu and show Lightning Address
            await s.GoToStore(s.StoreId);
            await s.Page.ClickAsync("#menu-item-LightningAddress");

            // Add first lightning address (defaults)
            await s.ClickPagePrimary();
            var lnaddress1 = Guid.NewGuid().ToString();
            await s.Page.FillAsync("#Add_Username", lnaddress1);
            await s.Page.ClickAsync("button[value='add']");
            await s.FindAlertMessage();

            // Add a second lightning address with advanced settings
            // Ensure the add form is open
            if (!await s.Page.Locator("#Add_Username").IsVisibleAsync())
            {
                await s.ClickPagePrimary();
            }
            var lnaddress2 = "EUR" + Guid.NewGuid();
            await s.Page.FillAsync("#Add_Username", lnaddress2);
            await s.Page.ClickAsync("#AdvancedSettingsButton");
            await s.Page.FillAsync("#Add_CurrencyCode", "EUR");
            await s.Page.FillAsync("#Add_Min", "2");
            await s.Page.FillAsync("#Add_Max", "10");
            await s.Page.FillAsync("#Add_InvoiceMetadata", "{\"test\":\"lol\"}");
            await s.Page.ClickAsync("button[value='add']");
            await s.FindAlertMessage();

            //cannot test this directly as https is not supported on our e2e tests
            // Verify addresses are listed and resolve LNURLP metadata
            var addresses = s.Page.Locator(".lightning-address-value");
            Assert.Equal(2, await addresses.CountAsync());
            var callbacks = new List<Uri>();
            var lnaddress2Resolved = lnaddress2.ToLowerInvariant();

            for (var i = 0; i < await addresses.CountAsync(); i++)
            {
                var value = await addresses.Nth(i).GetAttributeAsync("value");
                Assert.NotNull(value);
                var lnurl = new Uri(LNURL.LNURL.ExtractUriFromInternetIdentifier(value).ToString().Replace("https", "http"));
                var request = (LNURLPayRequest)await LNURL.LNURL.FetchInformation(lnurl, new HttpClient());
                var m = request.ParsedMetadata.ToDictionary(o => o.Key, o => o.Value);
                if (value.StartsWith(lnaddress2Resolved, StringComparison.OrdinalIgnoreCase))
                {
                    Assert.StartsWith(lnaddress2Resolved + "@", m["text/identifier"]);
                    lnaddress2Resolved = m["text/identifier"];
                    Assert.Equal(2, request.MinSendable.ToDecimal(LightMoneyUnit.Satoshi));
                    Assert.Equal(10, request.MaxSendable.ToDecimal(LightMoneyUnit.Satoshi));
                    callbacks.Add(request.Callback);
                }
                else if (value.StartsWith(lnaddress1, StringComparison.OrdinalIgnoreCase))
                {
                    Assert.StartsWith(lnaddress1 + "@", m["text/identifier"]);
                    lnaddress1 = m["text/identifier"];
                    Assert.Equal(1, request.MinSendable.ToDecimal(LightMoneyUnit.Satoshi));
                    Assert.Equal(6.12m, request.MaxSendable.ToDecimal(LightMoneyUnit.BTC));
                    callbacks.Add(request.Callback);
                }
                else
                {
                    Assert.Fail("Should have matched");
                }
            }

            var repo = s.Server.PayTester.GetService<InvoiceRepository>();
            var invoices = await repo.GetInvoices(new InvoiceQuery { StoreId = new[] { s.StoreId } });
            // Resolving a ln address shouldn't create any btcpay invoice.
            // This must be done because some NOST clients resolve ln addresses preemptively without user interaction
            Assert.Empty(invoices);

            // Calling the callbacks should create the invoices
            foreach (var callback in callbacks)
            {
                using var r = await s.Server.PayTester.HttpClient.GetAsync(callback);
                await r.Content.ReadAsStringAsync();
            }
            invoices = await repo.GetInvoices(new InvoiceQuery { StoreId = new[] { s.StoreId } });
            Assert.Equal(2, invoices.Length);
            foreach (var inv in invoices)
            {
                var prompt = inv.GetPaymentPrompt(PaymentTypes.LNURL.GetPaymentMethodId("BTC"));
                Assert.NotNull(prompt);
                var handlers = s.Server.PayTester.GetService<PaymentMethodHandlerDictionary>();
                var details = (LNURLPayPaymentMethodDetails)handlers.ParsePaymentPromptDetails(prompt);
                Assert.NotNull(details);
                Assert.Contains(details.ConsumedLightningAddress, new[] { lnaddress1, lnaddress2Resolved });
                if (details.ConsumedLightningAddress == lnaddress2Resolved)
                {
                    Assert.Equal("lol", inv.Metadata.AdditionalData["test"].Value<string>());
                }
            }

            // Check if we can get the same payrequest through the callback
            var lnUsername = lnaddress1.Split('@')[0];
            LNURLPayRequest req;
            using (var resp = await s.Server.PayTester.HttpClient.GetAsync($"/.well-known/lnurlp/{lnUsername}"))
            {
                var str = await resp.Content.ReadAsStringAsync();
                req = JsonConvert.DeserializeObject<LNURLPayRequest>(str);
                Assert.Contains(req.ParsedMetadata, mm => mm.Key == "text/identifier" && mm.Value == lnaddress1);
                Assert.Contains(req.ParsedMetadata, mm => mm.Key == "text/plain" && mm.Value.StartsWith("Paid to"));
                Assert.NotNull(req.Callback);
                Assert.Equal(new LightMoney(1000), req.MinSendable);
                Assert.Equal(LightMoney.FromUnit(6.12m, LightMoneyUnit.BTC), req.MaxSendable);
            }

            lnUsername = lnaddress2Resolved.Split('@')[0];
            using (var resp = await s.Server.PayTester.HttpClient.GetAsync($"/.well-known/lnurlp/{lnUsername}"))
            {
                var str = await resp.Content.ReadAsStringAsync();
                req = JsonConvert.DeserializeObject<LNURLPayRequest>(str);
                Assert.Equal(new LightMoney(2000), req.MinSendable);
                Assert.Equal(new LightMoney(10_000), req.MaxSendable);
            }

            // Check via callback without amount
            using (var resp = await s.Server.PayTester.HttpClient.GetAsync(req.Callback))
            {
                var str = await resp.Content.ReadAsStringAsync();
                req = JsonConvert.DeserializeObject<LNURLPayRequest>(str);
                Assert.Equal(new LightMoney(2000), req.MinSendable);
                Assert.Equal(new LightMoney(10_000), req.MaxSendable);
            }

            // Can we ask for invoice? (Should fail, below minSpendable)
            using (var resp = await s.Server.PayTester.HttpClient.GetAsync(req.Callback + "?amount=1999"))
            {
                var str = await resp.Content.ReadAsStringAsync();
                var err = JsonConvert.DeserializeObject<LNUrlStatusResponse>(str);
                Assert.Equal("Amount is out of bounds.", err.Reason);
            }

            // Can we ask for invoice?
            using (var resp = await s.Server.PayTester.HttpClient.GetAsync(req.Callback + "?amount=2000"))
            {
                var str = await resp.Content.ReadAsStringAsync();
                var succ = JsonConvert.DeserializeObject<LNURLPayRequest.LNURLPayRequestCallbackResponse>(str);
                Assert.NotNull(succ.Pr);
                Assert.Equal(new LightMoney(2000), BOLT11PaymentRequest.Parse(succ.Pr, Network.RegTest).MinimumAmount);
            }

            // Can we change comment?
            using (var resp = await s.Server.PayTester.HttpClient.GetAsync(req.Callback + "?amount=2001"))
            {
                var str = await resp.Content.ReadAsStringAsync();
                var succ = JsonConvert.DeserializeObject<LNURLPayRequest.LNURLPayRequestCallbackResponse>(str);
                Assert.NotNull(succ.Pr);
                Assert.Equal(new LightMoney(2001), BOLT11PaymentRequest.Parse(succ.Pr, Network.RegTest).MinimumAmount);
                await s.Server.CustomerLightningD.Pay(succ.Pr);
            }

            // Can we find our comment and address in the payment list?
            var allInvoices = await repo.GetInvoices(new InvoiceQuery { StoreId = new[] { s.StoreId } });
            var handlers2 = s.Server.PayTester.GetService<PaymentMethodHandlerDictionary>();
            var match = allInvoices.FirstOrDefault(i =>
            {
                var prompt = i.GetPaymentPrompt(PaymentTypes.LNURL.GetPaymentMethodId("BTC"));
                if (prompt == null) return false;
                var det = (LNURLPayPaymentMethodDetails)handlers2.ParsePaymentPromptDetails(prompt);
                Assert.NotNull(det);
                return det.ConsumedLightningAddress?.StartsWith(lnUsername, StringComparison.OrdinalIgnoreCase) == true;
            });
            Assert.NotNull(match);
            await s.GoToInvoice(match!.Id);
            await Expect(s.Page.Locator("body")).ToContainTextAsync(lnUsername);
        }

        [Fact]
        public async Task CanManageUsers()
        {
            await using var s = CreatePlaywrightTester();
            await s.StartAsync();
            await s.RegisterNewUser();
            var user = s.AsTestAccount();
            await s.SkipWizard();
            await s.Logout();
            await s.GoToRegister();
            await s.RegisterNewUser(true);
            await s.SkipWizard();
            await s.GoToServer(ServerNavPages.Users);


            // Manage user password reset
            await s.Page.Locator("#SearchTerm").ClearAsync();
            await s.Page.FillAsync("#SearchTerm", user.RegisterDetails.Email);
            await s.Page.Locator("#SearchTerm").PressAsync("Enter");
            await s.Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            var rows = s.Page.Locator("#UsersList tr.user-overview-row");
            Assert.Equal(1, await rows.CountAsync());
            Assert.Contains(user.RegisterDetails.Email, await rows.First.TextContentAsync());
            await s.Page.ClickAsync("#UsersList tr.user-overview-row:first-child .reset-password");
            await s.Page.FillAsync("#Password", "Password@1!");
            await s.Page.FillAsync("#ConfirmPassword", "Password@1!");
            await s.ClickPagePrimary();
            await s.FindAlertMessage(partialText: "Password successfully set");
            user.Password = "Password@1!";

            var userPage = await s.Browser.NewPageAsync();
            await using (await s.SwitchPage(userPage, false))
            {
                await s.GoToLogin();
                await s.LogIn(user.Email, user.Password);
                await s.SkipWizard();
            }
            // Manage user status (disable and enable)
            // Disable user
            await s.Page.Locator("#SearchTerm").ClearAsync();
            await s.Page.FillAsync("#SearchTerm", user.RegisterDetails.Email);
            await s.Page.Locator("#SearchTerm").PressAsync("Enter");
            rows = s.Page.Locator("#UsersList tr.user-overview-row");
            Assert.Equal(1, await rows.CountAsync());
            Assert.Contains(user.RegisterDetails.Email, await rows.First.TextContentAsync());
            await s.Page.ClickAsync("#UsersList tr.user-overview-row:first-child .disable-user");
            await s.Page.ClickAsync("#ConfirmContinue");
            await s.FindAlertMessage(partialText: "User disabled");

            await using (await s.SwitchPage(userPage, false))
            {
                await s.Page.ReloadAsync();
                await s.LogIn(user.Email, user.Password);
                await s.FindAlertMessage(StatusMessageModel.StatusSeverity.Warning, partialText: "Your user account is currently disabled");
            }

            //Enable user
            await s.Page.Locator("#SearchTerm").ClearAsync();
            await s.Page.FillAsync("#SearchTerm", user.RegisterDetails.Email);
            await s.Page.Locator("#SearchTerm").PressAsync("Enter");
            rows = s.Page.Locator("#UsersList tr.user-overview-row");
            Assert.Equal(1, await rows.CountAsync());
            Assert.Contains(user.RegisterDetails.Email, await rows.First.TextContentAsync());
            await s.Page.ClickAsync("#UsersList tr.user-overview-row:first-child .enable-user");
            await s.Page.ClickAsync("#ConfirmContinue");
            await s.FindAlertMessage(partialText: "User enabled");

            await using (await s.SwitchPage(userPage))
            {
                // Can log again
                await s.LogIn(user.Email, "Password@1!");
                await s.CreateNewStore();
                await s.Logout();
            }

            // Manage user details (edit)
            await s.Page.Locator("#SearchTerm").ClearAsync();
            await s.Page.FillAsync("#SearchTerm", user.RegisterDetails.Email);
            await s.Page.Locator("#SearchTerm").PressAsync("Enter");
            rows = s.Page.Locator("#UsersList tr.user-overview-row");
            Assert.Equal(1, await rows.CountAsync());
            Assert.Contains(user.RegisterDetails.Email, await rows.First.TextContentAsync());
            await s.Page.ClickAsync("#UsersList tr.user-overview-row:first-child .user-edit");
            await s.Page.FillAsync("#Name", "Test User");
            await s.ClickPagePrimary();
            await s.FindAlertMessage(partialText: "User successfully updated");

            // Manage user deletion
            await s.GoToServer(ServerNavPages.Users);
            await s.Page.Locator("#SearchTerm").ClearAsync();
            await s.Page.FillAsync("#SearchTerm", user.RegisterDetails.Email);
            await s.Page.Locator("#SearchTerm").PressAsync("Enter");
            rows = s.Page.Locator("#UsersList tr.user-overview-row");
            Assert.Equal(1, await rows.CountAsync());
            Assert.Contains(user.RegisterDetails.Email, await rows.First.TextContentAsync());
            await s.Page.ClickAsync("#UsersList tr.user-overview-row:first-child .delete-user");
            await s.Page.ClickAsync("#ConfirmContinue");
            await s.FindAlertMessage(partialText: "User deleted");
            await s.Page.AssertNoError();
        }


        [Fact]
        public async Task CanUseSSHService()
        {
            await using var s = CreatePlaywrightTester();
            await s.StartAsync();
            var settings = s.Server.PayTester.GetService<SettingsRepository>();
            var policies = await settings.GetSettingAsync<PoliciesSettings>() ?? new PoliciesSettings();
            policies.DisableSSHService = false;
            await settings.UpdateSetting(policies);
            await s.RegisterNewUser(isAdmin: true);
            await s.GoToUrl("/server/services");
            Assert.Contains("server/services/ssh", await s.Page.ContentAsync());
            using (var client = await s.Server.PayTester.GetService<BTCPayServerOptions>().SSHSettings
                .ConnectAsync())
            {
                var result = await client.RunBash("echo hello");
                Assert.Equal(string.Empty, result.Error);
                Assert.Equal("hello\n", result.Output);
                Assert.Equal(0, result.ExitStatus);
            }

            await s.GoToUrl("/server/services/ssh");
            await s.Page.AssertNoError();
            await s.Page.Locator("#SSHKeyFileContent").ClearAsync();
            await s.Page.FillAsync("#SSHKeyFileContent", "tes't\r\ntest2");
            await s.Page.ClickAsync("#submit");
            await s.Page.AssertNoError();

            var text = await s.Page.Locator("#SSHKeyFileContent").TextContentAsync();
            // Browser replace \n to \r\n, so it is hard to compare exactly what we want
            Assert.Contains("tes't", text);
            Assert.Contains("test2", text);
            Assert.True((await s.Page.ContentAsync()).Contains("authorized_keys has been updated", StringComparison.OrdinalIgnoreCase));

            await s.Page.Locator("#SSHKeyFileContent").ClearAsync();
            await s.Page.ClickAsync("#submit");

            text = await s.Page.Locator("#SSHKeyFileContent").TextContentAsync();
            Assert.DoesNotContain("test2", text);

            // Let's try to disable it now
            await s.Page.ClickAsync("#disable");
            await s.Page.FillAsync("#ConfirmInput", "DISABLE");
            await s.Page.ClickAsync("#ConfirmContinue");
            await s.GoToUrl("/server/services/ssh");
            Assert.True((await s.Page.ContentAsync()).Contains("404 - Page not found", StringComparison.OrdinalIgnoreCase));

            policies = await settings.GetSettingAsync<PoliciesSettings>();
            Assert.NotNull(policies);
            Assert.True(policies.DisableSSHService);

            policies.DisableSSHService = false;
            await settings.UpdateSetting(policies);
        }

        [Fact]
        public async Task NewUserLogin()
        {
            await using var s = CreatePlaywrightTester();
            await s.StartAsync();
            //Register & Log Out
            var email = await s.RegisterNewUser();
            await s.SkipWizard();
            await s.Logout();
            await s.Page.AssertNoError();
            Assert.Contains("/login", s.Page.Url);

            await s.GoToUrl("/account");
            Assert.Contains("ReturnUrl=%2Faccount", s.Page.Url);

            // We should be redirected to login
            //Same User Can Log Back In
            await s.Page.FillAsync("#Email", email);
            await s.Page.FillAsync("#Password", "123456");
            await s.Page.ClickAsync("#LoginButton");

            // We should be redirected to invoice
            Assert.EndsWith("/account", s.Page.Url);

            // Should not be able to reach server settings
            await s.GoToUrl("/server/users");
            Assert.Contains("ReturnUrl=%2Fserver%2Fusers", s.Page.Url);
            await s.GoToHome();
            await s.GoToHome();

            //Change Password & Log Out
            var newPassword = "abc???";
            await s.GoToProfile(ManageNavPages.ChangePassword);
            await s.Page.FillAsync("#OldPassword", "123456");
            await s.Page.FillAsync("#NewPassword", newPassword);
            await s.Page.FillAsync("#ConfirmPassword", newPassword);
            await s.ClickPagePrimary();
            await s.Logout();
            await s.Page.AssertNoError();

            //Log In With New Password
            await s.Page.FillAsync("#Email", email);
            await s.Page.FillAsync("#Password", newPassword);
            await s.Page.ClickAsync("#LoginButton");

            await s.GoToHome();
            await s.GoToProfile();
            await s.ClickOnAllSectionLinks("#mainNavSettings");

            //let's test invite link
            await s.Logout();
            await s.GoToRegister();
            await s.RegisterNewUser(true);
            await s.SkipWizard();
            await s.GoToServer(ServerNavPages.Users);
            await s.ClickPagePrimary();

            var usr = RandomUtils.GetUInt256().ToString().Substring(64 - 20) + "@a.com";
            await s.Page.FillAsync("#Email", usr);
            await s.ClickPagePrimary();
            var url = await s.Page.Locator("#InvitationUrl").GetAttributeAsync("data-text");
            Assert.NotNull(url);
            await s.Logout();
            await s.GoToUrl(new Uri(url).AbsolutePath);
            Assert.Equal("hidden", await s.Page.Locator("#Email").GetAttributeAsync("type"));
            Assert.Equal(usr, await s.Page.Locator("#Email").GetAttributeAsync("value"));
            Assert.Equal("Create Account", await s.Page.Locator("h4").TextContentAsync());
            await s.FindAlertMessage(StatusMessageModel.StatusSeverity.Info, partialText: "Invitation accepted. Please set your password.");

            await s.Page.FillAsync("#Password", "123456");
            await s.Page.FillAsync("#ConfirmPassword", "123456");
            await s.ClickPagePrimary();
            await s.FindAlertMessage(partialText: "Account successfully created.");

            // We should be logged in now
            await s.GoToHome();
            await s.Page.Locator("#mainNav").WaitForAsync();

            //let's test delete user quickly while we're at it
            await s.GoToProfile();
            await s.Page.ClickAsync("#delete-user");
            await s.ConfirmDeleteModal();
            Assert.Contains("/login", s.Page.Url);
        }

        [Fact]
        public async Task CanUseStoreTemplate()
        {
            await using var s = CreatePlaywrightTester(newDb: true);
            await s.StartAsync();
            await s.RegisterNewUser(true);
            await s.CreateNewStore(preferredExchange: "Kraken");
            var client = await s.AsTestAccount().CreateClient();
            await client.UpdateStore(s.StoreId, new UpdateStoreRequest
            {
                Name = "Can Use Store?",
                Website = "https://test.com/",
                CelebratePayment = false,
                DefaultLang = "fr-FR",
                NetworkFeeMode = NetworkFeeMode.MultiplePaymentsOnly,
                ShowStoreHeader = false
            });
            await s.GoToServer();
            await s.Page.ClickAsync("#SetTemplate");
            await s.FindAlertMessage();

            var newStore = await client.CreateStore(new ());
            Assert.Equal("Can Use Store?", newStore.Name);
            Assert.Equal("https://test.com/", newStore.Website);
            Assert.False(newStore.CelebratePayment);
            Assert.Equal("fr-FR", newStore.DefaultLang);
            Assert.Equal(NetworkFeeMode.MultiplePaymentsOnly, newStore.NetworkFeeMode);
            Assert.False(newStore.ShowStoreHeader);

            newStore = await client.CreateStore(new (){ Name = "Yes you can also customize"});
            Assert.Equal("Yes you can also customize", newStore.Name);
            Assert.Equal("https://test.com/", newStore.Website);
            Assert.False(newStore.CelebratePayment);
            Assert.Equal("fr-FR", newStore.DefaultLang);
            Assert.Equal(NetworkFeeMode.MultiplePaymentsOnly, newStore.NetworkFeeMode);
            Assert.False(newStore.ShowStoreHeader);

            await s.GoToUrl("/stores/create");
            Assert.Equal("Can Use Store?" ,await s.Page.InputValueAsync("#Name"));
            await s.Page.FillAsync("#Name", "Just changed it!");
            await s.Page.ClickAsync("#Create");
            await s.GoToStore();
            var newStoreId = await s.Page.InputValueAsync("#Id");
            Assert.NotEqual(newStoreId, s.StoreId);

            newStore = await client.GetStore(newStoreId);
            Assert.Equal("Just changed it!", newStore.Name);
            Assert.Equal("https://test.com/", newStore.Website);
            Assert.False(newStore.CelebratePayment);
            Assert.Equal("fr-FR", newStore.DefaultLang);
            Assert.Equal(NetworkFeeMode.MultiplePaymentsOnly, newStore.NetworkFeeMode);
            Assert.False(newStore.ShowStoreHeader);

            await s.GoToServer();
            await s.Page.ClickAsync("#ResetTemplate");
            await s.FindAlertMessage(partialText: "Store template successfully unset");

            await s.GoToUrl("/stores/create");
            Assert.Equal("" ,await s.Page.InputValueAsync("#Name"));

            newStore = await client.CreateStore(new (){ Name = "Test"});
            Assert.Equal(TimeSpan.FromDays(30), newStore.RefundBOLT11Expiration);
            Assert.Equal(TimeSpan.FromDays(1), newStore.MonitoringExpiration);
            Assert.Equal(TimeSpan.FromMinutes(5), newStore.DisplayExpirationTimer);
            Assert.Equal(TimeSpan.FromMinutes(15), newStore.InvoiceExpiration);

            // What happens if the default template doesn't have all the fields?
            var settings = s.Server.PayTester.GetService<SettingsRepository>();
            var policies = await settings.GetSettingAsync<PoliciesSettings>() ?? new();
            policies.DefaultStoreTemplate = new JObject
            {
                ["blob"] = new JObject
                {
                    ["defaultCurrency"] = "AAA",
                    ["defaultLang"] = "de-DE"
                }
            };
            await settings.UpdateSetting(policies);
            newStore = await client.CreateStore(new() { Name = "Test2"});
            Assert.Equal("AAA", newStore.DefaultCurrency);
            Assert.Equal("de-DE", newStore.DefaultLang);
            Assert.Equal(TimeSpan.FromDays(30), newStore.RefundBOLT11Expiration);
            Assert.Equal(TimeSpan.FromDays(1), newStore.MonitoringExpiration);
            Assert.Equal(TimeSpan.FromMinutes(5), newStore.DisplayExpirationTimer);
            Assert.Equal(TimeSpan.FromMinutes(15), newStore.InvoiceExpiration);
        }

        [Fact]
        [Trait("Altcoins", "Altcoins")]
        public async Task CanExposeRates()
        {
            await using var s = CreatePlaywrightTester();
            s.Server.ActivateLTC();
            await s.StartAsync();
            await s.RegisterNewUser(true);
            await s.CreateNewStore();

            var btcDerivationScheme = new ExtKey().Neuter().GetWif(Network.RegTest) + "-[legacy]";
            await s.AddDerivationScheme("BTC", btcDerivationScheme);
            await s.AddDerivationScheme("LTC", new ExtKey().Neuter().GetWif(Litecoin.Instance.Regtest)  + "-[legacy]");

            await s.GoToStore();
            await s.Page.FillAsync("[name='DefaultCurrency']", "USD");
            await s.Page.FillAsync("[name='AdditionalTrackedRates']", "CAD,JPY,EUR");
            await s.ClickPagePrimary();

            await s.GoToStore(StoreNavPages.Rates);
            await s.Page.ClickAsync("#PrimarySource_ShowScripting_submit");
            await s.FindAlertMessage();

            // BTC can solves USD,EUR,CAD
            // LTC can solves and JPY and USD
            await s.Page.FillAsync("[name='PrimarySource.Script']",
                """
                BTC_JPY = bitflyer(BTC_JPY);

                BTC_USD = coingecko(BTC_USD);
                BTC_EUR = coingecko(BTC_EUR);
                BTC_CAD = coingecko(BTC_CAD);
                LTC_BTC = coingecko(LTC_BTC);

                LTC_USD = coingecko(LTC_USD);
                LTC_JPY = LTC_BTC * BTC_JPY;
                """);
            await s.ClickPagePrimary();
            var expectedSolvablePairs = new[]
            {
                (Crypto: "BTC", Currency: "JPY"),
                (Crypto: "BTC", Currency: "USD"),
                (Crypto: "BTC", Currency: "CAD"),
                (Crypto: "BTC", Currency: "EUR"),
                (Crypto: "LTC", Currency: "JPY"),
                (Crypto: "LTC", Currency: "USD"),
            };
            var expectedUnsolvablePairs = new[]
            {
                (Crypto: "LTC", Currency: "CAD"),
                (Crypto: "LTC", Currency: "EUR"),
            };

            Dictionary<string, uint256> txIds = new();
            foreach (var cryptoCode in new[] { "BTC", "LTC" })
            {
                await s.Server.GetExplorerNode(cryptoCode).EnsureGenerateAsync(1);
                await s.GoToWallet(new(s.StoreId, cryptoCode), WalletsNavPages.Receive);
                var address = await s.Page.GetAttributeAsync("#Address", "data-text");
                var network = s.Server.GetNetwork(cryptoCode);

                var txId = uint256.Zero;
                await s.Server.WaitForEvent<NewOnChainTransactionEvent>(async () =>
                {
                    txId = await s.Server.GetExplorerNode(cryptoCode)
                        .SendToAddressAsync(BitcoinAddress.Create(address!, network.NBitcoinNetwork), Money.Coins(1));
                });
                txIds.Add(cryptoCode, txId);
                // The rates are fetched asynchronously... let's wait it's done.
                await Task.Delay(500);
                var pmo = await s.GoToWalletTransactions(new(s.StoreId, cryptoCode));
                await pmo.WaitTransactionsLoaded();
                if (cryptoCode == "BTC")
                {
                    await pmo.AssertRowContains(txId, "4,500.00 CAD");
                    await pmo.AssertRowContains(txId, "700,000 JPY");
                    await pmo.AssertRowContains(txId, "4 000,00 EUR");
                    await pmo.AssertRowContains(txId, "5,000.00 USD");
                }
                else if (cryptoCode == "LTC")
                {
                    await pmo.AssertRowContains(txId, "4,321 JPY");
                    await pmo.AssertRowContains(txId, "500.00 USD");
                }
            }

            var fee = Money.Zero;
            var feeRate = FeeRate.Zero;
            // Quick check on some internal of wallet that isn't related to this test
            var wallet = s.Server.PayTester.GetService<BTCPayWalletProvider>().GetWallet("BTC");
            var derivation = s.Server.GetNetwork("BTC").NBXplorerNetwork.DerivationStrategyFactory.Parse(btcDerivationScheme);
            foreach (var forceHasFeeInfo in new bool?[]{ true, false, null})
            foreach (var inefficient in new[] { true, false })
            {
                wallet.ForceInefficientPath = inefficient;
                wallet.ForceHasFeeInformation = forceHasFeeInfo;
                wallet.InvalidateCache(derivation);
                var fetched = await wallet.FetchTransactionHistory(derivation);
                var tx = fetched.First(f => f.TransactionId == txIds["BTC"]);
                if (forceHasFeeInfo is true or null || inefficient)
                {
                    Assert.NotNull(tx.Fee);
                    Assert.NotNull(tx.FeeRate);
                    fee = tx.Fee;
                    feeRate = tx.FeeRate;
                }
                else
                {
                    Assert.Null(tx.Fee);
                    Assert.Null(tx.FeeRate);
                }
            }
            wallet.InvalidateCache(derivation);
            wallet.ForceHasFeeInformation = null;
            wallet.ForceInefficientPath = false;

            var pmo3 = await s.GoToWalletTransactions(new(s.StoreId, "BTC"));
            await pmo3.AssertRowContains(txIds["BTC"], $"{fee} ({feeRate})");

            await s.ClickViewReport();
            var csvTxt = await s.DownloadReportCSV();
            var csvTester = new CSVWalletsTester(csvTxt);

            foreach (var cryptoCode in new[] { "BTC", "LTC" })
            {
                if (cryptoCode == "BTC")
                {
                    csvTester
                        .ForTxId(txIds[cryptoCode].ToString())
                        .AssertValues(
                            ("FeeRate", feeRate.SatoshiPerByte.ToString(CultureInfo.InvariantCulture)),
                            ("Fee", fee.ToString()),
                            ("Rate (USD)", "5000"),
                            ("Rate (CAD)", "4500"),
                            ("Rate (JPY)", "700000"),
                            ("Rate (EUR)", "4000")
                        );
                }
                else
                {
                    csvTester
                        .ForTxId(txIds[cryptoCode].ToString())
                        .AssertValues(
                            ("Rate (USD)", "500"),
                            ("Rate (CAD)", ""),
                            ("Rate (JPY)", "4320.9876543209875"),
                            ("Rate (EUR)", "")
                        );
                }
            }

            // This shouldn't crash if NBX doesn't support fee fetching
            wallet.ForceHasFeeInformation = false;
            await s.Page.ReloadAsync();
            csvTxt = await s.DownloadReportCSV();
            csvTester = new CSVWalletsTester(csvTxt);
            csvTester
                .ForTxId(txIds["BTC"].ToString())
                .AssertValues(
                    ("FeeRate", ""),
                    ("Fee", ""),
                    ("Rate (USD)", "5000"),
                    ("Rate (CAD)", "4500"),
                    ("Rate (JPY)", "700000"),
                    ("Rate (EUR)", "4000")
                );
            wallet.ForceHasFeeInformation = null;

            var invId = await s.CreateInvoice(storeId: s.StoreId, amount: 10_000);
            await s.GoToInvoiceCheckout(invId);
            await s.PayInvoice();
            await s.GoToInvoices(s.StoreId);
            await s.ClickViewReport();

            await s.Page.ReloadAsync();
            csvTxt = await s.DownloadReportCSV();
            var csvInvTester = new CSVInvoicesTester(csvTxt);
            csvInvTester
                .ForInvoice(invId)
                .AssertValues(
                    ("Rate (BTC_CAD)", "4500"),
                    ("Rate (BTC_JPY)", "700000"),
                    ("Rate (BTC_EUR)", "4000"),
                    ("Rate (BTC_USD)", "5000"),
                    ("Rate (LTC_USD)", "500"),
                    ("Rate (LTC_JPY)", "4320.9876543209875"),
                    ("Rate (LTC_CAD)", ""),
                    ("Rate (LTC_EUR)", "")
                    );

            var txId2 = new uint256(csvInvTester.GetPaymentId().Split("-")[0]);
            var pmo2 = await s.GoToWalletTransactions(new(s.StoreId, "BTC"));
            await pmo2.WaitTransactionsLoaded();
            await pmo2.AssertRowContains(txId2, "5,000.00 USD");

            // When removing the wallet rates, we should still have the rates from the invoice
            var ctx = s.Server.PayTester.GetService<ApplicationDbContextFactory>().CreateContext();
            Assert.Equal(1, await ctx.Database
                .GetDbConnection()
                .ExecuteAsync("""
                              UPDATE "WalletObjects" SET "Data"='{}'::JSONB WHERE "Id"=@txId
                              """, new{ txId = txId2.ToString() }));

            pmo2 = await s.GoToWalletTransactions(new(s.StoreId, "BTC"));
            await pmo2.WaitTransactionsLoaded();
            await pmo2.AssertRowContains(txId2, "5,000.00 USD");
        }

        class CSVWalletsTester(string text) : CSVTester(text)
        {
            string txId = "";

            public CSVWalletsTester ForTxId(string txId)
            {
                this.txId = txId;
                return this;
            }

            public CSVWalletsTester AssertValues(params (string, string)[] values)
            {
                var line = _lines
                    .First(l => l[_indexes["TransactionId"]] == txId);
                foreach (var (key, value) in values)
                {
                    Assert.Equal(value, line[_indexes[key]]);
                }
                return this;
            }
        }

        [Fact]
        public async Task CanUsePaymentRequest()
        {
            await using var s = CreatePlaywrightTester();
            await s.StartAsync();
            await s.RegisterNewUser(true);
            await s.CreateNewStore();
            await s.GenerateWallet("BTC", "", true);

            // Create a payment request
            await s.GoToStore();
            await s.Page.ClickAsync("#menu-item-PaymentRequests");
            await s.ClickPagePrimary();
            var paymentRequestTitle = "Test Payment Request";
            await s.Page.FillAsync("#Title", paymentRequestTitle);
            await s.Page.FillAsync("#Amount", "0.1");
            await s.Page.FillAsync("#Currency", "BTC");
            await s.ClickPagePrimary();
            await s.FindAlertMessage(partialText: "Payment request");

            var paymentRequestUrl = s.Page.Url;
            var uri = new Uri(paymentRequestUrl);
            var queryParams = HttpUtility.ParseQueryString(uri.Query);
            var payReqId = queryParams["payReqId"];
            Assert.NotNull(payReqId);
            Assert.NotEmpty(payReqId);
            var markAsSettledExists = await s.Page.Locator("button:has-text('Mark as settled')").CountAsync();
            Assert.Equal(0, markAsSettledExists);

            await using (_ = await s.SwitchPage(async () =>
            {
                await s.Page.ClickAsync($"#PaymentRequest-{payReqId}");
            }))
            {
                await s.Page.ClickAsync("button:has-text('Pay')");
                await s.Page.WaitForLoadStateAsync();

                await s.Page.WaitForSelectorAsync("iframe[name='btcpay']", new() { Timeout = 10000 });

                var iframe = s.Page.Frame("btcpay");
                Assert.NotNull(iframe);

                await iframe.FillAsync("#test-payment-amount", "0.05");
                await iframe.ClickAsync("#FakePayment");
                await iframe.WaitForSelectorAsync("#CheatSuccessMessage", new() { Timeout = 10000 });
            }
            await s.GoToInvoices();

            await s.Page.ClickAsync("[data-invoice-state-badge] .dropdown-toggle");
            await s.Page.ClickAsync("[data-invoice-state-badge] .dropdown-menu button:has-text('Mark as settled')");
            await s.Page.WaitForLoadStateAsync();

            await s.GoToStore();
            await s.Page.ClickAsync("#menu-item-PaymentRequests");
            await s.Page.WaitForLoadStateAsync();

            await using (_ = await s.SwitchPage(async () =>
                         {
                             await s.Page.ClickAsync($"#PaymentRequest-{payReqId}");
                         }))
            {
                await s.Page.WaitForLoadStateAsync();

                var markSettledExists = await s.Page.Locator("button:has-text('Mark as settled')").CountAsync();
                Assert.True(markSettledExists > 0, "Mark as settled button should be visible on public page after invoice is settled");
                await s.Page.ClickAsync("button:has-text('Mark as settled')");
                await s.Page.WaitForLoadStateAsync();
            }

            await s.GoToStore();
            await s.Page.ClickAsync("#menu-item-PaymentRequests");
            await s.Page.WaitForLoadStateAsync();

            var listContent = await s.Page.ContentAsync();
            var isSettledInList = listContent.Contains("Settled");
            var isPendingInList = listContent.Contains("Pending");

            var settledBadgeExists = await s.Page.Locator(".badge:has-text('Settled')").CountAsync();
            var pendingBadgeExists = await s.Page.Locator(".badge:has-text('Pending')").CountAsync();

            Assert.True(isSettledInList || settledBadgeExists > 0, "Payment request should show as Settled in the list");
            Assert.False(isPendingInList && pendingBadgeExists > 0, "Payment request should not show as Pending anymore");

            await s.Page.ClickAsync("#menu-item-PaymentRequests");
            await s.Page.WaitForLoadStateAsync();
            await s.ClickPagePrimary();
            await s.Page.FillAsync("#Title", "Other Payment Request");
            await s.Page.FillAsync("#Amount", "0.2");
            await s.Page.FillAsync("#Currency", "BTC");
            await s.ClickPagePrimary();
            await s.FindAlertMessage(partialText: "Payment request");
            await s.Page.ClickAsync("#menu-item-PaymentRequests");
            await s.Page.WaitForLoadStateAsync();
            await Expect(s.Page.Locator("table tbody tr")).ToHaveCountAsync(2);

            // Filter by Title
            await s.Page.FillAsync("input[name='SearchText']", paymentRequestTitle);
            await s.Page.PressAsync("input[name='SearchText']", "Enter");
            await s.Page.WaitForLoadStateAsync();

            await Expect(s.Page.Locator("table tbody tr")).ToHaveCountAsync(1);
            Assert.Contains(paymentRequestTitle, await s.Page.Locator("table tbody tr").First.InnerTextAsync());

            // Filter by Status
            await s.Page.ClickAsync("#StatusOptionsToggle");
            await s.Page.ClickAsync("a:has-text('Settled')");
            await s.Page.WaitForLoadStateAsync();
            await Expect(s.Page.Locator("input[name='SearchText']"))
                .ToHaveValueAsync(paymentRequestTitle);
            var urlAfterStatusFilter = new Uri(s.Page.Url);
            var qsAfterStatusFilter = HttpUtility.ParseQueryString(urlAfterStatusFilter.Query);
            Assert.Equal(paymentRequestTitle, qsAfterStatusFilter["SearchText"]);

            // Filter by Amount
            await s.Page.FillAsync("input[name='SearchText']", "0.1");
            await s.Page.PressAsync("input[name='SearchText']", "Enter");
            await s.Page.WaitForLoadStateAsync();
            var rowsAfterAmountSearch = s.Page.Locator("table tbody tr");

            await Expect(rowsAfterAmountSearch).ToHaveCountAsync(1);
            var amountRowText = await rowsAfterAmountSearch.First.InnerTextAsync();
            Assert.Contains(paymentRequestTitle, amountRowText);

            // Filter by Id
            await s.Page.FillAsync("input[name='SearchText']", payReqId);
            await s.Page.PressAsync("input[name='SearchText']", "Enter");
            await s.Page.WaitForLoadStateAsync();
            var rowsAfterIdSearch = s.Page.Locator("table tbody tr");
            await Expect(rowsAfterIdSearch).ToHaveCountAsync(1);
            var idRowText = await rowsAfterIdSearch.First.InnerTextAsync();
            Assert.Contains(paymentRequestTitle, idRowText);

            // Clear All
            await Expect(s.Page.Locator("#clearAllFiltersBtn")).ToHaveCountAsync(1);
            await s.Page.ClickAsync("#clearAllFiltersBtn");
            await s.Page.WaitForLoadStateAsync();
            await Expect(s.Page.Locator("input[name='SearchText']")).ToHaveValueAsync(string.Empty);
            var urlAfterClearAll = new Uri(s.Page.Url);
            var qsAfterClearAll = HttpUtility.ParseQueryString(urlAfterClearAll.Query);
            Assert.True(string.IsNullOrEmpty(qsAfterClearAll["SearchText"]));
            Assert.True(string.IsNullOrEmpty(qsAfterClearAll["SearchTerm"]));
            await Expect(s.Page.Locator("table tbody tr")).ToHaveCountAsync(2);

            // Labels
            const string labelName = "test-label";
            var testPrRow = s.Page.Locator("table tbody tr", new PageLocatorOptions { HasText = paymentRequestTitle });
            await s.AddStoreLabelAsync(testPrRow, labelName);

            await TestUtils.EventuallyAsync(async () =>
            {
                var value = await testPrRow.InnerTextAsync();
                Assert.Contains(labelName, value);
            });

            //Filter by Label
            await s.Page.ClickAsync("#menu-item-PaymentRequests");
            await s.Page.WaitForLoadStateAsync();
            await s.Page.ClickAsync("#LabelOptionsToggle");
            await s.Page.ClickAsync($".dropdown-menu a:has-text(\"{labelName}\")");
            await s.Page.WaitForLoadStateAsync();
            await TestUtils.EventuallyAsync(async () =>
            {
                await Expect(s.Page.Locator("table tbody tr")).ToHaveCountAsync(1);
                var filteredText = await s.Page.InnerTextAsync("table tbody");
                Assert.Contains(paymentRequestTitle, filteredText);
                Assert.Contains(labelName, filteredText);
            });

            // Report
            await s.Page.ClickAsync("#view-report");
            await s.Page.WaitForLoadStateAsync();
            Assert.Contains("/reports", s.Page.Url);
            var requestsTabClasses = await s.Page.GetAttributeAsync("#SectionNav a[data-view='Requests']", "class");
            Assert.NotNull(requestsTabClasses);
            Assert.Contains("active", requestsTabClasses);
            await Expect(s.Page.Locator("#fromDate")).ToBeVisibleAsync();
            await Expect(s.Page.Locator("#toDate")).ToBeVisibleAsync();
            var reportHtml = await s.Page.ContentAsync();
            Assert.Contains("\"viewName\":\"Requests\"", reportHtml);
            await s.Page.WaitForSelectorAsync("#app table tbody tr");
            await Expect(s.Page.Locator("#app table tbody tr").Filter(new LocatorFilterOptions { HasText = "Payment Request" })).ToHaveCountAsync(2);
        }

        [Fact]
        public async Task CanUseStoreLabels()
        {
            await using var s = CreatePlaywrightTester();
            await s.StartAsync();
            await s.RegisterNewUser(true);
            await s.CreateNewStore();
            await s.GenerateWallet("BTC", "", true);

            await s.GoToStore();
            await s.Page.ClickAsync("#menu-item-PaymentRequests");

            await s.ClickPagePrimary();
            var paymentRequestTitle1 = "Label Case PR 1";
            await s.Page.FillAsync("#Title", paymentRequestTitle1);
            await s.Page.FillAsync("#Amount", "0.1");
            await s.Page.FillAsync("#Currency", "BTC");
            await s.ClickPagePrimary();
            await s.FindAlertMessage(partialText: "Payment request");

            await s.Page.ClickAsync("#menu-item-PaymentRequests");
            await s.ClickPagePrimary();
            var paymentRequestTitle2 = "Label Case PR 2";
            await s.Page.FillAsync("#Title", paymentRequestTitle2);
            await s.Page.FillAsync("#Amount", "0.2");
            await s.Page.FillAsync("#Currency", "BTC");
            await s.ClickPagePrimary();
            await s.FindAlertMessage(partialText: "Payment request");

            await s.Page.ClickAsync("#menu-item-PaymentRequests");
            await s.Page.WaitForLoadStateAsync();

            const string labelOriginal = "Case";
            const string labelLower = "case";

            var row1 = s.Page.Locator("table tbody tr", new PageLocatorOptions { HasText = paymentRequestTitle1 });
            await s.AddStoreLabelAsync(row1, labelOriginal);

            var row2 = s.Page.Locator("table tbody tr", new PageLocatorOptions { HasText = paymentRequestTitle2 });
            await s.AddStoreLabelAsync(row2, labelLower);

            await TestUtils.EventuallyAsync(async () =>
            {
                var text1 = await row1.InnerTextAsync();
                var text2 = await row2.InnerTextAsync();
                Assert.Contains(labelOriginal, text1);
                Assert.Contains(labelOriginal, text2);
            });

            await s.Page.ReloadAsync();
            await s.Page.WaitForLoadStateAsync();
            await s.Page.WaitForSelectorAsync("#LabelOptionsToggle");
            await s.Page.ClickAsync("#LabelOptionsToggle");
            var labelItems = await s.Page.Locator(".dropdown-menu a").AllInnerTextsAsync();
            var matches = labelItems.Where(t => t.Equals(labelOriginal, StringComparison.OrdinalIgnoreCase)).ToArray();
            Assert.Single(matches);
            Assert.Equal(labelOriginal, matches[0]);
        }

        [Fact]
        public async Task CanRequireApprovalForNewAccounts()
        {
            await using var s = CreatePlaywrightTester(newDb: true);
            await s.StartAsync();

            var settings = s.Server.PayTester.GetService<SettingsRepository>();
            var policies = await settings.GetSettingAsync<PoliciesSettings>() ?? new PoliciesSettings();
            Assert.True(policies.EnableRegistration);
            Assert.False(policies.RequiresUserApproval);

            await s.RegisterNewUser(true);
            var admin = s.AsTestAccount();
            await s.SkipWizard();
            await s.GoToServer();

            Assert.True(await s.Page.Locator("#EnableRegistration").IsCheckedAsync());
            Assert.False(await s.Page.Locator("#RequiresUserApproval").IsCheckedAsync());

            await s.Page.Locator("#RequiresUserApproval").ClickAsync();
            await s.ClickPagePrimary();
            await s.FindAlertMessage(partialText: "Policies updated successfully");
            Assert.True(await s.Page.Locator("#RequiresUserApproval").IsCheckedAsync());

            await Expect(s.Page.Locator("#NotificationsBadge")).Not.ToBeVisibleAsync();
            await s.Logout();

            await s.GoToRegister();
            await s.RegisterNewUser();
            await s.FindAlertMessage(StatusMessageModel.StatusSeverity.Warning, partialText: "Your user account requires approval by an admin before you can log in.");
            Assert.Contains("/login", s.Page.Url);

            var unapproved = s.AsTestAccount();
            await s.LogIn(unapproved.RegisterDetails.Email, unapproved.RegisterDetails.Password);
            await s.FindAlertMessage(StatusMessageModel.StatusSeverity.Warning, partialText: "Your user account requires approval by an admin before you can log in");
            Assert.Contains("/login", s.Page.Url);

            await s.GoToLogin();
            await s.LogIn(admin.RegisterDetails.Email, admin.RegisterDetails.Password);
            await s.GoToHome();

            await Expect(s.Page.Locator("#Notifications.rendered")).ToBeVisibleAsync();
            await Expect(s.Page.Locator("#NotificationsBadge")).ToContainTextAsync("1");
            await s.Page.ClickAsync("#NotificationsHandle");
            await Expect(s.Page.Locator("#NotificationsList .notification")).ToContainTextAsync($"New user {unapproved.RegisterDetails.Email} requires approval");
            await s.Page.ClickAsync("#NotificationsMarkAllAsSeen");

            await s.GoToServer();
            Assert.True(await s.Page.Locator("#EnableRegistration").IsCheckedAsync());
            Assert.True(await s.Page.Locator("#RequiresUserApproval").IsCheckedAsync());
            await s.Page.ClickAsync("#RequiresUserApproval");
            await s.ClickPagePrimary();
            await s.FindAlertMessage(partialText: "Policies updated successfully");
            Assert.False(await s.Page.Locator("#RequiresUserApproval").IsCheckedAsync());

            await s.GoToServer(ServerNavPages.Users);
            await s.ClickPagePrimary();
            await Expect(s.Page.Locator("#Approved")).Not.ToBeVisibleAsync();

            await s.Logout();

            await s.GoToLogin();
            await s.LogIn(unapproved.RegisterDetails.Email, unapproved.RegisterDetails.Password);
            await s.FindAlertMessage(StatusMessageModel.StatusSeverity.Warning, partialText: "Your user account requires approval by an admin before you can log in");
            Assert.Contains("/login", s.Page.Url);

            await s.GoToRegister();
            await s.RegisterNewUser();
            await s.Page.AssertNoError();
            Assert.DoesNotContain("/login", s.Page.Url);
            var autoApproved = s.AsTestAccount();
            await s.CreateNewStore();
            await s.Logout();

            await s.GoToLogin();
            await s.LogIn(admin.RegisterDetails.Email, admin.RegisterDetails.Password);
            await s.GoToHome();
            await Expect(s.Page.Locator("#NotificationsBadge")).Not.ToBeVisibleAsync();

            await s.GoToServer(ServerNavPages.Users);
            var rows = s.Page.Locator("#UsersList tr.user-overview-row");
            Assert.True(await rows.CountAsync() >= 3);

            await s.Page.Locator("#SearchTerm").ClearAsync();
            await s.Page.FillAsync("#SearchTerm", autoApproved.RegisterDetails.Email);
            await s.Page.PressAsync("#SearchTerm", "Enter");
            Assert.Equal(1, await rows.CountAsync());
            Assert.Contains(autoApproved.RegisterDetails.Email, await rows.First.TextContentAsync());
            await Expect(s.Page.Locator("#UsersList tr.user-overview-row:first-child .user-approved")).Not.ToBeVisibleAsync();

            await s.Page.ClickAsync("#UsersList tr.user-overview-row:first-child .user-edit");
            await Expect(s.Page.Locator("#Approved")).Not.ToBeVisibleAsync();

            await s.GoToServer(ServerNavPages.Users);
            await s.Page.Locator("#SearchTerm").ClearAsync();
            await s.Page.FillAsync("#SearchTerm", unapproved.RegisterDetails.Email);
            await s.Page.PressAsync("#SearchTerm", "Enter");
            Assert.Equal(1, await rows.CountAsync());
            Assert.Contains(unapproved.RegisterDetails.Email, await rows.First.TextContentAsync());
            Assert.Contains("Pending Approval", await s.Page.Locator("#UsersList tr.user-overview-row:first-child .user-status").TextContentAsync());

            await s.Page.ClickAsync("#UsersList tr.user-overview-row:first-child .user-edit");
            await s.Page.ClickAsync("#Approved");
            await s.ClickPagePrimary();
            await s.FindAlertMessage(partialText: "User successfully updated");

            await s.GoToServer(ServerNavPages.Users);
            Assert.Contains(unapproved.RegisterDetails.Email, await s.Page.GetAttributeAsync("#SearchTerm", "value"));
            Assert.Equal(1, await rows.CountAsync());
            Assert.Contains(unapproved.RegisterDetails.Email, await rows.First.TextContentAsync());
            Assert.Contains("Active", await s.Page.Locator("#UsersList tr.user-overview-row:first-child .user-status").TextContentAsync());

            await s.Logout();
            await s.GoToLogin();
            await s.LogIn(unapproved.RegisterDetails.Email, unapproved.RegisterDetails.Password);
            await s.Page.AssertNoError();
            Assert.DoesNotContain("/login", s.Page.Url);
            await s.CreateNewStore();
        }

        [Fact]
        public async Task CanUseDynamicDns()
        {
            await using var s = CreatePlaywrightTester();
            await s.StartAsync();
            await s.RegisterNewUser(isAdmin: true);
            await s.GoToUrl("/server/services");
            await Expect(s.Page.Locator("td").Filter(new() { HasText = "Dynamic DNS" })).ToBeVisibleAsync();

            await s.GoToUrl("/server/services/dynamic-dns");
            await s.Page.AssertNoError();
            if ((await s.Page.ContentAsync()).Contains("pouet.hello.com"))
            {
                await s.GoToUrl("/server/services/dynamic-dns/pouet.hello.com/delete");
                await s.Page.ClickAsync("#ConfirmContinue");
            }

            await s.ClickPagePrimary();
            await s.Page.AssertNoError();
            await s.Page.FillAsync("#ServiceUrl", s.Link("/"));
            await s.Page.FillAsync("#Settings_Hostname", "pouet.hello.com");
            await s.Page.FillAsync("#Settings_Login", "MyLog");
            await s.Page.FillAsync("#Settings_Password", "MyLog");
            await s.ClickPagePrimary();
            await s.Page.AssertNoError();
            await s.FindAlertMessage(partialText: "The Dynamic DNS has been successfully queried");
            Assert.EndsWith("/server/services/dynamic-dns", s.Page.Url);

            // Try to create the same hostname (should fail)
            await s.ClickPagePrimary();
            await s.Page.AssertNoError();
            await s.Page.FillAsync("#ServiceUrl", s.Link("/"));
            await s.Page.FillAsync("#Settings_Hostname", "pouet.hello.com");
            await s.Page.FillAsync("#Settings_Login", "MyLog");
            await s.Page.FillAsync("#Settings_Password", "MyLog");
            await s.ClickPagePrimary();
            await s.Page.AssertNoError();
            await Expect(s.Page.Locator(".validation-summary-errors")).ToContainTextAsync("This hostname already exists");

            // Delete the hostname
            await s.GoToUrl("/server/services/dynamic-dns");
            await s.Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            Assert.Contains("/server/services/dynamic-dns/pouet.hello.com/delete", await s.Page.ContentAsync());
            await s.GoToUrl("/server/services/dynamic-dns/pouet.hello.com/delete");
            await s.Page.ClickAsync("#ConfirmContinue");
            await s.Page.AssertNoError();

            Assert.DoesNotContain("/server/services/dynamic-dns/pouet.hello.com/delete", await s.Page.ContentAsync());
        }

        [Fact]
        public async Task CanCreateInvoiceInUI()
        {
            await using var s = CreatePlaywrightTester();
            await s.StartAsync();
            await s.RegisterNewUser(true);
            await s.CreateNewStore();
            await s.GoToInvoices();

            await s.ClickPagePrimary();

            await s.AddDerivationScheme();
            await s.GoToInvoices();
            await s.CreateInvoice();
            await s.Page.ClickAsync("[data-invoice-state-badge] .dropdown-toggle");
            await s.Page.ClickAsync("[data-invoice-state-badge] .dropdown-menu button:first-child");
            await TestUtils.EventuallyAsync(async () => Assert.Contains("Invalid (marked)", await s.Page.ContentAsync()));
            await s.Page.ReloadAsync();

            await s.Page.ClickAsync("[data-invoice-state-badge] .dropdown-toggle");
            await s.Page.ClickAsync("[data-invoice-state-badge] .dropdown-menu button:first-child");
            await TestUtils.EventuallyAsync(async () => Assert.Contains("Settled (marked)", await s.Page.ContentAsync()));

            await s.Page.ReloadAsync();

            await s.Page.ClickAsync("[data-invoice-state-badge] .dropdown-toggle");
            await s.Page.ClickAsync("[data-invoice-state-badge] .dropdown-menu button:first-child");
            await TestUtils.EventuallyAsync(async () => Assert.Contains("Invalid (marked)", await s.Page.ContentAsync()));
            await s.Page.ReloadAsync();

            await s.Page.ClickAsync("[data-invoice-state-badge] .dropdown-toggle");
            await s.Page.ClickAsync("[data-invoice-state-badge] .dropdown-menu button:first-child");
            await TestUtils.EventuallyAsync(async () => Assert.Contains("Settled (marked)", await s.Page.ContentAsync()));

            // Zero amount invoice should redirect to receipt
            var zeroAmountId = await s.CreateInvoice(0);
            await s.GoToUrl($"/i/{zeroAmountId}");
            Assert.EndsWith("/receipt", s.Page.Url);
            Assert.Contains("$0.00", await s.Page.ContentAsync());
            await s.GoToInvoice(zeroAmountId);
            Assert.Equal("Settled", (await s.Page.Locator("[data-invoice-state-badge]").TextContentAsync())?.Trim());
        }

        [Fact]
        public async Task CanSetupStoreViaGuide()
        {
            await using var s = CreatePlaywrightTester();
            await s.StartAsync();
            await s.RegisterNewUser();
            await s.GoToUrl("/");

            // verify redirected to create store page
            Assert.EndsWith("/stores/create", s.Page.Url);
            Assert.Contains("Create your first store", await s.Page.ContentAsync());
            Assert.Contains("Create a store to begin accepting payments", await s.Page.ContentAsync());
            Assert.Equal(0, await s.Page.Locator("#StoreSelectorDropdown").CountAsync());

            (_, string storeId) = await s.CreateNewStore();

            // should redirect to first store
            await s.GoToUrl("/");
            Assert.Contains($"/stores/{storeId}", s.Page.Url);
            Assert.Equal(1, await s.Page.Locator("#StoreSelectorDropdown").CountAsync());
            Assert.Equal(1, await s.Page.Locator("#SetupGuide").CountAsync());

            await s.GoToUrl("/stores/create");
            Assert.Contains("Create a new store", await s.Page.ContentAsync());
            Assert.DoesNotContain("Create your first store", await s.Page.ContentAsync());
            Assert.DoesNotContain("To start accepting payments, set up a store.", await s.Page.ContentAsync());
        }

        [Fact]
        [Trait("Lightning", "Lightning")]
        public async Task CanUseLndSeedBackup()
        {
            await using var s = CreatePlaywrightTester();
            s.Server.ActivateLightning();
            await s.StartAsync();
            await s.RegisterNewUser(true);
            await s.SkipWizard();
            await s.GoToServer(ServerNavPages.Services);
            await s.Page.AssertNoError();
            s.TestLogs.LogInformation("Let's see if we can access LND's seed");
            Assert.Contains("server/services/lndseedbackup/BTC", await s.Page.ContentAsync());
            await s.GoToUrl("/server/services/lndseedbackup/BTC");
            await s.Page.ClickAsync("#details");
            var seedEl = s.Page.Locator("#Seed");
            await Expect(seedEl).ToBeVisibleAsync();
            Assert.Contains("about over million", await seedEl.GetAttributeAsync("value"), StringComparison.OrdinalIgnoreCase);
            var passEl = s.Page.Locator("#WalletPassword");
            await Expect(passEl).ToBeVisibleAsync();
            Assert.Contains(await passEl.TextContentAsync(), "hellorockstar", StringComparison.OrdinalIgnoreCase);
            await s.Page.ClickAsync("#delete");
            await s.Page.WaitForSelectorAsync("#ConfirmInput");
            await s.ConfirmDeleteModal();
            await s.FindAlertMessage();
            seedEl = s.Page.Locator("#Seed");
            Assert.Contains("Seed removed", await seedEl.TextContentAsync(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task CanUseLNURLAuth()
        {
            await using var s = CreatePlaywrightTester();
            await s.StartAsync();
            var user = await s.RegisterNewUser(true);
            await s.SkipWizard();
            await s.GoToProfile(ManageNavPages.TwoFactorAuthentication);
            await s.Page.FillAsync("[name='Name']", "ln wallet");
            await s.Page.SelectOptionAsync("[name='type']", $"{(int)Fido2Credential.CredentialType.LNURLAuth}");
            await s.Page.ClickAsync("#btn-add");
            var linkElements = await s.Page.Locator(".tab-content a").AllAsync();
            var links = new List<string>();
            foreach (var element in linkElements)
            {
                var href = await element.GetAttributeAsync("href");
                if (href != null) links.Add(href);
            }
            Assert.Equal(2, links.Count);
            Uri prevEndpoint = null;
            foreach (string link in links)
            {
                var endpoint = LNURL.LNURL.Parse(link, out var tag);
                Assert.Equal("login", tag);
                if (endpoint.Scheme != "https")
                    prevEndpoint = endpoint;
            }

            var linkingKey = new Key();
            var request = Assert.IsType<LNAuthRequest>(await LNURL.LNURL.FetchInformation(prevEndpoint, null));
            _ = await request.SendChallenge(linkingKey, new HttpClient());
            await TestUtils.EventuallyAsync(async () => await s.FindAlertMessage());

            await s.CreateNewStore(); // create a store to prevent redirect after login
            await s.Logout();
            await s.LogIn(user);
            var section = s.Page.Locator("#lnurlauth-section");
            linkElements = await section.Locator(".tab-content a").AllAsync();
            links = new List<string>();
            foreach (var element in linkElements)
            {
                var href = await element.GetAttributeAsync("href");
                if (href != null) links.Add(href);
            }
            Assert.Equal(2, links.Count);
            prevEndpoint = null;
            foreach (string link in links)
            {
                var endpoint = LNURL.LNURL.Parse(link, out var tag);
                Assert.Equal("login", tag);
                if (endpoint.Scheme != "https")
                    prevEndpoint = endpoint;
            }
            request = Assert.IsType<LNAuthRequest>(await LNURL.LNURL.FetchInformation(prevEndpoint, null));
            _ = await request.SendChallenge(linkingKey, new HttpClient());
            await TestUtils.EventuallyAsync(() =>
            {
                Assert.StartsWith(s.ServerUri.ToString(), s.Page.Url);
                return Task.CompletedTask;
            });
        }

        [Fact]
        public async Task CookieReflectProperPermissions()
        {
            await using var s = CreatePlaywrightTester();
            await s.StartAsync();
            var alice = s.Server.NewAccount();
            alice.Register();
            await alice.CreateStoreAsync();
            var bob = s.Server.NewAccount();
            await bob.CreateStoreAsync();
            await bob.AddGuest(alice.UserId);

            await s.GoToLogin();
            await s.LogIn(alice.Email, alice.Password);
            await s.GoToUrl($"/cheat/permissions/stores/{bob.StoreId}");
            var pageSource = await s.Page.ContentAsync();
            AssertPermissions(pageSource, true,
                new[]
                {
                    Policies.CanViewInvoices,
                    Policies.CanModifyInvoices,
                    Policies.CanViewPaymentRequests,
                    Policies.CanViewPullPayments,
                    Policies.CanViewPayouts,
                    Policies.CanModifyStoreSettingsUnscoped,
                    Policies.CanDeleteUser
                });
            AssertPermissions(pageSource, false,
             new[]
             {
                    Policies.CanModifyStoreSettings,
                    Policies.CanCreateNonApprovedPullPayments,
                    Policies.CanCreatePullPayments,
                    Policies.CanManagePullPayments,
                    Policies.CanModifyServerSettings
             });

            await s.GoToUrl($"/cheat/permissions/stores/{alice.StoreId}");
            pageSource = await s.Page.ContentAsync();

            AssertPermissions(pageSource, true,
                new[]
                {
                    Policies.CanViewInvoices,
                    Policies.CanModifyInvoices,
                    Policies.CanViewPaymentRequests,
                    Policies.CanViewStoreSettings,
                    Policies.CanModifyStoreSettingsUnscoped,
                    Policies.CanDeleteUser,
                    Policies.CanModifyStoreSettings,
                    Policies.CanCreateNonApprovedPullPayments,
                    Policies.CanCreatePullPayments,
                    Policies.CanManagePullPayments,
                    Policies.CanArchivePullPayments,
                });
            AssertPermissions(pageSource, false,
             new[]
             {
                    Policies.CanModifyServerSettings
             });

            await s.GoToUrl("/logout");
            await alice.MakeAdmin();

            await s.GoToLogin();
            await s.LogIn(alice.Email, alice.Password);
            await s.GoToUrl($"/cheat/permissions/stores/{alice.StoreId}");
            pageSource = await s.Page.ContentAsync();

            AssertPermissions(pageSource, true,
            new[]
            {
                    Policies.CanViewInvoices,
                    Policies.CanModifyInvoices,
                    Policies.CanViewPaymentRequests,
                    Policies.CanViewStoreSettings,
                    Policies.CanModifyStoreSettingsUnscoped,
                    Policies.CanDeleteUser,
                    Policies.CanModifyStoreSettings,
                    Policies.CanCreateNonApprovedPullPayments,
                    Policies.CanCreatePullPayments,
                    Policies.CanManagePullPayments,
                    Policies.CanModifyServerSettings,
                    Policies.CanCreateUser,
                    Policies.CanManageUsers
            });
        }

        void AssertPermissions(string source, bool expected, string[] permissions)
        {
            if (expected)
            {
                foreach (var p in permissions)
                    Assert.Contains(p + "<", source);
            }
            else
            {
                foreach (var p in permissions)
                    Assert.DoesNotContain(p + "<", source);
            }
        }

        [Fact]
        public async Task CanUseAwaitProgressForInProgressPayout()
        {
            await using var s = CreatePlaywrightTester();
            await s.StartAsync();
            await s.RegisterNewUser(true);
            await s.CreateNewStore();
            await s.GenerateWallet(isHotWallet: true);
            await s.FundStoreWallet(denomination: 50.0m);

            await s.GoToStore(s.StoreId, StoreNavPages.PayoutProcessors);
            await s.Page.ClickAsync("#Configure-BTC-CHAIN");
            await s.Page.SetCheckedAsync("#ProcessNewPayoutsInstantly", true);
            await s.ClickPagePrimary();

            await s.GoToStore(s.StoreId, StoreNavPages.PullPayments);
            await s.ClickPagePrimary();
            await s.Page.FillAsync("#Name", "PP1");
            await s.Page.FillAsync("#Amount", "99.0");
            await s.Page.SetCheckedAsync("#AutoApproveClaims", true);
            await s.ClickPagePrimary();

            var o = s.Page.Context.WaitForPageAsync();
            await s.Page.ClickAsync("text=View");
            var newPage = await o;

            var address = await s.Server.ExplorerNode.GetNewAddressAsync();
            await newPage.FillAsync("#Destination", address.ToString());
            await newPage.PressAsync("#Destination", "Enter");

            await s.GoToStore(s.StoreId, StoreNavPages.Payouts);
            await s.Page.ClickAsync("#InProgress-view");

            // Wait for the payment processor to process the payment
            await TestUtils.EventuallyAsync(async () =>
            {
                await s.Page.ReloadAsync();
                var massActionSelect = s.Page.Locator(".mass-action-select-all[data-payout-state='InProgress']");
                await Expect(massActionSelect).ToBeVisibleAsync();
            });

            await s.Page.ClickAsync(".mass-action-select-all[data-payout-state='InProgress']");
            await s.Page.ClickAsync("#InProgress-mark-awaiting-payment");
            await s.Page.ClickAsync("#AwaitingPayment-view");

            await s.Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            var pageContent = await s.Page.ContentAsync();
            Assert.Contains("PP1", pageContent);
        }

        [Fact]
        public async Task CanUsePairing()
        {
            await using var s = CreatePlaywrightTester();
            await s.StartAsync();
            await s.Page.GotoAsync(s.Link("/api-access-request"));
            Assert.Contains("ReturnUrl", s.Page.Url);
            await s.GoToRegister();
            await s.RegisterNewUser();
            await s.CreateNewStore();
            await s.AddDerivationScheme();

            await s.GoToStore(s.StoreId, StoreNavPages.Tokens);
            await s.Page.Locator("#CreateNewToken").ClickAsync();
            await s.ClickPagePrimary();
            var url = s.Page.Url;
            var pairingCode = Regex.Match(new Uri(url, UriKind.Absolute).Query, "pairingCode=([^&]*)").Groups[1].Value;

            await s.ClickPagePrimary();
            await s.FindAlertMessage();
            Assert.Contains(pairingCode, await s.Page.ContentAsync());

            var client = new Bitpay(new Key(), s.ServerUri);
            await client.AuthorizeClient(new PairingCode(pairingCode));
            await client.CreateInvoiceAsync(
                new Invoice { Price = 1.000000012m, Currency = "USD", FullNotifications = true },
                Facade.Merchant);

            client = new Bitpay(new Key(), s.ServerUri);

            var code = await client.RequestClientAuthorizationAsync("hehe", Facade.Merchant);
            await s.Page.GotoAsync(code.CreateLink(s.ServerUri).ToString());
            await s.ClickPagePrimary();

            await client.CreateInvoiceAsync(
                new Invoice { Price = 1.000000012m, Currency = "USD", FullNotifications = true },
                Facade.Merchant);

            await s.Page.GotoAsync(s.Link("/api-tokens"));
            await s.ClickPagePrimary(); // Request
            await s.ClickPagePrimary(); // Approve
            var url2 = s.Page.Url;
            var pairingCode2 = Regex.Match(new Uri(url2, UriKind.Absolute).Query, "pairingCode=([^&]*)").Groups[1].Value;
            Assert.False(string.IsNullOrEmpty(pairingCode2));
        }

        [Fact]
        [Trait("Lightning", "Lightning")]
        public async Task CanCreateStores()
        {
            await using var s = CreatePlaywrightTester();
            s.Server.ActivateLightning();
            await s.StartAsync();
            var alice = await s.RegisterNewUser(true);
            var (storeName, storeId) = await s.CreateNewStore();
            var storeUrl = $"/stores/{storeId}";

            await s.GoToStore(storeId);
            Assert.Contains(storeName, await s.Page.ContentAsync());
            Assert.DoesNotContain("id=\"Dashboard\"", await s.Page.ContentAsync());

            // verify steps for wallet setup are displayed correctly
            await s.GoToStore(storeId, StoreNavPages.Dashboard);
            await s.Page.WaitForSelectorAsync("#SetupGuide-StoreDone");
            await s.Page.WaitForSelectorAsync("#SetupGuide-Wallet");
            await s.Page.WaitForSelectorAsync("#SetupGuide-Lightning");

            // setup onchain wallet
            await s.Page.Locator("#SetupGuide-Wallet").ClickAsync();
            await s.AddDerivationScheme();
            await s.Page.AssertNoError();

            await s.GoToStore(storeId, StoreNavPages.Dashboard);
            await s.Page.WaitForSelectorAsync("#Dashboard");
            Assert.DoesNotContain("id=\"SetupGuide\"", await s.Page.ContentAsync());

            // setup offchain wallet
            await s.Page.Locator("#menu-item-LightningSettings-BTC").ClickAsync();
            await s.AddLightningNode();
            await s.Page.AssertNoError();
            await s.FindAlertMessage(partialText: "BTC Lightning node updated.");

            // Only click on section links if they exist
            if (await s.Page.Locator("#SectionNav .nav-link").CountAsync() > 0)
            {
                await s.ClickOnAllSectionLinks();
            }

            await s.GoToInvoices(storeId);
            await Expect(s.Page.GetByTestId("no-invoices")).ToContainTextAsync("There are no invoices matching your criteria.");
            var invoiceId = await s.CreateInvoice(storeId);
            await s.FindAlertMessage();

            var invoiceUrl = s.Page.Url;

            //let's test archiving an invoice
            Assert.DoesNotContain("Archived", await s.Page.Locator("#btn-archive-toggle").InnerTextAsync());
            await s.Page.Locator("#btn-archive-toggle").ClickAsync();
            await Expect(s.Page.Locator("#btn-archive-toggle")).ToContainTextAsync("Unarchive");

            //check that it no longer appears in list
            await s.GoToInvoices(storeId);
            Assert.DoesNotContain(invoiceId, await s.Page.ContentAsync());

            //ok, let's unarchive and see that it shows again
            await s.Page.GotoAsync(invoiceUrl);
            await s.Page.Locator("#btn-archive-toggle").ClickAsync();
            await s.FindAlertMessage();
            await Expect(s.Page.Locator("#btn-archive-toggle")).Not.ToContainTextAsync("Unarchive");
            await s.GoToInvoices(storeId);
            await s.Page.WaitForSelectorAsync($"tr[id=invoice_{invoiceId}]");
            Assert.Contains(invoiceId, await s.Page.ContentAsync());

            // archive via list
            await s.Page.ClickAsync($".mass-action-select[value=\"{invoiceId}\"]");
            await s.Page.ClickAsync("#ArchiveSelected");
            await s.FindAlertMessage(partialText: "1 invoice archived");
            Assert.DoesNotContain(invoiceId, await s.Page.ContentAsync());

            // unarchive via list
            await s.Page.Locator("#StatusOptionsToggle").ClickAsync();
            await s.Page.Locator("#StatusOptionsIncludeArchived").ClickAsync();
            await s.Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            Assert.Contains(invoiceId, await s.Page.ContentAsync());
            await s.Page.ClickAsync($".mass-action-select[value=\"{invoiceId}\"]");
            await s.Page.ClickAsync("#UnarchiveSelected");
            await s.FindAlertMessage(partialText: "1 invoice unarchived");
            await s.Page.WaitForSelectorAsync($"tr[id=invoice_{invoiceId}]");

            // When logout out we should not be able to access store and invoice details
            await s.GoToUrl("/account");
            await s.Logout();
            await s.GoToUrl(storeUrl);
            Assert.Contains("ReturnUrl", s.Page.Url);
            await s.Page.GotoAsync(invoiceUrl);
            Assert.Contains("ReturnUrl", s.Page.Url);
            await s.GoToRegister();

            // When logged in as different user we should not be able to access store and invoice details
            var bob = await s.RegisterNewUser();
            await s.GoToUrl(storeUrl);
            Assert.Contains("ReturnUrl", s.Page.Url);
            await s.Page.GotoAsync(invoiceUrl);
            Assert.Contains("ReturnUrl", s.Page.Url);
            // s.AssertAccessDenied(); // TODO: Playwright equivalent if needed
            await s.GoToUrl("/account");
            await s.Logout();

            // Let's add Bob as an employee to alice's store
            await s.LogIn(alice);
            await s.AddUserToStore(storeId, bob, "Employee");
            await s.Logout();

            // Bob should not have access to store, but should have access to invoice
            await s.LogIn(bob);
            await s.GoToUrl(storeUrl);
            Assert.Contains("ReturnUrl", s.Page.Url);
            await s.GoToUrl(invoiceUrl);
            await s.Page.AssertNoError();
            await s.GoToUrl("/account");
            await s.Logout();
            await s.LogIn(alice);

            // Check if we can enable the payment button
            await s.GoToStore(storeId, StoreNavPages.PayButton);
            await s.Page.Locator("#enable-pay-button").ClickAsync();
            await s.Page.Locator("#disable-pay-button").ClickAsync();
            await s.FindAlertMessage();
            await s.GoToStore(storeId);
            Assert.False(await s.Page.Locator("#AnyoneCanCreateInvoice").IsCheckedAsync());
            await s.Page.Locator("#AnyoneCanCreateInvoice").CheckAsync();
            await s.ClickPagePrimary();
            await s.FindAlertMessage();
            Assert.True(await s.Page.Locator("#AnyoneCanCreateInvoice").IsCheckedAsync());

            // Store settings: Set and unset brand color
            await s.GoToStore(storeId);
            await s.Page.Locator("#BrandColor").FillAsync("#f7931a");
            await s.ClickPagePrimary();
            Assert.Contains("Store successfully updated", await (await s.FindAlertMessage()).InnerTextAsync());
            Assert.Equal("#f7931a", await s.Page.Locator("#BrandColor").InputValueAsync());
            await s.Page.Locator("#BrandColor").FillAsync("");
            await s.ClickPagePrimary();
            Assert.Contains("Store successfully updated", await (await s.FindAlertMessage()).InnerTextAsync());
            Assert.Equal(string.Empty, await s.Page.Locator("#BrandColor").InputValueAsync());

            // Alice should be able to delete the store
            await s.GoToStore(storeId);
            await s.Page.Locator("#DeleteStore").ClickAsync();
            await s.Page.Locator("#ConfirmInput").FillAsync("DELETE");
            await s.Page.Locator("#ConfirmContinue").ClickAsync();
            await s.GoToUrl(storeUrl);
            Assert.Contains("ReturnUrl", s.Page.Url);

            // Archive store
            (storeName, storeId) = await s.CreateNewStore();

            await s.Page.Locator("#StoreSelectorToggle").ClickAsync();
            Assert.Contains(storeName, await s.Page.Locator("#StoreSelectorMenu").InnerTextAsync());
            await s.Page.Locator($"#StoreSelectorMenuItem-{storeId}").ClickAsync();
            await s.GoToStore(storeId);
            await s.Page.Locator("#btn-archive-toggle").ClickAsync();
            Assert.Contains("The store has been archived and will no longer appear in the stores list by default.", await (await s.FindAlertMessage()).InnerTextAsync());

            await s.Page.Locator("#StoreSelectorToggle").ClickAsync();
            Assert.DoesNotContain(storeName, await s.Page.Locator("#StoreSelectorMenu").InnerTextAsync());
            Assert.Contains("1 Archived Store", await s.Page.Locator("#StoreSelectorMenu").InnerTextAsync());
            await s.Page.Locator("#StoreSelectorArchived").ClickAsync();

            var storeLink = s.Page.Locator($"#Store-{storeId}");
            Assert.Contains(storeName, await storeLink.InnerTextAsync());
            await s.GoToStore(storeId);
            await s.Page.Locator("#btn-archive-toggle").ClickAsync();
            Assert.Contains("The store has been unarchived and will appear in the stores list by default again.", await (await s.FindAlertMessage()).InnerTextAsync());
        }

        [Fact]
        [Trait("Lightning", "Lightning")]
        public async Task CanAccessUserStoreAsAdmin()
        {
            await using var s = CreatePlaywrightTester(newDb: true);
            s.Server.ActivateLightning();
            await s.StartAsync();
            await s.Server.EnsureChannelsSetup();

            // Setup user, store and wallets
            await s.RegisterNewUser();
            var (_, storeId) = await s.CreateNewStore();
            await s.GoToStore();
            await s.GenerateWallet(isHotWallet: true);
            await s.AddLightningNode(LightningConnectionType.CLightning, false);

            // Add apps
            await s.CreateApp("PointOfSale");
            await s.CreateApp("Crowdfund");
            await s.Logout();

            // Setup admin and check access
            await s.GoToRegister();
            await s.RegisterNewUser(true);
            string GetStorePath(string subPath) => $"/stores/{storeId}/{subPath}";

            // Admin access
            await s.AssertPageAccess(false, GetStorePath(""));
            await s.AssertPageAccess(true, GetStorePath("reports"));
            await s.AssertPageAccess(true, GetStorePath("invoices"));
            await s.AssertPageAccess(false, GetStorePath("invoices/create"));
            await s.AssertPageAccess(true, GetStorePath("payment-requests"));
            await s.AssertPageAccess(false, GetStorePath("payment-requests/edit"));
            await s.AssertPageAccess(true, GetStorePath("pull-payments"));
            await s.AssertPageAccess(true, GetStorePath("payouts"));
            await s.AssertPageAccess(false, GetStorePath("onchain/BTC"));
            await s.AssertPageAccess(false, GetStorePath("onchain/BTC/settings"));
            await s.AssertPageAccess(false, GetStorePath("lightning/BTC"));
            await s.AssertPageAccess(false, GetStorePath("lightning/BTC/settings"));
            await s.AssertPageAccess(false, GetStorePath("apps/create"));

            var storeSettingsPaths = new [] {"settings", "rates", "checkout", "tokens", "users", "roles", "webhooks",
                "payout-processors", "payout-processors/onchain-automated/BTC", "payout-processors/lightning-automated/BTC",
                "emails/rules", "email-settings", "forms"};
            foreach (var path in storeSettingsPaths)
            {   // should have view access to settings, but no submit buttons or create links
                s.TestLogs.LogInformation($"Checking access to store page {path} as admin");
                await s.AssertPageAccess(true, $"stores/{storeId}/{path}");
                if (path != "payout-processors")
                {
                    Assert.Equal(0, await s.Page.Locator("#mainContent .btn-primary").CountAsync());
                }
            }
        }

        [Fact]
        [Trait("Playwright", "Playwright")]
        public async Task CanSigninWithLoginCode()
        {
            await using var s = CreatePlaywrightTester();
            await s.StartAsync();
            var user = await s.RegisterNewUser();
            await s.GoToHome();
            await s.GoToProfile(ManageNavPages.LoginCodes);

            await s.Page.WaitForSelectorAsync("#LoginCode .qr-code");
            var code = await s.Page.Locator("#LoginCode .qr-code").GetAttributeAsync("alt");
            string prevCode = code;
            await s.Page.ReloadAsync();
            await s.Page.WaitForSelectorAsync("#LoginCode .qr-code");
            code = await s.Page.Locator("#LoginCode .qr-code").GetAttributeAsync("alt");
            Assert.NotEqual(prevCode, code);
            await s.Page.WaitForSelectorAsync("#LoginCode .qr-code");
            code = await s.Page.Locator("#LoginCode .qr-code").GetAttributeAsync("alt");
            await s.Logout();
            await s.GoToLogin();
            await s.Page.EvaluateAsync("document.getElementById('LoginCode').value = 'bad code'");
            await s.Page.EvaluateAsync("document.getElementById('logincode-form').submit()");
            await s.Page.WaitForLoadStateAsync();

            await s.GoToLogin();
            await s.Page.EvaluateAsync($"document.getElementById('LoginCode').value = '{code}'");
            await s.Page.EvaluateAsync("document.getElementById('logincode-form').submit()");
            await s.Page.WaitForLoadStateAsync();
            await s.Page.WaitForLoadStateAsync();

            await s.CreateNewStore();
            await s.GoToHome();
            await s.Page.WaitForLoadStateAsync();
            await s.Page.WaitForLoadStateAsync();
            var content = await s.Page.ContentAsync();
            Assert.Contains(user, content);
        }

        [Fact]
        public async Task CanUseInvoiceReceipts()
        {
            await using var s = CreatePlaywrightTester();
            await s.StartAsync();
            await s.RegisterNewUser(true);
            await s.CreateNewStore();
            await s.AddDerivationScheme();
            await s.GoToInvoices();
            var i = await s.CreateInvoice(100);
            await s.Server.PayTester.InvoiceRepository.MarkInvoiceStatus(i, InvoiceStatus.Settled);

            await TestUtils.EventuallyAsync(async () =>
            {
                await s.Page.ReloadAsync();
                await s.Page.ClickAsync("#Receipt");
            });

            await TestUtils.EventuallyAsync(async () =>
            {
                await s.Page.ReloadAsync();
                await s.Page.WaitForLoadStateAsync();
                var content = await s.Page.ContentAsync();
                Assert.DoesNotContain("invoice-unsettled", content);
                Assert.DoesNotContain("invoice-processing", content);
            });

            await s.Page.WaitForLoadStateAsync();
            var content = await s.Page.ContentAsync();
            Assert.Contains("100.00 USD", content);
            Assert.Contains(i, content);

            await s.GoToInvoices(s.StoreId);
            i = await s.CreateInvoice();
            await s.GoToInvoiceCheckout(i);
            await s.GoToUrl($"/i/{i}/receipt");
            await s.Page.Locator("#invoice-unsettled").WaitForAsync();

            await s.GoToInvoices(s.StoreId);
            await s.GoToInvoiceCheckout(i);
            var checkouturi = s.Page.Url;
            await s.PayInvoice(mine: true, clickReceipt: true);

            await TestUtils.EventuallyAsync(async () =>
            {
                await s.Page.ReloadAsync();
                await s.Page.WaitForLoadStateAsync();
                var pageContent = await s.Page.ContentAsync();
                Assert.DoesNotContain("invoice-unsettled", pageContent);
                Assert.Contains("\"PaymentDetails\"", pageContent);
            });

            await s.GoToUrl(checkouturi);

            await s.Server.PayTester.InvoiceRepository.MarkInvoiceStatus(i, InvoiceStatus.Settled);

            await s.Page.Locator("#ReceiptLink").WaitForAsync();
            await s.Page.ClickAsync("#ReceiptLink");
            await TestUtils.EventuallyAsync(async () =>
            {
                await s.Page.ReloadAsync();
                await s.Page.WaitForLoadStateAsync();
                var pageContent = await s.Page.ContentAsync();
                Assert.DoesNotContain("invoice-unsettled", pageContent);
                Assert.DoesNotContain("invoice-processing", pageContent);
            });

            // ensure archived invoices are not accessible for logged out users
            await s.Server.PayTester.InvoiceRepository.ToggleInvoiceArchival(i, true);
            await s.GoToHome();
            await s.Logout();

            await s.GoToUrl($"/i/{i}/receipt");
            await TestUtils.EventuallyAsync(async () =>
            {
                var title = await s.Page.TitleAsync();
                Assert.Contains("Page not found", title, StringComparison.OrdinalIgnoreCase);
            });

            await s.GoToUrl($"/i/{i}");
            await TestUtils.EventuallyAsync(async () =>
            {
                var title = await s.Page.TitleAsync();
                Assert.Contains("Page not found", title, StringComparison.OrdinalIgnoreCase);
            });

            await s.GoToUrl($"/i/{i}/status");
            await TestUtils.EventuallyAsync(async () =>
            {
                var title = await s.Page.TitleAsync();
                Assert.Contains("Page not found", title, StringComparison.OrdinalIgnoreCase);
            });
        }

        [Fact]
        public async Task CanCreateCrowdfundingApp()
        {
            await using var s = CreatePlaywrightTester();
            await s.StartAsync();
            await s.RegisterNewUser();
            await s.CreateNewStore();
            await s.AddDerivationScheme();

            (_, string appId) = await s.CreateApp("Crowdfund");
            await s.Page.Locator("#Title").ClearAsync();
            await s.Page.FillAsync("#Title", "Kukkstarter");
            await s.Page.FillAsync("div.note-editable.card-block", "1BTC = 1BTC");
            await s.Page.Locator("#TargetCurrency").ClearAsync();
            await s.Page.FillAsync("#TargetCurrency", "EUR");
            await s.Page.FillAsync("#TargetAmount", "700");

            // test wrong dates
            await s.Page.EvaluateAsync(@"
                const now = new Date();
                document.getElementById('StartDate').value = now.toISOString();
                const yst = new Date(now.setDate(now.getDate() - 1));
                document.getElementById('EndDate').value = yst.toISOString();
            ");
            await s.ClickPagePrimary();
            var pageContent = await s.Page.ContentAsync();
            Assert.Contains("End date cannot be before start date", pageContent);
            Assert.DoesNotContain("App updated", pageContent);

            // unset end date
            await s.Page.ClickAsync("#clear_end");
            await s.ClickPagePrimary();
            await s.FindAlertMessage(partialText: "App updated");

            var editUrl = s.Page.Url;

            // Check public page
            await using var pageSwitch = await POSTests.ViewApp(s);
            var cfUrl = s.Page.Url;

            await Expect(s.Page.Locator("[data-test='time-state']")).ToContainTextAsync("Currently active!");

            // Contribute
            await s.Page.ClickAsync("#crowdfund-body-header-cta");
            await s.Page.WaitForSelectorAsync("iframe[name='btcpay']", new() { Timeout = 10000 });

            var frameElement = s.Page.Frame("btcpay");
            Assert.NotNull(frameElement);
            await frameElement.WaitForSelectorAsync("#Checkout");

            var closeButton = frameElement.Locator("#close");
            Assert.True(await closeButton.IsVisibleAsync());
            await closeButton.ClickAsync();

            await s.Page.WaitForSelectorAsync("iframe[name='btcpay']", new() { State = WaitForSelectorState.Hidden });

            // Archive - navigate to the app edit page and archive it
            await s.Page.GotoAsync(editUrl);
            await s.Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await s.Page.ClickAsync("#btn-archive-toggle");
            await s.FindAlertMessage(partialText: "The app has been archived and will no longer appear in the apps list by default.");

            Assert.Equal(0, await s.Page.Locator("#ViewApp").CountAsync());

            // Go to store page to verify archived app link appears
            await s.GoToStore(s.StoreId);
            var archivedLink = s.Page.Locator("text='1 Archived App'");
            await archivedLink.WaitForAsync();
            await Expect(archivedLink).ToContainTextAsync("1 Archived App");

            // Verify crowdfund is no longer accessible
            await s.Page.GotoAsync(cfUrl);
            Assert.Contains("Page not found", await s.Page.TitleAsync(), StringComparison.OrdinalIgnoreCase);
            await s.Page.GoBackAsync();

            // Navigate to archived apps and unarchive
            await s.Page.Locator("text='1 Archived App'").ClickAsync();
            await s.Page.ClickAsync($"#App-{appId}");
            await s.Page.ClickAsync("#btn-archive-toggle");
            await s.FindAlertMessage(partialText: "The app has been unarchived and will appear in the apps list by default again.");

            // Crowdfund with form
            await s.GoToUrl(editUrl);
            await s.Page.SelectOptionAsync("#FormId", "Email");
            await s.ClickPagePrimary();
            // Verify "App updated" message
            await s.FindAlertMessage(partialText: "App updated");

            await using var formPageSwitch = await POSTests.ViewApp(s);
            await s.Page.ClickAsync("#crowdfund-body-header-cta");

            pageContent = await s.Page.ContentAsync();
            Assert.Contains("Enter your email", pageContent);
            await s.Page.FillAsync("[name='buyerEmail']", "test-without-perk@crowdfund.com");
            await s.Page.ClickAsync("input[type='submit']");

            await s.PayInvoice(true, 10);
            var invoiceId = s.Page.Url[(s.Page.Url.LastIndexOf("/", StringComparison.Ordinal) + 1)..];
            await s.GoToInvoice(invoiceId);
            await s.Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            pageContent = await s.Page.ContentAsync();
            Assert.Contains("test-without-perk@crowdfund.com", pageContent);

            // Crowdfund with perk
            await s.GoToUrl(editUrl);
            await s.Page.Locator("#btAddItem").ScrollIntoViewIfNeededAsync();
            await s.Page.ClickAsync("#btAddItem");
            await s.Page.Locator("#EditorTitle").WaitForAsync();
            await s.Page.FillAsync("#EditorTitle", "Perk 1");
            await s.Page.FillAsync("#EditorAmount", "20");
            // Test autogenerated ID
            Assert.Equal("perk-1", await s.Page.InputValueAsync("#EditorId"));
            await s.Page.Locator("#EditorId").ClearAsync();
            await s.Page.FillAsync("#EditorId", "Perk-1");
            await s.Page.ClickAsync(".offcanvas-header button");
            await s.Page.Locator("#CodeTabButton").WaitForAsync();
            await s.Page.Locator("#CodeTabButton").ScrollIntoViewIfNeededAsync();
            await s.Page.ClickAsync("#CodeTabButton");
            var template = await s.Page.InputValueAsync("#TemplateConfig");
            Assert.Contains("\"title\": \"Perk 1\"", template);
            Assert.Contains("\"id\": \"Perk-1\"", template);
            await s.ClickPagePrimary();
            // Verify "App updated" message
            await s.FindAlertMessage(partialText: "App updated");

            await using var perkPageSwitch = await POSTests.ViewApp(s);
            await s.Page.Locator(".perk.unexpanded[id='Perk-1']").WaitForAsync();
            await s.Page.ClickAsync(".perk.unexpanded[id='Perk-1']");
            await s.Page.Locator(".perk.expanded[id='Perk-1'] button[type=\"submit\"]").WaitForAsync();
            await s.Page.ClickAsync(".perk.expanded[id='Perk-1'] button[type=\"submit\"]");

            pageContent = await s.Page.ContentAsync();
            Assert.Contains("Enter your email", pageContent);
            await s.Page.FillAsync("[name='buyerEmail']", "test-with-perk@crowdfund.com");
            await s.Page.ClickAsync("input[type='submit']");

            await s.PayInvoice(true, 20);
            invoiceId = s.Page.Url[(s.Page.Url.LastIndexOf("/", StringComparison.Ordinal) + 1)..];
            await s.GoToInvoice(invoiceId);
            pageContent = await s.Page.ContentAsync();
            Assert.Contains("test-with-perk@crowdfund.com", pageContent);
        }

    }
}
