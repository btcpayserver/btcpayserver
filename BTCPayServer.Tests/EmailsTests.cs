using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Plugins.Emails.Controllers;
using BTCPayServer.Plugins.Emails.Services;
using BTCPayServer.Plugins.Emails.Views;
using BTCPayServer.Services;
using BTCPayServer.Tests.PMO;
using BTCPayServer.Views.Server;
using BTCPayServer.Views.Stores;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Playwright;
using MimeKit;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests;

[Collection(nameof(NonParallelizableCollectionDefinition))]
public class EmailsTests(ITestOutputHelper helper) : UnitTestBase(helper)
{
    [Fact]
    [Trait("FastTest", "FastTest")]
    public void CanParseEmailDestination()
    {
        var vm = new StoreEmailRuleViewModel();
        var actual = vm.AsArray("\"Nicolas, The, Great\" <emperor@btc.pay>,{SomeTemplate} ,\"Madd,Test\" <madd@example.com>");
        string[] expected = ["\"Nicolas, The, Great\" <emperor@btc.pay>", "{SomeTemplate}", "\"Madd,Test\" <madd@example.com>"];
        Assert.Equal(expected, actual);
    }
    [Fact(Timeout = TestUtils.LongRunningTestTimeout)]
    [Trait("Integration", "Integration")]
    public async Task EmailSenderTests()
    {
        using var tester = CreateServerTester(newDb: true);
        await tester.StartAsync();

        var acc = tester.NewAccount();
        await acc.GrantAccessAsync(true);

        var settings = tester.PayTester.GetService<SettingsRepository>();
        var emailSenderFactory = tester.PayTester.GetService<EmailSenderFactory>();

        Assert.Null(await Assert.IsType<ServerEmailSender>(await emailSenderFactory.GetEmailSender()).GetEmailSettings());
        Assert.Null(await Assert.IsType<StoreEmailSender>(await emailSenderFactory.GetEmailSender(acc.StoreId)).GetEmailSettings());


        await settings.UpdateSetting(new PoliciesSettings() { DisableStoresToUseServerEmailSettings = false });
        await settings.UpdateSetting(new EmailSettings()
        {
            From = "admin@admin.com",
            Login = "admin@admin.com",
            Password = "admin@admin.com",
            Port = 1234,
            Server = "admin.com",
        });

        async Task AssertIsLogin<TEmailSender>(string storeId, string expectedLogin) where TEmailSender: IEmailSender
        {
            var sender =
                storeId is not null ? Assert.IsType<TEmailSender>(await emailSenderFactory.GetEmailSender(storeId))
                    : Assert.IsType<TEmailSender>(await emailSenderFactory.GetEmailSender());
            var emailSettings = await sender.GetEmailSettings();
            if (emailSettings is null)
            {
                Assert.Null(emailSettings);
            }
            else
            {
                Assert.NotNull(emailSettings);
                Assert.Equal(expectedLogin, emailSettings.Login);
            }
        }

        await AssertIsLogin<ServerEmailSender>(null, "admin@admin.com");
        await AssertIsLogin<StoreEmailSender>(acc.StoreId, "admin@admin.com");

        await settings.UpdateSetting(new PoliciesSettings() { DisableStoresToUseServerEmailSettings = true });
        await AssertIsLogin<ServerEmailSender>(null, "admin@admin.com");
        await AssertIsLogin<StoreEmailSender>(acc.StoreId, null);

        Assert.IsType<RedirectToActionResult>(await acc.GetController<UIStoresEmailController>().StoreEmailSettings(acc.StoreId, new(new()
        {
            From = "store@store.com",
            Login = "store@store.com",
            Password = "store@store.com",
            Port = tester.MailPitSettings.SmtpPort,
            Server = tester.MailPitSettings.Hostname
        })
        {
            IsCustomSMTP = true
        }, ""));

        await AssertIsLogin<StoreEmailSender>(acc.StoreId, "store@store.com");

        var message = await tester.AssertHasEmail(async () =>
        {
            var sender = await emailSenderFactory.GetEmailSender(acc.StoreId);
            sender.SendEmail(MailboxAddress.Parse("destination@test.com"), "test", "hello world");
        });
        Assert.Equal("test", message.Subject);
        Assert.Equal("hello world", message.Text);

        // Configure at server level
        Assert.IsType<RedirectToActionResult>(await acc.GetController<UIServerEmailController>().ServerEmailSettings(new(new()
        {
            From = "server@server.com",
            Login = "server@server.com",
            Password = "server@server.com",
            Port = tester.MailPitSettings.SmtpPort,
            Server = tester.MailPitSettings.Hostname
        })
        {
            EnableStoresToUseServerEmailSettings = true
        }, ""));

        // The store should now use it
        Assert.IsType<RedirectToActionResult>(await acc.GetController<UIStoresEmailController>().StoreEmailSettings(acc.StoreId, new(new())
        {
            IsCustomSMTP = false
        }, ""));

        await AssertIsLogin<StoreEmailSender>(acc.StoreId, "server@server.com");
    }

