using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Hosting;
using BTCPayServer.Lightning;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Models.WalletViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.PointOfSale;
using BTCPayServer.Plugins.PointOfSale.Controllers;
using BTCPayServer.Plugins.PointOfSale.Models;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitpayClient;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;
using WalletSettingsViewModel = BTCPayServer.Models.StoreViewModels.WalletSettingsViewModel;

namespace BTCPayServer.Tests
{
    [Collection(nameof(NonParallelizableCollectionDefinition))]
    public class AltcoinTests : UnitTestBase
    {
        public const int TestTimeout = 60_000;
        public AltcoinTests(ITestOutputHelper helper) : base(helper)
        {
        }

        [Fact]
        [Trait("Integration", "Integration")]
        [Trait("Altcoins", "Altcoins")]
        [Trait("Lightning", "Lightning")]
        public async Task CanSetupWallet()
        {
            using (var tester = CreateServerTester())
            {
                tester.ActivateLTC();
                tester.ActivateLightning();
                await tester.StartAsync();
                var user = tester.NewAccount();
                var cryptoCode = "BTC";
                await user.GrantAccessAsync(true);
                user.RegisterDerivationScheme(cryptoCode);
                user.RegisterDerivationScheme("LTC");
                user.RegisterLightningNode(cryptoCode, LightningConnectionType.CLightning);
                user.SetLNUrl("BTC", false);
                var btcNetwork = tester.PayTester.Networks.GetNetwork<BTCPayNetwork>(cryptoCode);
                var invoice = await user.BitPay.CreateInvoiceAsync(
                    new Invoice
                    {
                        Price = 1.5m,
                        Currency = "USD",
                        PosData = "posData",
                        OrderId = "orderId",
                        ItemDesc = "Some description",
                        FullNotifications = true
                    }, Facade.Merchant);

                Assert.Equal(3, invoice.CryptoInfo.Length);

                // Setup Lightning
                var controller = user.GetController<UIStoresController>();
                var lightningVm = (LightningNodeViewModel)Assert.IsType<ViewResult>(controller.SetupLightningNode(user.StoreId, cryptoCode)).Model;
                Assert.True(lightningVm.Enabled);

                // Get enabled state from settings
                var response = controller.LightningSettings(user.StoreId, cryptoCode);
                var lnSettingsModel = (LightningSettingsViewModel)Assert.IsType<ViewResult>(response).Model;
                Assert.NotNull(lnSettingsModel?.ConnectionString);
                Assert.True(lnSettingsModel.Enabled);
                lnSettingsModel.Enabled = false;
                response = await controller.LightningSettings(lnSettingsModel);
                Assert.IsType<RedirectToActionResult>(response);
                response = controller.LightningSettings(user.StoreId, cryptoCode);
                lnSettingsModel = (LightningSettingsViewModel)Assert.IsType<ViewResult>(response).Model;
                Assert.False(lnSettingsModel.Enabled);

                // Setup wallet
                WalletSetupViewModel setupVm;
                var storeId = user.StoreId;
                response = await controller.GenerateWallet(storeId, cryptoCode, WalletSetupMethod.GenerateOptions, new WalletSetupRequest());
                Assert.IsType<ViewResult>(response);

                // Get enabled state from settings
                response = controller.WalletSettings(user.StoreId, cryptoCode).GetAwaiter().GetResult();
                var onchainSettingsModel = (WalletSettingsViewModel)Assert.IsType<ViewResult>(response).Model;
                Assert.NotNull(onchainSettingsModel?.DerivationScheme);
                Assert.True(onchainSettingsModel.Enabled);

                // Disable wallet
                onchainSettingsModel.Enabled = false;
                response = controller.UpdateWalletSettings(onchainSettingsModel).GetAwaiter().GetResult();
                Assert.IsType<RedirectToActionResult>(response);
                response = controller.WalletSettings(user.StoreId, cryptoCode).GetAwaiter().GetResult();
                onchainSettingsModel = (WalletSettingsViewModel)Assert.IsType<ViewResult>(response).Model;
                Assert.NotNull(onchainSettingsModel?.DerivationScheme);
                Assert.False(onchainSettingsModel.Enabled);

                var oldScheme = onchainSettingsModel.DerivationScheme;

                invoice = await user.BitPay.CreateInvoiceAsync(
                    new Invoice
                    {
                        Price = 1.5m,
                        Currency = "USD",
                        PosData = "posData",
                        OrderId = "orderId",
                        ItemDesc = "Some description",
                        FullNotifications = true
                    }, Facade.Merchant);

                Assert.Single(invoice.CryptoInfo);
                Assert.Equal("LTC", invoice.CryptoInfo[0].CryptoCode);

                // Removing the derivation scheme, should redirect to store page
                response = controller.ConfirmDeleteWallet(user.StoreId, cryptoCode).GetAwaiter().GetResult();
                Assert.IsType<RedirectToActionResult>(response);

                // Setting it again should show the confirmation page
                response = await controller.UpdateWallet(new WalletSetupViewModel { StoreId = storeId, CryptoCode = cryptoCode, DerivationScheme = oldScheme });
                setupVm = (WalletSetupViewModel)Assert.IsType<ViewResult>(response).Model;
                Assert.True(setupVm.Confirmation);

                // The following part posts a wallet update, confirms it and checks the result

                // cobo vault file
                var content = "{\"ExtPubKey\":\"xpub6CEqRFZ7yZxCFXuEWZBAdnC8bdvu9SRHevaoU2SsW9ZmKhrCShmbpGZWwaR15hdLURf8hg47g4TpPGaqEU8hw5LEJCE35AUhne67XNyFGBk\",\"MasterFingerprint\":\"7a7563b5\",\"DerivationPath\":\"M\\/84'\\/0'\\/0'\",\"CoboVaultFirmwareVersion\":\"1.2.0(BTC-Only)\"}";
                response = await controller.UpdateWallet(new WalletSetupViewModel { StoreId = storeId, CryptoCode = cryptoCode, WalletFile = TestUtils.GetFormFile("cobovault.json", content) });
                setupVm = (WalletSetupViewModel)Assert.IsType<ViewResult>(response).Model;
                Assert.True(setupVm.Confirmation);
                response = await controller.UpdateWallet(setupVm);
                Assert.IsType<RedirectToActionResult>(response);
                response = await controller.WalletSettings(storeId, cryptoCode);
                var settingsVm = (WalletSettingsViewModel)Assert.IsType<ViewResult>(response).Model;
                Assert.Equal("CoboVault", settingsVm.Source);

                // wasabi wallet file
                content = "{\r\n  \"EncryptedSecret\": \"6PYWBQ1zsukowsnTNA57UUx791aBuJusm7E4egXUmF5WGw3tcdG3cmTL57\",\r\n  \"ChainCode\": \"waSIVbn8HaoovoQg/0t8IS1+ZCxGsJRGFT21i06nWnc=\",\r\n  \"MasterFingerprint\": \"7a7563b5\",\r\n  \"ExtPubKey\": \"xpub6CEqRFZ7yZxCFXuEWZBAdnC8bdvu9SRHevaoU2SsW9ZmKhrCShmbpGZWwaR15hdLURf8hg47g4TpPGaqEU8hw5LEJCE35AUhne67XNyFGBk\",\r\n  \"PasswordVerified\": false,\r\n  \"MinGapLimit\": 21,\r\n  \"AccountKeyPath\": \"84'/0'/0'\",\r\n  \"BlockchainState\": {\r\n    \"Network\": \"RegTest\",\r\n    \"Height\": \"0\"\r\n  },\r\n  \"HdPubKeys\": []\r\n}";
                response = await controller.UpdateWallet(new WalletSetupViewModel { StoreId = storeId, CryptoCode = cryptoCode, WalletFile = TestUtils.GetFormFile("wasabi.json", content) });
                setupVm = (WalletSetupViewModel)Assert.IsType<ViewResult>(response).Model;
                Assert.True(setupVm.Confirmation);
                response = await controller.UpdateWallet(setupVm);
                Assert.IsType<RedirectToActionResult>(response);
                response = await controller.WalletSettings(storeId, cryptoCode);
                settingsVm = (WalletSettingsViewModel)Assert.IsType<ViewResult>(response).Model;
                Assert.Equal("WasabiFile", settingsVm.Source);

                // Can we upload coldcard settings? (Should fail, we are giving a mainnet file to a testnet network)
                content = "{\"keystore\": {\"ckcc_xpub\": \"xpub661MyMwAqRbcGVBsTGeNZN6QGVHmMHLdSA4FteGsRrEriu4pnVZMZWnruFFFXkMnyoBjyHndD3Qwcfz4MPzBUxjSevweNFQx7SAYZATtcDw\", \"xpub\": \"ypub6WWc2gWwHbdnAAyJDnR4SPL1phRh7REqrPBfZeizaQ1EmTshieRXJC3Z5YoU4wkcdKHEjQGkh6AYEzCQC1Kz3DNaWSwdc1pc8416hAjzqyD\", \"label\": \"Coldcard Import 0x60d1af8b\", \"ckcc_xfp\": 1624354699, \"type\": \"hardware\", \"hw_type\": \"coldcard\", \"derivation\": \"m/49'/0'/0'\"}, \"wallet_type\": \"standard\", \"use_encryption\": false, \"seed_version\": 17}";
                response = await controller.UpdateWallet(new WalletSetupViewModel { StoreId = storeId, CryptoCode = cryptoCode, WalletFile = TestUtils.GetFormFile("coldcard-ypub.json", content) });
                setupVm = (WalletSetupViewModel)Assert.IsType<ViewResult>(response).Model;
                Assert.False(setupVm.Confirmation); // Should fail, we are giving a mainnet file to a testnet network

                // And with a good file? (upub)
                content = "{\"keystore\": {\"ckcc_xpub\": \"tpubD6NzVbkrYhZ4YHNiuTdTmHRmbcPRLfqgyneZFCL1mkzkUBjXriQShxTh9HL34FK2mhieasJVk9EzJrUfkFqRNQBjiXgx3n5BhPkxKBoFmaS\", \"xpub\": \"upub5DBYp1qGgsTrkzCptMGZc2x18pquLwGrBw6nS59T4NViZ4cni1mGowQzziy85K8vzkp1jVtWrSkLhqk9KDfvrGeB369wGNYf39kX8rQfiLn\", \"label\": \"Coldcard Import 0x60d1af8b\", \"ckcc_xfp\": 1624354699, \"type\": \"hardware\", \"hw_type\": \"coldcard\", \"derivation\": \"m/49'/0'/0'\"}, \"wallet_type\": \"standard\", \"use_encryption\": false, \"seed_version\": 17}";
                response = await controller.UpdateWallet(new WalletSetupViewModel { StoreId = storeId, CryptoCode = cryptoCode, WalletFile = TestUtils.GetFormFile("coldcard-upub.json", content) });
                setupVm = (WalletSetupViewModel)Assert.IsType<ViewResult>(response).Model;
                Assert.True(setupVm.Confirmation);
                response = await controller.UpdateWallet(setupVm);
                Assert.IsType<RedirectToActionResult>(response);
                response = await controller.WalletSettings(storeId, cryptoCode);
                settingsVm = (WalletSettingsViewModel)Assert.IsType<ViewResult>(response).Model;
                Assert.Equal("ElectrumFile", settingsVm.Source);

                // Now let's check that no data has been lost in the process
                var store = tester.PayTester.StoreRepository.FindStore(storeId).GetAwaiter().GetResult();
                var handlers = tester.PayTester.GetService<PaymentMethodHandlerDictionary>();
                var pmi = PaymentTypes.CHAIN.GetPaymentMethodId("BTC");
                var onchainBTC = store.GetPaymentMethodConfig<DerivationSchemeSettings>(pmi, handlers);
                var network = handlers.GetBitcoinHandler("BTC").Network;
                FastTests.GetParsers().TryParseWalletFile(content, network, out var expected, out var error);
                var handler = handlers[pmi];
                Assert.Equal(JToken.FromObject(expected, handler.Serializer), JToken.FromObject(onchainBTC, handler.Serializer));
                Assert.Null(error);

                // Let's check that the root hdkey and account key path are taken into account when making a PSBT
                invoice = await user.BitPay.CreateInvoiceAsync(
                    new Invoice
                    {
                        Price = 1.5m,
                        Currency = "USD",
                        PosData = "posData",
                        OrderId = "orderId",
                        ItemDesc = "Some description",
                        FullNotifications = true
                    }, Facade.Merchant);

                tester.ExplorerNode.Generate(1);
                var invoiceAddress = BitcoinAddress.Create(invoice.CryptoInfo.First(c => c.CryptoCode == cryptoCode).Address,
                    tester.ExplorerNode.Network);
                tester.ExplorerNode.SendToAddress(invoiceAddress, Money.Coins(1m));
                TestUtils.Eventually(() =>
                {
                    invoice = user.BitPay.GetInvoice(invoice.Id);
                    Assert.Equal("paid", invoice.Status);
                });
                var wallet = tester.PayTester.GetController<UIWalletsController>();
                var psbt = wallet.CreatePSBT(btcNetwork, onchainBTC,
                    new WalletSendModel()
                    {
                        Outputs = new List<WalletSendModel.TransactionOutput>
                        {
                            new WalletSendModel.TransactionOutput
                            {
                                Amount = 0.5m,
                                DestinationAddress = new Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, btcNetwork.NBitcoinNetwork)
                                    .ToString(),
                            }
                        },
                        FeeSatoshiPerByte = 1
                    }, default).GetAwaiter().GetResult();

                Assert.NotNull(psbt);

                var root = new Mnemonic(
                        "usage fever hen zero slide mammal silent heavy donate budget pulse say brain thank sausage brand craft about save attract muffin advance illegal cabbage")
                    .DeriveExtKey().AsHDKeyCache();
                var account = root.Derive(new KeyPath("m/49'/0'/0'"));
                Assert.All(psbt.PSBT.Inputs, input =>
                {
                    var keyPath = input.HDKeyPaths.Single();
                    Assert.False(keyPath.Value.KeyPath.IsHardened);
                    Assert.Equal(account.Derive(keyPath.Value.KeyPath).GetPublicKey(), keyPath.Key);
                    Assert.Equal(keyPath.Value.MasterFingerprint,
                        onchainBTC.AccountKeySettings[0].AccountKey.GetPublicKey().GetHDFingerPrint());
                });
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        [Trait("Altcoins", "Altcoins")]
        [Trait("Lightning", "Lightning")]
        public async Task CanCreateInvoiceWithSpecificPaymentMethods()
        {
            using (var tester = CreateServerTester())
            {
                tester.ActivateLightning();
                tester.ActivateLTC();
                await tester.StartAsync();
                await tester.EnsureChannelsSetup();
                var user = tester.NewAccount();
                user.GrantAccess(true);
                user.RegisterLightningNode("BTC");
                user.RegisterDerivationScheme("BTC");
                user.RegisterDerivationScheme("LTC");

                var invoice = await user.BitPay.CreateInvoiceAsync(new Invoice(100, "BTC"));
                Assert.Equal(2, invoice.SupportedTransactionCurrencies.Count);

                invoice = await user.BitPay.CreateInvoiceAsync(new Invoice(100, "BTC")
                {
                    SupportedTransactionCurrencies = new Dictionary<string, InvoiceSupportedTransactionCurrency>()
                    {
                        {"BTC", new InvoiceSupportedTransactionCurrency() {Enabled = true}}
                    }
                });

                Assert.Single(invoice.SupportedTransactionCurrencies);
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        [Trait("Altcoins", "Altcoins")]
        public async Task CanHaveLTCOnlyStore()
        {
            using (var tester = CreateServerTester())
            {
                tester.ActivateLTC();
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("LTC");

                // First we try payment with a merchant having only BTC
                var invoice = user.BitPay.CreateInvoice(
                    new Invoice()
                    {
                        Price = 500,
                        Currency = "USD",
                        PosData = "posData",
                        OrderId = "orderId",
                        ItemDesc = "Some description",
                        FullNotifications = true
                    }, Facade.Merchant);

                Assert.Single(invoice.CryptoInfo);
                Assert.Equal("LTC", invoice.CryptoInfo[0].CryptoCode);
                Assert.True(invoice.PaymentCodes.ContainsKey("LTC"));
                Assert.True(invoice.SupportedTransactionCurrencies.ContainsKey("LTC"));
                Assert.True(invoice.SupportedTransactionCurrencies["LTC"].Enabled);
                Assert.True(invoice.PaymentSubtotals.ContainsKey("LTC"));
                Assert.True(invoice.PaymentTotals.ContainsKey("LTC"));
                var cashCow = tester.LTCExplorerNode;
                var invoiceAddress = BitcoinAddress.Create(invoice.CryptoInfo[0].Address, cashCow.Network);
                var firstPayment = Money.Coins(0.1m);
                var firstDue = invoice.CryptoInfo[0].Due;
                cashCow.SendToAddress(invoiceAddress, firstPayment);
                TestUtils.Eventually(() =>
                {
                    invoice = user.BitPay.GetInvoice(invoice.Id);
                    Assert.Equal(firstPayment, invoice.CryptoInfo[0].Paid);
                    Assert.Equal("paidPartial", invoice.ExceptionStatus?.ToString());
                });

                Assert.Single(invoice.CryptoInfo); // Only BTC should be presented

                var controller = tester.PayTester.GetController<UIInvoiceController>(null);
                var checkout =
                    (Models.InvoicingModels.CheckoutModel)((JsonResult)controller.GetStatus(invoice.Id)
                        .GetAwaiter().GetResult()).Value;
                Assert.Single(checkout.AvailablePaymentMethods);
                Assert.Equal("LTC", checkout.PaymentMethodCurrency);

                //////////////////////

                // Despite it is called BitcoinAddress it should be LTC because BTC is not available
                Assert.Null(invoice.BitcoinAddress);
                Assert.NotEqual(1.0m, invoice.Rate);
                Assert.NotEqual(invoice.BtcDue, invoice.CryptoInfo[0].Due); // Should be BTC rate
                cashCow.SendToAddress(invoiceAddress, invoice.CryptoInfo[0].Due);

                TestUtils.Eventually(() =>
                {
                    invoice = user.BitPay.GetInvoice(invoice.Id);
                    Assert.Equal("paid", invoice.Status);
                    checkout = (Models.InvoicingModels.CheckoutModel)((JsonResult)controller.GetStatus(invoice.Id)
                        .GetAwaiter().GetResult()).Value;
                    Assert.Equal("Processing", checkout.Status);
                });
            }
        }


        [Fact]
        [Trait("Selenium", "Selenium")]
        [Trait("Altcoins", "Altcoins")]
        public async Task CanCreateRefunds()
        {
            using (var s = CreateSeleniumTester())
            {
                s.Server.ActivateLTC();
                await s.StartAsync();
                var user = s.Server.NewAccount();
                await user.GrantAccessAsync();
                s.GoToLogin();
                s.LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
                user.RegisterDerivationScheme("BTC");
                await s.Server.ExplorerNode.GenerateAsync(1);

                foreach (var multiCurrency in new[] { false, true })
                {
                    if (multiCurrency)
                        user.RegisterDerivationScheme("LTC");
                    foreach (var rateSelection in new[] { "FiatOption", "CurrentRateOption", "RateThenOption", "CustomOption" })
                    {
                        TestLogs.LogInformation((multiCurrency, rateSelection).ToString());
                        await CanCreateRefundsCore(s, user, multiCurrency, rateSelection);
                    }
                }
            }
        }

        private static async Task CanCreateRefundsCore(SeleniumTester s, TestAccount user, bool multiCurrency, string rateSelection)
        {
            s.GoToHome();
            s.Server.PayTester.ChangeRate("BTC_USD", new Rating.BidAsk(5000.0m, 5100.0m));
            var invoice = await user.BitPay.CreateInvoiceAsync(new Invoice
            {
                Currency = "USD",
                Price = 5000.0m
            });
            var info = invoice.CryptoInfo.First(o => o.CryptoCode == "BTC");
            var totalDue = decimal.Parse(info.TotalDue, CultureInfo.InvariantCulture);
            var paid = totalDue + 0.1m;
            await s.Server.ExplorerNode.SendToAddressAsync(BitcoinAddress.Create(info.Address, Network.RegTest), Money.Coins(paid));
            await s.Server.ExplorerNode.GenerateAsync(1);
            await TestUtils.EventuallyAsync(async () =>
            {
                invoice = await user.BitPay.GetInvoiceAsync(invoice.Id);
                Assert.Equal("complete", invoice.Status);
            });

            // BTC crash by 50%
            s.Server.PayTester.ChangeRate("BTC_USD", new Rating.BidAsk(5000.0m / 2.0m, 5100.0m / 2.0m));
            s.GoToStore();
            s.Driver.FindElement(By.Id("BOLT11Expiration")).Clear();
            s.Driver.FindElement(By.Id("BOLT11Expiration")).SendKeys("5" + Keys.Enter);
            s.GoToInvoice(invoice.Id);
            s.Driver.FindElement(By.Id("IssueRefund")).Click();

            if (multiCurrency)
            {
                s.Driver.WaitUntilAvailable(By.Id("RefundForm"), TimeSpan.FromSeconds(1));
                s.Driver.WaitUntilAvailable(By.Id("SelectedPayoutMethod"), TimeSpan.FromSeconds(1));
                s.Driver.FindElement(By.Id("SelectedPayoutMethod")).SendKeys("BTC" + Keys.Enter);
                s.Driver.FindElement(By.Id("ok")).Click();
            }
            s.Driver.WaitUntilAvailable(By.Id("RefundForm"), TimeSpan.FromSeconds(1));
            Assert.Contains("5,500.00 USD", s.Driver.PageSource); // Should propose reimburse in fiat
            Assert.Contains("1.10000000 BTC", s.Driver.PageSource); // Should propose reimburse in BTC at the rate of before
            Assert.Contains("2.20000000 BTC", s.Driver.PageSource); // Should propose reimburse in BTC at the current rate
            s.Driver.WaitForAndClick(By.Id(rateSelection));
            s.Driver.FindElement(By.Id("ok")).Click();

            s.Driver.WaitUntilAvailable(By.Id("Destination"), TimeSpan.FromSeconds(1));
            Assert.Contains("pull-payments", s.Driver.Url);
            if (rateSelection == "FiatOption")
                Assert.Contains("5,500.00 USD", s.Driver.PageSource);
            if (rateSelection == "CurrentOption")
                Assert.Contains("2.20000000 BTC", s.Driver.PageSource);
            if (rateSelection == "RateThenOption")
                Assert.Contains("1.10000000 BTC", s.Driver.PageSource);

            s.GoToInvoice(invoice.Id);
            s.Driver.FindElement(By.Id("IssueRefund")).Click();
            s.Driver.WaitUntilAvailable(By.Id("Destination"), TimeSpan.FromSeconds(1));
            Assert.Contains("pull-payments", s.Driver.Url);
            var client = await user.CreateClient();
            var ppid = s.Driver.Url.Split('/').Last();
            var pps = await client.GetPullPayments(user.StoreId);
            var pp = Assert.Single(pps, p => p.Id == ppid);
            Assert.Equal(TimeSpan.FromDays(5.0), pp.BOLT11Expiration);
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        [Trait("Altcoins", "Altcoins")]
        public async Task CanPayWithTwoCurrencies()
        {
            using (var tester = CreateServerTester())
            {
                tester.ActivateLTC();
                await tester.StartAsync();
                var user = tester.NewAccount();
                await user.GrantAccessAsync();
                user.RegisterDerivationScheme("BTC");
                // First we try payment with a merchant having only BTC
                var invoice = await user.BitPay.CreateInvoiceAsync(
                    new Invoice
                    {
                        Price = 5000.0m,
                        Currency = "USD",
                        PosData = "posData",
                        OrderId = "orderId",
                        ItemDesc = "Some description",
                        FullNotifications = true
                    }, Facade.Merchant);

                var cashCow = tester.ExplorerNode;
                await cashCow.GenerateAsync(2); // get some money in case
                var invoiceAddress = BitcoinAddress.Create(invoice.BitcoinAddress, cashCow.Network);
                var firstPayment = Money.Coins(0.04m);
                await cashCow.SendToAddressAsync(invoiceAddress, firstPayment);
                TestUtils.Eventually(() =>
                {
                    invoice = user.BitPay.GetInvoice(invoice.Id);
                    Assert.True(invoice.BtcPaid == firstPayment);
                });

                Assert.Single(invoice.CryptoInfo); // Only BTC should be presented

                var controller = tester.PayTester.GetController<UIInvoiceController>(null);
                var checkout =
                    (Models.InvoicingModels.CheckoutModel)((JsonResult)controller.GetStatus(invoice.Id, null)
                        .GetAwaiter().GetResult()).Value;
                Assert.Single(checkout.AvailablePaymentMethods);
                Assert.Equal("BTC", checkout.PaymentMethodCurrency);

                Assert.Single(invoice.PaymentCodes);
                Assert.Single(invoice.SupportedTransactionCurrencies);
                Assert.Single(invoice.SupportedTransactionCurrencies);
                Assert.Single(invoice.PaymentSubtotals);
                Assert.Single(invoice.PaymentTotals);
                Assert.True(invoice.PaymentCodes.ContainsKey("BTC"));
                Assert.True(invoice.SupportedTransactionCurrencies.ContainsKey("BTC"));
                Assert.True(invoice.SupportedTransactionCurrencies["BTC"].Enabled);
                Assert.True(invoice.PaymentSubtotals.ContainsKey("BTC"));
                Assert.True(invoice.PaymentTotals.ContainsKey("BTC"));
                //////////////////////

                // Retry now with LTC enabled
                user.RegisterDerivationScheme("LTC");
                invoice = await user.BitPay.CreateInvoiceAsync(
                    new Invoice
                    {
                        Price = 5000.0m,
                        Currency = "USD",
                        PosData = "posData",
                        OrderId = "orderId",
                        ItemDesc = "Some description",
                        FullNotifications = true
                    }, Facade.Merchant);

                cashCow = tester.ExplorerNode;
                invoiceAddress = BitcoinAddress.Create(invoice.BitcoinAddress, cashCow.Network);
                firstPayment = Money.Coins(0.04m);
                await cashCow.SendToAddressAsync(invoiceAddress, firstPayment);
                TestLogs.LogInformation("First payment sent to " + invoiceAddress);
                TestUtils.Eventually(() =>
                {
                    invoice = user.BitPay.GetInvoice(invoice.Id);
                    Assert.True(invoice.BtcPaid == firstPayment);
                });

                cashCow = tester.LTCExplorerNode;
                var ltcCryptoInfo = invoice.CryptoInfo.FirstOrDefault(c => c.CryptoCode == "LTC");
                Assert.NotNull(ltcCryptoInfo);
                invoiceAddress = BitcoinAddress.Create(ltcCryptoInfo.Address, cashCow.Network);
                var secondPayment = Money.Coins(decimal.Parse(ltcCryptoInfo.Due, CultureInfo.InvariantCulture));
                await cashCow.GenerateAsync(4); // LTC is not worth a lot, so just to make sure we have money...
                await cashCow.SendToAddressAsync(invoiceAddress, secondPayment);
                TestLogs.LogInformation("Second payment sent to " + invoiceAddress);
                TestUtils.Eventually(() =>
                {
                    invoice = user.BitPay.GetInvoice(invoice.Id);
                    Assert.Equal(Money.Zero, invoice.BtcDue);
                    var ltcPaid = invoice.CryptoInfo.First(c => c.CryptoCode == "LTC");
                    Assert.Equal(Money.Zero, ltcPaid.Due);
                    Assert.Equal(secondPayment, ltcPaid.CryptoPaid);
                    Assert.Equal("paid", invoice.Status);
                    Assert.False((bool)((JValue)invoice.ExceptionStatus).Value);
                });

                controller = tester.PayTester.GetController<UIInvoiceController>(null);
                checkout = (Models.InvoicingModels.CheckoutModel)((JsonResult)controller.GetStatus(invoice.Id, "LTC")
                    .GetAwaiter().GetResult()).Value;
                Assert.Equal(2, checkout.AvailablePaymentMethods.Count);
                Assert.Equal("LTC", checkout.PaymentMethodCurrency);

                Assert.Equal(2, invoice.PaymentCodes.Count());
                Assert.Equal(2, invoice.SupportedTransactionCurrencies.Count());
                Assert.Equal(2, invoice.SupportedTransactionCurrencies.Count());
                Assert.Equal(2, invoice.PaymentSubtotals.Count());
                Assert.Equal(2, invoice.PaymentTotals.Count());
                Assert.True(invoice.PaymentCodes.ContainsKey("LTC"));
                Assert.True(invoice.SupportedTransactionCurrencies.ContainsKey("LTC"));
                Assert.True(invoice.SupportedTransactionCurrencies["LTC"].Enabled);
                Assert.True(invoice.PaymentSubtotals.ContainsKey("LTC"));
                Assert.True(invoice.PaymentTotals.ContainsKey("LTC"));

                // Check if we can disable LTC
                invoice = await user.BitPay.CreateInvoiceAsync(
                    new Invoice
                    {
                        Price = 5000.0m,
                        Currency = "USD",
                        PosData = "posData",
                        OrderId = "orderId",
                        ItemDesc = "Some description",
                        FullNotifications = true,
                        SupportedTransactionCurrencies = new Dictionary<string, InvoiceSupportedTransactionCurrency>()
                        {
                            {"BTC", new InvoiceSupportedTransactionCurrency() {Enabled = true}}
                        }
                    }, Facade.Merchant);

                Assert.Single(invoice.CryptoInfo.Where(c => c.CryptoCode == "BTC"));
                Assert.Empty(invoice.CryptoInfo.Where(c => c.CryptoCode == "LTC"));
            }
        }

        [Fact]
        [Trait("Integration", "Integration")]
        [Trait("Altcoins", "Altcoins")]
        public async Task CanUsePoSApp()
        {
            using (var tester = CreateServerTester())
            {
                tester.ActivateLTC();
                await tester.StartAsync();
                var user = tester.NewAccount();
                await user.GrantAccessAsync();
                user.RegisterDerivationScheme("BTC");
                user.RegisterDerivationScheme("LTC");
                var apps = user.GetController<UIAppsController>();
                var pos = user.GetController<UIPointOfSaleController>();
                var vm = Assert.IsType<CreateAppViewModel>(Assert.IsType<ViewResult>(apps.CreateApp(user.StoreId)).Model);
                var appType = PointOfSaleAppType.AppType;
                vm.AppName = "test";
                vm.SelectedAppType = appType;
                var redirect = Assert.IsType<RedirectResult>(apps.CreateApp(user.StoreId, vm).Result);
                Assert.EndsWith("/settings/pos", redirect.Url);
                var appList = Assert.IsType<ListAppsViewModel>(Assert.IsType<ViewResult>(apps.ListApps(user.StoreId).Result).Model);
                var app = appList.Apps[0];
                var appData = new AppData { Id = app.Id, StoreDataId = app.StoreId, Name = app.AppName, AppType = appType };
                apps.HttpContext.SetAppData(appData);
                pos.HttpContext.SetAppData(appData);
                var vmpos = await pos.UpdatePointOfSale(app.Id).AssertViewModelAsync<UpdatePointOfSaleViewModel>();
                vmpos.Title = "hello";
                vmpos.Currency = "CAD";
                vmpos.ButtonText = "{0} Purchase";
                vmpos.CustomButtonText = "Nicolas Sexy Hair";
                vmpos.CustomTipText = "Wanna tip?";
                vmpos.CustomTipPercentages = "15,18,20";
                vmpos.Template = @"
apple:
  price: 5.0
  title: good apple
orange:
  price: 10.0
donation:
  price: 1.02
  custom: true
";
                vmpos.Template = AppService.SerializeTemplate(MigrationStartupTask.ParsePOSYML(vmpos.Template));
                Assert.IsType<RedirectToActionResult>(pos.UpdatePointOfSale(app.Id, vmpos).Result);
                vmpos = await pos.UpdatePointOfSale(app.Id).AssertViewModelAsync<UpdatePointOfSaleViewModel>();
                Assert.Equal("hello", vmpos.Title);

                var publicApps = user.GetController<UIPointOfSaleController>();
                var vmview = await publicApps.ViewPointOfSale(app.Id, PosViewType.Cart).AssertViewModelAsync<ViewPointOfSaleViewModel>();
                Assert.Equal("hello", vmview.Title);
                Assert.Equal(3, vmview.Items.Length);
                Assert.Equal("good apple", vmview.Items[0].Title);
                Assert.Equal("orange", vmview.Items[1].Title);
                Assert.Equal(10.0m, vmview.Items[1].Price);
                Assert.Equal("{0} Purchase", vmview.ButtonText);
                Assert.Equal("Nicolas Sexy Hair", vmview.CustomButtonText);
                Assert.Equal("Wanna tip?", vmview.CustomTipText);
                Assert.Equal("15,18,20", string.Join(',', vmview.CustomTipPercentages));
                Assert.IsType<RedirectToActionResult>(publicApps
                    .ViewPointOfSale(app.Id, PosViewType.Cart, 0, choiceKey: "orange").Result);

                //
                var invoices = await user.BitPay.GetInvoicesAsync();
                var orangeInvoice = invoices.First();
                Assert.Equal(10.00m, orangeInvoice.Price);
                Assert.Equal("CAD", orangeInvoice.Currency);
                Assert.Equal("orange", orangeInvoice.ItemDesc);
                Assert.IsType<RedirectToActionResult>(publicApps
                    .ViewPointOfSale(app.Id, PosViewType.Cart, 0, choiceKey: "apple").Result);

                invoices = await user.BitPay.GetInvoicesAsync();
                var appleInvoice = invoices.SingleOrDefault(invoice => invoice.ItemCode.Equals("apple"));
                Assert.NotNull(appleInvoice);
                Assert.Equal("good apple", appleInvoice.ItemDesc);

                // testing custom amount
                var action = Assert.IsType<RedirectToActionResult>(publicApps
                    .ViewPointOfSale(app.Id, PosViewType.Cart, 6.6m, choiceKey: "donation").Result);
                Assert.Equal(nameof(UIInvoiceController.Checkout), action.ActionName);
                invoices = await user.BitPay.GetInvoicesAsync();
                var donationInvoice = invoices.Single(i => i.Price == 6.6m);
                Assert.NotNull(donationInvoice);
                Assert.Equal("CAD", donationInvoice.Currency);
                Assert.Equal("donation", donationInvoice.ItemDesc);

                foreach (var test in new[]
                {
                    (Code: "EUR", ExpectedSymbol: "€", ExpectedDecimalSeparator: ",", ExpectedDivisibility: 2,
                        ExpectedThousandSeparator: "\xa0", ExpectedPrefixed: false, ExpectedSymbolSpace: true),
                    (Code: "INR", ExpectedSymbol: "₹", ExpectedDecimalSeparator: ".", ExpectedDivisibility: 2,
                        ExpectedThousandSeparator: ",", ExpectedPrefixed: true, ExpectedSymbolSpace: true),
                    (Code: "JPY", ExpectedSymbol: "¥", ExpectedDecimalSeparator: ".", ExpectedDivisibility: 0,
                        ExpectedThousandSeparator: ",", ExpectedPrefixed: true, ExpectedSymbolSpace: false),
                    (Code: "BTC", ExpectedSymbol: "₿", ExpectedDecimalSeparator: ".", ExpectedDivisibility: 8,
                        ExpectedThousandSeparator: ",", ExpectedPrefixed: false, ExpectedSymbolSpace: true),
                })
                {
                    TestLogs.LogInformation($"Testing for {test.Code}");
                    vmpos = await pos.UpdatePointOfSale(app.Id).AssertViewModelAsync<UpdatePointOfSaleViewModel>();
                    vmpos.Title = "hello";
                    vmpos.Currency = test.Item1;
                    vmpos.ButtonText = "{0} Purchase";
                    vmpos.CustomButtonText = "Nicolas Sexy Hair";
                    vmpos.CustomTipText = "Wanna tip?";
                    vmpos.Template = @"
apple:
  price: 1000.0
  title: good apple
orange:
  price: 10.0
donation:
  price: 1.02
  custom: true
";
                    vmpos.Template = AppService.SerializeTemplate(MigrationStartupTask.ParsePOSYML(vmpos.Template));
                    Assert.IsType<RedirectToActionResult>(pos.UpdatePointOfSale(app.Id, vmpos).Result);
                    publicApps = user.GetController<UIPointOfSaleController>();
                    vmview = await publicApps.ViewPointOfSale(app.Id, PosViewType.Cart).AssertViewModelAsync<ViewPointOfSaleViewModel>();
                    Assert.Equal(test.Code, vmview.CurrencyCode);
                    Assert.Equal(test.ExpectedSymbol,
                        vmview.CurrencySymbol.Replace("￥", "¥")); // Hack so JPY test pass on linux as well);
                    Assert.Equal(test.ExpectedSymbol,
                        vmview.CurrencyInfo.CurrencySymbol
                            .Replace("￥", "¥")); // Hack so JPY test pass on linux as well);
                    Assert.Equal(test.ExpectedDecimalSeparator, vmview.CurrencyInfo.DecimalSeparator);
                    Assert.Equal(test.ExpectedThousandSeparator, vmview.CurrencyInfo.ThousandSeparator);
                    Assert.Equal(test.ExpectedPrefixed, vmview.CurrencyInfo.Prefixed);
                    Assert.Equal(test.ExpectedDivisibility, vmview.CurrencyInfo.Divisibility);
                    Assert.Equal(test.ExpectedSymbolSpace, vmview.CurrencyInfo.SymbolSpace);
                }

                //test inventory related features
                vmpos = await pos.UpdatePointOfSale(app.Id).AssertViewModelAsync<UpdatePointOfSaleViewModel>();
                vmpos.Title = "hello";
                vmpos.Currency = "BTC";
                vmpos.Template = @"
inventoryitem:
  price: 1.0
  title: good apple
  inventory: 1
noninventoryitem:
  price: 10.0";

                vmpos.Template = AppService.SerializeTemplate(MigrationStartupTask.ParsePOSYML(vmpos.Template));
                Assert.IsType<RedirectToActionResult>(pos.UpdatePointOfSale(app.Id, vmpos).Result);

                async Task AssertCanBuy(string choiceKey, bool expected)
                {
                    var redirect = Assert.IsType<RedirectToActionResult>(await publicApps
                        .ViewPointOfSale(app.Id, PosViewType.Cart, 1, choiceKey: choiceKey));
                    if (expected)
                        Assert.Equal("UIInvoice", redirect.ControllerName);
                    else
                        Assert.NotEqual("UIInvoice", redirect.ControllerName);
                }

                //inventoryitem has 1 item available
                await AssertCanBuy("inventoryitem", true);

                //we already bought all available stock so this should fail
                await Task.Delay(100);
                await AssertCanBuy("inventoryitem", false);

                //inventoryitem has unlimited items available
                await AssertCanBuy("noninventoryitem", true);
                await AssertCanBuy("noninventoryitem", true);

                //verify invoices where created
                invoices = user.BitPay.GetInvoices();
                Assert.Equal(2, invoices.Count(invoice => invoice.ItemCode.Equals("noninventoryitem")));
                var inventoryItemInvoice =
                    Assert.Single(invoices.Where(invoice => invoice.ItemCode.Equals("inventoryitem")));
                Assert.NotNull(inventoryItemInvoice);

                //let's mark the inventoryitem invoice as invalid, this should return the item to back in stock
                var controller = tester.PayTester.GetController<UIInvoiceController>(user.UserId, user.StoreId);
                Assert.IsType<JsonResult>(await controller.ChangeInvoiceState(inventoryItemInvoice.Id, "invalid"));
                //check that item is back in stock
                await TestUtils.EventuallyAsync(async () =>
                {
                    vmpos = await pos.UpdatePointOfSale(app.Id).AssertViewModelAsync<UpdatePointOfSaleViewModel>();
                    Assert.Equal(1,
                        AppService.Parse(vmpos.Template).Single(item => item.Id == "inventoryitem").Inventory);
                }, 10000);

                //test payment methods option
                vmpos = await pos.UpdatePointOfSale(app.Id).AssertViewModelAsync<UpdatePointOfSaleViewModel>();
                vmpos.Title = "hello";
                vmpos.Currency = "BTC";
                vmpos.Template = @"
btconly:
  price: 1.0
  title: good apple
  payment_methods:
    - BTC
normal:
  price: 1.0";
                vmpos.Template = AppService.SerializeTemplate(MigrationStartupTask.ParsePOSYML(vmpos.Template));
                Assert.IsType<RedirectToActionResult>(pos.UpdatePointOfSale(app.Id, vmpos).Result);
                Assert.IsType<RedirectToActionResult>(publicApps
                    .ViewPointOfSale(app.Id, PosViewType.Cart, 1, choiceKey: "btconly").Result);
                Assert.IsType<RedirectToActionResult>(publicApps
                    .ViewPointOfSale(app.Id, PosViewType.Cart, 1, choiceKey: "normal").Result);
                invoices = user.BitPay.GetInvoices();
                var normalInvoice = invoices.Single(invoice => invoice.ItemCode == "normal");
                var btcOnlyInvoice = invoices.Single(invoice => invoice.ItemCode == "btconly");
                Assert.Single(btcOnlyInvoice.CryptoInfo);
                Assert.Equal("BTC",
                    btcOnlyInvoice.CryptoInfo.First().CryptoCode);
                Assert.Equal("BTC-CHAIN",
                    btcOnlyInvoice.CryptoInfo.First().PaymentType);

                Assert.Equal(2, normalInvoice.CryptoInfo.Length);
                Assert.Contains(
                    normalInvoice.CryptoInfo,
                    s => "BTC-CHAIN" == s.PaymentType && new[] { "BTC", "LTC" }.Contains(
                             s.CryptoCode));

                //test topup option
                vmpos.Template = @"
a:
  price: 1000.0
  title: good apple

b:
  price: 10.0
  custom: false
c:
  price: 1.02
  custom: true
d:
  price: 1.02
  price_type: fixed
e:
  price: 1.02
  price_type: minimum
f:
  price_type: topup
g:
  custom: topup
";

                vmpos.Template = AppService.SerializeTemplate(MigrationStartupTask.ParsePOSYML(vmpos.Template));
                Assert.IsType<RedirectToActionResult>(pos.UpdatePointOfSale(app.Id, vmpos).Result);
                vmpos = await pos.UpdatePointOfSale(app.Id).AssertViewModelAsync<UpdatePointOfSaleViewModel>();
                Assert.DoesNotContain("custom", vmpos.Template);
                var items = AppService.Parse(vmpos.Template);
                Assert.Contains(items, item => item.Id == "a" && item.PriceType == ViewPointOfSaleViewModel.ItemPriceType.Fixed);
                Assert.Contains(items, item => item.Id == "b" && item.PriceType == ViewPointOfSaleViewModel.ItemPriceType.Fixed);
                Assert.Contains(items, item => item.Id == "c" && item.PriceType == ViewPointOfSaleViewModel.ItemPriceType.Minimum);
                Assert.Contains(items, item => item.Id == "d" && item.PriceType == ViewPointOfSaleViewModel.ItemPriceType.Fixed);
                Assert.Contains(items, item => item.Id == "e" && item.PriceType == ViewPointOfSaleViewModel.ItemPriceType.Minimum);
                Assert.Contains(items, item => item.Id == "f" && item.PriceType == ViewPointOfSaleViewModel.ItemPriceType.Topup);
                Assert.Contains(items, item => item.Id == "g" && item.PriceType == ViewPointOfSaleViewModel.ItemPriceType.Topup);

                Assert.IsType<RedirectToActionResult>(publicApps
                    .ViewPointOfSale(app.Id, PosViewType.Static, choiceKey: "g").Result);
                invoices = user.BitPay.GetInvoices();
                var topupInvoice = invoices.Single(invoice => invoice.ItemCode == "g");
                Assert.Equal(0, topupInvoice.Price);
                Assert.Equal("new", topupInvoice.Status);
            }
        }
    }
}
