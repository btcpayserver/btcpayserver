using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Views.Wallets;
using NBitcoin;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests;
public class WalletTests(ITestOutputHelper helper) : UnitTestBase(helper)
{
    [Fact]
    [Trait("Playwright", "Playwright")]
    public async Task CanUseBumpFee()
    {
        await using var s = CreatePlaywrightTester();
        await s.StartAsync();
        await s.Server.ExplorerNode.GenerateAsync(1);
        await s.RegisterNewUser(true);
        await s.CreateNewStore();
        await s.GenerateWallet(isHotWallet: true);
        await CreateInvoices(s);

        var client = await s.AsTestAccount().CreateClient();
        var txs = (await client.ShowOnChainWalletTransactions(s.StoreId, "BTC")).Select(t => t.TransactionHash).ToArray();
        Assert.Equal(3, txs.Length);

        await s.GoToWallet(navPages: WalletsNavPages.Transactions);
        await ClickBumpFee(s, txs[0]);

        // Because a single transaction is selected, we should be able to select CPFP only (Because no change are available, we can't do RBF)
        await s.Page.Locator("[name='txId']").WaitForAsync();
        Assert.Equal("disabled", await s.Page.GetAttributeAsync("#BumpMethod", "disabled"));
        Assert.Equal("CPFP", await s.Page.Locator("#BumpMethod").InnerTextAsync());
        await s.ClickCancel();

        // Same but using mass action
        await SelectTransactions(s, txs[0]);
        await s.Page.ClickAsync("#BumpFee");
        await s.Page.Locator("[name='txId']").WaitForAsync();
        await s.ClickCancel();

        // Because two transactions are select we can only mass bump on CPFP
        await SelectTransactions(s, txs[0], txs[1]);
        await s.Page.ClickAsync("#BumpFee");
        Assert.False(await s.Page.Locator("[name='txId']").IsVisibleAsync());
        Assert.Equal("disabled", await s.Page.GetAttributeAsync("#BumpMethod", "disabled"));
        Assert.Equal("CPFP", await s.Page.Locator("#BumpMethod").InnerTextAsync());

        var newExpectedEffectiveFeeRate = decimal.Parse(await s.Page.GetAttributeAsync("[name='FeeSatoshiPerByte']", "value") ?? string.Empty, CultureInfo.InvariantCulture);

        await s.ClickPagePrimary();
        await s.Page.ClickAsync("#BroadcastTransaction");
        await s.FindAlertMessage(partialText: "Transaction broadcasted successfully");

        // The CPFP tag should be applied to the new tx
        var cpfpTx = (await client.ShowOnChainWalletTransactions(s.StoreId, "BTC")).Select(t => t.TransactionHash).ToArray()[0];
        await AssertHasLabels(s, cpfpTx, "CPFP");

        // The CPFP should be RBF-able
        Assert.DoesNotContain(cpfpTx, txs);

        await ClickBumpFee(s, cpfpTx);
        Assert.Null(await s.Page.GetAttributeAsync("#BumpMethod", "disabled"));
        Assert.Equal("RBF", await s.Page.Locator("#BumpMethod option:checked").InnerTextAsync());

        var currentEffectiveFeeRate = decimal.Parse(
            await s.Page.GetAttributeAsync("[name='CurrentFeeSatoshiPerByte']", "value") ?? string.Empty,
            CultureInfo.InvariantCulture);

        // We CPFP'd two transactions with a newExpectedEffectiveFeeRate of 20.0
        // When we want to RBF the previous CPFP, the currentEffectiveFeeRate should be coherent with our ealier choice
        Assert.Equal(newExpectedEffectiveFeeRate, currentEffectiveFeeRate, 0);

        await s.ClickPagePrimary();
        await s.Page.ClickAsync("#BroadcastTransaction");

        await s.Page.ReloadAsync();
        var rbfTx = (await client.ShowOnChainWalletTransactions(s.StoreId, "BTC")).Select(t => t.TransactionHash).ToArray()[0];

        // CPFP has been replaced, so it should not be found
        Assert.False(await s.Page.Locator(TxRowSelector(cpfpTx)).IsVisibleAsync());

        // However, the new transaction should have copied the CPFP tag from the transaction it replaced, and have a RBF label as well.
        await AssertHasLabels(s, rbfTx, "CPFP");
        await AssertHasLabels(s, rbfTx, "RBF");
    }