    [Fact]
    [Trait("Integration", "Integration")]
    public async Task ServerEmailTests()
    {
        using var tester = CreateServerTester();
        await tester.StartAsync();
        var admin = tester.NewAccount();
        await admin.GrantAccessAsync(true);
        var adminClient = await admin.CreateClient(Policies.Unrestricted);
        // validate that clear email settings will not throw an error
        await adminClient.UpdateServerEmailSettings(new ServerEmailSettingsData());

        var data = new ServerEmailSettingsData
        {
            From = "admin@admin.com",
            Login = "admin@admin.com",
            Password = "admin@admin.com",
            Port = 1234,
            Server = "admin.com",
            EnableStoresToUseServerEmailSettings = false
        };
        var actualUpdated = await adminClient.UpdateServerEmailSettings(data);

        var finalEmailSettings = await adminClient.GetServerEmailSettings();
        // email password is masked and not returned from the server once set
        data.Password = null;
        data.PasswordSet = true;

        Assert.Equal(JsonConvert.SerializeObject(finalEmailSettings), JsonConvert.SerializeObject(data));
        Assert.Equal(JsonConvert.SerializeObject(finalEmailSettings), JsonConvert.SerializeObject(actualUpdated));

        // check that email validation works
        await AssertEx.AssertValidationError(new[] { nameof(EmailSettingsData.From) },
            async () => await adminClient.UpdateServerEmailSettings(new ServerEmailSettingsData
            {
                From = "invalid"
            }));

        // NOTE: This email test fails silently in EmailSender.cs#31, can't test, but leaving for the future as reminder
        //await adminClient.SendEmail(admin.StoreId,
        //    new SendEmailRequest { Body = "lol", Subject = "subj", Email = "to@example.org" });

        // check that clear server email settings works
        await adminClient.UpdateServerEmailSettings(new ServerEmailSettingsData());
        var clearedSettings = await adminClient.GetServerEmailSettings();
        Assert.Equal(JsonConvert.SerializeObject(new ServerEmailSettingsData { PasswordSet = false }), JsonConvert.SerializeObject(clearedSettings));
    }

