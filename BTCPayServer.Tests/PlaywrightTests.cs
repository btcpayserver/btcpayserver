using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
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
using ExchangeSharp;
using LNURL;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;
using NBitcoin;
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
            await s.GoToHome();
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
            await s.Page.ClickAsync("#StoreNav-CreatePointOfSale");
            await s.Page.FillAsync("#AppName", appName);
            await s.ClickPagePrimary();
            await s.FindAlertMessage(partialText: "App successfully created");
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
            Assert.Contains("aa@aa.com", await s.Page.ContentAsync());
            // Payment Request
            await s.Page.ClickAsync("#StoreNav-PaymentRequests");
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
            Assert.Contains("aa@aa.com", await s.Page.ContentAsync());
            var invoice = await s.Server.PayTester.GetService<InvoiceRepository>().GetInvoice(invoiceId);
            Assert.Equal("aa@aa.com", invoice.Metadata.BuyerEmail);

            //Custom Forms
            await s.GoToStore();
            await s.GoToStore(StoreNavPages.Forms);
            Assert.Contains("There are no forms yet.", await s.Page.ContentAsync());
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
            Assert.Contains("Custom Form 1", await s.Page.ContentAsync());
            await s.Page.GetByRole(AriaRole.Link, new() { Name = "Remove" }).ClickAsync();
            await s.Page.FillAsync("#ConfirmInput", "DELETE");
            await s.Page.ClickAsync("#ConfirmContinue");
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
            Assert.Contains("Custom Form 2", await s.Page.ContentAsync());
            await s.Page.GetByRole(AriaRole.Link, new() { Name = "Custom Form 2" }).ClickAsync();
            await s.Page.Locator("[name='Name']").ClearAsync();
            await s.Page.FillAsync("[name='Name']", "Custom Form 3");
            await s.ClickPagePrimary();
            await s.GoToStore(StoreNavPages.Forms);
            Assert.Contains("Custom Form 3", await s.Page.ContentAsync());
            await s.Page.ClickAsync("#StoreNav-PaymentRequests");
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
            var manager = tester.PayTester.GetService<UserManager<ApplicationUser>>();
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
            await s.GoToHome();
            await s.Logout();
            await s.GoToRegister();
            await s.RegisterNewUser(true);
            await s.GoToHome();
            await s.GoToServer(ServerNavPages.Users);

            // Manage user password reset
            await s.Page.Locator("#SearchTerm").ClearAsync();
            await s.Page.FillAsync("#SearchTerm", user.RegisterDetails.Email);
            await s.Page.Locator("#SearchTerm").PressAsync("Enter");
            var rows = s.Page.Locator("#UsersList tr.user-overview-row");
            Assert.Equal(1, await rows.CountAsync());
            Assert.Contains(user.RegisterDetails.Email, await rows.First.TextContentAsync());
            await s.Page.ClickAsync("#UsersList tr.user-overview-row:first-child .reset-password");
            await s.Page.FillAsync("#Password", "Password@1!");
            await s.Page.FillAsync("#ConfirmPassword", "Password@1!");
            await s.ClickPagePrimary();
            await s.FindAlertMessage(partialText: "Password successfully set");

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
        public async Task CanSetupEmailServer()
        {
            await using var s = CreatePlaywrightTester();
            await s.StartAsync();
            await s.RegisterNewUser(true);
            await s.CreateNewStore();

            // Ensure empty server settings
            await s.GoToUrl("/server/emails");
            if (await s.Page.Locator("#ResetPassword").IsVisibleAsync())
            {
                await s.Page.ClickAsync("#ResetPassword");
                await s.FindAlertMessage(partialText: "Email server password reset");
            }
            await s.Page.Locator("#Settings_Login").ClearAsync();
            await s.Page.Locator("#Settings_From").ClearAsync();
            await s.ClickPagePrimary();

            // Store Emails without server fallback
            await s.GoToStore();
            await s.GoToStore(StoreNavPages.Emails);
            Assert.Equal(0, await s.Page.Locator("#IsCustomSMTP").CountAsync());
            await s.Page.ClickAsync("#ConfigureEmailRules");
            Assert.Contains("You need to configure email settings before this feature works", await s.Page.ContentAsync());

            // Server Emails
            await s.GoToUrl("/server/emails");
            if ((await s.Page.ContentAsync()).Contains("Configured"))
            {
                await s.Page.ClickAsync("#ResetPassword");
                await s.FindAlertMessage();
            }
            await CanSetupEmailCore(s);

            // Store Emails with server fallback
            await s.GoToStore();
            await s.GoToStore(StoreNavPages.Emails);
            Assert.False(await s.Page.Locator("#IsCustomSMTP").IsCheckedAsync());
            await s.Page.ClickAsync("#ConfigureEmailRules");
            Assert.DoesNotContain("You need to configure email settings before this feature works", await s.Page.ContentAsync());

            await s.GoToStore(StoreNavPages.Emails);
            await s.Page.ClickAsync("#IsCustomSMTP");
            await CanSetupEmailCore(s);

            // Store Email Rules
            await s.Page.ClickAsync("#ConfigureEmailRules");
            await s.Page.Locator("text=There are no rules yet.").WaitForAsync();
            Assert.DoesNotContain("id=\"SaveEmailRules\"", await s.Page.ContentAsync());
            Assert.DoesNotContain("You need to configure email settings before this feature works", await s.Page.ContentAsync());

            await s.Page.ClickAsync("#CreateEmailRule");
            await s.Page.Locator("#Trigger").SelectOptionAsync(new[] { "InvoicePaymentSettled" });
            await s.Page.FillAsync("#To", "test@gmail.com");
            await s.Page.ClickAsync("#CustomerEmail");
            await s.Page.FillAsync("#Subject", "Thanks!");
            await s.Page.Locator(".note-editable").FillAsync("Your invoice is settled");
            await s.Page.ClickAsync("#SaveEmailRules");
            await s.FindAlertMessage();
            // we now have a rule
            Assert.DoesNotContain("There are no rules yet.", await s.Page.ContentAsync());
            Assert.Contains("test@gmail.com", await s.Page.ContentAsync());

            await s.GoToStore(StoreNavPages.Emails);
            Assert.True(await s.Page.Locator("#IsCustomSMTP").IsCheckedAsync());
        }

        [Fact]
        public async Task NewUserLogin()
        {
            await using var s = CreatePlaywrightTester();
            await s.StartAsync();
            //Register & Log Out
            var email = await s.RegisterNewUser();
            await s.GoToHome();
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
            await s.GoToHome();
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
            await s.Page.FillAsync("#ConfirmInput", "DELETE");
            await s.Page.ClickAsync("#ConfirmContinue");
            Assert.Contains("/login", s.Page.Url);
        }


        private static async Task CanSetupEmailCore(PlaywrightTester s)
        {
            await s.Page.Locator("#QuickFillDropdownToggle").ScrollIntoViewIfNeededAsync();
            await s.Page.ClickAsync("#QuickFillDropdownToggle");
            await s.Page.ClickAsync("#quick-fill .dropdown-menu .dropdown-item:first-child");
            await s.Page.Locator("#Settings_Login").ClearAsync();
            await s.Page.FillAsync("#Settings_Login", "test@gmail.com");
            await s.Page.Locator("#Settings_Password").ClearAsync();
            await s.Page.FillAsync("#Settings_Password", "mypassword");
            await s.Page.Locator("#Settings_From").ClearAsync();
            await s.Page.FillAsync("#Settings_From", "Firstname Lastname <email@example.com>");
            await s.ClickPagePrimary();
            await s.FindAlertMessage(partialText: "Email settings saved");
            Assert.Contains("Configured", await s.Page.ContentAsync());
            await s.Page.Locator("#Settings_Login").ClearAsync();
            await s.Page.FillAsync("#Settings_Login", "test_fix@gmail.com");
            await s.ClickPagePrimary();
            await s.FindAlertMessage(partialText: "Email settings saved");
            Assert.Contains("Configured", await s.Page.ContentAsync());
            Assert.Contains("test_fix", await s.Page.ContentAsync());
            await s.Page.Locator("#ResetPassword").PressAsync("Enter");
            await s.FindAlertMessage(partialText: "Email server password reset");
            Assert.DoesNotContain("Configured", await s.Page.ContentAsync());
            Assert.Contains("test_fix", await s.Page.ContentAsync());
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
            await s.Page.ClickAsync("#StoreNav-General");
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
            await s.Page.ClickAsync($"#StoreNav-Wallet{cryptoCode}");
            await s.Page.ClickAsync("#WalletNav-Send");
            //you cannot use the Sign with NBX option without saving private keys when generating the wallet.
            Assert.DoesNotContain("nbx-seed", await s.Page.ContentAsync());
            Assert.Equal(0, await s.Page.Locator("#GoBack").CountAsync());
            await s.Page.ClickAsync("#SignTransaction");
            await s.Page.WaitForSelectorAsync("text=Destination Address field is required");
            Assert.Equal(0, await s.Page.Locator("#GoBack").CountAsync());
            await s.Page.ClickAsync("#CancelWizard");
            await s.Page.ClickAsync("#WalletNav-Receive");

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

            await s.Page.ClickAsync($"#StoreNav-Wallet{cryptoCode}");
            await s.Page.ClickAsync("#WalletNav-Send");

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
            await s.Page.ClickAsync("#WalletNav-Send");
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
            await s.Page.ClickAsync("#StoreNav-PaymentRequests");
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
            await s.Page.ClickAsync("#StoreNav-PaymentRequests");
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
            await s.Page.ClickAsync("#StoreNav-PaymentRequests");
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
            await s.GoToHome();
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
            await s.Page.AssertNoError();
            await s.FindAlertMessage(partialText: "Account created. The new account requires approval by an admin before you can log in");
            Assert.Contains("/login", s.Page.Url);

            var unapproved = s.AsTestAccount();
            await s.LogIn(unapproved.RegisterDetails.Email, unapproved.RegisterDetails.Password);
            await s.FindAlertMessage(StatusMessageModel.StatusSeverity.Warning, partialText: "Your user account requires approval by an admin before you can log in");
            Assert.Contains("/login", s.Page.Url);

            await s.GoToLogin();
            await s.LogIn(admin.RegisterDetails.Email, admin.RegisterDetails.Password);
            await s.GoToHome();

            await TestUtils.EventuallyAsync(async () =>
            {
                Assert.Equal("1", await s.Page.Locator("#NotificationsBadge").TextContentAsync());
            });

            await s.Page.Locator("#NotificationsHandle").ClickAsync();
            Assert.Matches($"New user {unapproved.RegisterDetails.Email} requires approval", await s.Page.Locator("#NotificationsList .notification").TextContentAsync());
            await s.Page.Locator("#NotificationsMarkAllAsSeen").ClickAsync();

            await s.GoToServer(ServerNavPages.Policies);
            Assert.True(await s.Page.Locator("#EnableRegistration").IsCheckedAsync());
            Assert.True(await s.Page.Locator("#RequiresUserApproval").IsCheckedAsync());
            await s.Page.Locator("#RequiresUserApproval").ClickAsync();
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
        public async Task CanSetupEmailRules()
        {
            await using var s = CreatePlaywrightTester();
            await s.StartAsync();
            await s.RegisterNewUser(true);
            await s.CreateNewStore();

            await s.GoToStore(StoreNavPages.Emails);
            await s.Page.ClickAsync("#ConfigureEmailRules");
            Assert.Contains("There are no rules yet.", await s.Page.ContentAsync());
            Assert.Contains("You need to configure email settings before this feature works", await s.Page.ContentAsync());

            await s.Page.ClickAsync("#CreateEmailRule");
            await s.Page.SelectOptionAsync("#Trigger", "InvoiceCreated");
            await s.Page.FillAsync("#To", "invoicecreated@gmail.com");
            await s.Page.ClickAsync("#CustomerEmail");
            await s.Page.ClickAsync("#SaveEmailRules");

            await s.FindAlertMessage();
            Assert.DoesNotContain("There are no rules yet.", await s.Page.ContentAsync());
            Assert.Contains("invoicecreated@gmail.com", await s.Page.ContentAsync());
            Assert.Contains("Invoice {Invoice.Id} created", await s.Page.ContentAsync());
            Assert.Contains("Yes", await s.Page.ContentAsync());

            await s.Page.ClickAsync("#CreateEmailRule");
            await s.Page.SelectOptionAsync("#Trigger", "PaymentRequestStatusChanged");
            await s.Page.FillAsync("#To", "statuschanged@gmail.com");
            await s.Page.FillAsync("#Subject", "Status changed!");
            await s.Page.Locator(".note-editable").FillAsync("Your Payment Request Status is Changed");
            await s.Page.ClickAsync("#SaveEmailRules");

            await s.FindAlertMessage();
            Assert.Contains("statuschanged@gmail.com", await s.Page.ContentAsync());
            Assert.Contains("Status changed!", await s.Page.ContentAsync());

            var editButtons = s.Page.GetByRole(AriaRole.Link, new() { Name = "Edit" });
            Assert.True(await editButtons.CountAsync() >= 2);
            await editButtons.Nth(1).ClickAsync();

            await s.Page.Locator("#To").ClearAsync();
            await s.Page.FillAsync("#To", "changedagain@gmail.com");
            await s.Page.ClickAsync("#SaveEmailRules");

            await s.FindAlertMessage();
            Assert.Contains("changedagain@gmail.com", await s.Page.ContentAsync());
            Assert.DoesNotContain("statuschanged@gmail.com", await s.Page.ContentAsync());

            var deleteLinks = s.Page.GetByRole(AriaRole.Link, new() { Name = "Remove" });
            Assert.Equal(2, await deleteLinks.CountAsync());

            await deleteLinks.First.ClickAsync();
            await s.Page.FillAsync("#ConfirmInput", "REMOVE");
            await s.Page.ClickAsync("#ConfirmContinue");

            await s.FindAlertMessage();
            deleteLinks = s.Page.GetByRole(AriaRole.Link, new() { Name = "Remove" });
            Assert.Equal(1, await deleteLinks.CountAsync());

            await deleteLinks.First.ClickAsync();
            await s.Page.FillAsync("#ConfirmInput", "REMOVE");
            await s.Page.ClickAsync("#ConfirmContinue");

            await s.FindAlertMessage();
            Assert.Contains("There are no rules yet.", await s.Page.ContentAsync());
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
            Assert.Contains("To create an invoice, you need to", await s.Page.ContentAsync());

            await s.AddDerivationScheme();
            await s.GoToInvoices();
            var invoiceId = await s.CreateInvoice();
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
            await s.Page.ClickAsync($"#StoreNav-Wallet{cryptoCode}");
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
            await s.GoToHome();
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
            await s.Page.FillAsync("#ConfirmInput", "DELETE");
            await s.Page.ClickAsync("#ConfirmContinue");
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
            await s.GoToHome();
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
            await s.GoToWallet(walletId);
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
            await s.Page.Locator("#StoreNav-LightningBTC .btcpay-status--pending").WaitForAsync();

            // Set up LN node
            await s.AddLightningNode();
            await s.Page.Locator("#StoreNav-LightningBTC .btcpay-status--enabled").WaitForAsync();

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
            await s.Page.Locator("#StoreNav-LightningBTC .btcpay-status--disabled").WaitForAsync();

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
            await Expect(newPage.Locator("body")).ToContainTextAsync("Description Edit");
            await Expect(newPage.Locator("body")).ToContainTextAsync("PP1 Edited");
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

    }
}


