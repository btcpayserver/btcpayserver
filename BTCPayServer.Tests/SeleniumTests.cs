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

        [Fact(Timeout = TestTimeout)]
        public async Task CanUseInvoiceReceipts()
        {
            using var s = CreateSeleniumTester();
            await s.StartAsync();
            s.RegisterNewUser(true);
            s.CreateNewStore();
            s.AddDerivationScheme();
            s.GoToInvoices();
            var i = s.CreateInvoice();
            await s.Server.PayTester.InvoiceRepository.MarkInvoiceStatus(i, InvoiceStatus.Settled);
            TestUtils.Eventually(() =>
            {
                s.Driver.Navigate().Refresh();
                s.Driver.FindElement(By.Id($"Receipt")).Click();
            });
            TestUtils.Eventually(() =>
            {
                s.Driver.Navigate().Refresh();
                Assert.DoesNotContain("invoice-unsettled", s.Driver.PageSource);
                Assert.DoesNotContain("invoice-processing", s.Driver.PageSource);
            });

            Assert.Contains("100.00 USD", s.Driver.PageSource);
            Assert.Contains(i, s.Driver.PageSource);

            s.GoToInvoices(s.StoreId);
            i = s.CreateInvoice();
            s.GoToInvoiceCheckout(i);
            var receipturl = s.Driver.Url + "/receipt";
            s.Driver.Navigate().GoToUrl(receipturl);
            s.Driver.FindElement(By.Id("invoice-unsettled"));

            s.GoToInvoices(s.StoreId);
            s.GoToInvoiceCheckout(i);
            var checkouturi = s.Driver.Url;
            s.PayInvoice(mine: true);
            TestUtils.Eventually(() =>
            {
                s.Driver.Navigate().Refresh();
                s.Driver.FindElement(By.Id("ReceiptLink")).Click();
            });
            TestUtils.Eventually(() =>
            {
                s.Driver.Navigate().Refresh();
                Assert.DoesNotContain("invoice-unsettled", s.Driver.PageSource);
                Assert.Contains("\"PaymentDetails\"", s.Driver.PageSource);
            });
            s.GoToUrl(checkouturi);

            await s.Server.PayTester.InvoiceRepository.MarkInvoiceStatus(i, InvoiceStatus.Settled);

            TestUtils.Eventually(() => s.Driver.FindElement(By.Id("ReceiptLink")).Click());
            TestUtils.Eventually(() =>
            {
                s.Driver.Navigate().Refresh();
                Assert.DoesNotContain("invoice-unsettled", s.Driver.PageSource);
                Assert.DoesNotContain("invoice-processing", s.Driver.PageSource);
            });

            // ensure archived invoices are not accessible for logged out users
            await s.Server.PayTester.InvoiceRepository.ToggleInvoiceArchival(i, true);
            s.Logout();

            await s.Driver.Navigate().GoToUrlAsync(s.Driver.Url + $"/i/{i}/receipt");
            TestUtils.Eventually(() =>
            {
                Assert.Contains("Page not found", s.Driver.Title, StringComparison.OrdinalIgnoreCase);
            });

            await s.Driver.Navigate().GoToUrlAsync(s.Driver.Url + $"i/{i}");
            TestUtils.Eventually(() =>
            {
                Assert.Contains("Page not found", s.Driver.Title, StringComparison.OrdinalIgnoreCase);
            });

            await s.Driver.Navigate().GoToUrlAsync(s.Driver.Url + $"i/{i}/status");
            TestUtils.Eventually(() =>
            {
                Assert.Contains("Page not found", s.Driver.Title, StringComparison.OrdinalIgnoreCase);
            });
        }


        [Fact(Timeout = TestTimeout)]
        public async Task CanCreateAppPoS()
        {
            using var s = CreateSeleniumTester(newDb: true);
            await s.StartAsync();
            var userId = s.RegisterNewUser(true);
            s.CreateNewStore();
            s.GenerateWallet();
            (_, string appId) = s.CreateApp("PointOfSale");
            s.Driver.FindElement(By.Id("Title")).Clear();
            s.Driver.FindElement(By.Id("Title")).SendKeys("Tea shop");
            s.Driver.FindElement(By.CssSelector("label[for='DefaultView_Cart']")).Click();
            s.Driver.FindElement(By.CssSelector(".template-item:nth-of-type(1)")).Click();
            s.Driver.WaitUntilAvailable(By.Id("BuyButtonText"));
			s.Driver.FindElement(By.Id("BuyButtonText")).SendKeys("Take my money");
            s.Driver.FindElement(By.Id("EditorCategories-ts-control")).SendKeys("Drinks");
            s.Driver.FindElement(By.CssSelector(".offcanvas-header button")).Click();
            s.Driver.WaitUntilAvailable(By.Id("CodeTabButton"));
			s.Driver.ScrollTo(By.Id("CodeTabButton"));
            s.Driver.FindElement(By.Id("CodeTabButton")).Click();
			var template = s.Driver.FindElement(By.Id("TemplateConfig")).GetAttribute("value");
            Assert.Contains("\"buyButtonText\": \"Take my money\"", template);
            Assert.Matches("\"categories\": \\[\r?\n\\s*\"Drinks\"\\s*\\]", template);

            s.ClickPagePrimary();
            Assert.Contains("App updated", s.FindAlertMessage().Text);

            s.Driver.ScrollTo(By.Id("CodeTabButton"));
            s.Driver.FindElement(By.Id("CodeTabButton")).Click();
            template = s.Driver.FindElement(By.Id("TemplateConfig")).GetAttribute("value");
            s.Driver.FindElement(By.Id("TemplateConfig")).Clear();
            s.Driver.FindElement(By.Id("TemplateConfig")).SendKeys(template.Replace(@"""id"": ""green-tea"",", ""));

            s.ClickPagePrimary();
            Assert.Contains("Invalid template: Missing ID for item \"Green Tea\".", s.Driver.FindElement(By.CssSelector(".validation-summary-errors")).Text);

            s.Driver.FindElement(By.Id("ViewApp")).Click();
            var windows = s.Driver.WindowHandles;
            Assert.Equal(2, windows.Count);
            s.Driver.SwitchTo().Window(windows[1]);

            var posBaseUrl = s.Driver.Url.Replace("/cart", "");
            Assert.True(s.Driver.PageSource.Contains("Tea shop"), "Unable to create PoS");
            Assert.True(s.Driver.PageSource.Contains("Cart"), "PoS not showing correct default view");
            Assert.True(s.Driver.PageSource.Contains("Take my money"), "PoS not showing correct default view");
            Assert.Equal(6, s.Driver.FindElements(By.CssSelector(".posItem.posItem--displayed")).Count);

            var drinks = s.Driver.FindElement(By.CssSelector("label[for='Category-Drinks']"));
            Assert.Equal("Drinks", drinks.Text);
            drinks.Click();
            Assert.Single(s.Driver.FindElements(By.CssSelector(".posItem.posItem--displayed")));
            s.Driver.FindElement(By.CssSelector("label[for='Category-*']")).Click();
            Assert.Equal(6, s.Driver.FindElements(By.CssSelector(".posItem.posItem--displayed")).Count);

            s.Driver.Url = posBaseUrl + "/static";
            Assert.False(s.Driver.PageSource.Contains("Cart"), "Static PoS not showing correct view");

            s.Driver.Url = posBaseUrl + "/cart";
            Assert.True(s.Driver.PageSource.Contains("Cart"), "Cart PoS not showing correct view");

            // Let's set change the root app
            s.GoToHome();
            s.GoToServer(ServerNavPages.Policies);
            s.Driver.ScrollTo(By.Id("RootAppId"));
            var select = new SelectElement(s.Driver.FindElement(By.Id("RootAppId")));
            select.SelectByText("Point of", true);
            s.ClickPagePrimary();
            s.FindAlertMessage();
            // Make sure after login, we are not redirected to the PoS
            s.Logout();
            s.LogIn(userId);
            Assert.DoesNotContain("Tea shop", s.Driver.PageSource);
            var prevUrl = s.Driver.Url;
            // We are only if explicitly going to /
            s.GoToUrl("/");
            Assert.Contains("Tea shop", s.Driver.PageSource);
            // Check redirect to canonical url
            s.GoToUrl(posBaseUrl);
            Assert.Equal("/", new Uri(s.Driver.Url, UriKind.Absolute).AbsolutePath);

            // Let's check with domain mapping as well.
            s.Driver.Navigate().GoToUrl(new Uri(prevUrl, UriKind.Absolute));
            s.GoToServer(ServerNavPages.Policies);
            s.Driver.ScrollTo(By.Id("RootAppId"));
            select = new SelectElement(s.Driver.FindElement(By.Id("RootAppId")));
            select.SelectByText("None", true);
            s.ClickPagePrimary();
            s.Driver.ScrollTo(By.Id("RootAppId"));
            s.Driver.FindElement(By.Id("AddDomainButton")).Click();
            s.Driver.FindElement(By.Id("DomainToAppMapping_0__Domain")).SendKeys(new Uri(s.Driver.Url, UriKind.Absolute).DnsSafeHost);
            select = new SelectElement(s.Driver.FindElement(By.Id("DomainToAppMapping_0__AppId")));
            select.SelectByText("Point of", true);
            s.ClickPagePrimary();
            Assert.Contains("Policies updated successfully", s.FindAlertMessage().Text);
            // Make sure after login, we are not redirected to the PoS
            s.Logout();
            s.LogIn(userId);
            Assert.DoesNotContain("Tea shop", s.Driver.PageSource);
            // We are only if explicitly going to /
            s.GoToUrl("/");
            Assert.Contains("Tea shop", s.Driver.PageSource);
            // Check redirect to canonical url
            s.GoToUrl(posBaseUrl);
            Assert.Equal("/", new Uri(s.Driver.Url, UriKind.Absolute).AbsolutePath);

            // Archive
            s.Driver.SwitchTo().Window(windows[0]);
            Assert.True(s.Driver.ElementDoesNotExist(By.Id("menu-item-AppsNavPages")));
            s.Driver.FindElement(By.Id("btn-archive-toggle")).Click();
            Assert.Contains("The app has been archived and will no longer appear in the apps list by default.", s.FindAlertMessage().Text);

            Assert.True(s.Driver.ElementDoesNotExist(By.Id("ViewApp")));
            Assert.Contains("1 Archived App", s.Driver.FindElement(By.Id("menu-item-AppsNavPages")).Text);
            s.Driver.Navigate().GoToUrl(posBaseUrl);
            Assert.Contains("Page not found", s.Driver.Title, StringComparison.OrdinalIgnoreCase);
            s.Driver.Navigate().Back();
            s.Driver.FindElement(By.Id("menu-item-AppsNavPages")).Click();

            // Unarchive
            s.Driver.FindElement(By.Id($"App-{appId}")).Click();
            s.Driver.FindElement(By.Id("btn-archive-toggle")).Click();
            Assert.Contains("The app has been unarchived and will appear in the apps list by default again.", s.FindAlertMessage().Text);
        }

        [Fact(Timeout = TestTimeout)]
        public async Task CanCreateCrowdfundingApp()
        {
            using var s = CreateSeleniumTester();
            await s.StartAsync();
            s.RegisterNewUser();
            s.CreateNewStore();
            s.AddDerivationScheme();

            (_, string appId) = s.CreateApp("Crowdfund");
            s.Driver.FindElement(By.Id("Title")).Clear();
            s.Driver.FindElement(By.Id("Title")).SendKeys("Kukkstarter");
            s.Driver.FindElement(By.CssSelector("div.note-editable.card-block")).SendKeys("1BTC = 1BTC");
            s.Driver.FindElement(By.Id("TargetCurrency")).Clear();
            s.Driver.FindElement(By.Id("TargetCurrency")).SendKeys("EUR");
            s.Driver.FindElement(By.Id("TargetAmount")).SendKeys("700");

            // test wrong dates
            s.Driver.ExecuteJavaScript("const now = new Date();document.getElementById('StartDate').value = now.toISOString();" +
                "const yst = new Date(now.setDate(now.getDate() -1));document.getElementById('EndDate').value = yst.toISOString()");
            s.ClickPagePrimary();
            Assert.Contains("End date cannot be before start date", s.Driver.PageSource);
            Assert.DoesNotContain("App updated", s.Driver.PageSource);

            // unset end date
            s.Driver.ExecuteJavaScript("document.getElementById('EndDate').value = ''");
            s.ClickPagePrimary();
            Assert.Contains("App updated", s.FindAlertMessage().Text);
            var editUrl = s.Driver.Url;

            // Check public page
            s.Driver.FindElement(By.Id("ViewApp")).Click();
            var windows = s.Driver.WindowHandles;
            Assert.Equal(2, windows.Count);
            s.Driver.SwitchTo().Window(windows[1]);
            var cfUrl = s.Driver.Url;

            Assert.Equal("Currently active!", s.Driver.FindElement(By.CssSelector("[data-test='time-state']")).Text);

            // Contribute
            s.Driver.FindElement(By.Id("crowdfund-body-header-cta")).Click();
            TestUtils.Eventually(() =>
            {
                s.Driver.WaitUntilAvailable(By.Name("btcpay"));

                var frameElement = s.Driver.FindElement(By.Name("btcpay"));
                Assert.True(frameElement.Displayed);
                var iframe = s.Driver.SwitchTo().Frame(frameElement);
                iframe.WaitUntilAvailable(By.Id("Checkout"));

                var closeButton = iframe.FindElement(By.Id("close"));
                Assert.True(closeButton.Displayed);
                closeButton.Click();
            });
            s.Driver.AssertElementNotFound(By.Name("btcpay"));

            // Back to admin view
            s.Driver.Close();
            s.Driver.SwitchTo().Window(windows[0]);

            // Archive
            Assert.True(s.Driver.ElementDoesNotExist(By.Id("menu-item-AppsNavPages")));
            s.Driver.SwitchTo().Window(windows[0]);
            s.Driver.FindElement(By.Id("btn-archive-toggle")).Click();
            Assert.Contains("The app has been archived and will no longer appear in the apps list by default.", s.FindAlertMessage().Text);

            Assert.True(s.Driver.ElementDoesNotExist(By.Id("ViewApp")));
            Assert.Contains("1 Archived App", s.Driver.FindElement(By.Id("menu-item-AppsNavPages")).Text);
            s.Driver.Navigate().GoToUrl(cfUrl);
            Assert.Contains("Page not found", s.Driver.Title, StringComparison.OrdinalIgnoreCase);
            s.Driver.Navigate().Back();
            s.Driver.FindElement(By.Id("menu-item-AppsNavPages")).Click();

            // Unarchive
            s.Driver.FindElement(By.Id($"App-{appId}")).Click();
            s.Driver.FindElement(By.Id("btn-archive-toggle")).Click();
            Assert.Contains("The app has been unarchived and will appear in the apps list by default again.", s.FindAlertMessage().Text);

            // Crowdfund with form
            s.GoToUrl(editUrl);
            new SelectElement(s.Driver.FindElement(By.Id("FormId"))).SelectByValue("Email");
            s.ClickPagePrimary();
            Assert.Contains("App updated", s.FindAlertMessage().Text);

            s.Driver.FindElement(By.Id("ViewApp")).Click();
            s.Driver.SwitchTo().Window(s.Driver.WindowHandles.Last());
            s.Driver.FindElement(By.Id("crowdfund-body-header-cta")).Click();

            Assert.Contains("Enter your email", s.Driver.PageSource);
            s.Driver.FindElement(By.Name("buyerEmail")).SendKeys("test-without-perk@crowdfund.com");
            s.Driver.FindElement(By.CssSelector("input[type='submit']")).Click();

            s.PayInvoice(true, 10);
            var invoiceId = s.Driver.Url[(s.Driver.Url.LastIndexOf("/", StringComparison.Ordinal) + 1)..];
            s.Driver.Close();
            s.Driver.SwitchTo().Window(s.Driver.WindowHandles.First());

            s.GoToInvoice(invoiceId);
            Assert.Contains("test-without-perk@crowdfund.com", s.Driver.PageSource);

            // Crowdfund with perk
            s.GoToUrl(editUrl);
            s.Driver.ScrollTo(By.Id("btAddItem"));
            s.Driver.FindElement(By.Id("btAddItem")).Click();
            s.Driver.WaitUntilAvailable(By.Id("EditorTitle"));
            s.Driver.FindElement(By.Id("EditorTitle")).SendKeys("Perk 1");
            s.Driver.FindElement(By.Id("EditorAmount")).SendKeys("20");
            // Test autogenerated ID
            Assert.Equal("perk-1", s.Driver.FindElement(By.Id("EditorId")).GetAttribute("value"));
            s.Driver.FindElement(By.Id("EditorId")).Clear();
            s.Driver.FindElement(By.Id("EditorId")).SendKeys("Perk-1");
            s.Driver.FindElement(By.CssSelector(".offcanvas-header button")).Click();
            s.Driver.WaitUntilAvailable(By.Id("CodeTabButton"));
            s.Driver.ScrollTo(By.Id("CodeTabButton"));
            s.Driver.FindElement(By.Id("CodeTabButton")).Click();
            var template = s.Driver.FindElement(By.Id("TemplateConfig")).GetAttribute("value");
            Assert.Contains("\"title\": \"Perk 1\"", template);
            Assert.Contains("\"id\": \"Perk-1\"", template);
            s.ClickPagePrimary();
            Assert.Contains("App updated", s.FindAlertMessage().Text);

            s.Driver.FindElement(By.Id("ViewApp")).Click();
            s.Driver.SwitchTo().Window(s.Driver.WindowHandles.Last());
            s.Driver.WaitForElement(By.Id("Perk-1")).Click();
            s.Driver.WaitForElement(By.CssSelector("#Perk-1 button[type=\"submit\"]")).Submit();

            Assert.Contains("Enter your email", s.Driver.PageSource);
            s.Driver.FindElement(By.Name("buyerEmail")).SendKeys("test-with-perk@crowdfund.com");
            s.Driver.FindElement(By.CssSelector("input[type='submit']")).Click();

            s.PayInvoice(true, 20);
            invoiceId = s.Driver.Url[(s.Driver.Url.LastIndexOf("/", StringComparison.Ordinal) + 1)..];
            s.Driver.Close();
            s.Driver.SwitchTo().Window(s.Driver.WindowHandles.First());

            s.GoToInvoice(invoiceId);
            Assert.Contains("test-with-perk@crowdfund.com", s.Driver.PageSource);
        }

        [Fact(Timeout = TestTimeout)]
        public async Task CanCreatePayRequest()
        {
            using var s = CreateSeleniumTester();
            await s.StartAsync();
            s.RegisterNewUser();
            s.CreateNewStore();
            s.Driver.FindElement(By.Id("menu-item-PaymentRequests")).Click();

            // Should give us an error message if we try to create a payment request before adding a wallet
            s.ClickPagePrimary();
            Assert.Contains("To create a payment request, you need to", s.Driver.PageSource);

            s.AddDerivationScheme();
            s.Driver.FindElement(By.Id("menu-item-PaymentRequests")).Click();
            s.ClickPagePrimary();
            s.Driver.FindElement(By.Id("Title")).SendKeys("Pay123");
            s.Driver.FindElement(By.Id("Amount")).Clear();
            s.Driver.FindElement(By.Id("Amount")).SendKeys(".01");

            var currencyInput = s.Driver.FindElement(By.Id("Currency"));
            Assert.Equal("USD", currencyInput.GetAttribute("value"));
            currencyInput.Clear();
            currencyInput.SendKeys("BTC");

            s.ClickPagePrimary();
            s.Driver.FindElement(By.XPath("//a[starts-with(@id, 'Edit-')]")).Click();
            var editUrl = s.Driver.Url;

            s.Driver.FindElement(By.Id("ViewPaymentRequest")).Click();
            s.Driver.SwitchTo().Window(s.Driver.WindowHandles.Last());
            var viewUrl = s.Driver.Url;
            Assert.Equal("Pay Invoice", s.Driver.FindElement(By.Id("PayInvoice")).Text.Trim());
            s.Driver.Close();
            s.Driver.SwitchTo().Window(s.Driver.WindowHandles.First());

            // expire
            s.Driver.ExecuteJavaScript("document.getElementById('ExpiryDate').value = '2021-01-21T21:00:00.000Z'");
            s.ClickPagePrimary();
            s.Driver.FindElement(By.XPath("//a[starts-with(@id, 'Edit-')]")).Click();

            s.GoToUrl(viewUrl);
            Assert.Equal("Expired", s.Driver.WaitForElement(By.CssSelector("[data-test='status']")).Text);

            // unexpire
            s.GoToUrl(editUrl);
            s.Driver.FindElement(By.Id("ClearExpiryDate")).Click();
            s.ClickPagePrimary();
            s.Driver.FindElement(By.XPath("//a[starts-with(@id, 'Edit-')]")).Click();

            // amount and currency should be editable, because no invoice exists
            s.GoToUrl(editUrl);
            Assert.True(s.Driver.FindElement(By.Id("Amount")).Enabled);
            Assert.True(s.Driver.FindElement(By.Id("Currency")).Enabled);

            s.GoToUrl(viewUrl);
            Assert.Equal("Pay Invoice", s.Driver.FindElement(By.Id("PayInvoice")).Text.Trim());

            // test invoice creation
            s.Driver.FindElement(By.Id("PayInvoice")).Click();
            TestUtils.Eventually(() =>
            {
                s.Driver.WaitUntilAvailable(By.Name("btcpay"));

                var frameElement = s.Driver.FindElement(By.Name("btcpay"));
                Assert.True(frameElement.Displayed);
                var iframe = s.Driver.SwitchTo().Frame(frameElement);
                iframe.WaitUntilAvailable(By.Id("Checkout"));

                IWebElement closebutton = null;
                TestUtils.Eventually(() =>
                {
                    closebutton = iframe.FindElement(By.Id("close"));
                    Assert.True(closebutton.Displayed);
                });
                closebutton.Click();
                s.Driver.AssertElementNotFound(By.Name("btcpay"));
            });

            // amount and currency should not be editable, because invoice exists
            s.GoToUrl(editUrl);
            Assert.False(s.Driver.FindElement(By.Id("Amount")).Enabled);
            Assert.False(s.Driver.FindElement(By.Id("Currency")).Enabled);

            // archive (from details page)
            var payReqId = s.Driver.Url.Split('/').Last();
            s.Driver.FindElement(By.Id("ArchivePaymentRequest")).Click();
            Assert.Contains("The payment request has been archived", s.FindAlertMessage().Text);
            Assert.DoesNotContain("Pay123", s.Driver.PageSource);
            s.Driver.FindElement(By.Id("StatusOptionsToggle")).Click();
            s.Driver.WaitForElement(By.Id("StatusOptionsIncludeArchived")).Click();
            Assert.Contains("Pay123", s.Driver.PageSource);

            // unarchive (from list)
            s.Driver.FindElement(By.Id($"ToggleActions-{payReqId}")).Click();
            s.Driver.WaitForElement(By.Id($"ToggleArchival-{payReqId}")).Click();
            Assert.Contains("The payment request has been unarchived", s.FindAlertMessage().Text);
            Assert.Contains("Pay123", s.Driver.PageSource);

            // payment
            s.GoToUrl(viewUrl);
            s.Driver.FindElement(By.Id("PayInvoice")).Click();
            TestUtils.Eventually(() =>
            {
                s.Driver.WaitUntilAvailable(By.Name("btcpay"));

                var frameElement = s.Driver.FindElement(By.Name("btcpay"));
                Assert.True(frameElement.Displayed);
                var iframe = s.Driver.SwitchTo().Frame(frameElement);
                iframe.WaitUntilAvailable(By.Id("Checkout"));

                // Pay full amount
                s.PayInvoice();

                // Processing
                TestUtils.Eventually(() =>
                {
                    var processingSection = s.Driver.WaitForElement(By.Id("processing"));
                    Assert.True(processingSection.Displayed);
                    Assert.Contains("Payment Received", processingSection.Text);
                    Assert.Contains("Your payment has been received and is now processing", processingSection.Text);
                });

                s.Driver.SwitchTo().Window(s.Driver.WindowHandles[0]);
                Assert.Equal("Processing", s.Driver.WaitForElement(By.CssSelector("[data-test='status']")).Text);
                s.Driver.SwitchTo().Frame(frameElement);

                // Mine
                s.MineBlockOnInvoiceCheckout();
                TestUtils.Eventually(() =>
                {
                    Assert.Contains("Mined 1 block",
                        s.Driver.WaitForElement(By.Id("CheatSuccessMessage")).Text);
                });

                s.Driver.FindElement(By.Id("close")).Click();
                s.Driver.AssertElementNotFound(By.Name("btcpay"));
            });

            s.Driver.SwitchTo().Window(s.Driver.WindowHandles[0]);
            Assert.Equal("Settled", s.Driver.WaitForElement(By.CssSelector("[data-test='status']")).Text);
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

        [Fact]
        [Trait("Selenium", "Selenium")]
        [Trait("Lightning", "Lightning")]
        public async Task CanUseLNURL()
        {
            using var s = CreateSeleniumTester();
            s.Server.DeleteStore = false;
            s.Server.ActivateLightning();
            await s.StartAsync();
            await s.Server.EnsureChannelsSetup();
            var cryptoCode = "BTC";
            await Lightning.Tests.ConnectChannels.ConnectAll(s.Server.ExplorerNode,
                new[] { s.Server.MerchantLightningD },
                new[] { s.Server.MerchantLnd.Client });
            s.RegisterNewUser(true);
            (_, string storeId) = s.CreateNewStore();
            var network = s.Server.NetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode).NBitcoinNetwork;
            s.AddLightningNode(LightningConnectionType.CLightning, false);
            s.GoToLightningSettings();
            // LNURL is true by default
            Assert.True(s.Driver.FindElement(By.Id("LNURLEnabled")).Selected);
            s.Driver.SetCheckbox(By.Name("LUD12Enabled"), true);
            s.ClickPagePrimary();

            // Topup Invoice test
            var i = s.CreateInvoice(storeId, null, cryptoCode);
            s.GoToInvoiceCheckout(i);
            var lnurl = s.Driver.FindElement(By.CssSelector("#Lightning_BTC-LNURL .truncate-center")).GetAttribute("data-text");
            var parsed = LNURL.LNURL.Parse(lnurl, out var tag);
            var fetchedReuqest =
                Assert.IsType<LNURL.LNURLPayRequest>(await LNURL.LNURL.FetchInformation(parsed, new HttpClient()));
            Assert.Equal(1m, fetchedReuqest.MinSendable.ToDecimal(LightMoneyUnit.Satoshi));
            Assert.NotEqual(1m, fetchedReuqest.MaxSendable.ToDecimal(LightMoneyUnit.Satoshi));
            var lnurlResponse = await fetchedReuqest.SendRequest(new LightMoney(0.000001m, LightMoneyUnit.BTC),
                network, new HttpClient(), comment: "lol");

            Assert.Equal(new LightMoney(0.000001m, LightMoneyUnit.BTC),
                lnurlResponse.GetPaymentRequest(network).MinimumAmount);

            var lnurlResponse2 = await fetchedReuqest.SendRequest(new LightMoney(0.000002m, LightMoneyUnit.BTC),
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
            Assert.Single(paymentMethods, p =>
            {
                return p.AdditionalData["providedComment"].Value<string>() == "lol2";
            });
            // Standard invoice test
            s.GoToStore(storeId);
            i = s.CreateInvoice(storeId, 0.0000001m, cryptoCode);
            s.GoToInvoiceCheckout(i);
            // BOLT11 is also displayed for standard invoice (not LNURL, even if it is available)
            var bolt11 = s.Driver.FindElement(By.CssSelector("#Lightning_BTC-LN .truncate-center")).GetAttribute("data-text");
            BOLT11PaymentRequest.Parse(bolt11, s.Server.ExplorerNode.Network);
            var invoiceId = s.Driver.Url.Split('/').Last();
            using (var resp = await s.Server.PayTester.HttpClient.GetAsync("BTC/lnurl/pay/i/" + invoiceId))
            {
                resp.EnsureSuccessStatusCode();
                fetchedReuqest = JsonConvert.DeserializeObject<LNURLPayRequest>(await resp.Content.ReadAsStringAsync());
            }
            Assert.Equal(0.0000001m, fetchedReuqest.MaxSendable.ToDecimal(LightMoneyUnit.BTC));
            Assert.Equal(0.0000001m, fetchedReuqest.MinSendable.ToDecimal(LightMoneyUnit.BTC));


            await Assert.ThrowsAsync<LNUrlException>(async () =>
            {
                await fetchedReuqest.SendRequest(new LightMoney(0.0000002m, LightMoneyUnit.BTC),
                    network, new HttpClient());
            });
            await Assert.ThrowsAsync<LNUrlException>(async () =>
            {
                await fetchedReuqest.SendRequest(new LightMoney(0.00000005m, LightMoneyUnit.BTC),
                    network, new HttpClient());
            });

            lnurlResponse = await fetchedReuqest.SendRequest(new LightMoney(0.0000001m, LightMoneyUnit.BTC),
                network, new HttpClient());
            lnurlResponse2 = await fetchedReuqest.SendRequest(new LightMoney(0.0000001m, LightMoneyUnit.BTC),
                network, new HttpClient());
            //invoice amounts do no change so the payment request is not regenerated
            Assert.Equal(lnurlResponse.Pr, lnurlResponse2.Pr);
            await s.Server.CustomerLightningD.Pay(lnurlResponse.Pr);
            Assert.Equal(new LightMoney(0.0000001m, LightMoneyUnit.BTC),
                lnurlResponse2.GetPaymentRequest(network).MinimumAmount);
            s.GoToHome();

            i = s.CreateInvoice(storeId, 0.000001m, cryptoCode);
            s.GoToInvoiceCheckout(i);

            s.GoToStore(storeId);
            i = s.CreateInvoice(storeId, null, cryptoCode);
            s.GoToInvoiceCheckout(i);

            s.GoToHome();
            s.GoToLightningSettings();
            s.Driver.SetCheckbox(By.Id("LNURLBech32Mode"), false);
            s.ClickPagePrimary();
            Assert.Contains($"{cryptoCode} Lightning settings successfully updated", s.FindAlertMessage().Text);

            // Ensure the toggles are set correctly
            s.GoToLightningSettings();
            Assert.False(s.Driver.FindElement(By.Id("LNURLBech32Mode")).Selected);

            i = s.CreateInvoice(storeId, null, cryptoCode);
            s.GoToInvoiceCheckout(i);
            lnurl = s.Driver.FindElement(By.CssSelector("#Lightning_BTC-LNURL .truncate-center")).GetAttribute("data-text");
            Assert.StartsWith("lnurlp", lnurl);
            LNURL.LNURL.Parse(lnurl, out tag);

            s.GoToHome();
            s.CreateNewStore(false);
            s.AddLightningNode(LightningConnectionType.LndREST, false);
            s.GoToLightningSettings();
            s.Driver.SetCheckbox(By.Id("LNURLEnabled"), true);
            s.ClickPagePrimary();
            Assert.Contains($"{cryptoCode} Lightning settings successfully updated", s.FindAlertMessage().Text);
            var invForPP = s.CreateInvoice(null, cryptoCode);
            s.GoToInvoiceCheckout(invForPP);
            lnurl = s.Driver.FindElement(By.CssSelector("#Lightning_BTC-LNURL .truncate-center")).GetAttribute("data-text");
            LNURL.LNURL.Parse(lnurl, out tag);

            // Check that pull payment has lightning option
            s.GoToStore(s.StoreId, StoreNavPages.PullPayments);
            s.ClickPagePrimary();
            Assert.Equal(PaymentTypes.LN.GetPaymentMethodId(cryptoCode), PaymentMethodId.Parse(Assert.Single(s.Driver.FindElements(By.CssSelector("input[name='PayoutMethods']"))).GetAttribute("value")));
            s.Driver.FindElement(By.Id("Name")).SendKeys("PP1");
            s.Driver.FindElement(By.Id("Amount")).Clear();
            s.Driver.FindElement(By.Id("Amount")).SendKeys("0.0000001");

            var currencyInput = s.Driver.FindElement(By.Id("Currency"));
            Assert.Equal("USD", currencyInput.GetAttribute("value"));
            currencyInput.Clear();
            currencyInput.SendKeys("BTC");

            s.ClickPagePrimary();
            s.Driver.FindElement(By.LinkText("View")).Click();
            s.Driver.SwitchTo().Window(s.Driver.WindowHandles.Last());
            var pullPaymentId = s.Driver.Url.Split('/').Last();

            s.Driver.FindElement(By.Id("Destination")).SendKeys(lnurl);
            s.Driver.FindElement(By.Id("ClaimedAmount")).Clear();
            s.Driver.FindElement(By.Id("ClaimedAmount")).SendKeys("0.0000001" + Keys.Enter);
            s.FindAlertMessage();

            s.Driver.Close();
            s.Driver.SwitchTo().Window(s.Driver.WindowHandles.First());
            s.GoToStore(s.StoreId, StoreNavPages.PullPayments);
            var payouts = s.Driver.FindElements(By.ClassName("pp-payout"));
            payouts[0].Click();
            s.Driver.FindElement(By.Id("BTC-LN-view")).Click();
            Assert.NotEmpty(s.Driver.FindElements(By.ClassName("payout")));
            s.Driver.FindElement(By.ClassName("mass-action-select-all")).Click();
            s.Driver.FindElement(By.Id($"{PayoutState.AwaitingApproval}-approve-pay")).Click();

            Assert.Contains(lnurl, s.Driver.PageSource);

            s.Driver.FindElement(By.Id("pay-invoices-form")).Submit();

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
        [Trait("Selenium", "Selenium")]
        [Trait("Lightning", "Lightning")]
        public async Task CanUseLNAddress()
        {
            using var s = CreateSeleniumTester();
            s.Server.DeleteStore = false;
            s.Server.ActivateLightning();
            await s.StartAsync();
            await s.Server.EnsureChannelsSetup();
            s.RegisterNewUser(true);
            //ln address tests
            s.CreateNewStore();
            //ensure ln address is not available as Lightning is not enable
            s.Driver.AssertElementNotFound(By.Id("menu-item-LightningAddress"));

            s.AddLightningNode(LightningConnectionType.LndREST, false);

            s.Driver.FindElement(By.Id("menu-item-LightningAddress")).Click();

            s.Driver.ToggleCollapse("AddAddress");
            var lnaddress1 = Guid.NewGuid().ToString();
            s.Driver.FindElement(By.Id("Add_Username")).SendKeys(lnaddress1);
            s.Driver.FindElement(By.CssSelector("button[value='add']")).Click();
            s.FindAlertMessage(StatusMessageModel.StatusSeverity.Success);

            s.Driver.ToggleCollapse("AddAddress");

            var lnaddress2 = "EUR" + Guid.NewGuid();
            s.Driver.FindElement(By.Id("Add_Username")).SendKeys(lnaddress2);
            lnaddress2 = lnaddress2.ToLowerInvariant();

            s.Driver.ToggleCollapse("AdvancedSettings");
            s.Driver.FindElement(By.Id("Add_CurrencyCode")).SendKeys("EUR");
            s.Driver.FindElement(By.Id("Add_Min")).SendKeys("2");
            s.Driver.FindElement(By.Id("Add_Max")).SendKeys("10");
            s.Driver.FindElement(By.Id("Add_InvoiceMetadata")).SendKeys("{\"test\":\"lol\"}");
            s.Driver.FindElement(By.CssSelector("button[value='add']")).Click();
            s.FindAlertMessage(StatusMessageModel.StatusSeverity.Success);

            var addresses = s.Driver.FindElements(By.ClassName("lightning-address-value"));
            Assert.Equal(2, addresses.Count);
            var callbacks = new List<Uri>();
            foreach (IWebElement webElement in addresses)
            {
                var value = webElement.GetAttribute("value");
                //cannot test this directly as https is not supported on our e2e tests
                // var request = await LNURL.LNURL.FetchPayRequestViaInternetIdentifier(value, new HttpClient());

                var lnurl = new Uri(LNURL.LNURL.ExtractUriFromInternetIdentifier(value).ToString()
                    .Replace("https", "http"));
                var request = (LNURL.LNURLPayRequest)await LNURL.LNURL.FetchInformation(lnurl, new HttpClient());
                var m = request.ParsedMetadata.ToDictionary(o => o.Key, o => o.Value);
                switch (value)
                {
                    case { } v when v.StartsWith(lnaddress2):
                        Assert.StartsWith(lnaddress2 + "@", m["text/identifier"]);
                        lnaddress2 = m["text/identifier"];
                        Assert.Equal(2, request.MinSendable.ToDecimal(LightMoneyUnit.Satoshi));
                        Assert.Equal(10, request.MaxSendable.ToDecimal(LightMoneyUnit.Satoshi));
                        callbacks.Add(request.Callback);
                        break;

                    case { } v when v.StartsWith(lnaddress1):
                        Assert.StartsWith(lnaddress1 + "@", m["text/identifier"]);
                        lnaddress1 = m["text/identifier"];
                        Assert.Equal(1, request.MinSendable.ToDecimal(LightMoneyUnit.Satoshi));
                        Assert.Equal(6.12m, request.MaxSendable.ToDecimal(LightMoneyUnit.BTC));
                        callbacks.Add(request.Callback);
                        break;
                    default:
                        Assert.Fail("Should have matched");
                        break;
                }
            }
            var repo = s.Server.PayTester.GetService<InvoiceRepository>();

            var invoices = await repo.GetInvoices(new InvoiceQuery() { StoreId = new[] { s.StoreId } });
            // Resolving a ln address shouldn't create any btcpay invoice.
            // This must be done because some NOST clients resolve ln addresses preemptively without user interaction
            Assert.Empty(invoices);

            // Calling the callbacks should create the invoices
            foreach (var callback in callbacks)
            {
                using var r = await s.Server.PayTester.HttpClient.GetAsync(callback);
                await r.Content.ReadAsStringAsync();
            }
            invoices = await repo.GetInvoices(new InvoiceQuery() { StoreId = new[] { s.StoreId } });
            Assert.Equal(2, invoices.Length);
            foreach (var i in invoices)
            {
                var prompt = i.GetPaymentPrompt(PaymentTypes.LNURL.GetPaymentMethodId("BTC"));
                var handlers = s.Server.PayTester.GetService<PaymentMethodHandlerDictionary>();
                var details = (LNURLPayPaymentMethodDetails)handlers.ParsePaymentPromptDetails(prompt);
                Assert.Contains(
                    details.ConsumedLightningAddress,
                    new[] { lnaddress1, lnaddress2 });

                if (details.ConsumedLightningAddress == lnaddress2)
                {
                    Assert.Equal("lol", i.Metadata.AdditionalData["test"].Value<string>());
                }
            }

            var lnUsername = lnaddress1.Split('@')[0];


            LNURLPayRequest req;
            using (var resp = await s.Server.PayTester.HttpClient.GetAsync($"/.well-known/lnurlp/{lnUsername}"))
            {
                var str = await resp.Content.ReadAsStringAsync();
                req = JsonConvert.DeserializeObject<LNURLPayRequest>(str);
                Assert.Contains(req.ParsedMetadata, m => m.Key == "text/identifier" && m.Value == lnaddress1);
                Assert.Contains(req.ParsedMetadata, m => m.Key == "text/plain" && m.Value.StartsWith("Paid to"));
                Assert.NotNull(req.Callback);
                Assert.Equal(new LightMoney(1000), req.MinSendable);
                Assert.Equal(LightMoney.FromUnit(6.12m, LightMoneyUnit.BTC), req.MaxSendable);
            }
            lnUsername = lnaddress2.Split('@')[0];
            using (var resp = await s.Server.PayTester.HttpClient.GetAsync($"/.well-known/lnurlp/{lnUsername}"))
            {
                var str = await resp.Content.ReadAsStringAsync();
                req = JsonConvert.DeserializeObject<LNURLPayRequest>(str);
                Assert.Equal(new LightMoney(2000), req.MinSendable);
                Assert.Equal(new LightMoney(10_000), req.MaxSendable);
            }
            // Check if we can get the same payrequest through the callback
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
            s.GoToInvoices();
            var source = s.Driver.PageSource;
            Assert.Contains(lnUsername, source);
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