    [Fact]
    [Trait("Integration", "Integration")]
    public async Task StoreEmailTests()
    {
        using var tester = CreateServerTester();
        await tester.StartAsync();
        var admin = tester.NewAccount();
        await admin.GrantAccessAsync(true);
        var adminClient = await admin.CreateClient(Policies.Unrestricted);
        // validate that clear email settings will not throw an error
        await adminClient.UpdateStoreEmailSettings(admin.StoreId, new EmailSettingsData());

        var data = new EmailSettingsData
        {
            From = "admin@admin.com",
            Login = "admin@admin.com",
            Password = "admin@admin.com",
            Port = 1234,
            Server = "admin.com",
        };
        await adminClient.UpdateStoreEmailSettings(admin.StoreId, data);
        var s = await adminClient.GetStoreEmailSettings(admin.StoreId);
        // email password is masked and not returned from the server once set
        data.Password = null;
        data.PasswordSet = true;
        Assert.Equal(JsonConvert.SerializeObject(s), JsonConvert.SerializeObject(data));
        await AssertEx.AssertValidationError(new[] { nameof(EmailSettingsData.From) },
            async () => await adminClient.UpdateStoreEmailSettings(admin.StoreId,
                new EmailSettingsData { From = "invalid" }));

        // send test email
        await adminClient.SendEmail(admin.StoreId,
            new SendEmailRequest { Body = "lol", Subject = "subj", Email = "to@example.org" });

        // clear store email settings
        await adminClient.UpdateStoreEmailSettings(admin.StoreId, new EmailSettingsData());
        var clearedSettings = await adminClient.GetStoreEmailSettings(admin.StoreId);
        Assert.Equal(JsonConvert.SerializeObject(new EmailSettingsData { PasswordSet = false }), JsonConvert.SerializeObject(clearedSettings));
    }

