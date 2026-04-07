using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Events;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.GlobalSearch.Views;
using NBitcoin;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;
using static Microsoft.Playwright.Assertions;

namespace BTCPayServer.Tests;

public class GlobalSearchTests(ITestOutputHelper helper) : UnitTestBase(helper)
{
    [Fact]
    [Trait("Playwright", "Playwright-2")]
    public async Task TestGlobalSearch()
    {
        await using var s = CreatePlaywrightTester();
        await s.StartAsync();
        await s.RegisterNewUser(isAdmin: true);
        await s.CreateNewStore();

        await s.GlobalSearch.GoToPage("Setup wallet");
        await s.Page.WaitForURLAsync(s.ServerUri + $"stores/{s.StoreId}/onchain/BTC");
        var admin = (s.CreatedUser, s.Password);

        // Create a new invoice and check that you can search for it either via invoice id, bitcoin address or transaction id.
        await s.AddDerivationScheme();
        var invoiceId = await s.CreateInvoice(amount: 0.01m, currency: "BTC");
        var invoice = await s.Server.PayTester.InvoiceRepository.GetInvoice(invoiceId);
        var address = invoice.GetPaymentPrompt(PaymentTypes.CHAIN.GetPaymentMethodId("BTC"))!.Destination;
        Assert.NotNull(address);

        await SearchAndOpenInvoice(invoiceId);
        await SearchAndOpenInvoice(address);

        var txId = uint256.Zero;
        await s.Server.WaitForEvent<NewOnChainTransactionEvent>(async () =>
        {
            txId = await s.Server.ExplorerNode.SendToAddressAsync(BitcoinAddress.Create(address!, s.Server.ExplorerNode.Network), Money.Coins(0.001m));
        });
        await SearchAndOpenInvoice(txId.ToString());

        // Go back to home, and check that you can search by clicking rather that typing.
        await s.GoToHome();
        await s.GlobalSearch.Fill("users");
        var searchItem = await s.GlobalSearch.AssertShow("View server's registered users");
        await searchItem.ClickAsync();
        await s.Page.WaitForURLAsync(s.ServerUri + "server/users");

        // Now check that you can see some admin only route in the search suggestions
        await s.GoToHome();
        await s.GlobalSearch.Fill("server settings");
        await s.GlobalSearch.AssertShow("Configure the server settings");

        // Logout, create a new non admin user
        var nonAdmin = s.Server.NewAccount();
        await nonAdmin.GrantAccessAsync();
        await nonAdmin.MakeAdmin(false);
        await s.Logout();
        await s.GoToLogin();
        await s.LogIn(nonAdmin.RegisterDetails.Email, nonAdmin.RegisterDetails.Password);
        await s.GoToHome();

        // Check that you can't search for admin only routes
        await s.GlobalSearch.Fill("server settings");
        await Expect(s.GlobalSearch.GetResultLocator("Configure the server settings")).Not.ToBeVisibleAsync();

        await s.Logout();
        await s.LogIn(admin.CreatedUser, admin.Password);
        // Access UISearchController.Global route, and check that all the routes are accessible
        var response = await s.Page.Context.APIRequest.GetAsync(s.Link($"/search/global?storeId={s.StoreId}"));
        Assert.True(response.Ok, $"Global search endpoint returned {response.Status}: {await response.TextAsync()}");
        var items = JsonConvert.DeserializeObject<List<ResultItemViewModel>>(await response.TextAsync());
        Assert.NotNull(items);
        Assert.NotEmpty(items);

        foreach (var item in items.Where(item => !string.IsNullOrEmpty(item.Url)))
        {
            await s.GoToUrl(item.Url);
            await s.Page.AssertNoError();
        }

        async Task SearchAndOpenInvoice(string query)
        {
            await s.GoToHome();
            await s.GlobalSearch.Fill(query);
            await s.GlobalSearch.AssertShow("Invoice");
            await s.GlobalSearch.Enter();
            await s.Page.WaitForURLAsync(s.ServerUri + $"invoices/{invoiceId}");
        }
    }
}
