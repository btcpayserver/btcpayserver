using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Views.Server;
using BTCPayServer.Views.Stores;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests
{
    public class PlaywrightTests : PlaywrightBaseTest
    {
        public string TestDir { get; private set; }
        public ServerTester ServerTester { get; private set; }
        public PlaywrightTests(ITestOutputHelper helper) : base(helper)
        {
            TestDir = Path.Combine(Directory.GetCurrentDirectory(), "PlaywrightTests");
        }

        [Fact]
        public async Task CanNavigateServerSettingss()
        {
            using var s = CreatePlaywrightTester();
            await s.StartAsync();
            await s.RegisterNewUser(true);
            await s.GoToHome();
            await s.GoToServer();
            await s.Page.AssertNoError();
            await s.ClickOnAllSectionLinks();
            await s.GoToServer(ServerNavPages.Services);
            TestLogs.LogInformation("Let's check if we can access the logs");
            await s.Page.GetByRole(AriaRole.Link, new() { Name = "Logs" }).ClickAsync();
            await s.Page.Locator("a:has-text('.log')").First.ClickAsync();
            Assert.Contains("Starting listening NBXplorer", await s.Page.ContentAsync());
            await s.Page.Context.Browser.CloseAsync();
        }


        [Fact]
        public async Task CanUseForms()
        {
            ServerTester = CreateServerTester(TestDir, newDb: true);
            await ServerTester.StartAsync();
            ServerUri = ServerTester.PayTester.ServerUri;
            await InitializeBTCPayServer();
            // Point Of Sale
            var appName = $"PoS-{Guid.NewGuid().ToString()[..21]}";
            await Page.Locator("#StoreNav-CreatePointOfSale").ClickAsync();
            await Page.Locator("#AppName").FillAsync(appName);
            await ClickPagePrimaryAsync();
            var textContent = await (await FindAlertMessageAsync()).TextContentAsync();
            Assert.Contains("App successfully created", textContent);
            await Page.SelectOptionAsync("#FormId", "Email");
            await ClickPagePrimaryAsync();
            textContent = await (await FindAlertMessageAsync()).TextContentAsync();
            Assert.Contains("App updated", textContent);
            await Page.Locator("#ViewApp").ClickAsync();
            var popOutPage = await Page.Context.WaitForPageAsync();
            await popOutPage.Locator("button[type='submit']").First.ClickAsync();
            await popOutPage.Locator("[name='buyerEmail']").FillAsync("aa@aa.com");
            await popOutPage.Locator("input[type='submit']").ClickAsync();
            await PayInvoiceAsync(popOutPage, true);
            var invoiceId = popOutPage.Url[(popOutPage.Url.LastIndexOf("/", StringComparison.Ordinal) + 1)..];
            await popOutPage.CloseAsync();

            await Page.Context.Pages.First().BringToFrontAsync();
            await GoToUrl($"/invoices/{invoiceId}/");
            Assert.Contains("aa@aa.com", await Page.ContentAsync());
            // Payment Request
            await Page.Locator("#StoreNav-PaymentRequests").ClickAsync();
            await ClickPagePrimaryAsync();
            await Page.Locator("#Title").FillAsync("Pay123");
            await Page.Locator("#Amount").FillAsync("700");
            await Page.SelectOptionAsync("#FormId", "Email");
            await ClickPagePrimaryAsync();
            await Page.Locator("a[id^='Edit-']").First.ClickAsync();
            var editUrl = new Uri(Page.Url);
            await Page.Locator("#ViewPaymentRequest").ClickAsync();
            popOutPage = await Page.Context.WaitForPageAsync();
            await popOutPage.Locator("[data-test='form-button']").ClickAsync();
            Assert.Contains("Enter your email", await popOutPage.ContentAsync());
            await popOutPage.Locator("input[name='buyerEmail']").FillAsync("aa@aa.com");
            await popOutPage.Locator("#page-primary").ClickAsync();
            invoiceId = popOutPage.Url.Split('/').Last();
            await popOutPage.CloseAsync();
            await Page.Context.Pages.First().BringToFrontAsync();
            await GoToUrl(editUrl.PathAndQuery);
            Assert.Contains("aa@aa.com", await Page.ContentAsync());
            var invoice = await ServerTester.PayTester.GetService<InvoiceRepository>().GetInvoice(invoiceId);
            Assert.Equal("aa@aa.com", invoice.Metadata.BuyerEmail);

            //Custom Forms
            await GoToStore();
            await GoToStore(StoreNavPages.Forms);
            Assert.Contains("There are no forms yet.", await Page.ContentAsync());
            await ClickPagePrimaryAsync();
            await Page.Locator("[name='Name']").FillAsync("Custom Form 1");
            await Page.Locator("#ApplyEmailTemplate").ClickAsync();
            await Page.Locator("#CodeTabButton").ClickAsync();
            await Page.Locator("#CodeTabPane").WaitForAsync();
            var config = await Page.Locator("[name='FormConfig']").InputValueAsync();
            Assert.Contains("buyerEmail", config);
            await Page.Locator("[name='FormConfig']").ClearAsync();
            await Page.Locator("[name='FormConfig']").FillAsync(config.Replace("Enter your email", "CustomFormInputTest"));
            await ClickPagePrimaryAsync();
            await Page.Locator("#ViewForm").ClickAsync();
            var formUrl = Page.Url;
            Assert.Contains("CustomFormInputTest", await Page.ContentAsync());
            await Page.Locator("[name='buyerEmail']").FillAsync("aa@aa.com");
            await Page.Locator("input[type='submit']").ClickAsync();
            await PayInvoiceAsync(Page, true, 0.001m);
            var result = await ServerTester.PayTester.HttpClient.GetAsync(formUrl);
            Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
            await GoToHome();
            await GoToStore();
            await GoToStore(StoreNavPages.Forms);
            Assert.Contains("Custom Form 1", await Page.ContentAsync());
            await Page.GetByRole(AriaRole.Link, new() { Name = "Remove" }).ClickAsync();
            await Page.Locator("#ConfirmInput").FillAsync("DELETE");
            await Page.Locator("#ConfirmContinue").ClickAsync();
            Assert.DoesNotContain("Custom Form 1", await Page.ContentAsync());
            await ClickPagePrimaryAsync();
            await Page.Locator("[name='Name']").FillAsync("Custom Form 2");
            await Page.Locator("#ApplyEmailTemplate").ClickAsync();
            await Page.Locator("#CodeTabButton").ClickAsync();
            await Page.Locator("#CodeTabPane").WaitForAsync();
            await Page.Locator("input[type='checkbox'][name='Public']").SetCheckedAsync(true);
            await Page.Locator("[name='FormConfig']").ClearAsync();
            await Page.Locator("[name='FormConfig']").FillAsync(config.Replace("Enter your email", "CustomFormInputTest2"));
            await ClickPagePrimaryAsync();
            await Page.Locator("#ViewForm").ClickAsync();
            formUrl = Page.Url;
            result = await ServerTester.PayTester.HttpClient.GetAsync(formUrl);
            Assert.NotEqual(HttpStatusCode.NotFound, result.StatusCode);
            await GoToHome();
            await GoToStore();
            await GoToStore(StoreNavPages.Forms);
            Assert.Contains("Custom Form 2", await Page.ContentAsync());
            await Page.GetByRole(AriaRole.Link, new() { Name = "Custom Form 2" }).ClickAsync();
            await Page.Locator("[name='Name']").ClearAsync();
            await Page.Locator("[name='Name']").FillAsync("Custom Form 3");
            await ClickPagePrimaryAsync();
            await GoToStore(StoreNavPages.Forms);
            Assert.Contains("Custom Form 3", await Page.ContentAsync());
            await Page.Locator("#StoreNav-PaymentRequests").ClickAsync();
            await ClickPagePrimaryAsync();
            var selectOptions = await Page.Locator("#FormId >> option").CountAsync();
            Assert.Equal(4, selectOptions);
        }
    }
}
