using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Lightning;
using BTCPayServer.Lightning.CLightning;
using BTCPayServer.Payments;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Wallets;
using BTCPayServer.Views.Manage;
using BTCPayServer.Views.Server;
using BTCPayServer.Views.Stores;
using BTCPayServer.Views.Wallets;
using LNURL;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBitcoin.Payment;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.Extensions;
using OpenQA.Selenium.Support.UI;
using Renci.SshNet.Security.Cryptography;
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
        public async Task CanNavigateServerSettings()
        {
            using var s = CreateSeleniumTester();
            await s.StartAsync();
            s.RegisterNewUser(true);
            s.Driver.FindElement(By.Id("Nav-ServerSettings")).Click();
            s.Driver.AssertNoError();
            s.ClickOnAllSectionLinks();
            s.Driver.FindElement(By.Id("Nav-ServerSettings")).Click();
            s.Driver.FindElement(By.LinkText("Services")).Click();

            TestLogs.LogInformation("Let's check if we can access the logs");
            s.Driver.FindElement(By.LinkText("Logs")).Click();
            s.Driver.FindElement(By.PartialLinkText(".log")).Click();
            Assert.Contains("Starting listening NBXplorer", s.Driver.PageSource);
            s.Driver.Quit();
        }
        
        [Fact(Timeout = TestTimeout)]
        public async Task CanUseCPFP()
        {
            using var s = CreateSeleniumTester();
            await s.StartAsync();
            s.RegisterNewUser(true);
            s.CreateNewStore();
            s.GenerateWallet(isHotWallet: true);
            await s.FundStoreWallet();
            for (int i = 0; i < 3; i++)
            {
                s.CreateInvoice();
                s.GoToInvoiceCheckout();
                s.PayInvoice();
                s.GoToInvoices(s.StoreId);
            }
            // Let's CPFP from the invoices page
            s.Driver.SetCheckbox(By.Id("selectAllCheckbox"), true);
            s.Driver.FindElement(By.Id("ActionsDropdownToggle")).Click();
            s.Driver.FindElement(By.Id("BumpFee")).Click();
            s.Driver.FindElement(By.Id("BroadcastTransaction")).Click();
            s.FindAlertMessage();
            Assert.Contains($"/stores/{s.StoreId}/invoices", s.Driver.Url);

            // CPFP again should fail because all invoices got bumped
            s.GoToInvoices();
            s.Driver.SetCheckbox(By.Id("selectAllCheckbox"), true);
            s.Driver.FindElement(By.Id("ActionsDropdownToggle")).Click();
            s.Driver.FindElement(By.Id("BumpFee")).Click();
            var err = s.FindAlertMessage(StatusMessageModel.StatusSeverity.Error);
            Assert.Contains("any UTXO available", err.Text);
            Assert.Contains($"/stores/{s.StoreId}/invoices", s.Driver.Url);

            // But we should be able to bump from the wallet's page
            s.GoToWallet(navPages: WalletsNavPages.Transactions);
            s.Driver.SetCheckbox(By.Id("selectAllCheckbox"), true);
            s.Driver.FindElement(By.Id("ActionsDropdownToggle")).Click();
            s.Driver.FindElement(By.Id("BumpFee")).Click();
            s.Driver.FindElement(By.Id("BroadcastTransaction")).Click();
            s.FindAlertMessage();
            Assert.Contains($"/wallets/{s.WalletId}", s.Driver.Url);
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Lightning", "Lightning")]
        public async Task CanUseLndSeedBackup()
        {
            using var s = CreateSeleniumTester();
            s.Server.ActivateLightning();
            await s.StartAsync();
            s.RegisterNewUser(true);
            s.Driver.FindElement(By.Id("Nav-ServerSettings")).Click();
            s.Driver.AssertNoError();
            s.Driver.FindElement(By.LinkText("Services")).Click();

            TestLogs.LogInformation("Let's if we can access LND's seed");
            Assert.Contains("server/services/lndseedbackup/BTC", s.Driver.PageSource);
            s.Driver.Navigate().GoToUrl(s.Link("/server/services/lndseedbackup/BTC"));
            s.Driver.FindElement(By.Id("details")).Click();
            var seedEl = s.Driver.FindElement(By.Id("Seed"));
            Assert.True(seedEl.Displayed);
            Assert.Contains("about over million", seedEl.Text, StringComparison.OrdinalIgnoreCase);
            var passEl = s.Driver.FindElement(By.Id("WalletPassword"));
            Assert.True(passEl.Displayed);
            Assert.Contains(passEl.Text, "hellorockstar", StringComparison.OrdinalIgnoreCase);
            s.Driver.FindElement(By.Id("delete")).Click();
            s.Driver.WaitForElement(By.Id("ConfirmInput")).SendKeys("DELETE");
            s.Driver.FindElement(By.Id("ConfirmContinue")).Click();
            s.FindAlertMessage();
            seedEl = s.Driver.FindElement(By.Id("Seed"));
            Assert.Contains("Seed removed", seedEl.Text, StringComparison.OrdinalIgnoreCase);
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Selenium", "Selenium")]
        public async Task CanChangeUserMail()
        {
            using var s = CreateSeleniumTester();
            await s.StartAsync();

            var tester = s.Server;
            var u1 = tester.NewAccount();
            await u1.GrantAccessAsync();
            await u1.MakeAdmin(false);

            var u2 = tester.NewAccount();
            await u2.GrantAccessAsync();
            await u2.MakeAdmin(false);

            s.GoToLogin();
            s.LogIn(u1.RegisterDetails.Email, u1.RegisterDetails.Password);
            s.GoToProfile();
            s.Driver.FindElement(By.Id("Email")).Clear();
            s.Driver.FindElement(By.Id("Email")).SendKeys(u2.RegisterDetails.Email);
            s.Driver.FindElement(By.Id("save")).Click();

            Assert.Contains("The email address is already in use with an other account.",
                s.FindAlertMessage(StatusMessageModel.StatusSeverity.Error).Text);

            s.GoToProfile();
            s.Driver.FindElement(By.Id("Email")).Clear();
            var changedEmail = Guid.NewGuid() + "@lol.com";
            s.Driver.FindElement(By.Id("Email")).SendKeys(changedEmail);
            s.Driver.FindElement(By.Id("save")).Click();
            s.FindAlertMessage();

            var manager = tester.PayTester.GetService<UserManager<ApplicationUser>>();
            Assert.NotNull(await manager.FindByNameAsync(changedEmail));
            Assert.NotNull(await manager.FindByEmailAsync(changedEmail));
        }

        [Fact(Timeout = TestTimeout)]
        public async Task NewUserLogin()
        {
            using var s = CreateSeleniumTester();
            await s.StartAsync();
            //Register & Log Out
            var email = s.RegisterNewUser();
            s.Logout();
            s.Driver.AssertNoError();
            Assert.Contains("/login", s.Driver.Url);

            s.GoToUrl("/account");
            Assert.Contains("ReturnUrl=%2Faccount", s.Driver.Url);

            // We should be redirected to login
            //Same User Can Log Back In
            s.Driver.FindElement(By.Id("Email")).SendKeys(email);
            s.Driver.FindElement(By.Id("Password")).SendKeys("123456");
            s.Driver.FindElement(By.Id("LoginButton")).Click();

            // We should be redirected to invoice
            Assert.EndsWith("/account", s.Driver.Url);

            // Should not be able to reach server settings
            s.GoToUrl("/server/users");
            Assert.Contains("ReturnUrl=%2Fserver%2Fusers", s.Driver.Url);
            s.GoToHome();

            //Change Password & Log Out
            s.GoToProfile(ManageNavPages.ChangePassword);
            s.Driver.FindElement(By.Id("OldPassword")).SendKeys("123456");
            s.Driver.FindElement(By.Id("NewPassword")).SendKeys("abc???");
            s.Driver.FindElement(By.Id("ConfirmPassword")).SendKeys("abc???");
            s.Driver.FindElement(By.Id("UpdatePassword")).Click();
            s.Logout();
            s.Driver.AssertNoError();

            //Log In With New Password
            s.Driver.FindElement(By.Id("Email")).SendKeys(email);
            s.Driver.FindElement(By.Id("Password")).SendKeys("abc???");
            s.Driver.FindElement(By.Id("LoginButton")).Click();

            s.GoToProfile();
            s.ClickOnAllSectionLinks();

            //let's test invite link
            s.Logout();
            s.GoToRegister();
            s.RegisterNewUser(true);
            s.GoToServer(ServerNavPages.Users);
            s.Driver.FindElement(By.Id("CreateUser")).Click();

            var usr = RandomUtils.GetUInt256().ToString().Substring(64 - 20) + "@a.com";
            s.Driver.FindElement(By.Id("Email")).SendKeys(usr);
            s.Driver.FindElement(By.Id("Save")).Click();
            var url = s.FindAlertMessage().FindElement(By.TagName("a")).Text;

            s.Logout();
            s.Driver.Navigate().GoToUrl(url);
            Assert.Equal("hidden", s.Driver.FindElement(By.Id("Email")).GetAttribute("type"));
            Assert.Equal(usr, s.Driver.FindElement(By.Id("Email")).GetAttribute("value"));

            s.Driver.FindElement(By.Id("Password")).SendKeys("123456");
            s.Driver.FindElement(By.Id("ConfirmPassword")).SendKeys("123456");
            s.Driver.FindElement(By.Id("SetPassword")).Click();
            s.FindAlertMessage();
            s.Driver.FindElement(By.Id("Email")).SendKeys(usr);
            s.Driver.FindElement(By.Id("Password")).SendKeys("123456");
            s.Driver.FindElement(By.Id("LoginButton")).Click();

            // We should be logged in now
            s.Driver.FindElement(By.Id("mainNav"));

            //let's test delete user quickly while we're at it 
            s.GoToProfile();
            s.Driver.FindElement(By.Id("delete-user")).Click();
            s.Driver.WaitForElement(By.Id("ConfirmInput")).SendKeys("DELETE");
            s.Driver.FindElement(By.Id("ConfirmContinue")).Click();

            Assert.Contains("/login", s.Driver.Url);
        }

        [Fact(Timeout = TestTimeout)]
        public async Task CanUseSSHService()
        {
            using var s = CreateSeleniumTester();
            await s.StartAsync();
            var settings = s.Server.PayTester.GetService<SettingsRepository>();
            var policies = await settings.GetSettingAsync<PoliciesSettings>() ?? new PoliciesSettings();
            policies.DisableSSHService = false;
            await settings.UpdateSetting(policies);
            s.RegisterNewUser(isAdmin: true);
            s.Driver.Navigate().GoToUrl(s.Link("/server/services"));
            Assert.Contains("server/services/ssh", s.Driver.PageSource);
            using (var client = await s.Server.PayTester.GetService<Configuration.BTCPayServerOptions>().SSHSettings
                .ConnectAsync())
            {
                var result = await client.RunBash("echo hello");
                Assert.Equal(string.Empty, result.Error);
                Assert.Equal("hello\n", result.Output);
                Assert.Equal(0, result.ExitStatus);
            }

            s.Driver.Navigate().GoToUrl(s.Link("/server/services/ssh"));
            s.Driver.AssertNoError();
            s.Driver.FindElement(By.Id("SSHKeyFileContent")).Clear();
            s.Driver.FindElement(By.Id("SSHKeyFileContent")).SendKeys("tes't\r\ntest2");
            s.Driver.FindElement(By.Id("submit")).Click();
            s.Driver.AssertNoError();

            var text = s.Driver.FindElement(By.Id("SSHKeyFileContent")).Text;
            // Browser replace \n to \r\n, so it is hard to compare exactly what we want
            Assert.Contains("tes't", text);
            Assert.Contains("test2", text);
            Assert.True(s.Driver.PageSource.Contains("authorized_keys has been updated",
                StringComparison.OrdinalIgnoreCase));

            s.Driver.FindElement(By.Id("SSHKeyFileContent")).Clear();
            s.Driver.FindElement(By.Id("submit")).Click();

            text = s.Driver.FindElement(By.Id("SSHKeyFileContent")).Text;
            Assert.DoesNotContain("test2", text);

            // Let's try to disable it now
            s.Driver.FindElement(By.Id("disable")).Click();
            s.Driver.WaitForElement(By.Id("ConfirmInput")).SendKeys("DISABLE");
            s.Driver.FindElement(By.Id("ConfirmContinue")).Click();
            s.Driver.Navigate().GoToUrl(s.Link("/server/services/ssh"));
            Assert.True(s.Driver.PageSource.Contains("404 - Page not found", StringComparison.OrdinalIgnoreCase));

            policies = await settings.GetSettingAsync<PoliciesSettings>();
            Assert.True(policies.DisableSSHService);

            policies.DisableSSHService = false;
            await settings.UpdateSetting(policies);
        }

        [Fact(Timeout = TestTimeout)]
        public async Task CanSetupEmailServer()
        {
            using var s = CreateSeleniumTester();
            await s.StartAsync();
            s.RegisterNewUser(true);
            s.Driver.Navigate().GoToUrl(s.Link("/server/emails"));
            if (s.Driver.PageSource.Contains("Configured"))
            {
                s.Driver.FindElement(By.Id("ResetPassword")).Submit();
                s.FindAlertMessage();
            }
            CanSetupEmailCore(s);
            s.CreateNewStore();
            s.GoToUrl($"stores/{s.StoreId}/emails");
            CanSetupEmailCore(s);
        }

        [Fact(Timeout = TestTimeout)]
        public async Task CanUseDynamicDns()
        {
            using var s = CreateSeleniumTester();
            await s.StartAsync();
            s.RegisterNewUser(isAdmin: true);
            s.Driver.Navigate().GoToUrl(s.Link("/server/services"));
            Assert.Contains("Dynamic DNS", s.Driver.PageSource);

            s.Driver.Navigate().GoToUrl(s.Link("/server/services/dynamic-dns"));
            s.Driver.AssertNoError();
            if (s.Driver.PageSource.Contains("pouet.hello.com"))
            {
                // Cleanup old test run
                s.Driver.Navigate().GoToUrl(s.Link("/server/services/dynamic-dns/pouet.hello.com/delete"));
                s.Driver.FindElement(By.Id("ConfirmContinue")).Click();
            }

            s.Driver.FindElement(By.Id("AddDynamicDNS")).Click();
            s.Driver.AssertNoError();
            // We will just cheat for test purposes by only querying the server
            s.Driver.FindElement(By.Id("ServiceUrl")).SendKeys(s.Link("/"));
            s.Driver.FindElement(By.Id("Settings_Hostname")).SendKeys("pouet.hello.com");
            s.Driver.FindElement(By.Id("Settings_Login")).SendKeys("MyLog");
            s.Driver.FindElement(By.Id("Settings_Password")).SendKeys("MyLog" + Keys.Enter);
            s.Driver.AssertNoError();
            Assert.Contains("The Dynamic DNS has been successfully queried", s.Driver.PageSource);
            Assert.EndsWith("/server/services/dynamic-dns", s.Driver.Url);

            // Try to do the same thing should fail (hostname already exists)
            s.Driver.FindElement(By.Id("AddDynamicDNS")).Click();
            s.Driver.AssertNoError();
            s.Driver.FindElement(By.Id("ServiceUrl")).SendKeys(s.Link("/"));
            s.Driver.FindElement(By.Id("Settings_Hostname")).SendKeys("pouet.hello.com");
            s.Driver.FindElement(By.Id("Settings_Login")).SendKeys("MyLog");
            s.Driver.FindElement(By.Id("Settings_Password")).SendKeys("MyLog" + Keys.Enter);
            s.Driver.AssertNoError();
            Assert.Contains("This hostname already exists", s.Driver.PageSource);

            // Delete it
            s.Driver.Navigate().GoToUrl(s.Link("/server/services/dynamic-dns"));
            Assert.Contains("/server/services/dynamic-dns/pouet.hello.com/delete", s.Driver.PageSource);
            s.Driver.Navigate().GoToUrl(s.Link("/server/services/dynamic-dns/pouet.hello.com/delete"));
            s.Driver.FindElement(By.Id("ConfirmContinue")).Click();
            s.Driver.AssertNoError();

            Assert.DoesNotContain("/server/services/dynamic-dns/pouet.hello.com/delete", s.Driver.PageSource);
        }
        
        [Fact(Timeout = TestTimeout)]
        public async Task CanCreateInvoiceInUI()
        {
            using var s = CreateSeleniumTester();
            await s.StartAsync();
            s.RegisterNewUser(true);
            s.CreateNewStore();
            s.GoToInvoices();
            s.Driver.FindElement(By.Id("CreateNewInvoice")).Click();
            // Should give us an error message if we try to create an invoice before adding a wallet
            Assert.Contains("To create an invoice, you need to", s.Driver.PageSource);
            s.AddDerivationScheme();
            s.GoToInvoices();
            s.CreateInvoice();
            s.Driver.FindElement(By.ClassName("changeInvoiceStateToggle")).Click();
            s.Driver.FindElements(By.ClassName("changeInvoiceState"))[0].Click();
            TestUtils.Eventually(() => Assert.Contains("Invalid (marked)", s.Driver.PageSource));
            s.Driver.Navigate().Refresh();

            s.Driver.FindElement(By.ClassName("changeInvoiceStateToggle")).Click();
            s.Driver.FindElements(By.ClassName("changeInvoiceState"))[0].Click();
            TestUtils.Eventually(() => Assert.Contains("Settled (marked)", s.Driver.PageSource));

            s.Driver.FindElement(By.ClassName("invoice-details-link")).Click();
            Assert.Contains("Settled (marked)", s.Driver.PageSource);

            s.Driver.FindElement(By.ClassName("changeInvoiceStateToggle")).Click();
            s.Driver.FindElements(By.ClassName("changeInvoiceState"))[0].Click();
            TestUtils.Eventually(() => Assert.Contains("Invalid (marked)", s.Driver.PageSource));
            s.Driver.Navigate().Refresh();

            s.Driver.FindElement(By.ClassName("changeInvoiceStateToggle")).Click();
            s.Driver.FindElements(By.ClassName("changeInvoiceState"))[0].Click();
            TestUtils.Eventually(() => Assert.Contains("Settled (marked)", s.Driver.PageSource));
        }

        [Fact(Timeout = TestTimeout)]
        public async Task CanSetupStoreViaGuide()
        {
            using var s = CreateSeleniumTester();
            await s.StartAsync();
            s.RegisterNewUser();
            s.GoToUrl("/");

            Assert.False(s.Driver.PageSource.Contains("id=\"StoreSelectorDropdown\""), "Store selector dropdown should not be present");
            Assert.True(s.Driver.PageSource.Contains("id=\"StoreSelectorCreate\""), "Store selector create button should be present");

            // verify steps for store creation are displayed correctly
            s.Driver.FindElement(By.Id("SetupGuide-Store")).Click();
            Assert.Contains("/stores/create", s.Driver.Url);

            (_, string storeId) = s.CreateNewStore();
            
            // should redirect to store
            s.GoToUrl("/");

            Assert.Contains($"/stores/{storeId}", s.Driver.Url);
            Assert.True(s.Driver.PageSource.Contains("id=\"StoreSelectorDropdown\""), "Store selector dropdown should be present");
            Assert.True(s.Driver.PageSource.Contains("id=\"SetupGuide\""), "Store setup guide should be present");
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Lightning", "Lightning")]
        public async Task CanCreateStores()
        {
            using var s = CreateSeleniumTester();
            s.Server.ActivateLightning();
            await s.StartAsync();
            var alice = s.RegisterNewUser(true);
            (string storeName, string storeId) = s.CreateNewStore();
            var storeUrl = $"/stores/{storeId}";

            s.GoToStore();
            Assert.Contains(storeName, s.Driver.PageSource);
            Assert.DoesNotContain("id=\"Dashboard\"", s.Driver.PageSource);

            // verify steps for wallet setup are displayed correctly
            s.GoToStore(StoreNavPages.Dashboard);
            Assert.True(s.Driver.FindElement(By.Id("SetupGuide-StoreDone")).Displayed);
            Assert.True(s.Driver.FindElement(By.Id("SetupGuide-Wallet")).Displayed);
            Assert.True(s.Driver.FindElement(By.Id("SetupGuide-Lightning")).Displayed);

            // setup onchain wallet
            s.Driver.FindElement(By.Id("SetupGuide-Wallet")).Click();
            s.AddDerivationScheme();
            s.Driver.AssertNoError();

            s.GoToStore(StoreNavPages.Dashboard);
            Assert.DoesNotContain("id=\"SetupGuide\"", s.Driver.PageSource);
            Assert.True(s.Driver.FindElement(By.Id("Dashboard")).Displayed);

            // setup offchain wallet
            s.Driver.FindElement(By.Id("StoreNav-LightningBTC")).Click();
            s.AddLightningNode();
            s.Driver.AssertNoError();
            var successAlert = s.FindAlertMessage();
            Assert.Contains("BTC Lightning node updated.", successAlert.Text);

            s.ClickOnAllSectionLinks();

            s.GoToInvoices();
            Assert.Contains("There are no invoices matching your criteria.", s.Driver.PageSource);
            var invoiceId = s.CreateInvoice();
            s.FindAlertMessage();
            s.Driver.FindElement(By.ClassName("invoice-details-link")).Click();
            var invoiceUrl = s.Driver.Url;

            //let's test archiving an invoice
            Assert.DoesNotContain("Archived", s.Driver.FindElement(By.Id("btn-archive-toggle")).Text);
            s.Driver.FindElement(By.Id("btn-archive-toggle")).Click();
            Assert.Contains("Unarchive", s.Driver.FindElement(By.Id("btn-archive-toggle")).Text);

            //check that it no longer appears in list
            s.GoToInvoices();
            Assert.DoesNotContain(invoiceId, s.Driver.PageSource);

            //ok, let's unarchive and see that it shows again
            s.Driver.Navigate().GoToUrl(invoiceUrl);
            s.Driver.FindElement(By.Id("btn-archive-toggle")).Click();
            s.FindAlertMessage();
            Assert.DoesNotContain("Unarchive", s.Driver.FindElement(By.Id("btn-archive-toggle")).Text);
            s.GoToInvoices();
            Assert.Contains(invoiceId, s.Driver.PageSource);

            // archive via list
            s.Driver.FindElement(By.CssSelector($".selector[value=\"{invoiceId}\"]")).Click();
            s.Driver.FindElement(By.Id("ActionsDropdownToggle")).Click();
            s.Driver.FindElement(By.Id("ActionsDropdownArchive")).Click();
            Assert.Contains("1 invoice archived", s.FindAlertMessage().Text);
            Assert.DoesNotContain(invoiceId, s.Driver.PageSource);

            // unarchive via list
            s.Driver.FindElement(By.Id("SearchOptionsToggle")).Click();
            s.Driver.FindElement(By.Id("SearchOptionsIncludeArchived")).Click();
            Assert.Contains(invoiceId, s.Driver.PageSource);
            s.Driver.FindElement(By.CssSelector($".selector[value=\"{invoiceId}\"]")).Click();
            s.Driver.FindElement(By.Id("ActionsDropdownToggle")).Click();
            s.Driver.FindElement(By.Id("ActionsDropdownUnarchive")).Click();
            Assert.Contains("1 invoice unarchived", s.FindAlertMessage().Text);
            Assert.Contains(invoiceId, s.Driver.PageSource);

            // When logout out we should not be able to access store and invoice details
            s.Logout();
            s.GoToUrl(storeUrl);
            Assert.Contains("ReturnUrl", s.Driver.Url);
            s.Driver.Navigate().GoToUrl(invoiceUrl);
            Assert.Contains("ReturnUrl", s.Driver.Url);
            s.GoToRegister();

            // When logged in as different user we should not be able to access store and invoice details
            var bob = s.RegisterNewUser();
            s.GoToUrl(storeUrl);
            Assert.Contains("ReturnUrl", s.Driver.Url);
            s.Driver.Navigate().GoToUrl(invoiceUrl);
            s.AssertAccessDenied();
            s.GoToHome();
            s.Logout();

            // Let's add Bob as a guest to alice's store
            s.LogIn(alice);
            s.GoToUrl(storeUrl + "/users");
            s.Driver.FindElement(By.Id("Email")).SendKeys(bob + Keys.Enter);
            Assert.Contains("User added successfully", s.Driver.PageSource);
            s.Logout();

            // Bob should not have access to store, but should have access to invoice
            s.LogIn(bob);
            s.GoToUrl(storeUrl);
            Assert.Contains("ReturnUrl", s.Driver.Url);
            s.GoToUrl(invoiceUrl);
            s.Driver.AssertNoError();

            s.Logout();
            s.LogIn(alice);

            // Check if we can enable the payment button
            s.GoToStore(StoreNavPages.PayButton);
            s.Driver.FindElement(By.Id("enable-pay-button")).Click();
            s.Driver.FindElement(By.Id("disable-pay-button")).Click();
            s.FindAlertMessage();
            s.GoToStore(StoreNavPages.General);
            Assert.False(s.Driver.FindElement(By.Id("AnyoneCanCreateInvoice")).Selected);
            s.Driver.SetCheckbox(By.Id("AnyoneCanCreateInvoice"), true);
            s.Driver.FindElement(By.Id("Save")).Click();
            s.FindAlertMessage();
            Assert.True(s.Driver.FindElement(By.Id("AnyoneCanCreateInvoice")).Selected);

            // Alice should be able to delete the store
            s.GoToStore(StoreNavPages.General);
            s.Driver.FindElement(By.Id("DeleteStore")).Click();
            s.Driver.WaitForElement(By.Id("ConfirmInput")).SendKeys("DELETE");
            s.Driver.FindElement(By.Id("ConfirmContinue")).Click();
            s.GoToUrl(storeUrl);
            Assert.Contains("ReturnUrl", s.Driver.Url);
        }

        [Fact(Timeout = TestTimeout)]
        public async Task CanUsePairing()
        {
            using var s = CreateSeleniumTester();
            await s.StartAsync();
            s.Driver.Navigate().GoToUrl(s.Link("/api-access-request"));
            Assert.Contains("ReturnUrl", s.Driver.Url);
            s.GoToRegister();
            s.RegisterNewUser();
            s.CreateNewStore();
            s.AddDerivationScheme();
            
            s.GoToStore(StoreNavPages.Tokens);
            s.Driver.FindElement(By.Id("CreateNewToken")).Click();
            s.Driver.FindElement(By.Id("RequestPairing")).Click();
            var pairingCode = AssertUrlHasPairingCode(s);

            s.Driver.FindElement(By.Id("ApprovePairing")).Click();
            s.FindAlertMessage();
            Assert.Contains(pairingCode, s.Driver.PageSource);

            var client = new NBitpayClient.Bitpay(new Key(), s.ServerUri);
            await client.AuthorizeClient(new NBitpayClient.PairingCode(pairingCode));
            await client.CreateInvoiceAsync(
                new NBitpayClient.Invoice() { Price = 1.000000012m, Currency = "USD", FullNotifications = true },
                NBitpayClient.Facade.Merchant);

            client = new NBitpayClient.Bitpay(new Key(), s.ServerUri);

            var code = await client.RequestClientAuthorizationAsync("hehe", NBitpayClient.Facade.Merchant);
            s.Driver.Navigate().GoToUrl(code.CreateLink(s.ServerUri));
            s.Driver.FindElement(By.Id("ApprovePairing")).Click();

            await client.CreateInvoiceAsync(
                new NBitpayClient.Invoice() { Price = 1.000000012m, Currency = "USD", FullNotifications = true },
                NBitpayClient.Facade.Merchant);

            s.Driver.Navigate().GoToUrl(s.Link("/api-tokens"));
            s.Driver.FindElement(By.Id("RequestPairing")).Click();
            s.Driver.FindElement(By.Id("ApprovePairing")).Click();
            AssertUrlHasPairingCode(s);
        }

        [Fact(Timeout = TestTimeout)]
        public async Task CanCreateAppPoS()
        {
            using var s = CreateSeleniumTester(newDb: true);
            await s.StartAsync();
            var userId = s.RegisterNewUser(true);
            s.CreateNewStore();
            s.Driver.FindElement(By.Id("StoreNav-CreateApp")).Click();
            s.Driver.FindElement(By.Name("AppName")).SendKeys("PoS" + Guid.NewGuid());
            s.Driver.FindElement(By.Id("SelectedAppType")).SendKeys("Point of Sale");
            s.Driver.FindElement(By.Id("Create")).Click();
            Assert.Contains("App successfully created", s.FindAlertMessage().Text);
            
            s.Driver.FindElement(By.CssSelector(".template-item:nth-of-type(1) .btn-primary")).Click();
            s.Driver.FindElement(By.Id("BuyButtonText")).SendKeys("Take my money");
            s.Driver.FindElement(By.Id("SaveItemChanges")).Click();
            s.Driver.FindElement(By.Id("ToggleRawEditor")).Click();

            var template = s.Driver.FindElement(By.Id("Template")).GetAttribute("value");
            Assert.Contains("buyButtonText: Take my money", template);

            s.Driver.FindElement(By.Id("DefaultView")).SendKeys("Item list and cart");
            s.Driver.FindElement(By.Id("SaveSettings")).Click();
            Assert.Contains("App updated", s.FindAlertMessage().Text);

            s.Driver.FindElement(By.Id("ViewApp")).Click();
            var windows = s.Driver.WindowHandles;
            Assert.Equal(2, windows.Count);
            s.Driver.SwitchTo().Window(windows[1]);

            var posBaseUrl = s.Driver.Url.Replace("/cart", "");
            Assert.True(s.Driver.PageSource.Contains("Tea shop"), "Unable to create PoS");
            Assert.True(s.Driver.PageSource.Contains("Cart"), "PoS not showing correct default view");
            Assert.True(s.Driver.PageSource.Contains("Take my money"), "PoS not showing correct default view");

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
            s.Driver.FindElement(By.Id("SaveButton")).Click();
            s.FindAlertMessage();

            s.Logout();
            s.GoToLogin();
            s.LogIn(userId);
            // Make sure after login, we are not redirected to the PoS
            Assert.DoesNotContain("Tea shop", s.Driver.PageSource);
            var prevUrl = s.Driver.Url;
            
            // We are only if explicitly going to /
            s.GoToUrl("/");
            Assert.Contains("Tea shop", s.Driver.PageSource);
            s.Driver.Navigate().GoToUrl(new Uri(prevUrl, UriKind.Absolute));

            // Let's check with domain mapping as well.
            s.GoToServer(ServerNavPages.Policies);
            s.Driver.ScrollTo(By.Id("RootAppId"));
            select = new SelectElement(s.Driver.FindElement(By.Id("RootAppId")));
            select.SelectByText("None", true);
            s.Driver.FindElement(By.Id("SaveButton")).Click();
            s.Driver.ScrollTo(By.Id("RootAppId"));
            s.Driver.FindElement(By.Id("AddDomainButton")).Click();
            s.Driver.FindElement(By.Id("DomainToAppMapping_0__Domain")).SendKeys(new Uri(s.Driver.Url, UriKind.Absolute).DnsSafeHost);
            select = new SelectElement(s.Driver.FindElement(By.Id("DomainToAppMapping_0__AppId")));
            select.SelectByText("Point of", true);
            s.Driver.FindElement(By.Id("SaveButton")).Click();

            s.Logout();
            s.LogIn(userId);
            // Make sure after login, we are not redirected to the PoS
            Assert.DoesNotContain("Tea shop", s.Driver.PageSource);
            
            // We are only if explicitly going to /
            s.GoToUrl("/");
            Assert.Contains("Tea shop", s.Driver.PageSource);
        }

        [Fact(Timeout = TestTimeout)]
        public async Task CanCreateCrowdfundingApp()
        {
            using var s = CreateSeleniumTester();
            await s.StartAsync();
            s.RegisterNewUser();
            s.CreateNewStore();
            s.AddDerivationScheme();

            s.Driver.FindElement(By.Id("StoreNav-CreateApp")).Click();
            s.Driver.FindElement(By.Name("AppName")).SendKeys("CF" + Guid.NewGuid());
            s.Driver.FindElement(By.Id("SelectedAppType")).SendKeys("Crowdfund");
            s.Driver.FindElement(By.Id("Create")).Click();
            Assert.Contains("App successfully created", s.FindAlertMessage().Text);
            
            s.Driver.FindElement(By.Id("Title")).SendKeys("Kukkstarter");
            s.Driver.FindElement(By.CssSelector("div.note-editable.card-block")).SendKeys("1BTC = 1BTC");
            s.Driver.FindElement(By.Id("TargetCurrency")).Clear();
            s.Driver.FindElement(By.Id("TargetCurrency")).SendKeys("JPY");
            s.Driver.FindElement(By.Id("TargetAmount")).SendKeys("700");
            s.Driver.FindElement(By.Id("SaveSettings")).Click();
            Assert.Contains("App updated", s.FindAlertMessage().Text);

            s.Driver.FindElement(By.Id("ViewApp")).Click();
            var windows = s.Driver.WindowHandles;
            Assert.Equal(2, windows.Count);
            s.Driver.SwitchTo().Window(windows[1]);

            Assert.Equal("currently active!",
                s.Driver.FindElement(By.CssSelector("[data-test='time-state']")).Text);
            
            s.Driver.Close();
            s.Driver.SwitchTo().Window(windows[0]);
        }

        [Fact(Timeout = TestTimeout)]
        public async Task CanCreatePayRequest()
        {
            using var s = CreateSeleniumTester();
            await s.StartAsync();
            s.RegisterNewUser();
            s.CreateNewStore();
            s.AddDerivationScheme();

            s.Driver.FindElement(By.Id("StoreNav-PaymentRequests")).Click();
            s.Driver.FindElement(By.Id("CreatePaymentRequest")).Click();
            s.Driver.FindElement(By.Id("Title")).SendKeys("Pay123");
            s.Driver.FindElement(By.Id("Amount")).SendKeys("700");

            var currencyInput = s.Driver.FindElement(By.Id("Currency"));
            Assert.Equal("USD", currencyInput.GetAttribute("value"));
            currencyInput.Clear();
            currencyInput.SendKeys("BTC");
            
            s.Driver.FindElement(By.Id("SaveButton")).Click();
            s.Driver.FindElement(By.XPath($"//a[starts-with(@id, 'Edit-')]")).Click();
            s.Driver.FindElement(By.Id("ViewPaymentRequest")).Click();
            s.Driver.SwitchTo().Window(s.Driver.WindowHandles.Last());
            Assert.Equal("Amount due", s.Driver.FindElement(By.CssSelector("[data-test='amount-due-title']")).Text);
            Assert.Equal("Pay Invoice",
                s.Driver.FindElement(By.CssSelector("[data-test='pay-button']")).Text.Trim());

            // expire
            s.Driver.SwitchTo().Window(s.Driver.WindowHandles.First());
            s.Driver.ExecuteJavaScript("document.getElementById('ExpiryDate').value = '2021-01-21T21:00:00.000Z'");
            s.Driver.FindElement(By.Id("SaveButton")).Click();
            s.Driver.FindElement(By.XPath($"//a[starts-with(@id, 'Edit-')]")).Click();
            s.Driver.SwitchTo().Window(s.Driver.WindowHandles.Last());
            s.Driver.Navigate().Refresh();
            Assert.Equal("Expired", s.Driver.WaitForElement(By.CssSelector("[data-test='status']")).Text);

            // unexpire
            s.Driver.SwitchTo().Window(s.Driver.WindowHandles.First());
            s.Driver.FindElement(By.Id("ClearExpiryDate")).Click();
            s.Driver.FindElement(By.Id("SaveButton")).Click();
            s.Driver.FindElement(By.XPath($"//a[starts-with(@id, 'Edit-')]")).Click();
            s.Driver.SwitchTo().Window(s.Driver.WindowHandles.Last());
            s.Driver.Navigate().Refresh();
            s.Driver.AssertElementNotFound(By.CssSelector("[data-test='status']"));
            Assert.Equal("Pay Invoice",
                s.Driver.FindElement(By.CssSelector("[data-test='pay-button']")).Text.Trim());
            s.Driver.SwitchTo().Window(s.Driver.WindowHandles.First());
            
            // archive (from details page)
            var payReqId = s.Driver.Url.Split('/').Last();
            s.Driver.FindElement(By.Id("ArchivePaymentRequest")).Click();
            Assert.Contains("The payment request has been archived", s.FindAlertMessage().Text);
            Assert.DoesNotContain("Pay123", s.Driver.PageSource);
            s.Driver.FindElement(By.Id("SearchDropdownToggle")).Click();
            s.Driver.FindElement(By.Id("SearchIncludeArchived")).Click();
            Assert.Contains("Pay123", s.Driver.PageSource);
            
            // unarchive (from list)
            s.Driver.FindElement(By.Id($"ToggleArchival-{payReqId}")).Click();
            Assert.Contains("The payment request has been unarchived", s.FindAlertMessage().Text);
            Assert.Contains("Pay123", s.Driver.PageSource);
        }

        [Fact(Timeout = TestTimeout)]
        public async Task CanUseCoinSelection()
        {
            using var s = CreateSeleniumTester();
            await s.StartAsync();
            s.RegisterNewUser(true);
            (_, string storeId) = s.CreateNewStore();
            s.GenerateWallet("BTC", "", false, true);
            var walletId = new WalletId(storeId, "BTC");
            s.GoToWallet(walletId, WalletsNavPages.Receive);
            s.Driver.FindElement(By.Id("generateButton")).Click();
            var addressStr = s.Driver.FindElement(By.Id("address")).GetAttribute("value");
            var address = BitcoinAddress.Create(addressStr,
                ((BTCPayNetwork)s.Server.NetworkProvider.GetNetwork("BTC")).NBitcoinNetwork);
            await s.Server.ExplorerNode.GenerateAsync(1);
            for (int i = 0; i < 6; i++)
            {
                await s.Server.ExplorerNode.SendToAddressAsync(address, Money.Coins(1.0m));
            }

            var targetTx = await s.Server.ExplorerNode.SendToAddressAsync(address, Money.Coins(1.2m));
            var tx = await s.Server.ExplorerNode.GetRawTransactionAsync(targetTx);
            var spentOutpoint = new OutPoint(targetTx,
                tx.Outputs.FindIndex(txout => txout.Value == Money.Coins(1.2m)));
            await TestUtils.EventuallyAsync(async () =>
            {
                var store = await s.Server.PayTester.StoreRepository.FindStore(storeId);
                var x = store.GetSupportedPaymentMethods(s.Server.NetworkProvider)
                    .OfType<DerivationSchemeSettings>()
                    .Single(settings => settings.PaymentId.CryptoCode == walletId.CryptoCode);
                var wallet = s.Server.PayTester.GetService<BTCPayWalletProvider>().GetWallet(walletId.CryptoCode);
                wallet.InvalidateCache(x.AccountDerivation);
                Assert.Contains(
                    await wallet.GetUnspentCoins(x.AccountDerivation),
                    coin => coin.OutPoint == spentOutpoint);
            });
            await s.Server.ExplorerNode.GenerateAsync(1);
            s.GoToWallet(walletId);
            s.Driver.WaitForAndClick(By.Id("toggleInputSelection"));
            s.Driver.WaitForElement(By.Id(spentOutpoint.ToString()));
            Assert.Equal("true",
                s.Driver.FindElement(By.Name("InputSelection")).GetAttribute("value").ToLowerInvariant());
            var el = s.Driver.FindElement(By.Id(spentOutpoint.ToString()));
            s.Driver.FindElement(By.Id(spentOutpoint.ToString())).Click();
            var inputSelectionSelect = s.Driver.FindElement(By.Name("SelectedInputs"));
            Assert.Single(inputSelectionSelect.FindElements(By.CssSelector("[selected]")));

            var bob = new Key().PubKey.Hash.GetAddress(Network.RegTest);
            SetTransactionOutput(s, 0, bob, 0.3m);
            s.Driver.FindElement(By.Id("SignTransaction")).Click();
            s.Driver.FindElement(By.CssSelector("button[value=broadcast]")).Click();
            var happyElement = s.FindAlertMessage();
            var happyText = happyElement.Text;
            var txid = Regex.Match(happyText, @"\((.*)\)").Groups[1].Value;

            tx = await s.Server.ExplorerNode.GetRawTransactionAsync(new uint256(txid));
            Assert.Single(tx.Inputs);
            Assert.Equal(spentOutpoint, tx.Inputs[0].PrevOut);
        }

        [Fact(Timeout = TestTimeout)]
        public async Task CanUseWebhooks()
        {
            using var s = CreateSeleniumTester();
            await s.StartAsync();
            s.RegisterNewUser(true);
            s.CreateNewStore();
            s.GoToStore(StoreNavPages.Webhooks);

            TestLogs.LogInformation("Let's create two webhooks");
            for (var i = 0; i < 2; i++)
            {
                s.Driver.FindElement(By.Id("CreateWebhook")).Click();
                s.Driver.FindElement(By.Name("PayloadUrl")).SendKeys($"http://127.0.0.1/callback{i}");
                new SelectElement(s.Driver.FindElement(By.Id("Everything"))).SelectByValue("false");
                s.Driver.FindElement(By.Id("InvoiceCreated")).Click();
                s.Driver.FindElement(By.Id("InvoiceProcessing")).Click();
                s.Driver.FindElement(By.Name("add")).Click();
            }

            TestLogs.LogInformation("Let's delete one of them");
            var deletes = s.Driver.FindElements(By.LinkText("Delete"));
            Assert.Equal(2, deletes.Count);
            deletes[0].Click();
            s.Driver.WaitForElement(By.Id("ConfirmInput")).SendKeys("DELETE");
            s.Driver.FindElement(By.Id("ConfirmContinue")).Click();
            deletes = s.Driver.FindElements(By.LinkText("Delete"));
            Assert.Single(deletes);
            s.FindAlertMessage();

            TestLogs.LogInformation("Let's try to update one of them");
            s.Driver.FindElement(By.LinkText("Modify")).Click();

            using FakeServer server = new FakeServer();
            await server.Start();
            s.Driver.FindElement(By.Name("PayloadUrl")).Clear();
            s.Driver.FindElement(By.Name("PayloadUrl")).SendKeys(server.ServerUri.AbsoluteUri);
            s.Driver.FindElement(By.Name("Secret")).Clear();
            s.Driver.FindElement(By.Name("Secret")).SendKeys("HelloWorld");
            s.Driver.FindElement(By.Name("update")).Click();
            s.FindAlertMessage();
            s.Driver.FindElement(By.LinkText("Modify")).Click();
            foreach (var value in Enum.GetValues(typeof(WebhookEventType)))
            {
                // Here we make sure we did not forget an event type in the list
                // However, maybe some event should not appear here because not at the store level.
                // Fix as needed.
                Assert.Contains($"value=\"{value}\"", s.Driver.PageSource);
            }

            // This one should be checked
            Assert.Contains($"value=\"InvoiceProcessing\" checked", s.Driver.PageSource);
            Assert.Contains($"value=\"InvoiceCreated\" checked", s.Driver.PageSource);
            // This one never been checked
            Assert.DoesNotContain($"value=\"InvoiceReceivedPayment\" checked", s.Driver.PageSource);

            s.Driver.FindElement(By.Name("update")).Click();
            s.FindAlertMessage();
            Assert.Contains(server.ServerUri.AbsoluteUri, s.Driver.PageSource);

            TestLogs.LogInformation("Let's see if we can generate an event");
            s.GoToStore();
            s.AddDerivationScheme();
            s.CreateInvoice();
            var request = await server.GetNextRequest();
            var headers = request.Request.Headers;
            var actualSig = headers["BTCPay-Sig"].First();
            var bytes = await request.Request.Body.ReadBytesAsync((int)headers.ContentLength.Value);
            var expectedSig =
                $"sha256={Encoders.Hex.EncodeData(new HMACSHA256(Encoding.UTF8.GetBytes("HelloWorld")).ComputeHash(bytes))}";
            Assert.Equal(expectedSig, actualSig);
            request.Response.StatusCode = 200;
            server.Done();

            TestLogs.LogInformation("Let's make a failed event");
            s.CreateInvoice();
            request = await server.GetNextRequest();
            request.Response.StatusCode = 404;
            server.Done();

            // The delivery is done asynchronously, so small wait here
            await Task.Delay(500);
            s.GoToStore(StoreNavPages.Webhooks);
            s.Driver.FindElement(By.LinkText("Modify")).Click();
            var elements = s.Driver.FindElements(By.ClassName("redeliver"));
            // One worked, one failed
            s.Driver.FindElement(By.ClassName("fa-times"));
            s.Driver.FindElement(By.ClassName("fa-check"));
            elements[0].Click();

            s.FindAlertMessage();
            request = await server.GetNextRequest();
            request.Response.StatusCode = 404;
            server.Done();

            TestLogs.LogInformation("Can we browse the json content?");
            CanBrowseContent(s);

            s.GoToInvoices();
            s.Driver.FindElement(By.LinkText("Details")).Click();
            CanBrowseContent(s);
            var element = s.Driver.FindElement(By.ClassName("redeliver"));
            element.Click();

            s.FindAlertMessage();
            request = await server.GetNextRequest();
            request.Response.StatusCode = 404;
            server.Done();

            TestLogs.LogInformation("Let's see if we can delete store with some webhooks inside");
            s.GoToStore();
            s.Driver.FindElement(By.Id("DeleteStore")).Click();
            s.Driver.WaitForElement(By.Id("ConfirmInput")).SendKeys("DELETE");
            s.Driver.FindElement(By.Id("ConfirmContinue")).Click();
            s.FindAlertMessage();
        }

        [Fact(Timeout = TestTimeout)]
        public async Task CanImportMnemonic()
        {
            using var s = CreateSeleniumTester();
            await s.StartAsync();
            s.RegisterNewUser(true);
            foreach (var isHotwallet in new[] { false, true })
            {
                var cryptoCode = "BTC";
                s.CreateNewStore();
                s.GenerateWallet(cryptoCode, "melody lizard phrase voice unique car opinion merge degree evil swift cargo", isHotWallet: isHotwallet);
                s.GoToWalletSettings(cryptoCode);
                if (isHotwallet)
                    Assert.Contains("View seed", s.Driver.PageSource);
                else
                    Assert.DoesNotContain("View seed", s.Driver.PageSource);
            }
        }

        [Fact(Timeout = TestTimeout)]
        public async Task CanManageWallet()
        {
            using var s = CreateSeleniumTester();
            await s.StartAsync();
            s.RegisterNewUser(true);
            (_, string storeId) = s.CreateNewStore();
            const string cryptoCode = "BTC";

            // In this test, we try to spend from a manual seed. We import the xpub 49'/0'/0',
            // then try to use the seed to sign the transaction
            s.GenerateWallet(cryptoCode, "", true);

            //let's test quickly the receive wallet page
            s.Driver.FindElement(By.Id($"StoreNav-Wallet{cryptoCode}")).Click();
            s.Driver.FindElement(By.Id("WalletNav-Send")).Click();
            s.Driver.FindElement(By.Id("SignTransaction")).Click();

            //you cannot use the Sign with NBX option without saving private keys when generating the wallet.
            Assert.DoesNotContain("nbx-seed", s.Driver.PageSource);

            s.Driver.FindElement(By.Id("WalletNav-Receive")).Click();
            //generate a receiving address
            s.Driver.FindElement(By.CssSelector("button[value=generate-new-address]")).Click();
            Assert.True(s.Driver.FindElement(By.CssSelector("#address-tab .qr-container")).Displayed);
            var receiveAddr = s.Driver.FindElement(By.Id("address")).GetAttribute("value");
            //unreserve
            s.Driver.FindElement(By.CssSelector("button[value=unreserve-current-address]")).Click();
            //generate it again, should be the same one as before as nothing got used in the meantime
            s.Driver.FindElement(By.CssSelector("button[value=generate-new-address]")).Click();
            Assert.True(s.Driver.FindElement(By.CssSelector("#address-tab .qr-container")).Displayed);
            Assert.Equal(receiveAddr, s.Driver.FindElement(By.Id("address")).GetAttribute("value"));

            //send money to addr and ensure it changed
            var sess = await s.Server.ExplorerClient.CreateWebsocketNotificationSessionAsync();
            await sess.ListenAllTrackedSourceAsync();
            var nextEvent = sess.NextEventAsync();
            await s.Server.ExplorerNode.SendToAddressAsync(BitcoinAddress.Create(receiveAddr, Network.RegTest),
                Money.Parse("0.1"));
            await nextEvent;
            await Task.Delay(200);
            s.Driver.Navigate().Refresh();
            s.Driver.FindElement(By.CssSelector("button[value=generate-new-address]")).Click();
            Assert.NotEqual(receiveAddr, s.Driver.FindElement(By.Id("address")).GetAttribute("value"));
            receiveAddr = s.Driver.FindElement(By.Id("address")).GetAttribute("value");

            //change the wallet and ensure old address is not there and generating a new one does not result in the prev one
            s.GenerateWallet(cryptoCode, "", true);
            s.GoToWallet(null, WalletsNavPages.Receive);
            s.Driver.FindElement(By.CssSelector("button[value=generate-new-address]")).Click();

            Assert.NotEqual(receiveAddr, s.Driver.FindElement(By.Id("address")).GetAttribute("value"));

            var invoiceId = s.CreateInvoice(storeId);
            var invoice = await s.Server.PayTester.InvoiceRepository.GetInvoice(invoiceId);
            var address = invoice.EntityToDTO().Addresses["BTC"];

            //wallet should have been imported to bitcoin core wallet in watch only mode.
            var result =
                await s.Server.ExplorerNode.GetAddressInfoAsync(BitcoinAddress.Create(address, Network.RegTest));
            Assert.True(result.IsWatchOnly);
            s.GoToStore(storeId);
            var mnemonic = s.GenerateWallet(cryptoCode, "", true, true);

            //lets import and save private keys
            var root = mnemonic.DeriveExtKey();
            invoiceId = s.CreateInvoice(storeId);
            invoice = await s.Server.PayTester.InvoiceRepository.GetInvoice(invoiceId);
            address = invoice.EntityToDTO().Addresses["BTC"];
            result = await s.Server.ExplorerNode.GetAddressInfoAsync(
                BitcoinAddress.Create(address, Network.RegTest));
            //spendable from bitcoin core wallet!
            Assert.False(result.IsWatchOnly);
            var tx = s.Server.ExplorerNode.SendToAddress(BitcoinAddress.Create(address, Network.RegTest),
                Money.Coins(3.0m));
            await s.Server.ExplorerNode.GenerateAsync(1);

            s.GoToStore(storeId);
            s.Driver.FindElement(By.Id($"StoreNav-Wallet{cryptoCode}")).Click();
            s.ClickOnAllSectionLinks();

            // Make sure wallet info is correct
            s.GoToWalletSettings(cryptoCode);
            Assert.Contains(mnemonic.DeriveExtKey().GetPublicKey().GetHDFingerPrint().ToString(),
                s.Driver.FindElement(By.Id("AccountKeys_0__MasterFingerprint")).GetAttribute("value"));
            Assert.Contains("m/84'/1'/0'",
                s.Driver.FindElement(By.Id("AccountKeys_0__AccountKeyPath")).GetAttribute("value"));

            // Make sure we can rescan, because we are admin!
            s.Driver.FindElement(By.Id("ActionsDropdownToggle")).Click();
            s.Driver.FindElement(By.Id("Rescan")).Click();
            Assert.Contains("The batch size make sure", s.Driver.PageSource);

            // Check the tx sent earlier arrived
            s.Driver.FindElement(By.Id($"StoreNav-Wallet{cryptoCode}")).Click();

            var walletTransactionLink = s.Driver.Url;
            Assert.Contains(tx.ToString(), s.Driver.PageSource);

            // Send to bob
            s.Driver.FindElement(By.Id("WalletNav-Send")).Click();
            var bob = new Key().PubKey.Hash.GetAddress(Network.RegTest);
            SetTransactionOutput(s, 0, bob, 1);
            s.Driver.FindElement(By.Id("SignTransaction")).Click();

            // Broadcast
            Assert.Contains(bob.ToString(), s.Driver.PageSource);
            Assert.Contains("1.00000000", s.Driver.PageSource);
            s.Driver.FindElement(By.CssSelector("button[value=broadcast]")).Click();
            Assert.Equal(walletTransactionLink, s.Driver.Url);

            s.Driver.FindElement(By.Id($"StoreNav-Wallet{cryptoCode}")).Click();
            s.Driver.FindElement(By.Id("WalletNav-Send")).Click();

            var jack = new Key().PubKey.Hash.GetAddress(Network.RegTest);
            SetTransactionOutput(s, 0, jack, 0.01m);
            s.Driver.FindElement(By.Id("SignTransaction")).Click();

            Assert.Contains(jack.ToString(), s.Driver.PageSource);
            Assert.Contains("0.01000000", s.Driver.PageSource);
            Assert.EndsWith("psbt/ready", s.Driver.Url);
            s.Driver.FindElement(By.CssSelector("button[value=broadcast]")).Click();
            Assert.Equal(walletTransactionLink, s.Driver.Url);

            var bip21 = invoice.EntityToDTO().CryptoInfo.First().PaymentUrls.BIP21;
            //let's make bip21 more interesting
            bip21 += "&label=Solid Snake&message=Snake? Snake? SNAAAAKE!";
            var parsedBip21 = new BitcoinUrlBuilder(bip21, Network.RegTest);
            s.Driver.FindElement(By.Id($"StoreNav-Wallet{cryptoCode}")).Click();
            s.Driver.FindElement(By.Id("WalletNav-Send")).Click();
            s.Driver.FindElement(By.Id("bip21parse")).Click();
            s.Driver.SwitchTo().Alert().SendKeys(bip21);
            s.Driver.SwitchTo().Alert().Accept();
            s.FindAlertMessage(StatusMessageModel.StatusSeverity.Info);
            Assert.Equal(parsedBip21.Amount.ToString(false),
                s.Driver.FindElement(By.Id("Outputs_0__Amount")).GetAttribute("value"));
            Assert.Equal(parsedBip21.Address.ToString(),
                s.Driver.FindElement(By.Id("Outputs_0__DestinationAddress")).GetAttribute("value"));

            s.GoToWalletSettings(cryptoCode);
            var settingsUrl = s.Driver.Url;
            s.Driver.FindElement(By.Id("ActionsDropdownToggle")).Click();
            s.Driver.FindElement(By.Id("ViewSeed")).Click();

            // Seed backup page
            var recoveryPhrase = s.Driver.FindElements(By.Id("RecoveryPhrase")).First()
                .GetAttribute("data-mnemonic");
            Assert.Equal(mnemonic.ToString(), recoveryPhrase);
            Assert.Contains("The recovery phrase will also be stored on the server as a hot wallet.",
                s.Driver.PageSource);

            // No confirmation, just a link to return to the wallet
            Assert.Empty(s.Driver.FindElements(By.Id("confirm")));
            s.Driver.FindElement(By.Id("proceed")).Click();
            Assert.Equal(settingsUrl, s.Driver.Url);
            
            // Transactions list contains export and action, ensure functions are present.
            s.Driver.FindElement(By.Id($"StoreNav-Wallet{cryptoCode}")).Click();
            s.Driver.FindElement(By.Id("ActionsDropdownToggle")).Click();
            s.Driver.FindElement(By.Id("BumpFee"));
            
            // JSON export
            s.Driver.FindElement(By.Id("ExportDropdownToggle")).Click();
            s.Driver.FindElement(By.Id("ExportJSON")).Click();
            Thread.Sleep(1000);
            s.Driver.SwitchTo().Window(s.Driver.WindowHandles.Last());
            Assert.Contains(s.WalletId.ToString(), s.Driver.Url);
            Assert.EndsWith("export?format=json", s.Driver.Url);
            Assert.Contains("\"Amount\": \"3.00000000\"", s.Driver.PageSource);
            s.Driver.SwitchTo().Window(s.Driver.WindowHandles.First());
            
            // CSV export
            s.Driver.FindElement(By.Id("ExportDropdownToggle")).Click();
            s.Driver.FindElement(By.Id("ExportCSV")).Click();
        }

        [Fact(Timeout = TestTimeout)]
        public async Task CanImportWallet()
        {
            using var s = CreateSeleniumTester();
            await s.StartAsync();
            s.RegisterNewUser(true);
            s.CreateNewStore();
            const string cryptoCode = "BTC";
            var mnemonic = s.GenerateWallet(cryptoCode, "click chunk owner kingdom faint steak safe evidence bicycle repeat bulb wheel");

            // Make sure wallet info is correct
            s.GoToWalletSettings(cryptoCode);
            Assert.Contains(mnemonic.DeriveExtKey().GetPublicKey().GetHDFingerPrint().ToString(),
                s.Driver.FindElement(By.Id("AccountKeys_0__MasterFingerprint")).GetAttribute("value"));
            Assert.Contains("m/84'/1'/0'",
                s.Driver.FindElement(By.Id("AccountKeys_0__AccountKeyPath")).GetAttribute("value"));
                
            // Transactions list is empty 
            s.Driver.FindElement(By.Id($"StoreNav-Wallet{cryptoCode}")).Click();
            Assert.Contains("There are no transactions yet.", s.Driver.PageSource);
            s.Driver.AssertElementNotFound(By.Id("ExportDropdownToggle"));
            s.Driver.AssertElementNotFound(By.Id("ActionsDropdownToggle"));
        }

        [Fact]
        [Trait("Selenium", "Selenium")]
        [Trait("Lightning", "Lightning")]
        public async Task CanUsePullPaymentsViaUI()
        {
            using var s = CreateSeleniumTester();
            s.Server.ActivateLightning(LightningConnectionType.LndREST);
            await s.StartAsync();
            await s.Server.EnsureChannelsSetup();
            s.RegisterNewUser(true);
            s.CreateNewStore();
            s.GenerateWallet("BTC", "", true, true);

            await s.Server.ExplorerNode.GenerateAsync(1);
            await s.FundStoreWallet(denomination: 50.0m);
            s.GoToStore(s.StoreId, StoreNavPages.PullPayments);
            s.Driver.FindElement(By.Id("NewPullPayment")).Click();
            s.Driver.FindElement(By.Id("Name")).SendKeys("PP1");
            s.Driver.FindElement(By.Id("Amount")).Clear();
            s.Driver.FindElement(By.Id("Amount")).SendKeys("99.0");
            s.Driver.FindElement(By.Id("Create")).Click();
            s.Driver.FindElement(By.LinkText("View")).Click();

            s.GoToStore(s.StoreId, StoreNavPages.PullPayments);

            s.Driver.FindElement(By.Id("NewPullPayment")).Click();
            s.Driver.FindElement(By.Id("Name")).SendKeys("PP2");
            s.Driver.FindElement(By.Id("Amount")).Clear();
            s.Driver.FindElement(By.Id("Amount")).SendKeys("100.0");
            s.Driver.FindElement(By.Id("Create")).Click();

            // This should select the first View, ie, the last one PP2
            s.Driver.FindElement(By.LinkText("View")).Click();
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
            s.Driver.FindElement(By.Id($"{PayoutState.AwaitingApproval}-selectAllCheckbox")).Click();
            s.Driver.FindElement(By.Id($"{PayoutState.AwaitingApproval}-actions")).Click();
            s.Driver.FindElement(By.Id($"{PayoutState.AwaitingApproval}-approve-pay")).Click();

            s.Driver.FindElement(By.Id("SignTransaction")).Click();
            s.Driver.FindElement(By.CssSelector("button[value=broadcast]")).Click();
            s.FindAlertMessage();

            TestUtils.Eventually(() =>
            {
                s.Driver.Navigate().Refresh();
                Assert.Contains("badge transactionLabel", s.Driver.PageSource);
            });
            Assert.Equal("payout", s.Driver.FindElement(By.ClassName("transactionLabel")).Text);

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
            s.Driver.FindElement(By.Id("NotificationsHandle")).Click();
            s.Driver.FindElement(By.CssSelector("#notificationsForm button")).Click();

            var newStore = s.CreateNewStore();
            s.GenerateWallet("BTC", "", true, true);
            s.GoToStore(s.StoreId, StoreNavPages.PullPayments);

            s.Driver.FindElement(By.Id("NewPullPayment")).Click();
            s.Driver.FindElement(By.Id("Name")).SendKeys("External Test");
            s.Driver.FindElement(By.Id("Amount")).Clear();
            s.Driver.FindElement(By.Id("Amount")).SendKeys("0.001");
            s.Driver.FindElement(By.Id("Currency")).Clear();
            s.Driver.FindElement(By.Id("Currency")).SendKeys("BTC");
            s.Driver.FindElement(By.Id("Create")).Click();
            s.Driver.FindElement(By.LinkText("View")).Click();

            address = await s.Server.ExplorerNode.GetNewAddressAsync();
            s.Driver.FindElement(By.Id("Destination")).SendKeys(address.ToString());
            s.Driver.FindElement(By.Id("ClaimedAmount")).SendKeys(Keys.Enter);
            s.FindAlertMessage();

            Assert.Contains(PayoutState.AwaitingApproval.GetStateString(), s.Driver.PageSource);
            s.GoToStore(s.StoreId, StoreNavPages.Payouts);
            s.Driver.FindElement(By.Id($"{PayoutState.AwaitingApproval}-view")).Click();
            s.Driver.FindElement(By.Id($"{PayoutState.AwaitingApproval}-selectAllCheckbox")).Click();
            s.Driver.FindElement(By.Id($"{PayoutState.AwaitingApproval}-actions")).Click();
            s.Driver.FindElement(By.Id($"{PayoutState.AwaitingApproval}-approve")).Click();
            s.FindAlertMessage();
            var tx = await s.Server.ExplorerNode.SendToAddressAsync(address, Money.FromUnit(0.001m, MoneyUnit.BTC));

            s.GoToStore(s.StoreId, StoreNavPages.Payouts);

            s.Driver.FindElement(By.Id($"{PayoutState.AwaitingPayment}-view")).Click();
            Assert.Contains(PayoutState.AwaitingPayment.GetStateString(), s.Driver.PageSource);
            s.Driver.FindElement(By.Id($"{PayoutState.AwaitingPayment}-selectAllCheckbox")).Click();
            s.Driver.FindElement(By.Id($"{PayoutState.AwaitingPayment}-actions")).Click();
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

            s.Driver.FindElement(By.Id("NewPullPayment")).Click();

            var paymentMethodOptions = s.Driver.FindElements(By.CssSelector("input[name='PaymentMethods']"));
            Assert.Equal(2, paymentMethodOptions.Count);

            s.Driver.FindElement(By.Id("Name")).SendKeys("Lightning Test");
            s.Driver.FindElement(By.Id("Amount")).Clear();
            s.Driver.FindElement(By.Id("Amount")).SendKeys(payoutAmount.ToString());
            s.Driver.FindElement(By.Id("Currency")).Clear();
            s.Driver.FindElement(By.Id("Currency")).SendKeys("BTC");
            s.Driver.FindElement(By.Id("Create")).Click();
            s.Driver.FindElement(By.LinkText("View")).Click();

            var bolt = (await s.Server.CustomerLightningD.CreateInvoice(
                payoutAmount,
                $"LN payout test {DateTime.UtcNow.Ticks}",
                TimeSpan.FromHours(1), CancellationToken.None)).BOLT11;
            s.Driver.FindElement(By.Id("Destination")).SendKeys(bolt);
            s.Driver.FindElement(By.Id("SelectedPaymentMethod")).Click();
            s.Driver.FindElement(By.CssSelector(
                    $"#SelectedPaymentMethod option[value={new PaymentMethodId("BTC", PaymentTypes.LightningLike)}]"))
                .Click();

            s.Driver.FindElement(By.Id("ClaimedAmount")).SendKeys(Keys.Enter);
            //we do not allow short-life bolts.
            s.FindAlertMessage(StatusMessageModel.StatusSeverity.Error);

            bolt = (await s.Server.CustomerLightningD.CreateInvoice(
                payoutAmount,
                $"LN payout test {DateTime.UtcNow.Ticks}",
                TimeSpan.FromDays(31), CancellationToken.None)).BOLT11;
            s.Driver.FindElement(By.Id("Destination")).Clear();
            s.Driver.FindElement(By.Id("Destination")).SendKeys(bolt);
            s.Driver.FindElement(By.Id("SelectedPaymentMethod")).Click();
            s.Driver.FindElement(By.CssSelector(
                    $"#SelectedPaymentMethod option[value={new PaymentMethodId("BTC", PaymentTypes.LightningLike)}]"))
                .Click();

            s.Driver.FindElement(By.Id("ClaimedAmount")).SendKeys(Keys.Enter);
            s.FindAlertMessage();

            Assert.Contains(PayoutState.AwaitingApproval.GetStateString(), s.Driver.PageSource);

            s.GoToStore(newStore.storeId, StoreNavPages.Payouts);
            s.Driver.FindElement(By.Id($"{new PaymentMethodId("BTC", PaymentTypes.LightningLike)}-view")).Click();
            s.Driver.FindElement(By.Id($"{PayoutState.AwaitingApproval}-view")).Click();
            s.Driver.FindElement(By.Id($"{PayoutState.AwaitingApproval}-selectAllCheckbox")).Click();
            s.Driver.FindElement(By.Id($"{PayoutState.AwaitingApproval}-actions")).Click();
            s.Driver.FindElement(By.Id($"{PayoutState.AwaitingApproval}-approve-pay")).Click();
            Assert.Contains(bolt, s.Driver.PageSource);
            Assert.Contains($"{payoutAmount.ToString()} BTC", s.Driver.PageSource);
            s.Driver.FindElement(By.CssSelector("#pay-invoices-form")).Submit();

            s.FindAlertMessage(StatusMessageModel.StatusSeverity.Success);
            s.GoToStore(newStore.storeId, StoreNavPages.Payouts);
            s.Driver.FindElement(By.Id($"{new PaymentMethodId("BTC", PaymentTypes.LightningLike)}-view")).Click();

            s.Driver.FindElement(By.Id($"{PayoutState.Completed}-view")).Click();
            if (!s.Driver.PageSource.Contains(bolt))
            {
                s.Driver.FindElement(By.Id($"{PayoutState.AwaitingPayment}-view")).Click();
                Assert.Contains(bolt, s.Driver.PageSource);

                s.Driver.FindElement(By.Id($"{PayoutState.AwaitingPayment}-selectAllCheckbox")).Click();
                s.Driver.FindElement(By.Id($"{PayoutState.AwaitingPayment}-actions")).Click();
                s.Driver.FindElement(By.Id($"{PayoutState.AwaitingPayment}-mark-paid")).Click();
                s.Driver.FindElement(By.Id($"{new PaymentMethodId("BTC", PaymentTypes.LightningLike)}-view")).Click();

                s.Driver.FindElement(By.Id($"{PayoutState.Completed}-view")).Click();
                Assert.Contains(bolt, s.Driver.PageSource);
            }
            
            

            //auto-approve pull payments

            s.GoToStore(StoreNavPages.PullPayments);
            s.Driver.FindElement(By.Id("NewPullPayment")).Click();
            s.Driver.FindElement(By.Id("Name")).SendKeys("PP1");
            s.Driver.SetCheckbox(By.Id("AutoApproveClaims"), true);
            s.Driver.FindElement(By.Id("Amount")).Clear();
            s.Driver.FindElement(By.Id("Amount")).SendKeys("99.0" + Keys.Enter);
            s.FindAlertMessage(StatusMessageModel.StatusSeverity.Success);
            s.Driver.FindElement(By.LinkText("View")).Click();
            address = await s.Server.ExplorerNode.GetNewAddressAsync();
            s.Driver.FindElement(By.Id("Destination")).Clear();
            s.Driver.FindElement(By.Id("Destination")).SendKeys(address.ToString());
            s.Driver.FindElement(By.Id("ClaimedAmount")).Clear();
            s.Driver.FindElement(By.Id("ClaimedAmount")).SendKeys("20" + Keys.Enter);
            s.FindAlertMessage(StatusMessageModel.StatusSeverity.Success);

            Assert.Contains(PayoutState.AwaitingPayment.GetStateString(), s.Driver.PageSource);

        }

        [Fact]
        [Trait("Selenium", "Selenium")]
        [Trait("Lightning", "Lightning")]
        public async Task CanUsePOSPrint()
        {
            using var s = CreateSeleniumTester();
            s.Server.ActivateLightning();
            await s.StartAsync();

            await s.Server.EnsureChannelsSetup();

            s.RegisterNewUser(true);
            s.CreateNewStore();
            s.GoToStore();
            s.AddLightningNode(LightningConnectionType.CLightning, false);
            s.GoToLightningSettings();
            s.Driver.SetCheckbox(By.Id("LNURLEnabled"), true);
            s.Driver.FindElement(By.Id("StoreNav-CreateApp")).Click();
            s.Driver.FindElement(By.Id("SelectedAppType")).Click();
            s.Driver.FindElement(By.CssSelector("option[value='PointOfSale']")).Click();
            s.Driver.FindElement(By.Id("AppName")).SendKeys(Guid.NewGuid().ToString());
            s.Driver.FindElement(By.Id("Create")).Click();
            TestUtils.Eventually(() => Assert.Contains("App successfully created", s.FindAlertMessage().Text));
            s.Driver.FindElement(By.Id("DefaultView")).Click();
            s.Driver.FindElement(By.CssSelector("option[value='3']")).Click();
            s.Driver.FindElement(By.Id("SaveSettings")).Click();
            Assert.Contains("App updated", s.FindAlertMessage().Text);

            s.Driver.FindElement(By.Id("ViewApp")).Click();
            var btns = s.Driver.FindElements(By.ClassName("lnurl"));
            foreach (IWebElement webElement in btns)
            {
                var choice = webElement.GetAttribute("data-choice");
                var lnurl = webElement.GetAttribute("href");
                var parsed = LNURL.LNURL.Parse(lnurl, out _);
                Assert.EndsWith(choice, parsed.ToString());
                Assert.IsType<LNURLPayRequest>(await LNURL.LNURL.FetchInformation(parsed, new HttpClient()));
            }
        }

        [Fact]
        [Trait("Selenium", "Selenium")]
        [Trait("Lightning", "Lightning")]
        public async Task CanUseLNURL()
        {
            using var s = CreateSeleniumTester();
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
            s.Driver.FindElement(By.Id("save")).Click();

            // Topup Invoice test
            var i = s.CreateInvoice(storeId, null, cryptoCode);
            s.GoToInvoiceCheckout(i);
            s.Driver.FindElement(By.Id("copy-tab")).Click();
            var lnurl = s.Driver.FindElement(By.CssSelector("input.checkoutTextbox")).GetAttribute("value");
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

            await s.Server.CustomerLightningD.Pay(lnurlResponse2.Pr);
            await TestUtils.EventuallyAsync(async () =>
            {
                var inv = await s.Server.PayTester.InvoiceRepository.GetInvoice(i);
                Assert.Equal(InvoiceStatusLegacy.Complete, inv.Status);
            });
            var greenfield = await s.AsTestAccount().CreateClient();
            var paymentMethods = await greenfield.GetInvoicePaymentMethods(s.StoreId, i);
            Assert.Single(paymentMethods, p => {
                return p.AdditionalData["providedComment"].Value<string>() == "lol2";
            });
            // Standard invoice test
            s.GoToStore(storeId);
            s.GoToLightningSettings();
            s.Driver.SetCheckbox(By.Id("LNURLStandardInvoiceEnabled"), true);
            SudoForceSaveLightningSettingsRightNowAndFast(s, cryptoCode);
            i = s.CreateInvoice(storeId, 0.0000001m, cryptoCode);
            s.GoToInvoiceCheckout(i);
            s.Driver.FindElement(By.ClassName("payment__currencies")).Click();
            // BOLT11 is also available for standard invoices
            Assert.Equal(2, s.Driver.FindElements(By.CssSelector(".vex.vex-theme-btcpay .vex-content .vexmenu li.vexmenuitem")).Count);
            s.Driver.FindElement(By.CssSelector(".vex.vex-theme-btcpay .vex-content .vexmenu li.vexmenuitem")).Click();
            s.Driver.FindElement(By.Id("copy-tab")).Click();
            lnurl = s.Driver.FindElement(By.CssSelector("input.checkoutTextbox")).GetAttribute("value");
            parsed = LNURL.LNURL.Parse(lnurl, out tag);
            fetchedReuqest = Assert.IsType<LNURLPayRequest>(await LNURL.LNURL.FetchInformation(parsed, new HttpClient()));
            Assert.Equal(0.0000001m, fetchedReuqest.MaxSendable.ToDecimal(LightMoneyUnit.BTC));
            Assert.Equal(0.0000001m, fetchedReuqest.MinSendable.ToDecimal(LightMoneyUnit.BTC));

            await Assert.ThrowsAsync<HttpRequestException>(async () =>
            {
                await fetchedReuqest.SendRequest(new LightMoney(0.0000002m, LightMoneyUnit.BTC),
                    network, new HttpClient());
            });
            await Assert.ThrowsAsync<HttpRequestException>(async () =>
            {
                await fetchedReuqest.SendRequest(new LightMoney(0.00000005m, LightMoneyUnit.BTC),
                    network, new HttpClient());
            });

            lnurlResponse = await fetchedReuqest.SendRequest(new LightMoney(0.0000001m, LightMoneyUnit.BTC),
                network, new HttpClient());
            lnurlResponse2 = await fetchedReuqest.SendRequest(new LightMoney(0.0000001m, LightMoneyUnit.BTC),
                network, new HttpClient());
            //invoice amounts do no change so the paymnet request is not regenerated
            Assert.Equal(lnurlResponse.Pr, lnurlResponse2.Pr);
            await s.Server.CustomerLightningD.Pay(lnurlResponse.Pr);
            Assert.Equal(new LightMoney(0.0000001m, LightMoneyUnit.BTC),
                lnurlResponse2.GetPaymentRequest(network).MinimumAmount);
            s.GoToHome();
            s.GoToLightningSettings();
            // LNURL is enabled and settings are expanded
            Assert.True(s.Driver.FindElement(By.Id("LNURLEnabled")).Selected);
            Assert.Contains("show", s.Driver.FindElement(By.Id("LNURLSettings")).GetAttribute("class"));
            s.Driver.SetCheckbox(By.Id("LNURLStandardInvoiceEnabled"), false);
            s.Driver.FindElement(By.Id("save")).Click();
            Assert.Contains($"{cryptoCode} Lightning settings successfully updated", s.FindAlertMessage().Text);

            i = s.CreateInvoice(storeId, 0.000001m, cryptoCode);
            s.GoToInvoiceCheckout(i);
            s.Driver.FindElement(By.ClassName("payment__currencies_noborder"));

            s.GoToStore(storeId);
            i = s.CreateInvoice(storeId, null, cryptoCode);
            s.GoToInvoiceCheckout(i);
            s.Driver.FindElement(By.ClassName("payment__currencies_noborder"));

            s.GoToHome();
            s.GoToLightningSettings();
            s.Driver.SetCheckbox(By.Id("LNURLBech32Mode"), false);
            s.Driver.SetCheckbox(By.Id("LNURLStandardInvoiceEnabled"), false);
            s.Driver.SetCheckbox(By.Id("DisableBolt11PaymentMethod"), true);
            s.Driver.FindElement(By.Id("save")).Click();
            Assert.Contains($"{cryptoCode} Lightning settings successfully updated", s.FindAlertMessage().Text);

            // Ensure the toggles are set correctly
            s.GoToLightningSettings();

            //TODO: DisableBolt11PaymentMethod is actually disabled because LNURLStandardInvoiceEnabled is disabled
            // checkboxes is not good choice here, in next release we should have multi choice instead
            Assert.False(s.Driver.FindElement(By.Id("LNURLBech32Mode")).Selected);
            Assert.False(s.Driver.FindElement(By.Id("LNURLStandardInvoiceEnabled")).Selected);

            //even though we set DisableBolt11PaymentMethod to true, logic when saving it turns it back off as otherwise no lightning option is available at all!
            Assert.False(s.Driver.FindElement(By.Id("DisableBolt11PaymentMethod")).Selected);
            // Invoice creation should fail, because it is a standard invoice with amount, but DisableBolt11PaymentMethod  = true and LNURLStandardInvoiceEnabled = false
            s.CreateInvoice(storeId, 0.0000001m, cryptoCode, "", null, expectedSeverity: StatusMessageModel.StatusSeverity.Success);

            i = s.CreateInvoice(storeId, null, cryptoCode);
            s.GoToInvoiceCheckout(i);
            s.Driver.FindElement(By.ClassName("payment__currencies_noborder"));
            s.Driver.FindElement(By.Id("copy-tab")).Click();
            lnurl = s.Driver.FindElement(By.CssSelector("input.checkoutTextbox")).GetAttribute("value");
            Assert.StartsWith("lnurlp", lnurl);
            LNURL.LNURL.Parse(lnurl, out tag);

            s.GoToHome();
            s.CreateNewStore(false);
            s.AddLightningNode(LightningConnectionType.LndREST, false);
            s.GoToLightningSettings();
            s.Driver.SetCheckbox(By.Id("LNURLEnabled"), true);
            s.Driver.SetCheckbox(By.Id("DisableBolt11PaymentMethod"), true);
            s.Driver.SetCheckbox(By.Id("LNURLStandardInvoiceEnabled"), true);
            s.Driver.FindElement(By.Id("save")).Click();
            Assert.Contains($"{cryptoCode} Lightning settings successfully updated", s.FindAlertMessage().Text);
            var invForPP = s.CreateInvoice(0.0000001m, cryptoCode);
            s.GoToInvoiceCheckout(invForPP);
            s.Driver.FindElement(By.Id("copy-tab")).Click();
            lnurl = s.Driver.FindElement(By.CssSelector("input.checkoutTextbox")).GetAttribute("value");
            parsed = LNURL.LNURL.Parse(lnurl, out tag);

            // Check that pull payment has lightning option
            s.GoToStore(s.StoreId, StoreNavPages.PullPayments);
            s.Driver.FindElement(By.Id("NewPullPayment")).Click();
            Assert.Equal(new PaymentMethodId(cryptoCode, PaymentTypes.LightningLike), PaymentMethodId.Parse(Assert.Single(s.Driver.FindElements(By.CssSelector("input[name='PaymentMethods']"))).GetAttribute("value")));
            s.Driver.FindElement(By.Id("Name")).SendKeys("PP1");
            s.Driver.FindElement(By.Id("Amount")).Clear();
            s.Driver.FindElement(By.Id("Amount")).SendKeys("0.0000001");
            
            var currencyInput = s.Driver.FindElement(By.Id("Currency"));
            Assert.Equal("USD", currencyInput.GetAttribute("value"));
            currencyInput.Clear();
            currencyInput.SendKeys("BTC");
            
            s.Driver.FindElement(By.Id("Create")).Click();
            s.Driver.FindElement(By.LinkText("View")).Click();
            s.Driver.FindElement(By.Id("Destination")).SendKeys(lnurl);

            var pullPaymentId = s.Driver.Url.Split('/').Last();
            s.Driver.FindElement(By.Id("ClaimedAmount")).Clear();
            s.Driver.FindElement(By.Id("ClaimedAmount")).SendKeys("0.0000001" + Keys.Enter);
            s.FindAlertMessage();

            s.GoToStore(s.StoreId, StoreNavPages.PullPayments);
            var payouts = s.Driver.FindElements(By.ClassName("pp-payout"));
            payouts[0].Click();
            s.Driver.FindElement(By.Id("BTC_LightningLike-view")).Click();
            Assert.NotEmpty(s.Driver.FindElements(By.ClassName("payout")));
            s.Driver.FindElement(By.Id($"{PayoutState.AwaitingApproval}-selectAllCheckbox")).Click();
            s.Driver.FindElement(By.Id($"{PayoutState.AwaitingApproval}-actions")).Click();
            s.Driver.FindElement(By.Id($"{PayoutState.AwaitingApproval}-approve-pay")).Click();

            Assert.Contains(lnurl, s.Driver.PageSource);

            s.Driver.FindElement(By.Id("pay-invoices-form")).Submit();

            await TestUtils.EventuallyAsync(async () =>
            {
                var inv = await s.Server.PayTester.InvoiceRepository.GetInvoice(invForPP);
                Assert.Equal(InvoiceStatusLegacy.Complete, inv.Status);

                await using var ctx = s.Server.PayTester.GetService<ApplicationDbContextFactory>().CreateContext();
                var payoutsData = await ctx.Payouts.Where(p => p.PullPaymentDataId == pullPaymentId).ToListAsync();
                Assert.True(payoutsData.All(p => p.State == PayoutState.Completed));
            });

            //lnurl crowdfund support
            s.GoToStore();
            s.Driver.FindElement(By.Id("StoreNav-CreateApp")).Click();
            s.Driver.FindElement(By.Name("AppName")).SendKeys("CF" + Guid.NewGuid());
            s.Driver.FindElement(By.Id("SelectedAppType")).SendKeys("Crowdfund");
            s.Driver.FindElement(By.Id("Create")).Click();
            Assert.Contains("App successfully created", s.FindAlertMessage().Text);
            
            s.Driver.FindElement(By.Id("Title")).SendKeys("Kukkstarter");
            s.Driver.FindElement(By.CssSelector("div.note-editable.card-block")).SendKeys("1BTC = 1BTC");
            s.Driver.FindElement(By.Id("SaveSettings")).Click();
            Assert.Contains("App updated", s.FindAlertMessage().Text);
            
            s.Driver.FindElement(By.Id("ViewApp")).Click();
            
            var windows = s.Driver.WindowHandles;
            Assert.Equal(2, windows.Count);
            s.Driver.SwitchTo().Window(windows[1]);

            s.Driver.FindElement(By.CssSelector("#crowdfund-body-contribution-container .perk")).Click();
            s.Driver.FindElement(By.PartialLinkText("LNURL")).Click();
            lnurl = s.Driver.FindElement(By.ClassName("lnurl"))
                .GetAttribute("href");
            
            LNURL.LNURL.Parse(lnurl, out tag);
            
            s.Driver.Close();
            s.Driver.SwitchTo().Window(windows[0]);
        }

        [Fact]
        [Trait("Selenium", "Selenium")]
        [Trait("Lightning", "Lightning")]
        public async Task CanUseLNAddress()
        {
            using var s = CreateSeleniumTester();
            s.Server.ActivateLightning();
            await s.StartAsync();
            await s.Server.EnsureChannelsSetup();
            s.RegisterNewUser(true);
            //ln address tests
            s.CreateNewStore();
            //ensure ln address is not available as Lightning is not enable
            s.Driver.AssertElementNotFound(By.Id("StoreNav-LightningAddress"));

            s.AddLightningNode(LightningConnectionType.LndREST, false);

            s.Driver.FindElement(By.Id("StoreNav-LightningAddress")).Click();

            s.Driver.ToggleCollapse("AddAddress");
            var lnaddress1 = Guid.NewGuid().ToString();
            s.Driver.FindElement(By.Id("Add_Username")).SendKeys(lnaddress1);
            s.Driver.FindElement(By.CssSelector("button[value='add']")).Click();
            s.FindAlertMessage(StatusMessageModel.StatusSeverity.Success);

            s.Driver.ToggleCollapse("AddAddress");
            var lnaddress2 = "EUR" + Guid.NewGuid().ToString();
            s.Driver.FindElement(By.Id("Add_Username")).SendKeys(lnaddress2);

            s.Driver.ToggleCollapse("AdvancedSettings");
            s.Driver.FindElement(By.Id("Add_CurrencyCode")).SendKeys("EUR");
            s.Driver.FindElement(By.Id("Add_Min")).SendKeys("2");
            s.Driver.FindElement(By.Id("Add_Max")).SendKeys("10");
            s.Driver.FindElement(By.CssSelector("button[value='add']")).Click();
            s.FindAlertMessage(StatusMessageModel.StatusSeverity.Success);

            var addresses = s.Driver.FindElements(By.ClassName("lightning-address-value"));
            Assert.Equal(2, addresses.Count);

            foreach (IWebElement webElement in addresses)
            {
                var value = webElement.GetAttribute("value");
                //cannot test this directly as https is not supported on our e2e tests
                // var request = await LNURL.LNURL.FetchPayRequestViaInternetIdentifier(value, new HttpClient());

                var lnurl = new Uri(LNURL.LNURL.ExtractUriFromInternetIdentifier(value).ToString()
                    .Replace("https", "http"));
                var request = (LNURL.LNURLPayRequest)await LNURL.LNURL.FetchInformation(lnurl, new HttpClient());

                switch (value)
                {
                    case { } v when v.StartsWith(lnaddress2):
                        Assert.Equal(2, request.MinSendable.ToDecimal(LightMoneyUnit.Satoshi));
                        Assert.Equal(10, request.MaxSendable.ToDecimal(LightMoneyUnit.Satoshi));
                        break;

                    case { } v when v.StartsWith(lnaddress1):
                        Assert.Equal(1, request.MinSendable.ToDecimal(LightMoneyUnit.Satoshi));
                        Assert.Equal(6.12m, request.MaxSendable.ToDecimal(LightMoneyUnit.BTC));
                        break;
                }
            }
        }

        [Fact]
        [Trait("Selenium", "Selenium")]
        public async Task CanSigninWithLoginCode()
        {
            using var s = CreateSeleniumTester();
            await s.StartAsync();
            var user = s.RegisterNewUser();
            s.GoToProfile(ManageNavPages.LoginCodes);
            var code = s.Driver.FindElement(By.Id("logincode")).GetAttribute("value");
            s.Driver.FindElement(By.Id("regeneratecode")).Click();
            Assert.NotEqual(code, s.Driver.FindElement(By.Id("logincode")).GetAttribute("value"));

            code = s.Driver.FindElement(By.Id("logincode")).GetAttribute("value");
            s.Logout();
            s.GoToLogin();
            s.Driver.SetAttribute("LoginCode", "value", "bad code");
            s.Driver.InvokeJSFunction("logincode-form", "submit");


            s.Driver.SetAttribute("LoginCode", "value", code);
            s.Driver.InvokeJSFunction("logincode-form", "submit");
            s.GoToProfile();
            Assert.Contains(user, s.Driver.PageSource);
        }


        // For god know why, selenium have problems clicking on the save button, resulting in ultimate hacks
        // to make it works.
        private void SudoForceSaveLightningSettingsRightNowAndFast(SeleniumTester s, string cryptoCode)
        {
            int maxAttempts = 5;
retry:
            s.Driver.WaitForAndClick(By.Id("save"));
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

        
        [Fact]
        [Trait("Selenium", "Selenium")]
        public async Task CanUseLNURLAuth()
        {
            using var s = CreateSeleniumTester();
            await s.StartAsync();
            var user = s.RegisterNewUser(true);
            s.GoToProfile(ManageNavPages.TwoFactorAuthentication);
            s.Driver.FindElement(By.Name("Name")).SendKeys("ln wallet");
            s.Driver.FindElement(By.Name("type"))
                .FindElement(By.CssSelector($"option[value='{(int)Fido2Credential.CredentialType.LNURLAuth}']")).Click();
            s.Driver.FindElement(By.Id("btn-add")).Click();
            var links = s.Driver.FindElements(By.CssSelector(".tab-content a")).Select(element => element.GetAttribute("href"));
            Assert.Equal(2,links.Count());
            Uri prevEndpoint = null;
            foreach (string link in links)
            {
                var endpoint = LNURL.LNURL.Parse(link, out var tag);
                Assert.Equal("login",tag);
                if(endpoint.Scheme != "https")
                    prevEndpoint = endpoint;
            }

            var linkingKey = new Key();
            var request = Assert.IsType<LNAuthRequest>(await LNURL.LNURL.FetchInformation(prevEndpoint, null));
            _ = await request.SendChallenge(linkingKey, new HttpClient());
           TestUtils.Eventually(() => s.FindAlertMessage());
            
            s.Logout();
            s.LogIn(user, "123456");
            var section = s.Driver.FindElement(By.Id("lnurlauth-section"));
            links = section.FindElements(By.CssSelector(".tab-content a")).Select(element => element.GetAttribute("href"));
            Assert.Equal(2,links.Count());
            prevEndpoint = null;
            foreach (string link in links)
            {
                var endpoint = LNURL.LNURL.Parse(link, out var tag);
                Assert.Equal("login",tag);
                if(endpoint.Scheme != "https")
                    prevEndpoint = endpoint;
            }
            request = Assert.IsType<LNAuthRequest>(await LNURL.LNURL.FetchInformation(prevEndpoint, null));
            _ = await request.SendChallenge(linkingKey, new HttpClient());
            TestUtils.Eventually(() =>
            {
                Assert.Equal(s.Driver.Url, s.ServerUri.ToString());
            });
        }
        
        private static void CanBrowseContent(SeleniumTester s)
        {
            s.Driver.FindElement(By.ClassName("delivery-content")).Click();
            var windows = s.Driver.WindowHandles;
            Assert.Equal(2, windows.Count);
            s.Driver.SwitchTo().Window(windows[1]);
            JObject.Parse(s.Driver.FindElement(By.TagName("body")).Text);
            s.Driver.Close();
            s.Driver.SwitchTo().Window(windows[0]);
        }

        private static void CanSetupEmailCore(SeleniumTester s)
        {
            s.Driver.FindElement(By.Id("QuickFillDropdownToggle")).Click();
            s.Driver.FindElement(By.CssSelector("#quick-fill .dropdown-menu .dropdown-item:first-child")).Click();
            s.Driver.FindElement(By.Id("Settings_Login")).SendKeys("test@gmail.com");
            s.Driver.FindElement(By.CssSelector("button[value=\"Save\"]")).Submit();
            s.FindAlertMessage();
            s.Driver.FindElement(By.Id("Settings_Password")).SendKeys("mypassword");
            s.Driver.FindElement(By.Id("Save")).SendKeys(Keys.Enter);
            Assert.Contains("Configured", s.Driver.PageSource);
            s.Driver.FindElement(By.Id("Settings_Login")).SendKeys("test_fix@gmail.com");
            s.Driver.FindElement(By.Id("Save")).SendKeys(Keys.Enter);
            Assert.Contains("Configured", s.Driver.PageSource);
            Assert.Contains("test_fix", s.Driver.PageSource);
            s.Driver.FindElement(By.Id("ResetPassword")).SendKeys(Keys.Enter);
            s.FindAlertMessage();
            Assert.DoesNotContain("Configured", s.Driver.PageSource);
            Assert.Contains("test_fix", s.Driver.PageSource);
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
