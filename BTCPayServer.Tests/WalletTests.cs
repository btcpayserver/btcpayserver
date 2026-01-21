using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Wallets;
using BTCPayServer.Views.Wallets;
using Microsoft.Playwright;
using NBitcoin;
using NBitcoin.Payment;
using NBXplorer.Models;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests;

public class WalletTests(ITestOutputHelper helper) : UnitTestBase(helper)
{
    [Fact]
    [Trait("Playwright", "Playwright-2")]
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
        await TestUtils.EventuallyAsync(async () =>
        {
            Assert.Single(await s.Page.Locator("li.list-group-item").AllAsync());
        });

        // Test amountmax
        await input.ClearAsync();
        await input.FillAsync("amountmax:0.002");
        await TestUtils.EventuallyAsync(async () =>
        {
            Assert.Single(await s.Page.Locator("li.list-group-item").AllAsync());
        });

        // Test general text (txid)
        await input.ClearAsync();
        await input.FillAsync(txs[2].ToString()[..8]);
        await TestUtils.EventuallyAsync(async () =>
        {
            Assert.Single(await s.Page.Locator("li.list-group-item").AllAsync());
        });

        // Test timestamp before/after
        await input.ClearAsync();
        await input.FillAsync("after:2099-01-01");
        await TestUtils.EventuallyAsync(async () =>
        {
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
    [Trait("Playwright", "Playwright-2")]
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
    [Trait("Playwright", "Playwright-2")]
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
    [Trait("Playwright", "Playwright-2")]
    public async Task CanManageWallet()
    {
        await using var s = CreatePlaywrightTester();
        await s.StartAsync();
        await s.RegisterNewUser(true);
        var (_, storeId) = await s.CreateNewStore();
        const string cryptoCode = "BTC";

        // ReSharper disable once GrammarMistakeInComment
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

        //let's import and save private keys
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

        var bip21 = invoice
            .EntityToDTO(s.Server.PayTester.GetService<Dictionary<PaymentMethodId, IPaymentMethodBitpayAPIExtension>>(),
                s.Server.PayTester.GetService<CurrencyNameTable>()).CryptoInfo.First().PaymentUrls.BIP21;
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
    [Trait("Playwright", "Playwright-2")]
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
    [Trait("Playwright", "Playwright-2")]
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

        var newExpectedEffectiveFeeRate = decimal.Parse(await s.Page.GetAttributeAsync("[name='FeeSatoshiPerByte']", "value") ?? string.Empty,
            CultureInfo.InvariantCulture);

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

        // However, the new transaction should have copied the CPFP tag from the transaction it replaced, and have an RBF label as well.
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
    [Trait("Playwright", "Playwright-2")]
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
        for (var i = 0; i < 6; i++)
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
            var x = store!.GetPaymentMethodConfig<DerivationSchemeSettings>(pmi, handlers);
            var wallet = s.Server.PayTester.GetService<BTCPayWalletProvider>().GetWallet(walletId.CryptoCode);
            wallet.InvalidateCache(x!.AccountDerivation);
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

        var bob = new Key().PubKey.Hash.GetAddress(Network.RegTest);
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
    [Trait("Playwright", "Playwright-2")]
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

        for (var i = 0; i < 5; i++)
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
