using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Data;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Views.Manage;
using BTCPayServer.Views.Server;
using BTCPayServer.Views.Stores;
using Microsoft.AspNetCore.Identity;
using Microsoft.Playwright;
using NBitcoin;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests
{
    [Trait("Playwright", "Playwright")]
    [Collection(nameof(NonParallelizableCollectionDefinition))]
    public class PlaywrightTests : UnitTestBase
    {
        public PlaywrightTests(ITestOutputHelper helper) : base(helper)
        {
        }

        [Fact]
        public async Task CanNavigateServerSettings()
        {
            using var s = CreatePlaywrightTester();
            await s.StartAsync();
            await s.RegisterNewUser(true);
            await s.GoToHome();
            await s.GoToServer();
            await s.Page.AssertNoError();
            await s.ClickOnAllSectionLinks();
            await s.GoToServer(ServerNavPages.Services);
            s.TestLogs.LogInformation("Let's check if we can access the logs");
            await s.Page.GetByRole(AriaRole.Link, new() { Name = "Logs" }).ClickAsync();
            await s.Page.Locator("a:has-text('.log')").First.ClickAsync();
            Assert.Contains("Starting listening NBXplorer", await s.Page.ContentAsync());
        }


        [Fact]
        public async Task CanUseForms()
        {
            using var s = CreatePlaywrightTester();
            await s.StartAsync();
            await s.InitializeBTCPayServer();
            // Point Of Sale
            var appName = $"PoS-{Guid.NewGuid().ToString()[..21]}";
            await s.Page.Locator("#StoreNav-CreatePointOfSale").ClickAsync();
            await s.Page.Locator("#AppName").FillAsync(appName);
            await s.ClickPagePrimary();
            var textContent = await (await s.FindAlertMessage()).TextContentAsync();
            Assert.Contains("App successfully created", textContent);
            await s.Page.SelectOptionAsync("#FormId", "Email");
            await s.ClickPagePrimary();
            textContent = await (await s.FindAlertMessage()).TextContentAsync();
            Assert.Contains("App updated", textContent);
            await s.Page.Locator("#ViewApp").ClickAsync();
            var popOutPage = await s.Page.Context.WaitForPageAsync();
            await popOutPage.Locator("button[type='submit']").First.ClickAsync();
            await popOutPage.Locator("[name='buyerEmail']").FillAsync("aa@aa.com");
            await popOutPage.Locator("input[type='submit']").ClickAsync();
            await s.PayInvoiceAsync(popOutPage, true);
            var invoiceId = popOutPage.Url[(popOutPage.Url.LastIndexOf("/", StringComparison.Ordinal) + 1)..];
            await popOutPage.CloseAsync();

            await s.Page.Context.Pages.First().BringToFrontAsync();
            await s.GoToUrl($"/invoices/{invoiceId}/");
            Assert.Contains("aa@aa.com", await s.Page.ContentAsync());
            // Payment Request
            await s.Page.Locator("#StoreNav-PaymentRequests").ClickAsync();
            await s.ClickPagePrimary();
            await s.Page.Locator("#Title").FillAsync("Pay123");
            await s.Page.Locator("#Amount").FillAsync("700");
            await s.Page.SelectOptionAsync("#FormId", "Email");
            await s.ClickPagePrimary();
            await s.Page.Locator("a[id^='Edit-']").First.ClickAsync();
            var editUrl = new Uri(s.Page.Url);
            await s.Page.Locator("#ViewPaymentRequest").ClickAsync();
            popOutPage = await s.Page.Context.WaitForPageAsync();
            await popOutPage.Locator("[data-test='form-button']").ClickAsync();
            Assert.Contains("Enter your email", await popOutPage.ContentAsync());
            await popOutPage.Locator("input[name='buyerEmail']").FillAsync("aa@aa.com");
            await popOutPage.Locator("#page-primary").ClickAsync();
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
            await s.Page.Locator("[name='Name']").FillAsync("Custom Form 1");
            await s.Page.Locator("#ApplyEmailTemplate").ClickAsync();
            await s.Page.Locator("#CodeTabButton").ClickAsync();
            await s.Page.Locator("#CodeTabPane").WaitForAsync();
            var config = await s.Page.Locator("[name='FormConfig']").InputValueAsync();
            Assert.Contains("buyerEmail", config);
            await s.Page.Locator("[name='FormConfig']").ClearAsync();
            await s.Page.Locator("[name='FormConfig']").FillAsync(config.Replace("Enter your email", "CustomFormInputTest"));
            await s.ClickPagePrimary();
            await s.Page.Locator("#ViewForm").ClickAsync();
            var formUrl = s.Page.Url;
            Assert.Contains("CustomFormInputTest", await s.Page.ContentAsync());
            await s.Page.Locator("[name='buyerEmail']").FillAsync("aa@aa.com");
            await s.Page.Locator("input[type='submit']").ClickAsync();
            await s.PayInvoiceAsync(s.Page, true, 0.001m);
            var result = await s.Server.PayTester.HttpClient.GetAsync(formUrl);
            Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
            await s.GoToHome();
            await s.GoToStore();
            await s.GoToStore(StoreNavPages.Forms);
            Assert.Contains("Custom Form 1", await s.Page.ContentAsync());
            await s.Page.GetByRole(AriaRole.Link, new() { Name = "Remove" }).ClickAsync();
            await s.Page.Locator("#ConfirmInput").FillAsync("DELETE");
            await s.Page.Locator("#ConfirmContinue").ClickAsync();
            Assert.DoesNotContain("Custom Form 1", await s.Page.ContentAsync());
            await s.ClickPagePrimary();
            await s.Page.Locator("[name='Name']").FillAsync("Custom Form 2");
            await s.Page.Locator("#ApplyEmailTemplate").ClickAsync();
            await s.Page.Locator("#CodeTabButton").ClickAsync();
            await s.Page.Locator("#CodeTabPane").WaitForAsync();
            await s.Page.Locator("input[type='checkbox'][name='Public']").SetCheckedAsync(true);
            await s.Page.Locator("[name='FormConfig']").ClearAsync();
            await s.Page.Locator("[name='FormConfig']").FillAsync(config.Replace("Enter your email", "CustomFormInputTest2"));
            await s.ClickPagePrimary();
            await s.Page.Locator("#ViewForm").ClickAsync();
            formUrl = s.Page.Url;
            result = await s.Server.PayTester.HttpClient.GetAsync(formUrl);
            Assert.NotEqual(HttpStatusCode.NotFound, result.StatusCode);
            await s.GoToHome();
            await s.GoToStore();
            await s.GoToStore(StoreNavPages.Forms);
            Assert.Contains("Custom Form 2", await s.Page.ContentAsync());
            await s.Page.GetByRole(AriaRole.Link, new() { Name = "Custom Form 2" }).ClickAsync();
            await s.Page.Locator("[name='Name']").ClearAsync();
            await s.Page.Locator("[name='Name']").FillAsync("Custom Form 3");
            await s.ClickPagePrimary();
            await s.GoToStore(StoreNavPages.Forms);
            Assert.Contains("Custom Form 3", await s.Page.ContentAsync());
            await s.Page.Locator("#StoreNav-PaymentRequests").ClickAsync();
            await s.ClickPagePrimary();
            var selectOptions = await s.Page.Locator("#FormId >> option").CountAsync();
            Assert.Equal(4, selectOptions);
        }


        [Fact]
        public async Task CanChangeUserMail()
        {
            using var s = CreatePlaywrightTester();
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
            await s.Page.Locator("#Email").FillAsync(u2.RegisterDetails.Email);
            await s.ClickPagePrimary();
            var alert = await s.FindAlertMessage(StatusMessageModel.StatusSeverity.Error);
            Assert.Contains("The email address is already in use with an other account.", await alert.TextContentAsync());
            await s.GoToProfile();
            await s.Page.Locator("#Email").ClearAsync();
            var changedEmail = Guid.NewGuid() + "@lol.com";
            await s.Page.Locator("#Email").FillAsync(changedEmail);
            await s.ClickPagePrimary();
            await s.FindAlertMessage();
            var manager = tester.PayTester.GetService<UserManager<ApplicationUser>>();
            Assert.NotNull(await manager.FindByNameAsync(changedEmail));
            Assert.NotNull(await manager.FindByEmailAsync(changedEmail));
        }

        [Fact]
        public async Task CanManageUsers()
        {
            using var s = CreatePlaywrightTester();
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
            var rows = s.Page.Locator("#UsersList tr.user-overview-row");
            await s.Page.Locator("#SearchTerm").ClearAsync();
            await s.Page.Locator("#SearchTerm").FillAsync(user.RegisterDetails.Email);
            await s.Page.Locator("#SearchTerm").PressAsync("Enter");
            rows = s.Page.Locator("#UsersList tr.user-overview-row");
            Assert.Equal(1, await rows.CountAsync());
            Assert.Contains(user.RegisterDetails.Email, await rows.First.TextContentAsync());
            await s.Page.Locator("#UsersList tr.user-overview-row:first-child .reset-password").ClickAsync();
            await s.Page.Locator("#Password").FillAsync("Password@1!");
            await s.Page.Locator("#ConfirmPassword").FillAsync("Password@1!");
            await s.ClickPagePrimary();
            var passwordSetAlert = await s.FindAlertMessage(StatusMessageModel.StatusSeverity.Success);
            Assert.Contains("Password successfully set", await passwordSetAlert.TextContentAsync());

            // Manage user status (disable and enable)
            // Disable user
            await s.Page.Locator("#SearchTerm").ClearAsync();
            await s.Page.Locator("#SearchTerm").FillAsync(user.RegisterDetails.Email);
            await s.Page.Locator("#SearchTerm").PressAsync("Enter");
            rows = s.Page.Locator("#UsersList tr.user-overview-row");
            Assert.Equal(1, await rows.CountAsync());
            Assert.Contains(user.RegisterDetails.Email, await rows.First.TextContentAsync());
            await s.Page.Locator("#UsersList tr.user-overview-row:first-child .disable-user").ClickAsync();
            await s.Page.Locator("#ConfirmContinue").ClickAsync();
            var disabledUserAlert = await s.FindAlertMessage(StatusMessageModel.StatusSeverity.Success);
            Assert.Contains("User disabled", await disabledUserAlert.TextContentAsync());
            //Enable user
            await s.Page.Locator("#SearchTerm").ClearAsync();
            await s.Page.Locator("#SearchTerm").FillAsync(user.RegisterDetails.Email);
            await s.Page.Locator("#SearchTerm").PressAsync("Enter");
            rows = s.Page.Locator("#UsersList tr.user-overview-row");
            Assert.Equal(1, await rows.CountAsync());
            Assert.Contains(user.RegisterDetails.Email, await rows.First.TextContentAsync());
            await s.Page.Locator("#UsersList tr.user-overview-row:first-child .enable-user").ClickAsync();
            await s.Page.Locator("#ConfirmContinue").ClickAsync();
            var enabledUserAlert = await s.FindAlertMessage(StatusMessageModel.StatusSeverity.Success);
            Assert.Contains("User enabled", await enabledUserAlert.TextContentAsync());

            // Manage user details (edit)
            await s.Page.Locator("#SearchTerm").ClearAsync();
            await s.Page.Locator("#SearchTerm").FillAsync(user.RegisterDetails.Email);
            await s.Page.Locator("#SearchTerm").PressAsync("Enter");
            rows = s.Page.Locator("#UsersList tr.user-overview-row");
            Assert.Equal(1, await rows.CountAsync());
            Assert.Contains(user.RegisterDetails.Email, await rows.First.TextContentAsync());
            await s.Page.Locator("#UsersList tr.user-overview-row:first-child .user-edit").ClickAsync();
            await s.Page.Locator("#Name").FillAsync("Test User");
            await s.ClickPagePrimary();
            var editUserAlert = await s.FindAlertMessage(StatusMessageModel.StatusSeverity.Success);
            Assert.Contains("User successfully updated", await editUserAlert.TextContentAsync());

            // Manage user deletion
            await s.GoToServer(ServerNavPages.Users);
            await s.Page.Locator("#SearchTerm").ClearAsync();
            await s.Page.Locator("#SearchTerm").FillAsync(user.RegisterDetails.Email);
            await s.Page.Locator("#SearchTerm").PressAsync("Enter");
            rows = s.Page.Locator("#UsersList tr.user-overview-row");
            Assert.Equal(1, await rows.CountAsync());
            Assert.Contains(user.RegisterDetails.Email, await rows.First.TextContentAsync());
            await s.Page.Locator("#UsersList tr.user-overview-row:first-child .delete-user").ClickAsync();
            await s.Page.Locator("#ConfirmContinue").ClickAsync();
            var userDeletionAlert = await s.FindAlertMessage(StatusMessageModel.StatusSeverity.Success);
            Assert.Contains("User deleted", await userDeletionAlert.TextContentAsync());
            await s.Page.AssertNoError();
        }


        [Fact]
        public async Task CanUseSSHService()
        {
            using var s = CreatePlaywrightTester();
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
            await s.Page.Locator("#SSHKeyFileContent").FillAsync("tes't\r\ntest2");
            await s.Page.Locator("#submit").ClickAsync();
            await s.Page.AssertNoError();

            var text = await s.Page.Locator("#SSHKeyFileContent").TextContentAsync();
            // Browser replace \n to \r\n, so it is hard to compare exactly what we want
            Assert.Contains("tes't", text);
            Assert.Contains("test2", text);
            Assert.True((await s.Page.ContentAsync()).Contains("authorized_keys has been updated", StringComparison.OrdinalIgnoreCase));

            await s.Page.Locator("#SSHKeyFileContent").ClearAsync();
            await s.Page.Locator("#submit").ClickAsync();

            text = await s.Page.Locator("#SSHKeyFileContent").TextContentAsync();
            Assert.DoesNotContain("test2", text);

            // Let's try to disable it now
            await s.Page.Locator("#disable").ClickAsync();
            await s.Page.Locator("#ConfirmInput").FillAsync("DISABLE");
            await s.Page.Locator("#ConfirmContinue").ClickAsync();
            await s.GoToUrl("/server/services/ssh");
            Assert.True((await s.Page.ContentAsync()).Contains("404 - Page not found", StringComparison.OrdinalIgnoreCase));

            policies = await settings.GetSettingAsync<PoliciesSettings>();
            Assert.True(policies.DisableSSHService);

            policies.DisableSSHService = false;
            await settings.UpdateSetting(policies);
        }

        [Fact]
        public async Task CanSetupEmailServer()
        {
            using var s = CreatePlaywrightTester();
            await s.StartAsync();
            await s.RegisterNewUser(true);
            await s.CreateNewStore();

            // Ensure empty server settings
            await s.GoToUrl("/server/emails");
            if (await s.Page.Locator("#ResetPassword").IsVisibleAsync())
            {
                await s.Page.Locator("#ResetPassword").ClickAsync();
                var responseAlert = await s.FindAlertMessage();
                Assert.Contains("Email server password reset", await responseAlert.TextContentAsync());
            }
            await s.Page.Locator("#Settings_Login").ClearAsync();
            await s.Page.Locator("#Settings_From").ClearAsync();
            await s.ClickPagePrimary();

            // Store Emails without server fallback
            await s.GoToStore();
            await s.GoToStore(StoreNavPages.Emails);
            Assert.Equal(0, await s.Page.Locator("#IsCustomSMTP").CountAsync());
            await s.Page.Locator("#ConfigureEmailRules").ClickAsync();
            Assert.Contains("You need to configure email settings before this feature works", await s.Page.ContentAsync());

            // Server Emails
            await s.GoToUrl("/server/emails");
            if ((await s.Page.ContentAsync()).Contains("Configured"))
            {
                await s.Page.Locator("#ResetPassword").ClickAsync();
                await s.FindAlertMessage();
            }
            await CanSetupEmailCore(s);

            // Store Emails with server fallback
            await s.GoToStore();
            await s.GoToStore(StoreNavPages.Emails);
            Assert.False(await s.Page.Locator("#IsCustomSMTP").IsCheckedAsync());
            await s.Page.Locator("#ConfigureEmailRules").ClickAsync();
            Assert.DoesNotContain("You need to configure email settings before this feature works", await s.Page.ContentAsync());

            await s.GoToStore(StoreNavPages.Emails);
            await s.Page.Locator("#IsCustomSMTP").ClickAsync();
            await CanSetupEmailCore(s);

            // Store Email Rules
            await s.Page.Locator("#ConfigureEmailRules").ClickAsync();
            Assert.Contains("There are no rules yet.", await s.Page.ContentAsync());
            Assert.DoesNotContain("id=\"SaveEmailRules\"", await s.Page.ContentAsync());
            Assert.DoesNotContain("You need to configure email settings before this feature works", await s.Page.ContentAsync());

            await s.Page.Locator("#CreateEmailRule").ClickAsync();
            await s.Page.Locator("#Trigger").SelectOptionAsync(new[] { "InvoicePaymentSettled" });
            await s.Page.Locator("#To").FillAsync("test@gmail.com");
            await s.Page.Locator("#CustomerEmail").ClickAsync();
            await s.Page.Locator("#Subject").FillAsync("Thanks!");
            await s.Page.Locator(".note-editable").FillAsync("Your invoice is settled");
            await s.Page.Locator("#SaveEmailRules").ClickAsync();
            // we now have a rule
            Assert.DoesNotContain("There are no rules yet.", await s.Page.ContentAsync());
            Assert.Contains("test@gmail.com", await s.Page.ContentAsync());

            await s.GoToStore(StoreNavPages.Emails);
            Assert.True(await s.Page.Locator("#IsCustomSMTP").IsCheckedAsync());
        }

        [Fact]
        public async Task NewUserLogin()
        {
            using var s = CreatePlaywrightTester();
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
            await s.Page.Locator("#Email").FillAsync(email);
            await s.Page.Locator("#Password").FillAsync("123456");
            await s.Page.Locator("#LoginButton").ClickAsync();

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
            await s.Page.Locator("#OldPassword").FillAsync("123456");
            await s.Page.Locator("#NewPassword").FillAsync(newPassword);
            await s.Page.Locator("#ConfirmPassword").FillAsync(newPassword);
            await s.ClickPagePrimary();
            await s.Logout();
            await s.Page.AssertNoError();

            //Log In With New Password
            await s.Page.Locator("#Email").FillAsync(email);
            await s.Page.Locator("#Password").FillAsync(newPassword);
            await s.Page.Locator("#LoginButton").ClickAsync();

            await s.GoToHome();
            await s.GoToProfile();
            await s.ClickOnAllSectionLinks();

            //let's test invite link
            await s.Logout();
            await s.GoToRegister();
            await s.RegisterNewUser(true);
            await s.GoToHome();
            await s.GoToServer(ServerNavPages.Users);
            await s.ClickPagePrimary();

            var usr = RandomUtils.GetUInt256().ToString().Substring(64 - 20) + "@a.com";
            await s.Page.Locator("#Email").FillAsync(usr);
            await s.ClickPagePrimary();
            var url = await s.Page.Locator("#InvitationUrl").GetAttributeAsync("data-text");

            await s.Logout();
            await s.GoToUrl(new Uri(url).AbsolutePath);
            Assert.Equal("hidden", await s.Page.Locator("#Email").GetAttributeAsync("type"));
            Assert.Equal(usr, await s.Page.Locator("#Email").GetAttributeAsync("value"));
            Assert.Equal("Create Account", await s.Page.Locator("h4").TextContentAsync());
            var invitationAlert = await s.FindAlertMessage(StatusMessageModel.StatusSeverity.Info);
            Assert.Contains("Invitation accepted. Please set your password.", await invitationAlert.TextContentAsync());

            await s.Page.Locator("#Password").FillAsync("123456");
            await s.Page.Locator("#ConfirmPassword").FillAsync("123456");
            await s.ClickPagePrimary();
            var accountCreationAlert = await s.FindAlertMessage();
            Assert.Contains("Account successfully created.", await accountCreationAlert.TextContentAsync());

            // We should be logged in now
            await s.GoToHome();
            await s.Page.Locator("#mainNav").WaitForAsync();

            //let's test delete user quickly while we're at it
            await s.GoToProfile();
            await s.Page.Locator("#delete-user").ClickAsync();
            await s.Page.Locator("#ConfirmInput").FillAsync("DELETE");
            await s.Page.Locator("#ConfirmContinue").ClickAsync();
            Assert.Contains("/login", s.Page.Url);
        }


        private static async Task CanSetupEmailCore(PlaywrightTester s)
        {
            await s.Page.Locator("#QuickFillDropdownToggle").ScrollIntoViewIfNeededAsync();
            await s.Page.Locator("#QuickFillDropdownToggle").ClickAsync();
            await s.Page.Locator("#quick-fill .dropdown-menu .dropdown-item:first-child").ClickAsync();
            await s.Page.Locator("#Settings_Login").ClearAsync();
            await s.Page.Locator("#Settings_Login").FillAsync("test@gmail.com");
            await s.Page.Locator("#Settings_Password").ClearAsync();
            await s.Page.Locator("#Settings_Password").FillAsync("mypassword");
            await s.Page.Locator("#Settings_From").ClearAsync();
            await s.Page.Locator("#Settings_From").FillAsync("Firstname Lastname <email@example.com>");
            await s.ClickPagePrimary();
            await s.FindAlertMessage(partialText: "Email settings saved");
            Assert.Contains("Configured", await s.Page.ContentAsync());
            await s.Page.Locator("#Settings_Login").ClearAsync();
            await s.Page.Locator("#Settings_Login").FillAsync("test_fix@gmail.com");
            await s.ClickPagePrimary();
            await s.FindAlertMessage(partialText: "Email settings saved");
            Assert.Contains("Configured", await s.Page.ContentAsync());
            Assert.Contains("test_fix", await s.Page.ContentAsync());
            await s.Page.Locator("#ResetPassword").PressAsync("Enter");
            await s.FindAlertMessage(partialText: "Email server password reset");
            Assert.DoesNotContain("Configured", await s.Page.ContentAsync());
            Assert.Contains("test_fix", await s.Page.ContentAsync());
        }
    }
}
