using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
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
    [Collection(nameof(NonParallelizableCollectionDefinition))]
    public class PlaywrightTests(ITestOutputHelper helper) : UnitTestBase(helper)
    {
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
            await s.Page.ClickAsync("#ViewApp");
            var popOutPage = await s.Page.Context.WaitForPageAsync();
            string invoiceId;
            await using (var o = await s.SwitchPage(popOutPage))
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
    }
}
