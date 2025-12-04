using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Lightning;
using BTCPayServer.Payments;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
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
using static Microsoft.Playwright.Assertions;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBitcoin.Payment;
using NBXplorer;
using NBXplorer.Models;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests
{
    [Trait("Playwright", "Playwright")]
    [Collection(nameof(NonParallelizableCollectionDefinition))]
    public class PlaywrightTests(ITestOutputHelper helper) : UnitTestBase(helper)
    {
        private const int TestTimeout = TestUtils.TestTimeout;
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
            var opening = s.Page.Context.WaitForPageAsync();
            await s.Page.ClickAsync("#ViewApp");
            string invoiceId;
            await using (_ = await s.SwitchPage(opening))
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
            opening = s.Page.Context.WaitForPageAsync();
            await s.Page.ClickAsync("#ViewPaymentRequest");
            var popOutPage = await opening;
            await popOutPage.ClickAsync("[data-test='form-button']");
            Assert.Contains("Enter your email", await popOutPage.ContentAsync());
            await popOutPage.FillAsync("input[name='buyerEmail']", "aa@aa.com");
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
            using (var client = await s.Server.PayTester.GetService<Configuration.BTCPayServerOptions>().SSHSettings
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
            await client.UpdateStore(s.StoreId, new UpdateStoreRequest()
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
            policies.DefaultStoreTemplate = new JObject()
            {
                ["blob"] = new JObject()
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

            var btcDerivationScheme = new ExtKey().Neuter().GetWif(Network.RegTest).ToString() + "-[legacy]";
            await s.AddDerivationScheme("BTC", btcDerivationScheme);
            await s.AddDerivationScheme("LTC", new ExtKey().Neuter().GetWif(NBitcoin.Altcoins.Litecoin.Instance.Regtest).ToString()  + "-[legacy]");

            await s.GoToStore();
            await s.Page.FillAsync("[name='DefaultCurrency']", "USD");
            await s.Page.FillAsync("[name='AdditionalTrackedRates']", "CAD,JPY,EUR");
            await s.ClickPagePrimary();

            await s.GoToStore(StoreNavPages.Rates);
            await s.Page.ClickAsync($"#PrimarySource_ShowScripting_submit");
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
        public async Task CanManageWallet()
        {
            await using var s = CreatePlaywrightTester();
            await s.StartAsync();
            await s.RegisterNewUser(true);
            var (_, storeId) = await s.CreateNewStore();
            const string cryptoCode = "BTC";

            // In this test, we try to spend from a manual seed. We import the xpub 49'/0'/0',
            // then try to use the seed to sign the transaction
            await s.GenerateWallet(cryptoCode, "", true);

            //let's test quickly the wallet send page
            await s.GoToWallet(navPages: WalletsNavPages.Send);
            //you cannot use the Sign with NBX option without saving private keys when generating the wallet.
            Assert.DoesNotContain("nbx-seed", await s.Page.ContentAsync());
            Assert.Equal(0, await s.Page.Locator("#GoBack").CountAsync());
            await s.Page.ClickAsync("#SignTransaction");
            await s.Page.WaitForSelectorAsync("text=Destination Address field is required");
            Assert.Equal(0, await s.Page.Locator("#GoBack").CountAsync());
            await s.Page.ClickAsync("#CancelWizard");
            await s.GoToWallet(navPages: WalletsNavPages.Receive);

            //generate a receiving address
            await s.Page.WaitForSelectorAsync("#address-tab .qr-container");
            Assert.True(await s.Page.Locator("#address-tab .qr-container").IsVisibleAsync());
            // no previous page in the wizard, hence no back button
            Assert.Equal(0, await s.Page.Locator("#GoBack").CountAsync());
            var receiveAddr = await s.Page.Locator("#Address").GetAttributeAsync("data-text");

            // Can add a label?
            await TestUtils.EventuallyAsync(async () =>
            {
                await s.Page.ClickAsync("div.label-manager input");
                await Task.Delay(500);
                await s.Page.FillAsync("div.label-manager input", "test-label");
                await s.Page.Keyboard.PressAsync("Enter");
                await Task.Delay(500);
                await s.Page.FillAsync("div.label-manager input", "label2");
                await s.Page.Keyboard.PressAsync("Enter");
                await Task.Delay(500);
            });

            await TestUtils.EventuallyAsync(async () =>
            {
                await s.Page.ReloadAsync();
                await s.Page.WaitForSelectorAsync("[data-value='test-label']");
            });

            Assert.True(await s.Page.Locator("#address-tab .qr-container").IsVisibleAsync());
            Assert.Equal(receiveAddr, await s.Page.Locator("#Address").GetAttributeAsync("data-text"));
            await TestUtils.EventuallyAsync(async () =>
            {
                var content = await s.Page.ContentAsync();
                Assert.Contains("test-label", content);
            });

            // Remove a label
            await s.Page.WaitForSelectorAsync("[data-value='test-label']");
            await s.Page.ClickAsync("[data-value='test-label']");
            await Task.Delay(500);
            await s.Page.EvaluateAsync(@"() => {
                const l = document.querySelector('[data-value=""test-label""]');
                l.click();
                l.nextSibling.dispatchEvent(new KeyboardEvent('keydown', {'key': 'Delete', keyCode: 8}));
            }");
            await Task.Delay(500);
            await s.Page.ReloadAsync();
            Assert.DoesNotContain("test-label", await s.Page.ContentAsync());
            Assert.Equal(0, await s.Page.Locator("#GoBack").CountAsync());

            //send money to addr and ensure it changed
            var sess = await s.Server.ExplorerClient.CreateWebsocketNotificationSessionAsync();
            await s.Server.ExplorerNode.SendToAddressAsync(BitcoinAddress.Create(receiveAddr!, Network.RegTest),
                Money.Parse("0.1"));
            await sess.WaitNext<NewTransactionEvent>(e => e.Outputs.FirstOrDefault()?.Address.ToString() == receiveAddr);
            await Task.Delay(200);
            await s.Page.ReloadAsync();
            await s.Page.ClickAsync("button[value=generate-new-address]");
            Assert.NotEqual(receiveAddr, await s.Page.Locator("#Address").GetAttributeAsync("data-text"));
            receiveAddr = await s.Page.Locator("#Address").GetAttributeAsync("data-text");
            await s.Page.ClickAsync("#CancelWizard");

            // Check the label is applied to the tx
            var wt = s.InWalletTransactions();
            await wt.AssertHasLabels("label2");

            //change the wallet and ensure old address is not there and generating a new one does not result in the prev one
            await s.GenerateWallet(cryptoCode, "", true);
            await s.GoToWallet(null, WalletsNavPages.Receive);
            await s.Page.ClickAsync("button[value=generate-new-address]");
            var newAddr = await s.Page.Locator("#Address").GetAttributeAsync("data-text");
            Assert.NotEqual(receiveAddr, newAddr);

            var invoiceId = await s.CreateInvoice(storeId);
            var invoice = await s.Server.PayTester.InvoiceRepository.GetInvoice(invoiceId);
            var btc = PaymentTypes.CHAIN.GetPaymentMethodId("BTC");
            var address = invoice.GetPaymentPrompt(btc)!.Destination;

            //wallet should have been imported to bitcoin core wallet in watch only mode.
            var result =
                await s.Server.ExplorerNode.GetAddressInfoAsync(BitcoinAddress.Create(address, Network.RegTest));
            Assert.True(result.IsWatchOnly);
            await s.GoToStore(storeId);
            var mnemonic = await s.GenerateWallet(cryptoCode, "", true, true);

            //lets import and save private keys
            invoiceId = await s.CreateInvoice(storeId);
            invoice = await s.Server.PayTester.InvoiceRepository.GetInvoice(invoiceId);
            address = invoice.GetPaymentPrompt(btc)!.Destination;
            result = await s.Server.ExplorerNode.GetAddressInfoAsync(
                BitcoinAddress.Create(address, Network.RegTest));
            //spendable from bitcoin core wallet!
            Assert.False(result.IsWatchOnly);
            var tx = await s.Server.ExplorerNode.SendToAddressAsync(BitcoinAddress.Create(address, Network.RegTest),
                Money.Coins(3.0m));
            await s.Server.ExplorerNode.GenerateAsync(1);

            await s.GoToStore(storeId);
            await s.GoToWalletSettings();
            var url = s.Page.Url;
            await s.ClickOnAllSectionLinks("#Nav-Wallets");

            // Make sure wallet info is correct
            await s.GoToUrl(url);

            await s.Page.WaitForSelectorAsync("#AccountKeys_0__MasterFingerprint");
            Assert.Equal(mnemonic.DeriveExtKey().GetPublicKey().GetHDFingerPrint().ToString(),
                await s.Page.Locator("#AccountKeys_0__MasterFingerprint").GetAttributeAsync("value"));
            Assert.Equal("m/84'/1'/0'",
                await s.Page.Locator("#AccountKeys_0__AccountKeyPath").GetAttributeAsync("value"));

            // Make sure we can rescan, because we are admin!
            await s.Page.ClickAsync("#ActionsDropdownToggle");
            await s.Page.ClickAsync("#Rescan");
            await s.Page.GetByText("The batch size make sure").WaitForAsync();
            //
            // Check the tx sent earlier arrived
            wt = await s.GoToWalletTransactions();
            await wt.WaitTransactionsLoaded();
            await s.Page.Locator($"[data-text='{tx}']").WaitForAsync();

            var walletTransactionUri = new Uri(s.Page.Url);

            // Send to bob
            var ws = await s.GoToWalletSend();
            var bob = new Key().PubKey.Hash.GetAddress(Network.RegTest);
            await ws.FillAddress(bob);
            await ws.FillAmount(1);

            // Add labels to the transaction output
            await TestUtils.EventuallyAsync(async () =>
            {
                await s.Page.ClickAsync("div.label-manager input");
                await s.Page.FillAsync("div.label-manager input", "tx-label");
                await s.Page.Keyboard.PressAsync("Enter");
                await s.Page.WaitForSelectorAsync("[data-value='tx-label']");
            });

            await ws.Sign();
            // Back button should lead back to the previous page inside the send wizard
            var backUrl = await s.Page.Locator("#GoBack").GetAttributeAsync("href");
            Assert.EndsWith($"/send?returnUrl={Uri.EscapeDataString(walletTransactionUri.AbsolutePath)}", backUrl);
            // Cancel button should lead to the page that referred to the send wizard
            var cancelUrl = await s.Page.Locator("#CancelWizard").GetAttributeAsync("href");
            Assert.EndsWith(walletTransactionUri.AbsolutePath, cancelUrl);

            // Broadcast
            var wb = s.InBroadcast();
            await wb.AssertSending(bob, 1.0m);
            await wb.Broadcast();
            Assert.Equal(walletTransactionUri.ToString(), s.Page.Url);
            // Assert that the added label is associated with the transaction
            await wt.AssertHasLabels("tx-label");

            await s.GoToWallet(navPages: WalletsNavPages.Send);

            var jack = new Key().PubKey.Hash.GetAddress(Network.RegTest);
            await ws.FillAddress(jack);
            await ws.FillAmount(0.01m);
            await ws.Sign();

            await wb.AssertSending(jack, 0.01m);
            Assert.EndsWith("psbt/ready", s.Page.Url);
            await wb.Broadcast();
            await s.FindAlertMessage();

            var bip21 = invoice.EntityToDTO(s.Server.PayTester.GetService<Dictionary<PaymentMethodId, IPaymentMethodBitpayAPIExtension>>(), s.Server.PayTester.GetService<CurrencyNameTable>()).CryptoInfo.First().PaymentUrls.BIP21;
            //let's make bip21 more interesting
            bip21 += "&label=Solid Snake&message=Snake? Snake? SNAAAAKE!";
            var parsedBip21 = new BitcoinUrlBuilder(bip21, Network.RegTest);
            await s.GoToWalletSend();

            // ReSharper disable once AsyncVoidMethod
            async void PasteBIP21(object sender, IDialog e)
            {
                await e.AcceptAsync(bip21);
            }
            s.Page.Dialog += PasteBIP21;
            await s.Page.ClickAsync("#bip21parse");
            s.Page.Dialog -= PasteBIP21;
            await s.FindAlertMessage(StatusMessageModel.StatusSeverity.Info);

            Assert.Equal(parsedBip21.Amount!.ToString(false),
                await s.Page.Locator("#Outputs_0__Amount").GetAttributeAsync("value"));
            Assert.Equal(parsedBip21.Address!.ToString(),
                await s.Page.Locator("#Outputs_0__DestinationAddress").GetAttributeAsync("value"));

            await s.Page.ClickAsync("#CancelWizard");
            await s.GoToWalletSettings();
            var settingsUri = new Uri(s.Page.Url);
            await s.Page.ClickAsync("#ActionsDropdownToggle");
            await s.Page.ClickAsync("#ViewSeed");

            // Seed backup page
            var recoveryPhrase = await s.Page.Locator("#RecoveryPhrase").First.GetAttributeAsync("data-mnemonic");
            Assert.Equal(mnemonic.ToString(), recoveryPhrase);
            Assert.Contains("The recovery phrase will also be stored on the server as a hot wallet.",
                await s.Page.ContentAsync());

            // No confirmation, just a link to return to the wallet
            Assert.Equal(0, await s.Page.Locator("#confirm").CountAsync());
            await s.Page.ClickAsync("#proceed");
            Assert.Equal(settingsUri.ToString(), s.Page.Url);

            // Once more, test the cancel link of the wallet send page leads back to the previous page
            await s.GoToWallet(navPages: WalletsNavPages.Send);
            cancelUrl = await s.Page.Locator("#CancelWizard").GetAttributeAsync("href");
            Assert.EndsWith(settingsUri.AbsolutePath, cancelUrl);
            // no previous page in the wizard, hence no back button
            Assert.Equal(0, await s.Page.Locator("#GoBack").CountAsync());
            await s.Page.ClickAsync("#CancelWizard");
            Assert.Equal(settingsUri.ToString(), s.Page.Url);

            // Transactions list contains export, ensure functions are present.
            await s.GoToWalletTransactions();

            await s.Page.ClickAsync(".mass-action-select-all");
            await s.Page.Locator("#BumpFee").WaitForAsync();

            // JSON export
            await s.Page.ClickAsync("#ExportDropdownToggle");
            var opening = s.Page.Context.WaitForPageAsync();
            await s.Page.ClickAsync("#ExportJSON");
            await using (_ = await s.SwitchPage(opening))
            {
                await s.Page.WaitForLoadStateAsync();
                Assert.Contains(s.WalletId.ToString(), s.Page.Url);
                Assert.EndsWith("export?format=json", s.Page.Url);
                Assert.Contains("\"Amount\": \"3.00000000\"", await s.Page.ContentAsync());
            }

            // CSV export
            await s.Page.ClickAsync("#ExportDropdownToggle");
            var download = await s.Page.RunAndWaitForDownloadAsync(async () =>
            {
                await s.Page.ClickAsync("#ExportCSV");
            });
            Assert.Contains(tx.ToString(), await File.ReadAllTextAsync(await download.PathAsync()));

            // BIP-329 export
            await s.Page.ClickAsync("#ExportDropdownToggle");
            download = await s.Page.RunAndWaitForDownloadAsync(async () =>
            {
                await s.Page.ClickAsync("#ExportBIP329");
            });
            Assert.Contains(tx.ToString(), await File.ReadAllTextAsync(await download.PathAsync()));
        }

        [Fact]
        public async Task CanUseReservedAddressesView()
        {
            await using var s = CreatePlaywrightTester();
            await s.StartAsync();
            await s.RegisterNewUser(true);
            await s.CreateNewStore();
            var walletId = new WalletId(s.StoreId, "BTC");
            s.WalletId = walletId;
            await s.GenerateWallet();

            await s.GoToWallet(walletId, WalletsNavPages.Receive);

            for (var i = 0; i < 10; i++)
            {
                var currentAddress = await s.Page.GetAttributeAsync("#Address", "data-text");
                await s.Page.ClickAsync("button[value=generate-new-address]");
                await TestUtils.EventuallyAsync(async () =>
                {
                    var newAddress = await s.Page.GetAttributeAsync("#Address[data-text]", "data-text");
                    Assert.False(string.IsNullOrEmpty(newAddress));
                    Assert.NotEqual(currentAddress, newAddress);
                });
            }

            await s.Page.ClickAsync("#reserved-addresses-button");
            await s.Page.WaitForSelectorAsync("#reserved-addresses");

            const string labelInputSelector = "#reserved-addresses table tbody tr .ts-control input";
            await s.Page.WaitForSelectorAsync(labelInputSelector);

            // Test Label Manager
            await s.Page.FillAsync(labelInputSelector, "test-label");
            await s.Page.Keyboard.PressAsync("Enter");
            await TestUtils.EventuallyAsync(async () =>
            {
                var text = await s.Page.InnerTextAsync("#reserved-addresses table tbody");
                Assert.Contains("test-label", text);
            });

            //Test Pagination
            await TestUtils.EventuallyAsync(async () =>
            {
                var rows = await s.Page.QuerySelectorAllAsync("#reserved-addresses table tbody tr");
                var visible = await Task.WhenAll(rows.Select(async r => await r.IsVisibleAsync()));
                Assert.Equal(10, visible.Count(v => v));
            });

            await s.Page.ClickAsync(".pagination li:last-child a");

            await TestUtils.EventuallyAsync(async () =>
            {
                var rows = await s.Page.QuerySelectorAllAsync("#reserved-addresses table tbody tr");
                var visible = await Task.WhenAll(rows.Select(async r => await r.IsVisibleAsync()));
                Assert.Single(visible, v => v);
            });

            await s.Page.ClickAsync(".pagination li:first-child a");
            await s.Page.WaitForSelectorAsync("#reserved-addresses");

            // Test Filter
            await s.Page.FillAsync("#filter-reserved-addresses", "test-label");
            await TestUtils.EventuallyAsync(async () =>
            {
                var rows = await s.Page.QuerySelectorAllAsync("#reserved-addresses table tbody tr");
                var visible = await Task.WhenAll(rows.Select(async r => await r.IsVisibleAsync()));
                Assert.Single(visible, v => v);
            });

            //Test WalletLabels redirect with filter
            await s.GoToWallet(walletId, WalletsNavPages.Settings);
            await s.Page.ClickAsync("#manage-wallet-labels-button");
            await s.Page.WaitForSelectorAsync("table");
            await s.Page.ClickAsync("a:has-text('Addresses')");

            await s.Page.WaitForSelectorAsync("#reserved-addresses");
            var currentFilter = await s.Page.InputValueAsync("#filter-reserved-addresses");
            Assert.Equal("test-label", currentFilter);
            await TestUtils.EventuallyAsync(async () =>
            {
                var rows = await s.Page.QuerySelectorAllAsync("#reserved-addresses table tbody tr");
                var visible = await Task.WhenAll(rows.Select(r => r.IsVisibleAsync()));
                Assert.Single(visible, v => v);
            });
        }

        [Fact]
        public async Task CanMarkPaymentRequestAsSettled()
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
            await s.Page.FillAsync("#Title", "Test Payment Request");
            await s.Page.FillAsync("#Amount", "0.1");
            await s.Page.FillAsync("#Currency", "BTC");
            await s.ClickPagePrimary();
            await s.FindAlertMessage(partialText: "Payment request");

            var paymentRequestUrl = s.Page.Url;
            var uri = new Uri(paymentRequestUrl);
            var queryParams = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var payReqId = queryParams["payReqId"];
            Assert.NotNull(payReqId);
            Assert.NotEmpty(payReqId);
            var markAsSettledExists = await s.Page.Locator("button:has-text('Mark as settled')").CountAsync();
            Assert.Equal(0, markAsSettledExists);
            var opening = s.Page.Context.WaitForPageAsync();
            await s.Page.ClickAsync("a:has-text('View')");
            string invoiceId;
            await using (_ = await s.SwitchPage(opening))
            {
                await s.Page.ClickAsync("button:has-text('Pay')");
                await s.Page.WaitForLoadStateAsync();

                await s.Page.WaitForSelectorAsync("iframe[name='btcpay']", new() { Timeout = 10000 });

                var iframe = s.Page.Frame("btcpay");
                Assert.NotNull(iframe);

                await iframe.FillAsync("#test-payment-amount", "0.05");
                await iframe.ClickAsync("#FakePayment");
                await iframe.WaitForSelectorAsync("#CheatSuccessMessage", new() { Timeout = 10000 });

                invoiceId = s.Page.Url.Split('/').Last();
            }
            await s.GoToInvoices();

            await s.Page.ClickAsync("[data-invoice-state-badge] .dropdown-toggle");
            await s.Page.ClickAsync("[data-invoice-state-badge] .dropdown-menu button:has-text('Mark as settled')");
            await s.Page.WaitForLoadStateAsync();

            await s.GoToStore();
            await s.Page.ClickAsync("#menu-item-PaymentRequests");
            await s.Page.WaitForLoadStateAsync();

            var opening2 = s.Page.Context.WaitForPageAsync();
            await s.Page.ClickAsync("a:has-text('View')");
            await using (_ = await s.SwitchPage(opening2))
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
            await s.GoToServer(ServerNavPages.Policies);

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

            await Expect(s.Page.Locator("#NotificationsBadge")).ToContainTextAsync("1");
            await s.Page.ClickAsync("#NotificationsHandle");
            await Expect(s.Page.Locator("#NotificationsList .notification")).ToContainTextAsync($"New user {unapproved.RegisterDetails.Email} requires approval");
            await s.Page.ClickAsync("#NotificationsMarkAllAsSeen");

            await s.GoToServer(ServerNavPages.Policies);
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
            Assert.Contains("Dynamic DNS", await s.Page.ContentAsync());

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
            Assert.Contains("The Dynamic DNS has been successfully queried", await s.Page.ContentAsync());
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
            Assert.Contains("This hostname already exists", await s.Page.ContentAsync());

            // Delete the hostname
            await s.GoToUrl("/server/services/dynamic-dns");
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
        public async Task CanImportMnemonic()
        {
            await using var s = CreatePlaywrightTester();
            await s.StartAsync();
            await s.RegisterNewUser(true);
            foreach (var isHotwallet in new[] { false, true })
            {
                var cryptoCode = "BTC";
                await s.CreateNewStore();
                await s.GenerateWallet(cryptoCode, "melody lizard phrase voice unique car opinion merge degree evil swift cargo", isHotWallet: isHotwallet);
                await s.GoToWalletSettings(cryptoCode);
                if (isHotwallet)
                {
                    await s.Page.ClickAsync("#ActionsDropdownToggle");
                    Assert.True(await s.Page.Locator("#ViewSeed").IsVisibleAsync());
                }
                else
                {
                    Assert.False(await s.Page.Locator("#ViewSeed").IsVisibleAsync());
                }
            }
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
        public async Task CanImportWallet()
        {
            await using var s = CreatePlaywrightTester();
            await s.StartAsync();
            await s.RegisterNewUser(true);
            await s.CreateNewStore();
            const string cryptoCode = "BTC";
            var mnemonic = await s.GenerateWallet(cryptoCode, "click chunk owner kingdom faint steak safe evidence bicycle repeat bulb wheel");

            // Make sure wallet info is correct
            await s.GoToWalletSettings(cryptoCode);
            Assert.Contains(mnemonic.DeriveExtKey().GetPublicKey().GetHDFingerPrint().ToString(),
                await s.Page.GetAttributeAsync("#AccountKeys_0__MasterFingerprint", "value"));
            Assert.Contains("m/84'/1'/0'",
                await s.Page.GetAttributeAsync("#AccountKeys_0__AccountKeyPath", "value"));

            // Transactions list is empty
            await s.GoToWallet();
            await s.Page.WaitForSelectorAsync("#WalletTransactions[data-loaded='true']");
            Assert.Contains("There are no transactions yet", await s.Page.Locator("#WalletTransactions").TextContentAsync());
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
            await s.LogIn(user, "123456");
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
        public async Task CanUseCoinSelectionFilters()
        {
            await using var s = CreatePlaywrightTester();
            await s.StartAsync();
            await s.RegisterNewUser(true);
            (_, string storeId) = await s.CreateNewStore();
            await s.GenerateWallet("BTC", "", false, true);
            var walletId = new WalletId(storeId, "BTC");

            await s.GoToWallet(walletId, WalletsNavPages.Receive);
            var addressStr = await s.Page.GetAttributeAsync("#Address", "data-text");
            var address = BitcoinAddress.Create(addressStr,
                ((BTCPayNetwork)s.Server.NetworkProvider.GetNetwork("BTC")).NBitcoinNetwork);

            await s.Server.ExplorerNode.GenerateAsync(1);

            const decimal AmountTiny = 0.001m;
            const decimal AmountSmall = 0.005m;
            const decimal AmountMedium = 0.009m;
            const decimal AmountLarge = 0.02m;

            List<uint256> txs =
            [
                await s.Server.ExplorerNode.SendToAddressAsync(address, Money.Coins(AmountTiny)),
                await s.Server.ExplorerNode.SendToAddressAsync(address, Money.Coins(AmountSmall)),
                await s.Server.ExplorerNode.SendToAddressAsync(address, Money.Coins(AmountMedium)),
                await s.Server.ExplorerNode.SendToAddressAsync(address, Money.Coins(AmountLarge))
            ];

            await s.Server.ExplorerNode.GenerateAsync(1);
            await s.GoToWallet(walletId, WalletsNavPages.Send);
            await s.Page.ClickAsync("#toggleInputSelection");

            var input = s.Page.Locator("input[placeholder^='Filter']");
            await input.WaitForAsync();
            Assert.NotNull(input);

            // Test amountmin
            await input.ClearAsync();
            await input.FillAsync("amountmin:0.01");
            await TestUtils.EventuallyAsync(async () => {
                Assert.Single(await s.Page.Locator("li.list-group-item").AllAsync());
            });

            // Test amountmax
            await input.ClearAsync();
            await input.FillAsync("amountmax:0.002");
            await TestUtils.EventuallyAsync(async () => {
                Assert.Single(await s.Page.Locator("li.list-group-item").AllAsync());
            });

            // Test general text (txid)
            await input.ClearAsync();
            await input.FillAsync(txs[2].ToString()[..8]);
            await TestUtils.EventuallyAsync(async () => {
                Assert.Single(await s.Page.Locator("li.list-group-item").AllAsync());
            });

            // Test timestamp before/after
            await input.ClearAsync();
            await input.FillAsync("after:2099-01-01");
            await TestUtils.EventuallyAsync(async () => {
                Assert.Empty(await s.Page.Locator("li.list-group-item").AllAsync());
            });

            await input.ClearAsync();
            await input.FillAsync("before:2099-01-01");
            await TestUtils.EventuallyAsync(async () =>
            {
                Assert.True((await s.Page.Locator("li.list-group-item").AllAsync()).Count >= 4);
            });
        }

        [Fact]
        [Trait("Playwright", "Playwright")]
        [Trait("Lightning", "Lightning")]
        public async Task CanManageLightningNode()
        {
            await using var s = CreatePlaywrightTester();
            s.Server.ActivateLightning();
            await s.StartAsync();
            await s.Server.EnsureChannelsSetup();
            await s.RegisterNewUser(true);
            (string storeName, _) = await s.CreateNewStore();

            // Check status in navigation
            await s.Page.Locator("#menu-item-LightningSettings-BTC .btcpay-status--pending").WaitForAsync();

            // Set up LN node
            await s.AddLightningNode();
            await s.Page.Locator("#menu-item-Lightning-BTC .btcpay-status--enabled").WaitForAsync();

            // Check public node info for availability
            var opening = s.Page.Context.WaitForPageAsync();
            await s.Page.ClickAsync("#PublicNodeInfo");
            var newPage = await opening;
            await Expect(newPage.Locator(".store-name")).ToHaveTextAsync(storeName);
            await Expect(newPage.Locator("#LightningNodeTitle")).ToHaveTextAsync("BTC Lightning Node");
            await Expect(newPage.Locator("#LightningNodeStatus")).ToHaveTextAsync("Online");
            await newPage.Locator(".btcpay-status--enabled").WaitForAsync();
            await newPage.Locator("#LightningNodeUrlClearnet").WaitForAsync();
            await newPage.CloseAsync();

            // Set wrong node connection string to simulate offline node
            await s.GoToLightningSettings();
            await s.Page.ClickAsync("#SetupLightningNodeLink");
            await s.Page.ClickAsync("label[for=\"LightningNodeType-Custom\"]");
            await s.Page.Locator("#ConnectionString").WaitForAsync();
            await s.Page.Locator("#ConnectionString").ClearAsync();
            await s.Page.FillAsync("#ConnectionString", "type=lnd-rest;server=https://doesnotwork:8080/");
            await s.Page.ClickAsync("#test");
            await s.FindAlertMessage(StatusMessageModel.StatusSeverity.Error);
            await s.ClickPagePrimary();
            await s.FindAlertMessage(partialText: "BTC Lightning node updated.");

            // Check offline state is communicated in nav item
            await s.Page.Locator("#menu-item-Lightning-BTC .btcpay-status--disabled").WaitForAsync();

            // Check public node info for availability
            opening = s.Page.Context.WaitForPageAsync();
            await s.Page.ClickAsync("#PublicNodeInfo");
            newPage = await opening;
            await Expect(newPage.Locator(".store-name")).ToHaveTextAsync(storeName);
            await Expect(newPage.Locator("#LightningNodeTitle")).ToHaveTextAsync("BTC Lightning Node");
            await Expect(newPage.Locator("#LightningNodeStatus")).ToHaveTextAsync("Unavailable");
            await newPage.Locator(".btcpay-status--disabled").WaitForAsync();
            await Expect(newPage.Locator("#LightningNodeUrlClearnet")).ToBeHiddenAsync();
        }

        [Fact]
        [Trait("Playwright", "Playwright")]
        [Trait("Lightning", "Lightning")]
        public async Task CanEditPullPaymentUI()
        {
            await using var s = CreatePlaywrightTester();
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
            await s.Page.Locator("#Amount").ClearAsync();
            await s.Page.FillAsync("#Amount", "99.0");
            await s.ClickPagePrimary();

            var opening = s.Page.Context.WaitForPageAsync();
            await s.Page.ClickAsync("text=View");
            var newPage = await opening;
            await Expect(newPage.Locator("body")).ToContainTextAsync("PP1");
            await newPage.CloseAsync();

            await s.GoToStore(s.StoreId, StoreNavPages.PullPayments);

            await s.Page.ClickAsync("text=PP1");
            var name = s.Page.Locator("#Name");
            await name.ClearAsync();
            await name.FillAsync("PP1 Edited");
            var description = s.Page.Locator(".card-block");
            await description.FillAsync("Description Edit");
            await s.ClickPagePrimary();

            opening = s.Page.Context.WaitForPageAsync();
            await s.Page.ClickAsync("text=View");
            newPage = await opening;
            await Expect(newPage.GetByTestId("description")).ToContainTextAsync("Description Edit");
            await Expect(newPage.GetByTestId("title")).ToContainTextAsync("PP1 Edited");
        }

        [Fact]
        public async Task CookieReflectProperPermissions()
        {
            await using var s = CreatePlaywrightTester();
            await s.StartAsync();
            var alice = s.Server.NewAccount();
            alice.Register(false);
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

            var pageContent = await s.Page.ContentAsync();
            Assert.Contains("PP1", pageContent);
        }

        [Fact]
        public async Task CanUseWebhooks()
        {
            await using var s = CreatePlaywrightTester();
            await s.StartAsync();
            await s.RegisterNewUser(true);
            await s.CreateNewStore();
            await s.GoToStore(StoreNavPages.Webhooks);

            TestLogs.LogInformation("Let's create two webhooks");
            for (var i = 0; i < 2; i++)
            {
                await s.ClickPagePrimary();
                await s.Page.FillAsync("[name='PayloadUrl']", $"http://127.0.0.1/callback{i}");
                await s.Page.SelectOptionAsync("#Everything", "false");
                await s.Page.ClickAsync("#InvoiceCreated");
                await s.Page.ClickAsync("#InvoiceProcessing");
                await s.ClickPagePrimary();
            }

            await s.FindAlertMessage();
            TestLogs.LogInformation("Let's delete one of them");
            var deleteLinks = await s.Page.Locator("a:has-text('Delete')").AllAsync();
            Assert.Equal(2, deleteLinks.Count);
            await deleteLinks[0].ClickAsync();
            await s.ConfirmDeleteModal();
            deleteLinks = await s.Page.Locator("a:has-text('Delete')").AllAsync();
            Assert.Single(deleteLinks);
            await s.FindAlertMessage();

            TestLogs.LogInformation("Let's try to update one of them");
            await s.Page.ClickAsync("text=Modify");

            using var server = new FakeServer();
            await server.Start();
            await s.Page.FillAsync("[name='PayloadUrl']", server.ServerUri.AbsoluteUri);
            await s.Page.FillAsync("[name='Secret']", "HelloWorld");
            await s.Page.ClickAsync("[name='update']");
            await s.FindAlertMessage();
            await s.Page.ClickAsync("text=Modify");

            // Check which events are selected
            await Expect(s.Page.Locator("input[value='InvoiceProcessing']")).ToBeCheckedAsync();
            await Expect(s.Page.Locator("input[value='InvoiceCreated']")).ToBeCheckedAsync();
            await Expect(s.Page.Locator("input[value='InvoiceReceivedPayment']")).Not.ToBeCheckedAsync();

            await s.Page.ClickAsync("[name='update']");
            await s.FindAlertMessage();
            var pageContent = await s.Page.ContentAsync();
            Assert.Contains(server.ServerUri.AbsoluteUri, pageContent);

            TestLogs.LogInformation("Let's see if we can generate an event");
            await s.GoToStore();
            await s.AddDerivationScheme();
            await s.CreateInvoice();
            var request = await server.GetNextRequest();
            var headers = request.Request.Headers;
            var actualSig = headers["BTCPay-Sig"].First();
            var bytes = await request.Request.Body.ReadBytesAsync((int)headers.ContentLength.Value);
            var expectedSig =
                $"sha256={Encoders.Hex.EncodeData(NBitcoin.Crypto.Hashes.HMACSHA256(Encoding.UTF8.GetBytes("HelloWorld"), bytes))}";
            Assert.Equal(expectedSig, actualSig);
            request.Response.StatusCode = 200;
            server.Done();

            TestLogs.LogInformation("Let's make a failed event");
            var invoiceId = await s.CreateInvoice();
            request = await server.GetNextRequest();
            request.Response.StatusCode = 404;
            server.Done();

            // The delivery is done asynchronously, so small wait here
            await Task.Delay(500);
            await s.GoToStore();
            await s.GoToStore(StoreNavPages.Webhooks);
            await s.Page.ClickAsync("text=Modify");
            var redeliverElements = await s.Page.Locator("button.redeliver").AllAsync();

            // One worked, one failed
            await s.Page.Locator(".icon-cross").WaitForAsync();
            await s.Page.Locator(".icon-checkmark").WaitForAsync();
            await redeliverElements[0].ClickAsync();

            await s.FindAlertMessage();
            request = await server.GetNextRequest();
            request.Response.StatusCode = 404;
            server.Done();

            TestLogs.LogInformation("Can we browse the json content?");
            await CanBrowseContentAsync(s);

            await s.GoToInvoices();
            await s.Page.ClickAsync($"text={invoiceId}");
            await CanBrowseContentAsync(s);
            var redeliverElement = s.Page.Locator("button.redeliver").First;
            await redeliverElement.ClickAsync();

            await s.FindAlertMessage();
            request = await server.GetNextRequest();
            request.Response.StatusCode = 404;
            server.Done();

            TestLogs.LogInformation("Let's see if we can delete store with some webhooks inside");
            await s.GoToStore();
            await s.Page.ClickAsync("#DeleteStore");
            await s.ConfirmDeleteModal();
            await s.FindAlertMessage();
        }

        private static async Task CanBrowseContentAsync(PlaywrightTester s)
        {
            var newPageDoing = s.Page.Context.WaitForPageAsync();
            await s.Page.ClickAsync(".delivery-content");
            var newPage = await newPageDoing;
            var bodyText = await newPage.Locator("body").TextContentAsync();
            JObject.Parse(bodyText);
            await newPage.CloseAsync();
        }

        [Fact]
        [Trait("Playwright", "Playwright")]
        [Trait("Lightning", "Lightning")]
        public async Task CanUsePredefinedRoles()
        {
            await using var s = CreatePlaywrightTester(newDb: true);
            s.Server.ActivateLightning();
            await s.StartAsync();
            await s.Server.EnsureChannelsSetup();
            var storeSettingsPaths = new [] {"settings", "rates", "checkout", "tokens", "users", "roles", "webhooks", "payout-processors",
                "payout-processors/onchain-automated/BTC", "payout-processors/lightning-automated/BTC", "emails/rules", "email-settings", "forms"};

            // Setup users
            var manager = await s.RegisterNewUser();
            await s.SkipWizard();
            await s.Logout();
            await s.GoToRegister();
            var employee = await s.RegisterNewUser();
            await s.SkipWizard();
            await s.Logout();
            await s.GoToRegister();
            var guest = await s.RegisterNewUser();
            await s.SkipWizard();
            await s.Logout();
            await s.GoToRegister();

            // Setup store, wallets and add users
            await s.RegisterNewUser(true);
            var (_, storeId) = await s.CreateNewStore();
            await s.GoToStore();
            await s.GenerateWallet(isHotWallet: true);
            await s.AddLightningNode(LightningConnectionType.CLightning, false);
            await s.AddUserToStore(storeId, manager, "Manager");
            await s.AddUserToStore(storeId, employee, "Employee");
            await s.AddUserToStore(storeId, guest, "Guest");

            // Add apps
            var (_, posId) = await s.CreateApp("PointOfSale");
            var (_, crowdfundId) = await s.CreateApp("Crowdfund");

            string GetStorePath(string subPath) => $"/stores/{storeId}" + (string.IsNullOrEmpty(subPath) ? "" : $"/{subPath}");

            // Owner access
            await s.AssertPageAccess(true, GetStorePath(""));
            await s.AssertPageAccess(true, GetStorePath("reports"));
            await s.AssertPageAccess(true, GetStorePath("invoices"));
            await s.AssertPageAccess(true, GetStorePath("invoices/create"));
            await s.AssertPageAccess(true, GetStorePath("payment-requests"));
            await s.AssertPageAccess(true, GetStorePath("payment-requests/edit"));
            await s.AssertPageAccess(true, GetStorePath("pull-payments"));
            await s.AssertPageAccess(true, GetStorePath("payouts"));
            await s.AssertPageAccess(true, GetStorePath("onchain/BTC"));
            await s.AssertPageAccess(true, GetStorePath("onchain/BTC/settings"));
            await s.AssertPageAccess(true, GetStorePath("lightning/BTC"));
            await s.AssertPageAccess(true, GetStorePath("lightning/BTC/settings"));
            await s.AssertPageAccess(true, GetStorePath("apps/create"));
            await s.AssertPageAccess(true, $"/apps/{posId}/settings/pos");
            await s.AssertPageAccess(true, $"/apps/{crowdfundId}/settings/crowdfund");
            foreach (var path in storeSettingsPaths)
            {   // should have manage access to settings, hence should see submit buttons or create links
                s.TestLogs.LogInformation($"Checking access to store page {path} as owner");
                await s.AssertPageAccess(true, $"/stores/{storeId}/{path}");
                await s.Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
                if (path != "payout-processors")
                {
                    var saveButton = s.Page.GetByRole(AriaRole.Button, new() { Name = "Save" });
                    if (await saveButton.CountAsync() > 0)
                    {
                        Assert.True(await saveButton.IsVisibleAsync());
                    }
                }
            }
            await s.Logout();

            // Manager access
            await s.LogIn(manager);
            await s.AssertPageAccess(false, GetStorePath(""));
            await s.AssertPageAccess(true, GetStorePath("reports"));
            await s.AssertPageAccess(true, GetStorePath("invoices"));
            await s.AssertPageAccess(true, GetStorePath("invoices/create"));
            await s.AssertPageAccess(true, GetStorePath("payment-requests"));
            await s.AssertPageAccess(true, GetStorePath("payment-requests/edit"));
            await s.AssertPageAccess(true, GetStorePath("pull-payments"));
            await s.AssertPageAccess(true, GetStorePath("payouts"));
            await s.AssertPageAccess(false, GetStorePath("onchain/BTC"));
            await s.AssertPageAccess(false, GetStorePath("onchain/BTC/settings"));
            await s.AssertPageAccess(false, GetStorePath("lightning/BTC"));
            await s.AssertPageAccess(false, GetStorePath("lightning/BTC/settings"));
            await s.AssertPageAccess(false, GetStorePath("apps/create"));
            await s.AssertPageAccess(true, $"/apps/{posId}/settings/pos");
            await s.AssertPageAccess(true, $"/apps/{crowdfundId}/settings/crowdfund");
            foreach (var path in storeSettingsPaths)
            {   // should have view access to settings, but no submit buttons or create links
                s.TestLogs.LogInformation($"Checking access to store page {path} as manager");
                await s.AssertPageAccess(true, $"stores/{storeId}/{path}");
                Assert.False(await s.Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).IsVisibleAsync());
            }
            await s.Logout();

            // Employee access
            await s.LogIn(employee);
            await s.AssertPageAccess(false, GetStorePath(""));
            await s.AssertPageAccess(false, GetStorePath("reports"));
            await s.AssertPageAccess(true, GetStorePath("invoices"));
            await s.AssertPageAccess(true, GetStorePath("invoices/create"));
            await s.AssertPageAccess(true, GetStorePath("payment-requests"));
            await s.AssertPageAccess(true, GetStorePath("payment-requests/edit"));
            await s.AssertPageAccess(true, GetStorePath("pull-payments"));
            await s.AssertPageAccess(true, GetStorePath("payouts"));
            await s.AssertPageAccess(false, GetStorePath("onchain/BTC"));
            await s.AssertPageAccess(false, GetStorePath("onchain/BTC/settings"));
            await s.AssertPageAccess(false, GetStorePath("lightning/BTC"));
            await s.AssertPageAccess(false, GetStorePath("lightning/BTC/settings"));
            await s.AssertPageAccess(false, GetStorePath("apps/create"));
            await s.AssertPageAccess(false, $"/apps/{posId}/settings/pos");
            await s.AssertPageAccess(false, $"/apps/{crowdfundId}/settings/crowdfund");
            foreach (var path in storeSettingsPaths)
            {   // should not have access to settings
                s.TestLogs.LogInformation($"Checking access to store page {path} as employee");
                await s.AssertPageAccess(false, $"/stores/{storeId}/{path}");
            }
            await s.GoToHome();
            await s.Logout();

            // Guest access
            await s.LogIn(guest);
            await s.AssertPageAccess(false, GetStorePath(""));
            await s.AssertPageAccess(false, GetStorePath("reports"));
            await s.AssertPageAccess(true, GetStorePath("invoices"));
            await s.AssertPageAccess(true, GetStorePath("invoices/create"));
            await s.AssertPageAccess(true, GetStorePath("payment-requests"));
            await s.AssertPageAccess(false, GetStorePath("payment-requests/edit"));
            await s.AssertPageAccess(true, GetStorePath("pull-payments"));
            await s.AssertPageAccess(true, GetStorePath("payouts"));
            await s.AssertPageAccess(false, GetStorePath("onchain/BTC"));
            await s.AssertPageAccess(false, GetStorePath("onchain/BTC/settings"));
            await s.AssertPageAccess(false, GetStorePath("lightning/BTC"));
            await s.AssertPageAccess(false, GetStorePath("lightning/BTC/settings"));
            await s.AssertPageAccess(false, GetStorePath("apps/create"));
            await s.AssertPageAccess(false, $"/apps/{posId}/settings/pos");
            await s.AssertPageAccess(false, $"/apps/{crowdfundId}/settings/crowdfund");
            foreach (var path in storeSettingsPaths)
            {   // should not have access to settings
                s.TestLogs.LogInformation($"Checking access to store page {path} as guest");
                await s.AssertPageAccess(false, $"/stores/{storeId}/{path}");
            }
            await s.GoToHome();
            await s.Logout();
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
            var pairingCode = System.Text.RegularExpressions.Regex.Match(new Uri(url, UriKind.Absolute).Query, "pairingCode=([^&]*)").Groups[1].Value;

            await s.ClickPagePrimary();
            await s.FindAlertMessage();
            Assert.Contains(pairingCode, await s.Page.ContentAsync());

            var client = new NBitpayClient.Bitpay(new NBitcoin.Key(), s.ServerUri);
            await client.AuthorizeClient(new NBitpayClient.PairingCode(pairingCode));
            await client.CreateInvoiceAsync(
                new NBitpayClient.Invoice() { Price = 1.000000012m, Currency = "USD", FullNotifications = true },
                NBitpayClient.Facade.Merchant);

            client = new NBitpayClient.Bitpay(new NBitcoin.Key(), s.ServerUri);

            var code = await client.RequestClientAuthorizationAsync("hehe", NBitpayClient.Facade.Merchant);
            await s.Page.GotoAsync(code.CreateLink(s.ServerUri).ToString());
            await s.ClickPagePrimary();

            await client.CreateInvoiceAsync(
                new NBitpayClient.Invoice() { Price = 1.000000012m, Currency = "USD", FullNotifications = true },
                NBitpayClient.Facade.Merchant);

            await s.Page.GotoAsync(s.Link("/api-tokens"));
            await s.ClickPagePrimary(); // Request
            await s.ClickPagePrimary(); // Approve
            var url2 = s.Page.Url;
            var pairingCode2 = System.Text.RegularExpressions.Regex.Match(new Uri(url2, UriKind.Absolute).Query, "pairingCode=([^&]*)").Groups[1].Value;
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
            Assert.Contains("There are no invoices matching your criteria.", await s.Page.ContentAsync());
            var invoiceId = await s.CreateInvoice(storeId);
            await s.FindAlertMessage();

            var invoiceUrl = s.Page.Url;

            //let's test archiving an invoice
            Assert.DoesNotContain("Archived", await s.Page.Locator("#btn-archive-toggle").InnerTextAsync());
            await s.Page.Locator("#btn-archive-toggle").ClickAsync();
            Assert.Contains("Unarchive", await s.Page.Locator("#btn-archive-toggle").InnerTextAsync());

            //check that it no longer appears in list
            await s.GoToInvoices(storeId);
            Assert.DoesNotContain(invoiceId, await s.Page.ContentAsync());

            //ok, let's unarchive and see that it shows again
            await s.Page.GotoAsync(invoiceUrl);
            await s.Page.Locator("#btn-archive-toggle").ClickAsync();
            await s.FindAlertMessage();
            Assert.DoesNotContain("Unarchive", await s.Page.Locator("#btn-archive-toggle").InnerTextAsync());
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
        public async Task CanUseCoinSelection()
        {
            await using var s = CreatePlaywrightTester();
            await s.StartAsync();
            await s.RegisterNewUser(true);
            var (_, storeId) = await s.CreateNewStore();
            await s.GenerateWallet("BTC", "", false, true);
            var walletId = new WalletId(storeId, "BTC");
            await s.GoToWallet(walletId, WalletsNavPages.Receive);
            var addressStr = await s.Page.Locator("#Address").GetAttributeAsync("data-text");
            var address = BitcoinAddress.Create(addressStr!, ((BTCPayNetwork)s.Server.NetworkProvider.GetNetwork("BTC")).NBitcoinNetwork);
            await s.Server.ExplorerNode.GenerateAsync(1);
            for (int i = 0; i < 6; i++)
            {
                await s.Server.ExplorerNode.SendToAddressAsync(address, Money.Coins(1.0m));
            }
            var handlers = s.Server.PayTester.GetService<PaymentMethodHandlerDictionary>();
            var targetTx = await s.Server.ExplorerNode.SendToAddressAsync(address, Money.Coins(1.2m));
            var tx = await s.Server.ExplorerNode.GetRawTransactionAsync(targetTx);
            var spentOutpoint = new OutPoint(targetTx, tx.Outputs.FindIndex(txout => txout.Value == Money.Coins(1.2m)));
            var pmi = PaymentTypes.CHAIN.GetPaymentMethodId(walletId.CryptoCode);
            await TestUtils.EventuallyAsync(async () =>
            {
                var store = await s.Server.PayTester.StoreRepository.FindStore(storeId);
                var x = store.GetPaymentMethodConfig<DerivationSchemeSettings>(pmi, handlers);
                var wallet = s.Server.PayTester.GetService<BTCPayWalletProvider>().GetWallet(walletId.CryptoCode);
                wallet.InvalidateCache(x.AccountDerivation);
                Assert.Contains(
                    await wallet.GetUnspentCoins(x.AccountDerivation),
                    coin => coin.OutPoint == spentOutpoint);
            });
            await s.Server.ExplorerNode.GenerateAsync(1);
            await s.GoToWallet(walletId, WalletsNavPages.Send);
            await s.Page.Locator("#toggleInputSelection").ClickAsync();
            await s.Page.Locator($"[id='{spentOutpoint}']").WaitForAsync();
            Assert.Equal("true", (await s.Page.Locator("[name='InputSelection']").InputValueAsync()).ToLowerInvariant());

            // Select All test
            await s.Page.Locator("#select-all-checkbox").ClickAsync();
            var selectedOptions = await s.Page.Locator("[name='SelectedInputs'] option[selected]").AllAsync();
            var listItems = await s.Page.Locator("li.list-group-item").AllAsync();
            Assert.Equal(listItems.Count, selectedOptions.Count);
            await s.Page.Locator("#select-all-checkbox").ClickAsync();
            selectedOptions = await s.Page.Locator("[name='SelectedInputs'] option[selected]").AllAsync();
            Assert.Empty(selectedOptions);

            await s.Page.Locator($"[id='{spentOutpoint}']").ClickAsync();
            selectedOptions = await s.Page.Locator("[name='SelectedInputs'] option[selected]").AllAsync();
            Assert.Single(selectedOptions);

            var bob = new NBitcoin.Key().PubKey.Hash.GetAddress(NBitcoin.Network.RegTest);
            await s.Page.Locator("[name='Outputs[0].DestinationAddress']").FillAsync(bob.ToString());
            var amountInput = s.Page.Locator("[name='Outputs[0].Amount']");
            await amountInput.FillAsync("0.3");
            var checkboxElement = s.Page.Locator("input[type='checkbox'][name='Outputs[0].SubtractFeesFromOutput']");
            if (!await checkboxElement.IsCheckedAsync())
            {
                await checkboxElement.ClickAsync();
            }
            await s.Page.Locator("#SignTransaction").ClickAsync();
            await s.Page.Locator("button[value='broadcast']").ClickAsync();
            var happyElement = await s.FindAlertMessage();
            var happyText = await happyElement.InnerTextAsync();
            var txid = System.Text.RegularExpressions.Regex.Match(happyText, @"\((.*)\)").Groups[1].Value;

            tx = await s.Server.ExplorerNode.GetRawTransactionAsync(new uint256(txid));
            Assert.Single(tx.Inputs);
            Assert.Equal(spentOutpoint, tx.Inputs[0].PrevOut);
        }

        [Fact]
        [Trait("Playwright", "Playwright")]
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
        public async Task CanChangeUserRoles()
        {
            await using var s = CreatePlaywrightTester(newDb: true);
            await s.StartAsync();

            // Setup users and store
            var employee = await s.RegisterNewUser();
            await s.SkipWizard();
            await s.Logout();
            await s.GoToRegister();
            var owner = await s.RegisterNewUser(true);
            var (_, storeId) = await s.CreateNewStore();
            await s.GoToStore();
            await s.AddUserToStore(storeId, employee, "Employee");

            // Should successfully change the role
            var userRows = await s.Page.Locator("#StoreUsersList tr").AllAsync();
            Assert.Equal(2, userRows.Count);
            ILocator employeeRow = null;
            foreach (var row in userRows)
            {
                if ((await row.InnerTextAsync()).Contains(employee, StringComparison.InvariantCultureIgnoreCase)) employeeRow = row;
            }
            Assert.NotNull(employeeRow);
            await employeeRow.Locator("a[data-bs-target='#EditModal']").ClickAsync();
            Assert.Equal(employee, await s.Page.InnerTextAsync("#EditUserEmail"));
            await s.Page.SelectOptionAsync("#EditUserRole", "Manager");
            await s.Page.ClickAsync("#EditContinue");
            await s.FindAlertMessage(partialText: $"The role of {employee} has been changed to Manager.");

            // Should not see a message when not changing role
            userRows = await s.Page.Locator("#StoreUsersList tr").AllAsync();
            Assert.Equal(2, userRows.Count);
            employeeRow = null;
            foreach (var row in userRows)
            {
                if ((await row.InnerTextAsync()).Contains(employee, StringComparison.InvariantCultureIgnoreCase)) employeeRow = row;
            }
            Assert.NotNull(employeeRow);
            await employeeRow.Locator("a[data-bs-target='#EditModal']").ClickAsync();
            Assert.Equal(employee, await s.Page.InnerTextAsync("#EditUserEmail"));
            await s.Page.ClickAsync("#EditContinue");
            await s.FindAlertMessage(StatusMessageModel.StatusSeverity.Error, "The user already has the role Manager.");

            // Should not change last owner
            userRows = await s.Page.Locator("#StoreUsersList tr").AllAsync();
            Assert.Equal(2, userRows.Count);
            ILocator ownerRow = null;
            foreach (var row in userRows)
            {
                if ((await row.InnerTextAsync()).Contains(owner, StringComparison.InvariantCultureIgnoreCase)) ownerRow = row;
            }
            Assert.NotNull(ownerRow);
            await ownerRow.Locator("a[data-bs-target='#EditModal']").ClickAsync();
            Assert.Equal(owner, await s.Page.InnerTextAsync("#EditUserEmail"));
            await s.Page.SelectOptionAsync("#EditUserRole", "Employee");
            await s.Page.ClickAsync("#EditContinue");
            await s.FindAlertMessage(StatusMessageModel.StatusSeverity.Error, "The user is the last owner. Their role cannot be changed.");
        }


        [Fact]
        [Trait("Playwright", "Playwright")]
        public async Task CanUseRoleManager()
        {
            await using var s = CreatePlaywrightTester(newDb: true);
            await s.StartAsync();
            await s.RegisterNewUser(true);
            await s.GoToHome();
            await s.GoToServer(ServerNavPages.Roles);
            var existingServerRoles = await s.Page.Locator("table tr").AllAsync();
            Assert.Equal(5, existingServerRoles.Count);
            ILocator ownerRow = null;
            ILocator managerRow = null;
            ILocator employeeRow = null;
            ILocator guestRow = null;
            foreach (var roleItem in existingServerRoles)
            {
                var text = await roleItem.TextContentAsync();
                if (text.Contains("owner", StringComparison.InvariantCultureIgnoreCase))
                {
                    ownerRow = roleItem;
                }
                else if (text.Contains("manager", StringComparison.InvariantCultureIgnoreCase))
                {
                    managerRow = roleItem;
                }
                else if (text.Contains("employee", StringComparison.InvariantCultureIgnoreCase))
                {
                    employeeRow = roleItem;
                }
                else if (text.Contains("guest", StringComparison.InvariantCultureIgnoreCase))
                {
                    guestRow = roleItem;
                }
            }

            Assert.NotNull(ownerRow);
            Assert.NotNull(managerRow);
            Assert.NotNull(employeeRow);
            Assert.NotNull(guestRow);

            var ownerBadges = await ownerRow.Locator(".badge").AllAsync();
            var ownerBadgeTexts = await Task.WhenAll(ownerBadges.Select(async element => await element.TextContentAsync()));
            Assert.Contains(ownerBadgeTexts, text => text.Equals("Default", StringComparison.InvariantCultureIgnoreCase));
            Assert.Contains(ownerBadgeTexts, text => text.Equals("Server-wide", StringComparison.InvariantCultureIgnoreCase));

            var managerBadges = await managerRow.Locator(".badge").AllAsync();
            var managerBadgeTexts = await Task.WhenAll(managerBadges.Select(async element => await element.TextContentAsync()));
            Assert.DoesNotContain(managerBadgeTexts, text => text.Equals("Default", StringComparison.InvariantCultureIgnoreCase));
            Assert.Contains(managerBadgeTexts, text => text.Equals("Server-wide", StringComparison.InvariantCultureIgnoreCase));

            var employeeBadges = await employeeRow.Locator(".badge").AllAsync();
            var employeeBadgeTexts = await Task.WhenAll(employeeBadges.Select(async element => await element.TextContentAsync()));
            Assert.DoesNotContain(employeeBadgeTexts, text => text.Equals("Default", StringComparison.InvariantCultureIgnoreCase));
            Assert.Contains(employeeBadgeTexts, text => text.Equals("Server-wide", StringComparison.InvariantCultureIgnoreCase));

            var guestBadges = await guestRow.Locator(".badge").AllAsync();
            var guestBadgeTexts = await Task.WhenAll(guestBadges.Select(async element => await element.TextContentAsync()));
            Assert.DoesNotContain(guestBadgeTexts, text => text.Equals("Default", StringComparison.InvariantCultureIgnoreCase));
            Assert.Contains(guestBadgeTexts, text => text.Equals("Server-wide", StringComparison.InvariantCultureIgnoreCase));
            await guestRow.Locator("#SetDefault").ClickAsync();
            await s.FindAlertMessage(partialText: "Role set default");

            existingServerRoles = await s.Page.Locator("table tr").AllAsync();
            foreach (var roleItem in existingServerRoles)
            {
                var text = await roleItem.TextContentAsync();
                if (text.Contains("owner", StringComparison.InvariantCultureIgnoreCase))
                {
                    ownerRow = roleItem;
                }
                else if (text.Contains("guest", StringComparison.InvariantCultureIgnoreCase))
                {
                    guestRow = roleItem;
                }
            }
            guestBadges = await guestRow.Locator(".badge").AllAsync();
            var guestBadgeTexts2 = await Task.WhenAll(guestBadges.Select(async element => await element.TextContentAsync()));
            Assert.Contains(guestBadgeTexts2, text => text.Equals("Default", StringComparison.InvariantCultureIgnoreCase));

            ownerBadges = await ownerRow.Locator(".badge").AllAsync();
            var ownerBadgeTexts2 = await Task.WhenAll(ownerBadges.Select(async element => await element.TextContentAsync()));
            Assert.DoesNotContain(ownerBadgeTexts2, text => text.Equals("Default", StringComparison.InvariantCultureIgnoreCase));
            await ownerRow.Locator("#SetDefault").ClickAsync();

            await s.FindAlertMessage(partialText: "Role set default");

            await s.CreateNewStore();
            await s.GoToStore(StoreNavPages.Roles);
            existingServerRoles = await s.Page.Locator("table tr").AllAsync();
            Assert.Equal(5, existingServerRoles.Count);
            var serverRoleTexts = await Task.WhenAll(existingServerRoles.Select(async element => await element.TextContentAsync()));
            Assert.Equal(4, serverRoleTexts.Count(text => text.Contains("Server-wide", StringComparison.InvariantCultureIgnoreCase)));

            foreach (var roleItem in existingServerRoles)
            {
                var text = await roleItem.TextContentAsync();
                if (text.Contains("owner", StringComparison.InvariantCultureIgnoreCase))
                {
                    ownerRow = roleItem;
                    break;
                }
            }

            await ownerRow.Locator("text=Remove").ClickAsync();
            await s.Page.WaitForLoadStateAsync();
            Assert.DoesNotContain("ConfirmContinue", await s.Page.ContentAsync());
            await s.Page.GoBackAsync();
            existingServerRoles = await s.Page.Locator("table tr").AllAsync();
            foreach (var roleItem in existingServerRoles)
            {
                var text = await roleItem.TextContentAsync();
                if (text.Contains("guest", StringComparison.InvariantCultureIgnoreCase))
                {
                    guestRow = roleItem;
                    break;
                }
            }

            await guestRow.Locator("text=Remove").ClickAsync();
            await s.Page.ClickAsync("#ConfirmContinue");
            await s.FindAlertMessage();

            await s.GoToStore(StoreNavPages.Roles);
            await s.ClickPagePrimary();

            Assert.Contains("Create role", await s.Page.ContentAsync());
            await s.ClickPagePrimary();
            await s.Page.Locator("#Role").FillAsync("store role");
            await s.ClickPagePrimary();
            await s.FindAlertMessage();

            existingServerRoles = await s.Page.Locator("table tr").AllAsync();
            foreach (var roleItem in existingServerRoles)
            {
                var text = await roleItem.TextContentAsync();
                if (text.Contains("store role", StringComparison.InvariantCultureIgnoreCase))
                {
                    guestRow = roleItem;
                    break;
                }
            }

            guestBadges = await guestRow.Locator(".badge").AllAsync();
            var guestBadgeTexts3 = await Task.WhenAll(guestBadges.Select(async element => await element.TextContentAsync()));
            Assert.DoesNotContain(guestBadgeTexts3, text => text.Equals("server-wide", StringComparison.InvariantCultureIgnoreCase));
            await s.GoToStore(StoreNavPages.Users);
            var options = await s.Page.Locator("#Role option").AllAsync();
            Assert.Equal(4, options.Count);
            var optionTexts = await Task.WhenAll(options.Select(async element => await element.TextContentAsync()));
            Assert.Contains(optionTexts, text => text.Equals("store role", StringComparison.InvariantCultureIgnoreCase));
            await s.CreateNewStore();
            await s.GoToStore(StoreNavPages.Roles);
            existingServerRoles = await s.Page.Locator("table tr").AllAsync();
            Assert.Equal(4, existingServerRoles.Count);
            var serverRoleTexts2 = await Task.WhenAll(existingServerRoles.Select(async element => await element.TextContentAsync()));
            Assert.Equal(3, serverRoleTexts2.Count(text => text.Contains("Server-wide", StringComparison.InvariantCultureIgnoreCase)));
            Assert.Equal(0, serverRoleTexts2.Count(text => text.Contains("store role", StringComparison.InvariantCultureIgnoreCase)));
            await s.GoToStore(StoreNavPages.Users);
            options = await s.Page.Locator("#Role option").AllAsync();
            Assert.Equal(3, options.Count);
            var optionTexts2 = await Task.WhenAll(options.Select(async element => await element.TextContentAsync()));
            Assert.DoesNotContain(optionTexts2, text => text.Equals("store role", StringComparison.InvariantCultureIgnoreCase));

            await s.Page.Locator("#Email").FillAsync(s.AsTestAccount().Email);
            await s.Page.Locator("#Role").SelectOptionAsync("Owner");
            await s.Page.ClickAsync("#AddUser");
            Assert.Contains("The user already has the role Owner.", await s.Page.Locator(".validation-summary-errors").TextContentAsync());
            await s.Page.Locator("#Role").SelectOptionAsync("Manager");
            await s.Page.ClickAsync("#AddUser");
            Assert.Contains("The user is the last owner. Their role cannot be changed.", await s.Page.Locator(".validation-summary-errors").TextContentAsync());

            await s.GoToStore(StoreNavPages.Roles);
            await s.ClickPagePrimary();
            await s.Page.Locator("#Role").FillAsync("Malice");

            await s.Page.EvaluateAsync($"document.getElementById('Policies')['{Policies.CanModifyServerSettings}']=new Option('{Policies.CanModifyServerSettings}', '{Policies.CanModifyServerSettings}', true,true);");

            await s.ClickPagePrimary();
            await s.FindAlertMessage();
            Assert.Contains("Malice", await s.Page.ContentAsync());
            Assert.DoesNotContain(Policies.CanModifyServerSettings, await s.Page.ContentAsync());
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

            string code = null;
            await s.Page.WaitForSelectorAsync("#LoginCode .qr-code");
            code = await s.Page.Locator("#LoginCode .qr-code").GetAttributeAsync("alt");
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
    }
}


