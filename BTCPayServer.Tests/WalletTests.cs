using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Models;
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

        var w = await s.GoToWalletTransactions(s.WalletId);
        await w.BumpFee(txs[0]);

        // Because a single transaction is selected, we should be able to select CPFP only (Because no change are available, we can't do RBF)
        await s.Page.Locator("[name='txId']").WaitForAsync();
        Assert.Equal("disabled", await s.Page.GetAttributeAsync("#BumpMethod", "disabled"));
        Assert.Equal("CPFP", await s.Page.Locator("#BumpMethod").InnerTextAsync());
        await s.ClickCancel();

        // Same but using mass action
        await w.Select(txs[0]);
        await s.Page.ClickAsync("#BumpFee");
        await s.Page.Locator("[name='txId']").WaitForAsync();
        await s.ClickCancel();

        // Because two transactions are select we can only mass bump on CPFP
        await w.Select(txs[0], txs[1]);
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
        await w.AssertHasLabels(cpfpTx, "CPFP");

        // The CPFP should be RBF-able
        Assert.DoesNotContain(cpfpTx, txs);

        await w.BumpFee(cpfpTx);
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
        await s.FindAlertMessage();

        await s.Page.ReloadAsync();
        var rbfTx = (await client.ShowOnChainWalletTransactions(s.StoreId, "BTC")).Select(t => t.TransactionHash).ToArray()[0];

        // CPFP has been replaced, so it should not be found
        await w.AssertNotFound(cpfpTx);

        // However, the new transaction should have copied the CPFP tag from the transaction it replaced, and have a RBF label as well.
        await w.AssertHasLabels(rbfTx, "CPFP");
        await w.AssertHasLabels(rbfTx, "RBF");

        // Now, we sweep all the UTXOs to a single destination. This should be RBF-able. (Fee deducted on the lone UTXO)
        var send = await s.GoToWalletSend();
        await send.FillAddress(new Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, Network.RegTest));
        await send.SweepBalance();
        await send.SetFeeRate(20m);
        await send.Sign();
        await s.Page.ClickAsync("button[value=broadcast]");
        // Now we RBF the sweep
        await w.BumpFee();
        Assert.Equal("RBF", await s.Page.Locator("#BumpMethod").InnerTextAsync());
        await s.ClickPagePrimary();
        await s.Page.ClickAsync("#BroadcastTransaction");
        await w.AssertHasLabels("RBF");
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

        for (int i = 0; i < 5; i++)
        {
            var txs = await s.GoToWalletTransactions(s.WalletId);
            await txs.SelectAll();
            await txs.BumpFeeSelected();
            await s.ClickPagePrimary();
            await s.Page.ClickAsync("#BroadcastTransaction");
            Assert.Contains($"/wallets/{s.WalletId}", s.Page.Url);
            await s.FindAlertMessage(partialText: "Transaction broadcasted successfully");

            // The CPFP tag should be applied to the new tx
            await txs.AssertHasLabels("CPFP");
        }
    }
}
