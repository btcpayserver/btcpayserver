using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Views.Manage;
using BTCPayServer.Views.Server;
using BTCPayServer.Views.Stores;
using Microsoft.AspNetCore.Identity;
using Microsoft.Playwright;
using NBitcoin;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests
{
    [Trait("Playwright", "Playwright")]
    [Collection(nameof(UISharedServerCollection))]
    public class PlaywrightTests(Fixtures.UISharedServerFixture fixture, ITestOutputHelper helper)
        : UnitTestBase(helper)
    {


        [Fact]
        public async Task CanNavigateServerSettings()
        {
            var s = await fixture.GetPlaywrightTester(helper);
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
            var s = await fixture.GetPlaywrightTester(helper);
            await s.AdminNewStoreWithBTC();
            // Point Of Sale
            var appName = $"PoS-{Guid.NewGuid().ToString()[..21]}";
            await s.Page.ClickAsync("#StoreNav-CreatePointOfSale");
            await s.Page.FillAsync("#AppName", appName);
            await s.ClickPagePrimary();
            await s.FindAlertMessage(partialText: "App successfully created");
            await s.Page.SelectOptionAsync("#FormId", "Email");
            await s.ClickPagePrimary();
            await s.FindAlertMessage(partialText: "App updated");
            await s.Page.ClickAsync("#ViewApp");
            var popOutPage = await s.Page.Context.WaitForPageAsync();
            await popOutPage.Locator("button[type='submit']").First.ClickAsync();
            await popOutPage.FillAsync("[name='buyerEmail']", "aa@aa.com");
            await popOutPage.ClickAsync("input[type='submit']");
            await s.PayInvoiceAsync(popOutPage, true);
            var invoiceId = popOutPage.Url[(popOutPage.Url.LastIndexOf("/", StringComparison.Ordinal) + 1)..];
            await popOutPage.CloseAsync();

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
            await s.Page.ClickAsync("#ViewPaymentRequest");
            popOutPage = await s.Page.Context.WaitForPageAsync();
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
            await s.PayInvoiceAsync(s.Page, true, 0.001m);
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
            var s = await fixture.GetPlaywrightTester(helper);
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
            var s = await fixture.GetPlaywrightTester(helper);
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
            var s = await fixture.GetPlaywrightTester(helper);
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
            var s = await fixture.GetPlaywrightTester(helper);
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
            Assert.Contains("There are no rules yet.", await s.Page.ContentAsync());
            Assert.DoesNotContain("id=\"SaveEmailRules\"", await s.Page.ContentAsync());
            Assert.DoesNotContain("You need to configure email settings before this feature works", await s.Page.ContentAsync());

            await s.Page.ClickAsync("#CreateEmailRule");
            await s.Page.Locator("#Trigger").SelectOptionAsync(new[] { "InvoicePaymentSettled" });
            await s.Page.FillAsync("#To", "test@gmail.com");
            await s.Page.ClickAsync("#CustomerEmail");
            await s.Page.FillAsync("#Subject", "Thanks!");
            await s.Page.Locator(".note-editable").FillAsync("Your invoice is settled");
            await s.Page.ClickAsync("#SaveEmailRules");
            // we now have a rule
            Assert.DoesNotContain("There are no rules yet.", await s.Page.ContentAsync());
            Assert.Contains("test@gmail.com", await s.Page.ContentAsync());

            await s.GoToStore(StoreNavPages.Emails);
            Assert.True(await s.Page.Locator("#IsCustomSMTP").IsCheckedAsync());
        }

        [Fact]
        public async Task NewUserLogin()
        {
            var s = await fixture.GetPlaywrightTester(helper);
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
        public async Task CanModifyRates()
        {
            var tester = await fixture.GetPlaywrightTester(helper);
            await tester.RegisterNewUser(true);
            await tester.CreateNewStore();
            await tester.GoToStore();
            await tester.GoToStore(StoreNavPages.Rates);

            async Task Test(string pairs)
            {
                await tester.Page.FillAsync("#ScriptTest", pairs);
                await tester.Page.ClickAsync("button[value='Test']");
            }

            foreach (var fallback in new[] { false, true })
            {
                tester.TestLogs.LogInformation($"Testing rates (fallback={fallback})");
                var source = fallback ? "FallbackSource" : "PrimarySource";
                var toggleScriptSelector = $"#{source}_ShowScripting_submit";

                var l = tester.Page.Locator(toggleScriptSelector);
                await l.WaitForAsync();
                Assert.DoesNotContain("btcpay-toggle--active", await l.GetAttributeAsync("class"));
                Assert.Equal(fallback ? "" : CoinGeckoRateProvider.CoinGeckoName, await tester.Page.Locator($"#{source}_PreferredExchange").InputValueAsync());

                Assert.Equal("0", await tester.Page.InputValueAsync("#Spread"));
                await tester.Page.SelectOptionAsync($"#{source}_PreferredExchange", "bitflyer");
                await tester.ClickPagePrimary();
                await tester.FindAlertMessage();

                await tester.Page.FillAsync("#Spread", "10");
                await Test("BTC_JPY,BTC_CAD");
                var rules = await tester.Page.Locator(".testresult .testresult_rule").AllAsync();
                if (fallback)
                {
                    // If fallback is set, we should see the results of the fallback too
                    Assert.Contains("(coingecko(BTC_CAD)) * (0.9, 1.1) = 4050.0", await rules[0].InnerTextAsync());
                    Assert.Contains("(ERR_RATE_UNAVAILABLE(bitflyer, BTC_CAD)) * (0.9, 1.1)", await rules[1].InnerTextAsync());
                    Assert.Contains("(ERR_RATE_UNAVAILABLE(coingecko, BTC_JPY)) * (0.9, 1.1)", await rules[2].InnerTextAsync());
                    Assert.Contains("(bitflyer(BTC_JPY)) * (0.9, 1.1) = 630000.0", await rules[3].InnerTextAsync());
                }
                else
                {
                    Assert.Contains("(ERR_RATE_UNAVAILABLE(bitflyer, BTC_CAD))", await rules[0].InnerTextAsync());
                    Assert.Contains("(bitflyer(BTC_JPY)) * (0.9, 1.1) =", await rules[1].InnerTextAsync());
                }

                await tester.ClickPagePrimary();
                await tester.FindAlertMessage();

                await tester.Page.ClickAsync(toggleScriptSelector);
                await tester.FindAlertMessage(partialText: "Rate rules scripting activated");

                l = tester.Page.Locator(toggleScriptSelector);
                await l.WaitForAsync();
                Assert.Contains("btcpay-toggle--active", await l.GetAttributeAsync("class"));

                await tester.Page.InputValueAsync($"#{source}_Script");
                var defaultScript = await tester.Page.GetAttributeAsync($"#{source}_DefaultScript", "data-defaultScript");
                Assert.Contains("X_JPY = bitbank(X_JPY);", defaultScript);
                Assert.Contains("X_TRY = btcturk(X_TRY);", defaultScript);

                await Test("BTC_JPY");
                rules = await tester.Page.Locator(".testresult .testresult_rule").AllAsync();
                if (fallback)
                {
                    Assert.Contains("(ERR_RATE_UNAVAILABLE(coingecko, BTC_JPY)) * (0.9, 1.1)", await rules[0].InnerTextAsync());
                    Assert.Contains("(bitflyer(BTC_JPY)) * (0.9, 1.1) = 630000.0", await rules[1].InnerTextAsync());
                }
                else
                {
                    Assert.Contains("(bitflyer(BTC_JPY)) * (0.9, 1.1) =", await rules[0].InnerTextAsync());
                }

                await tester.Page.FillAsync("#Spread", "50");
                await tester.Page.FillAsync($"#{source}_Script","""
                                                                DOGE_X = bitpay(DOGE_BTC) * BTC_X;
                                                                X_CAD = ndax(X_CAD);
                                                                X_X = coingecko(X_X);
                                                                """);
                await Test("BTC_USD,BTC_CAD,DOGE_USD,DOGE_CAD");
                rules = await tester.Page.Locator(".testresult").AllAsync();
                if (fallback)
                {
                    Assert.Equal(8, rules.Count);
                }
                else
                {
                    Assert.Equal(4, rules.Count);
                    foreach (var rule in rules)
                    {
                        await rule.Locator(".testresult_success").WaitForAsync();
                        Assert.Contains("(0.5, 1.5)", await rule.InnerTextAsync());
                    }
                }

                await tester.ClickPagePrimary();
                Assert.Equal("50", await tester.Page.InputValueAsync("#Spread"));
                var beforeReset = await tester.Page.InputValueAsync($"#{source}_Script");
                Assert.Contains("X_CAD = ndax(X_CAD);", beforeReset);
                await tester.Page.ClickAsync($"#{source}_DefaultScript");
                var afterReset = await tester.Page.InputValueAsync($"#{source}_Script");
                Assert.NotEqual(beforeReset, afterReset);

                await tester.Page.ClickAsync(toggleScriptSelector);
                await tester.ConfirmModal();
                await tester.FindAlertMessage(partialText: "Rate rules scripting deactivated");

                l = tester.Page.Locator(toggleScriptSelector);
                await l.WaitForAsync();
                Assert.DoesNotContain("btcpay-toggle--active", await l.GetAttributeAsync("class"));

                if (fallback)
                {
                    await tester.Page.ClickAsync("#HasFallback");
                    await tester.ClickPagePrimary();
                    await tester.Page.FillAsync("#Spread", "0");
                    await tester.ClickPagePrimary();
                }
                else
                {
                    await tester.Page.ClickAsync("#HasFallback");
                    await tester.ClickPagePrimary();
                    await tester.FindAlertMessage();
                    await tester.Page.SelectOptionAsync($"#{source}_PreferredExchange", CoinGeckoRateProvider.CoinGeckoName);
                    await tester.Page.FillAsync("#Spread", "0");
                    await tester.ClickPagePrimary();
                }
            }

            await tester.Page.ClickAsync("#HasFallback");
            await tester.ClickPagePrimary();
            await tester.FindAlertMessage();
            await tester.Page.SelectOptionAsync($"#PrimarySource_PreferredExchange", CoinGeckoRateProvider.CoinGeckoName);
            await tester.Page.SelectOptionAsync($"#FallbackSource_PreferredExchange", "bitflyer");

            await tester.Page.FillAsync("#DefaultCurrencyPairs", "BTC_JPY,BTC_CAD");
            await tester.ClickPagePrimary();

            using var req = await tester.Server.PayTester.HttpClient.GetAsync($"/api/rates?storeId={tester.StoreId}");
            var rates = JArray.Parse(await req.Content.ReadAsStringAsync());
            foreach (var expected in new[]
                     {
                         (name: "BTC_JPY", rate: 700000m),
                         (name: "BTC_CAD", rate: 4500m)
                     })
            {
                // JPY is handled by the fallback, CAD by the primary
                var r = rates.First(r => r["currencyPair"].ToString() == expected.name);
                Assert.Equal(expected.rate, r["rate"]!.Value<decimal>());
            }

            await tester.GenerateWallet();
            var invoiceId = await tester.CreateInvoice(currency: "JPY", amount: 700000m);
            var client = await tester.AsTestAccount().CreateClient();
            var paymentMethods = await client.GetInvoicePaymentMethods(tester.StoreId, invoiceId);
            Assert.Equal(1.0m, paymentMethods[0].Amount);

            // The fallback doesn't support JPY anymore
            var a = await client.GetStoreRateConfiguration(tester.StoreId, fallback: false);
            var b = await client.GetStoreRateConfiguration(tester.StoreId, fallback: true);
            Assert.Equal("coingecko", a.PreferredSource);
            Assert.Equal("bitflyer", b.PreferredSource);
            await client.UpdateStoreRateConfiguration(tester.StoreId, new()
            {
                PreferredSource = "coingecko"
            }, true);
            b = await client.GetStoreRateConfiguration(tester.StoreId, fallback: true);
            Assert.Equal("coingecko", b.PreferredSource);
            await tester.CreateInvoice(currency: "JPY", amount: 700000m, expectedSeverity: StatusMessageModel.StatusSeverity.Error);
        }
    }
}