    [Fact]
    [Trait("Playwright", "Playwright")]
    public async Task CanSetupEmailRules()
    {
        await using var s = CreatePlaywrightTester(newDb: true);
        await s.StartAsync();
        await s.RegisterNewUser(true);
        var (storeName, _) = await s.CreateNewStore();

        await s.GoToStore(StoreNavPages.Emails);
        await s.Page.ClickAsync("#ConfigureEmailRules");
        await AssertNoRules(s);
        Assert.Contains("You need to configure email settings before this feature works", await s.Page.ContentAsync());

        await s.Page.ClickAsync(".configure-email");

        var mailPMO = new ConfigureEmailPMO(s);
        await mailPMO.FillMailPit(new()
        {
            From = "store@store.com",
            Login = "store@store.com",
            Password = "password"
        });

        await s.GoToStore(StoreNavPages.Emails);
        await s.Page.ClickAsync("#ConfigureEmailRules");

        var pmo = new EmailRulePMO(s);
        await s.Page.ClickAsync("#CreateEmailRule");

        await pmo.Fill(new()
        {
            Trigger = "WH-InvoiceCreated",
            To = "invoicecreated@gmail.com",
            Subject = "Invoice Created in {Invoice.Currency}!",
            Body = "Invoice has been created in {Invoice.Currency} for {Invoice.Price}!",
            CustomerEmail = true
        });

        await s.FindAlertMessage();
        var page = await s.Page.ContentAsync();
        Assert.DoesNotContain("There are no rules yet.", page);
        Assert.Contains("invoicecreated@gmail.com", page);
        Assert.Contains("Invoice Created in {Invoice.Currency}!", page);
        Assert.Contains("Yes", page);

        await s.Page.ClickAsync("#CreateEmailRule");

        await pmo.Fill(new()
        {
            Trigger = "WH-PaymentRequestStatusChanged",
            To = "statuschanged@gmail.com",
            Subject = "Status changed!",
            Body = "Your Payment Request Status is Changed"
        });

        await s.FindAlertMessage();
        Assert.Contains("statuschanged@gmail.com", await s.Page.ContentAsync());
        Assert.Contains("Status changed!", await s.Page.ContentAsync());

        var editButtons = s.Page.GetByRole(AriaRole.Link, new() { Name = "Edit" });
        Assert.True(await editButtons.CountAsync() >= 2);
        await editButtons.Nth(1).ClickAsync();

        await pmo.Fill(new()
        {
            To = "changedagain@gmail.com"
        });

        await s.FindAlertMessage();
        Assert.Contains("changedagain@gmail.com", await s.Page.ContentAsync());
        Assert.DoesNotContain("statuschanged@gmail.com", await s.Page.ContentAsync());

        var rulesUrl = s.Page.Url;

        await s.AddDerivationScheme();
        await s.GoToInvoices();
        var message = await s.Server.AssertHasEmail(() => s.CreateInvoice(amount: 10m, currency: "USD"));
        Assert.Equal("Invoice has been created in USD for 10!", message.Text);

        await s.GoToUrl(rulesUrl);
        var deleteLinks = s.Page.GetByRole(AriaRole.Link, new() { Name = "Remove" });
        Assert.Equal(2, await deleteLinks.CountAsync());

        await deleteLinks.First.ClickAsync();
        await s.ConfirmDeleteModal();

        await s.FindAlertMessage();
        deleteLinks = s.Page.GetByRole(AriaRole.Link, new() { Name = "Remove" });
        Assert.Equal(1, await deleteLinks.CountAsync());

        await deleteLinks.First.ClickAsync();
        await s.ConfirmDeleteModal();

        await s.FindAlertMessage();
        await AssertNoRules(s);

        await s.Page.ClickAsync("#CreateEmailRule");

        await pmo.Fill(new()
        {
            Trigger = "WH-InvoiceCreated",
            To = "invoicecreated@gmail.com",
            Subject = "Invoice Created in {Invoice.Currency} for {Store.Name}!",
            Body = "Invoice has been created in {Invoice.Currency} for {Invoice.Price}!",
            CustomerEmail = true,
            Condition = "$ ?(@.Invoice.Metadata.buyerEmail == \"john@test.com\")"
        });

        await s.GoToInvoices();
        message = await s.Server.AssertHasEmail(() => s.CreateInvoice(amount: 10m, currency: "USD", refundEmail: "john@test.com"));
        Assert.Equal("Invoice Created in USD for " + storeName + "!", message.Subject);
        Assert.Equal("Invoice has been created in USD for 10!", message.Text);
        Assert.Equal("john@test.com", message.To[0].Address);

        await s.GoToServer(ServerNavPages.Emails);

        await mailPMO.FillMailPit();
        var rules = await mailPMO.ConfigureEmailRules();
        await rules.EditRule("SRV-PasswordReset");
        await pmo.Fill(new()
        {
            Trigger = "SRV-PasswordReset",
            HtmlBody = true,
            Body = "<p>Hello, <a id=\"reset-link\" href=\"{ResetLink}\">click here</a> to reset the password</p>"
        });
        await s.Logout();
        await s.Page.GetByRole(AriaRole.Link, new() { Name = "Forgot password?" }).ClickAsync();
        await s.Page.FillAsync("#Email", s.CreatedUser);
        message = await s.Server.AssertHasEmail(() => s.ClickPagePrimary());
        Assert.Contains("<p>Hello, <a id=\"reset-link\" href=\"http://", message.Html);
    }

    private static async Task AssertNoRules(PlaywrightTester s)
    {
        await s.Page.Locator("text=There are no rules yet.").WaitForAsync();
    }

    [Fact]
    [Trait("Playwright", "Playwright")]
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
        await AssertNoRules(s);
        Assert.DoesNotContain("id=\"SaveEmailRules\"", await s.Page.ContentAsync());
        Assert.DoesNotContain("You need to configure email settings before this feature works", await s.Page.ContentAsync());

        await s.Page.ClickAsync("#CreateEmailRule");
        var pmo = new EmailRulePMO(s);
        await pmo.Fill(new()
        {
            Trigger = "WH-InvoicePaymentSettled",
            To = "test@gmail.com",
            CustomerEmail = true,
            Subject = "Thanks!",
            Body = "Your invoice is settled"
        });

        await s.FindAlertMessage();
        // we now have a rule
        Assert.DoesNotContain("There are no rules yet.", await s.Page.ContentAsync());
        Assert.Contains("test@gmail.com", await s.Page.ContentAsync());

        await s.GoToStore(StoreNavPages.Emails);
        Assert.True(await s.Page.Locator("#IsCustomSMTP").IsCheckedAsync());
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
}