    private async Task CreateInvoices(PlaywrightTester tester)
    {
        var client = await tester.AsTestAccount().CreateClient();
        var creating = Enumerable.Range(0, 3)
            .Select(_ => client.CreateInvoice(tester.StoreId, new() { Amount = 10m }));
        foreach (var c in creating)
        {
            var created = await c;
            await tester.GoToUrl($"i/{created.Id}");
            await tester.PayInvoice();
        }
    }

    private async Task AssertHasLabels(PlaywrightTester s, uint256 txId, string label)
    {
        await s.Page.ReloadAsync();
        await s.Page.Locator($"{TxRowSelector(txId)} .transaction-label[data-value=\"{label}\"]").WaitForAsync();
    }
    private async Task AssertHasLabels(PlaywrightTester s, string label)
    {
        await s.Page.ReloadAsync();
        await s.Page.Locator($".transaction-label[data-value=\"{label}\"]").First.WaitForAsync();
    }

    static string TxRowSelector(uint256 txId) => $".transaction-row[data-value=\"{txId}\"]";

    private async Task SelectTransactions(PlaywrightTester s, params uint256[] txs)
    {
        foreach (var txId in txs)
        {
            await s.Page.SetCheckedAsync($"{TxRowSelector(txId)} .mass-action-select", true);
        }
    }

    private async Task ClickBumpFee(PlaywrightTester s, uint256 txId)
    {
        await s.Page.ClickAsync($"{TxRowSelector(txId)} .bumpFee-btn");
    }

    [Fact]
    [Trait("Playwright", "Playwright")]
    public async Task CanUseCPFP()
    {
        await using var s = CreatePlaywrightTester();
        await s.StartAsync();
        await s.RegisterNewUser(true);
        await s.CreateNewStore();
        await s.GenerateWallet(isHotWallet: true);
        await s.FundStoreWallet();
        await CreateInvoices(s);

        // Let's CPFP from the invoices page
        await s.GoToInvoices(s.StoreId);
        await s.Page.SetCheckedAsync(".mass-action-select-all", true);
        await s.Page.ClickAsync("#BumpFee");
        await s.ClickPagePrimary();
        await s.Page.ClickAsync("#BroadcastTransaction");
        await s.FindAlertMessage();
        Assert.Contains($"/stores/{s.StoreId}/invoices", s.Page.Url);

        // CPFP again should fail because all invoices got bumped
        await s.GoToInvoices();
        await s.Page.SetCheckedAsync(".mass-action-select-all", true);
        await s.Page.ClickAsync("#BumpFee");
        Assert.Contains($"/stores/{s.StoreId}/invoices", s.Page.Url);
        await s.FindAlertMessage(StatusMessageModel.StatusSeverity.Error, partialText: "No UTXOs available");

        // But we should be able to bump from the wallet's page
        await s.GoToWallet(navPages: WalletsNavPages.Transactions);
        await s.Page.SetCheckedAsync(".mass-action-select-all", true);
        await s.Page.ClickAsync("#BumpFee");
        await s.ClickPagePrimary();
        await s.Page.ClickAsync("#BroadcastTransaction");
        Assert.Contains($"/wallets/{s.WalletId}", s.Page.Url);
        await s.FindAlertMessage(partialText: "Transaction broadcasted successfully");

        // The CPFP tag should be applied to the new tx
        await AssertHasLabels(s, "CPFP");
    }
}
