using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Lightning;
using BTCPayServer.Models;
using BTCPayServer.Models.AccountViewModels;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Models.ServerViewModels;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Models.WalletViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Rating;
using BTCPayServer.Security.Bitpay;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Tests.Logging;
using BTCPayServer.U2F.Models;
using BTCPayServer.Validation;
using ExchangeSharp;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBitcoin.Payment;
using NBitcoin.Scripting.Parser;
using NBitpayClient;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;
using OpenQA.Selenium;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;
using RatesViewModel = BTCPayServer.Models.StoreViewModels.RatesViewModel;

namespace BTCPayServer.Tests
{
    public class AltcoinTests
    {
        public const int TestTimeout = 60_000;
        public AltcoinTests(ITestOutputHelper helper)
        {
            Logs.Tester = new XUnitLog(helper) { Name = "Tests" };
            Logs.LogProvider = new XUnitLogProvider(helper);
        }

        [Fact]
        [Trait("Integration", "Integration")]
        [Trait("Altcoins", "Altcoins")]
        [Trait("Lightning", "Lightning")]
        public async Task CanAddDerivationSchemes()
        {
            using (var tester = ServerTester.Create())
            {
                tester.ActivateLTC();
                tester.ActivateLightning();
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess(true);
                user.RegisterDerivationScheme("BTC");
                user.RegisterDerivationScheme("LTC");
                user.RegisterLightningNode("BTC", LightningConnectionType.CLightning);
                var btcNetwork = tester.PayTester.Networks.GetNetwork<BTCPayNetwork>("BTC");
                var invoice = user.BitPay.CreateInvoice(
                    new Invoice()
                    {
                        Price = 1.5m,
                        Currency = "USD",
                        PosData = "posData",
                        OrderId = "orderId",
                        ItemDesc = "Some description",
                        FullNotifications = true
                    }, Facade.Merchant);

                Assert.Equal(3, invoice.CryptoInfo.Length);

                var controller = user.GetController<StoresController>();
                var lightningVM =
                    (LightningNodeViewModel)Assert.IsType<ViewResult>(controller.AddLightningNode(user.StoreId, "BTC"))
                        .Model;
                Assert.True(lightningVM.Enabled);
                lightningVM.Enabled = false;
                controller.AddLightningNode(user.StoreId, lightningVM, "save", "BTC").GetAwaiter().GetResult();
                lightningVM =
                    (LightningNodeViewModel)Assert.IsType<ViewResult>(controller.AddLightningNode(user.StoreId, "BTC"))
                        .Model;
                Assert.False(lightningVM.Enabled);

                // Only Enabling/Disabling the payment method must redirect to store page
                var derivationVM = (DerivationSchemeViewModel)Assert
                    .IsType<ViewResult>(await controller.AddDerivationScheme(user.StoreId, "BTC")).Model;
                Assert.True(derivationVM.Enabled);
                derivationVM.Enabled = false;
                Assert.IsType<RedirectToActionResult>(controller.AddDerivationScheme(user.StoreId, derivationVM, "BTC")
                    .GetAwaiter().GetResult());
                derivationVM = (DerivationSchemeViewModel)Assert
                    .IsType<ViewResult>(await controller.AddDerivationScheme(user.StoreId, "BTC")).Model;
                Assert.False(derivationVM.Enabled);

                // Clicking next without changing anything should send to the confirmation screen
                derivationVM = (DerivationSchemeViewModel)Assert
                    .IsType<ViewResult>(await controller.AddDerivationScheme(user.StoreId, "BTC")).Model;
                derivationVM = (DerivationSchemeViewModel)Assert.IsType<ViewResult>(controller
                    .AddDerivationScheme(user.StoreId, derivationVM, "BTC").GetAwaiter().GetResult()).Model;
                Assert.True(derivationVM.Confirmation);

                invoice = user.BitPay.CreateInvoice(
                    new Invoice()
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
                var oldScheme = derivationVM.DerivationScheme;
                derivationVM = (DerivationSchemeViewModel)Assert
                    .IsType<ViewResult>(await controller.AddDerivationScheme(user.StoreId, "BTC")).Model;
                derivationVM.DerivationScheme = null;
                Assert.IsType<RedirectToActionResult>(controller.AddDerivationScheme(user.StoreId, derivationVM, "BTC")
                    .GetAwaiter().GetResult());

                // Setting it again should redirect to the confirmation page
                derivationVM = (DerivationSchemeViewModel)Assert
                    .IsType<ViewResult>(await controller.AddDerivationScheme(user.StoreId, "BTC")).Model;
                derivationVM.DerivationScheme = oldScheme;
                derivationVM = (DerivationSchemeViewModel)Assert.IsType<ViewResult>(controller
                    .AddDerivationScheme(user.StoreId, derivationVM, "BTC").GetAwaiter().GetResult()).Model;
                Assert.True(derivationVM.Confirmation);


                //cobo vault file
                var content = "{\"ExtPubKey\":\"xpub6CEqRFZ7yZxCFXuEWZBAdnC8bdvu9SRHevaoU2SsW9ZmKhrCShmbpGZWwaR15hdLURf8hg47g4TpPGaqEU8hw5LEJCE35AUhne67XNyFGBk\",\"MasterFingerprint\":\"7a7563b5\",\"DerivationPath\":\"M\\/84'\\/0'\\/0'\",\"CoboVaultFirmwareVersion\":\"1.2.0(BTC-Only)\"}";
                derivationVM = (DerivationSchemeViewModel)Assert
                    .IsType<ViewResult>(await controller.AddDerivationScheme(user.StoreId, "BTC")).Model;
                derivationVM.WalletFile = TestUtils.GetFormFile("wallet3.json", content);
                derivationVM.Enabled = true;
                derivationVM = (DerivationSchemeViewModel)Assert.IsType<ViewResult>(controller
                    .AddDerivationScheme(user.StoreId, derivationVM, "BTC").GetAwaiter().GetResult()).Model;
                Assert.True(derivationVM.Confirmation);
                Assert.IsType<RedirectToActionResult>(controller.AddDerivationScheme(user.StoreId, derivationVM, "BTC")
                    .GetAwaiter().GetResult());

                //wasabi wallet file
                content =
                    "{\r\n  \"EncryptedSecret\": \"6PYWBQ1zsukowsnTNA57UUx791aBuJusm7E4egXUmF5WGw3tcdG3cmTL57\",\r\n  \"ChainCode\": \"waSIVbn8HaoovoQg/0t8IS1+ZCxGsJRGFT21i06nWnc=\",\r\n  \"MasterFingerprint\": \"7a7563b5\",\r\n  \"ExtPubKey\": \"xpub6CEqRFZ7yZxCFXuEWZBAdnC8bdvu9SRHevaoU2SsW9ZmKhrCShmbpGZWwaR15hdLURf8hg47g4TpPGaqEU8hw5LEJCE35AUhne67XNyFGBk\",\r\n  \"PasswordVerified\": false,\r\n  \"MinGapLimit\": 21,\r\n  \"AccountKeyPath\": \"84'/0'/0'\",\r\n  \"BlockchainState\": {\r\n    \"Network\": \"RegTest\",\r\n    \"Height\": \"0\"\r\n  },\r\n  \"HdPubKeys\": []\r\n}";

                derivationVM = (DerivationSchemeViewModel)Assert
                    .IsType<ViewResult>(await controller.AddDerivationScheme(user.StoreId, "BTC")).Model;
                derivationVM.WalletFile = TestUtils.GetFormFile("wallet4.json", content);
                derivationVM.Enabled = true;
                derivationVM = (DerivationSchemeViewModel)Assert.IsType<ViewResult>(controller
                    .AddDerivationScheme(user.StoreId, derivationVM, "BTC").GetAwaiter().GetResult()).Model;
                Assert.True(derivationVM.Confirmation);
                Assert.IsType<RedirectToActionResult>(controller.AddDerivationScheme(user.StoreId, derivationVM, "BTC")
                    .GetAwaiter().GetResult());


                // Can we upload coldcard settings? (Should fail, we are giving a mainnet file to a testnet network)
                derivationVM = (DerivationSchemeViewModel)Assert
                    .IsType<ViewResult>(await controller.AddDerivationScheme(user.StoreId, "BTC")).Model;
                content =
                    "{\"keystore\": {\"ckcc_xpub\": \"xpub661MyMwAqRbcGVBsTGeNZN6QGVHmMHLdSA4FteGsRrEriu4pnVZMZWnruFFFXkMnyoBjyHndD3Qwcfz4MPzBUxjSevweNFQx7SAYZATtcDw\", \"xpub\": \"ypub6WWc2gWwHbdnAAyJDnR4SPL1phRh7REqrPBfZeizaQ1EmTshieRXJC3Z5YoU4wkcdKHEjQGkh6AYEzCQC1Kz3DNaWSwdc1pc8416hAjzqyD\", \"label\": \"Coldcard Import 0x60d1af8b\", \"ckcc_xfp\": 1624354699, \"type\": \"hardware\", \"hw_type\": \"coldcard\", \"derivation\": \"m/49'/0'/0'\"}, \"wallet_type\": \"standard\", \"use_encryption\": false, \"seed_version\": 17}";
                derivationVM.WalletFile = TestUtils.GetFormFile("wallet.json", content);
                derivationVM = (DerivationSchemeViewModel)Assert.IsType<ViewResult>(controller
                    .AddDerivationScheme(user.StoreId, derivationVM, "BTC").GetAwaiter().GetResult()).Model;
                Assert.False(derivationVM
                    .Confirmation); // Should fail, we are giving a mainnet file to a testnet network

                // And with a good file? (upub)
                content =
                    "{\"keystore\": {\"ckcc_xpub\": \"tpubD6NzVbkrYhZ4YHNiuTdTmHRmbcPRLfqgyneZFCL1mkzkUBjXriQShxTh9HL34FK2mhieasJVk9EzJrUfkFqRNQBjiXgx3n5BhPkxKBoFmaS\", \"xpub\": \"upub5DBYp1qGgsTrkzCptMGZc2x18pquLwGrBw6nS59T4NViZ4cni1mGowQzziy85K8vzkp1jVtWrSkLhqk9KDfvrGeB369wGNYf39kX8rQfiLn\", \"label\": \"Coldcard Import 0x60d1af8b\", \"ckcc_xfp\": 1624354699, \"type\": \"hardware\", \"hw_type\": \"coldcard\", \"derivation\": \"m/49'/0'/0'\"}, \"wallet_type\": \"standard\", \"use_encryption\": false, \"seed_version\": 17}";
                derivationVM = (DerivationSchemeViewModel)Assert
                    .IsType<ViewResult>(await controller.AddDerivationScheme(user.StoreId, "BTC")).Model;
                derivationVM.WalletFile = TestUtils.GetFormFile("wallet2.json", content);
                derivationVM.Enabled = true;
                derivationVM = (DerivationSchemeViewModel)Assert.IsType<ViewResult>(controller
                    .AddDerivationScheme(user.StoreId, derivationVM, "BTC").GetAwaiter().GetResult()).Model;
                Assert.True(derivationVM.Confirmation);
                Assert.IsType<RedirectToActionResult>(controller.AddDerivationScheme(user.StoreId, derivationVM, "BTC")
                    .GetAwaiter().GetResult());


                // Now let's check that no data has been lost in the process
                var store = tester.PayTester.StoreRepository.FindStore(user.StoreId).GetAwaiter().GetResult();
                var onchainBTC = store.GetSupportedPaymentMethods(tester.PayTester.Networks)
#pragma warning disable CS0618 // Type or member is obsolete
                    .OfType<DerivationSchemeSettings>().First(o => o.PaymentId.IsBTCOnChain);
#pragma warning restore CS0618 // Type or member is obsolete
                DerivationSchemeSettings.TryParseFromWalletFile(content, onchainBTC.Network, out var expected);
                Assert.Equal(expected.ToJson(), onchainBTC.ToJson());

                // Let's check that the root hdkey and account key path are taken into account when making a PSBT
                invoice = user.BitPay.CreateInvoice(
                    new Invoice()
                    {
                        Price = 1.5m,
                        Currency = "USD",
                        PosData = "posData",
                        OrderId = "orderId",
                        ItemDesc = "Some description",
                        FullNotifications = true
                    }, Facade.Merchant);

                tester.ExplorerNode.Generate(1);
                var invoiceAddress = BitcoinAddress.Create(invoice.CryptoInfo.First(c => c.CryptoCode == "BTC").Address,
                    tester.ExplorerNode.Network);
                tester.ExplorerNode.SendToAddress(invoiceAddress, Money.Coins(1m));
                TestUtils.Eventually(() =>
                {
                    invoice = user.BitPay.GetInvoice(invoice.Id);
                    Assert.Equal("paid", invoice.Status);
                });
                var wallet = tester.PayTester.GetController<WalletsController>();
                var psbt = wallet.CreatePSBT(btcNetwork, onchainBTC,
                    new WalletSendModel()
                    {
                        Outputs = new List<WalletSendModel.TransactionOutput>()
                        {
                            new WalletSendModel.TransactionOutput()
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
            using (var tester = ServerTester.Create())
            {
                tester.ActivateLightning();
                tester.ActivateLTC();
                await tester.StartAsync();
                await tester.EnsureChannelsSetup();
                var user = tester.NewAccount();
                user.GrantAccess(true);
                user.RegisterLightningNode("BTC", LightningConnectionType.Charge);
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
            using (var tester = ServerTester.Create())
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
                cashCow.SendToAddress(invoiceAddress, firstPayment);
                TestUtils.Eventually(() =>
                {
                    invoice = user.BitPay.GetInvoice(invoice.Id);
                    Assert.Equal(firstPayment, invoice.CryptoInfo[0].Paid);
                });

                Assert.Single(invoice.CryptoInfo); // Only BTC should be presented

                var controller = tester.PayTester.GetController<InvoiceController>(null);
                var checkout =
                    (Models.InvoicingModels.PaymentModel)((JsonResult)controller.GetStatus(invoice.Id, null)
                        .GetAwaiter().GetResult()).Value;
                Assert.Single(checkout.AvailableCryptos);
                Assert.Equal("LTC", checkout.CryptoCode);

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
                    checkout = (Models.InvoicingModels.PaymentModel)((JsonResult)controller.GetStatus(invoice.Id, null)
                        .GetAwaiter().GetResult()).Value;
                    Assert.Equal("paid", checkout.Status);
                });
            }
        }


        [Fact]
        [Trait("Selenium", "Selenium")]
        [Trait("Altcoins", "Altcoins")]
        public async Task CanCreateRefunds()
        {
            using (var s = SeleniumTester.Create())
            {
                s.Server.ActivateLTC();
                await s.StartAsync();
                var user = s.Server.NewAccount();
                await user.GrantAccessAsync();
                s.GoToLogin();
                s.Login(user.RegisterDetails.Email, user.RegisterDetails.Password);
                user.RegisterDerivationScheme("BTC");
                await s.Server.ExplorerNode.GenerateAsync(1);

                foreach (var multiCurrency in new[] { false, true })
                {
                    if (multiCurrency)
                        user.RegisterDerivationScheme("LTC");
                    foreach (var rateSelection in new[] { "FiatText", "CurrentRateText", "RateThenText" })
                        await CanCreateRefundsCore(s, user, multiCurrency, rateSelection);
                }
            }
        }

        private static async Task CanCreateRefundsCore(SeleniumTester s, TestAccount user, bool multiCurrency, string rateSelection)
        {
            s.GoToHome();
            s.Server.PayTester.ChangeRate("BTC_USD", new Rating.BidAsk(5000.0m, 5100.0m));
            var invoice = await user.BitPay.CreateInvoiceAsync(new NBitpayClient.Invoice()
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
                Assert.Equal("confirmed", invoice.Status);
            });

            // BTC crash by 50%
            s.Server.PayTester.ChangeRate("BTC_USD", new Rating.BidAsk(5000.0m / 2.0m, 5100.0m / 2.0m));
            s.GoToInvoice(invoice.Id);
            s.Driver.FindElement(By.Id("refundlink")).Click();
            if (multiCurrency)
            {
                s.Driver.FindElement(By.Id("SelectedPaymentMethod")).SendKeys("BTC" + Keys.Enter);
                s.Driver.FindElement(By.Id("ok")).Click();
            }
            Assert.Contains("$5,500.00", s.Driver.PageSource); // Should propose reimburse in fiat
            Assert.Contains("1.10000000 ₿", s.Driver.PageSource); // Should propose reimburse in BTC at the rate of before
            Assert.Contains("2.20000000 ₿", s.Driver.PageSource); // Should propose reimburse in BTC at the current rate
            s.Driver.FindElement(By.Id(rateSelection)).Click();
            s.Driver.FindElement(By.Id("ok")).Click();
            Assert.Contains("pull-payments", s.Driver.Url);
            if (rateSelection == "FiatText")
                Assert.Contains("$5,500.00", s.Driver.PageSource);
            if (rateSelection == "CurrentRateText")
                Assert.Contains("2.20000000 ₿", s.Driver.PageSource);
            if (rateSelection == "RateThenText")
                Assert.Contains("1.10000000 ₿", s.Driver.PageSource);
            s.GoToHome();
            s.GoToInvoices();
            s.GoToInvoice(invoice.Id);
            s.Driver.FindElement(By.Id("refundlink")).Click();
            Assert.Contains("pull-payments", s.Driver.Url);
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Altcoins", "Altcoins")]
        [Trait("Lightning", "Lightning")]
        public async Task CanUsePaymentMethodDropdown()
        {
            using (var s = SeleniumTester.Create())
            {
                s.Server.ActivateLTC();
                s.Server.ActivateLightning();
                await s.StartAsync();
                s.GoToRegister();
                s.RegisterNewUser();
                var store = s.CreateNewStore();
                s.AddDerivationScheme("BTC");

                //check that there is no dropdown since only one payment method is set
                var invoiceId = s.CreateInvoice(store.storeName, 10, "USD", "a@g.com");
                s.GoToInvoiceCheckout(invoiceId);
                s.Driver.FindElement(By.ClassName("payment__currencies_noborder"));
                s.GoToHome();
                s.GoToStore(store.storeId);
                s.AddDerivationScheme("LTC");
                s.AddLightningNode("BTC", LightningConnectionType.CLightning);
                //there should be three now
                invoiceId = s.CreateInvoice(store.storeName, 10, "USD", "a@g.com");
                s.GoToInvoiceCheckout(invoiceId);
                var currencyDropdownButton = s.Driver.FindElement(By.ClassName("payment__currencies"));
                Assert.Contains("BTC", currencyDropdownButton.Text);
                currencyDropdownButton.Click();

                var elements = s.Driver.FindElement(By.ClassName("vex-content")).FindElements(By.ClassName("vexmenuitem"));
                Assert.Equal(3, elements.Count);
                elements.Single(element => element.Text.Contains("LTC")).Click();
                currencyDropdownButton = s.Driver.FindElement(By.ClassName("payment__currencies"));
                Assert.Contains("LTC", currencyDropdownButton.Text);
                currencyDropdownButton.Click();

                elements = s.Driver.FindElement(By.ClassName("vex-content")).FindElements(By.ClassName("vexmenuitem"));
                elements.Single(element => element.Text.Contains("Lightning")).Click();

                currencyDropdownButton = s.Driver.FindElement(By.ClassName("payment__currencies"));
                Assert.Contains("Lightning", currencyDropdownButton.Text);

                s.Driver.Quit();
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        [Trait("Altcoins", "Altcoins")]
        public async Task CanPayWithTwoCurrencies()
        {
            using (var tester = ServerTester.Create())
            {
                tester.ActivateLTC();
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");
                // First we try payment with a merchant having only BTC
                var invoice = user.BitPay.CreateInvoice(
                    new Invoice()
                    {
                        Price = 5000.0m,
                        Currency = "USD",
                        PosData = "posData",
                        OrderId = "orderId",
                        ItemDesc = "Some description",
                        FullNotifications = true
                    }, Facade.Merchant);

                var cashCow = tester.ExplorerNode;
                cashCow.Generate(2); // get some money in case
                var invoiceAddress = BitcoinAddress.Create(invoice.BitcoinAddress, cashCow.Network);
                var firstPayment = Money.Coins(0.04m);
                cashCow.SendToAddress(invoiceAddress, firstPayment);
                TestUtils.Eventually(() =>
                {
                    invoice = user.BitPay.GetInvoice(invoice.Id);
                    Assert.True(invoice.BtcPaid == firstPayment);
                });

                Assert.Single(invoice.CryptoInfo); // Only BTC should be presented

                var controller = tester.PayTester.GetController<InvoiceController>(null);
                var checkout =
                    (Models.InvoicingModels.PaymentModel)((JsonResult)controller.GetStatus(invoice.Id, null)
                        .GetAwaiter().GetResult()).Value;
                Assert.Single(checkout.AvailableCryptos);
                Assert.Equal("BTC", checkout.CryptoCode);

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
                invoice = user.BitPay.CreateInvoice(
                    new Invoice()
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
                cashCow.SendToAddress(invoiceAddress, firstPayment);
                Logs.Tester.LogInformation("First payment sent to " + invoiceAddress);
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
                cashCow.Generate(4); // LTC is not worth a lot, so just to make sure we have money...
                cashCow.SendToAddress(invoiceAddress, secondPayment);
                Logs.Tester.LogInformation("Second payment sent to " + invoiceAddress);
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

                controller = tester.PayTester.GetController<InvoiceController>(null);
                checkout = (Models.InvoicingModels.PaymentModel)((JsonResult)controller.GetStatus(invoice.Id, "LTC")
                    .GetAwaiter().GetResult()).Value;
                Assert.Equal(2, checkout.AvailableCryptos.Count);
                Assert.Equal("LTC", checkout.CryptoCode);


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
                invoice = user.BitPay.CreateInvoice(
                    new Invoice()
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
            using (var tester = ServerTester.Create())
            {
                tester.ActivateLTC();
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");
                user.RegisterDerivationScheme("LTC");
                var apps = user.GetController<AppsController>();
                var vm = Assert.IsType<CreateAppViewModel>(Assert.IsType<ViewResult>(apps.CreateApp().Result).Model);
                vm.Name = "test";
                vm.SelectedAppType = AppType.PointOfSale.ToString();
                Assert.IsType<RedirectToActionResult>(apps.CreateApp(vm).Result);
                var appId = Assert.IsType<ListAppsViewModel>(Assert.IsType<ViewResult>(apps.ListApps().Result).Model)
                    .Apps[0].Id;
                var vmpos = Assert.IsType<UpdatePointOfSaleViewModel>(Assert
                    .IsType<ViewResult>(apps.UpdatePointOfSale(appId).Result).Model);
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
                Assert.IsType<RedirectToActionResult>(apps.UpdatePointOfSale(appId, vmpos).Result);
                vmpos = Assert.IsType<UpdatePointOfSaleViewModel>(Assert
                    .IsType<ViewResult>(apps.UpdatePointOfSale(appId).Result).Model);
                Assert.Equal("hello", vmpos.Title);

                var publicApps = user.GetController<AppsPublicController>();
                var vmview =
                    Assert.IsType<ViewPointOfSaleViewModel>(Assert
                        .IsType<ViewResult>(publicApps.ViewPointOfSale(appId, PosViewType.Cart).Result).Model);
                Assert.Equal("hello", vmview.Title);
                Assert.Equal(3, vmview.Items.Length);
                Assert.Equal("good apple", vmview.Items[0].Title);
                Assert.Equal("orange", vmview.Items[1].Title);
                Assert.Equal(10.0m, vmview.Items[1].Price.Value);
                Assert.Equal("$5.00", vmview.Items[0].Price.Formatted);
                Assert.Equal("{0} Purchase", vmview.ButtonText);
                Assert.Equal("Nicolas Sexy Hair", vmview.CustomButtonText);
                Assert.Equal("Wanna tip?", vmview.CustomTipText);
                Assert.Equal("15,18,20", string.Join(',', vmview.CustomTipPercentages));
                Assert.IsType<RedirectToActionResult>(publicApps
                    .ViewPointOfSale(appId, PosViewType.Cart, 0, null, null, null, null, "orange").Result);

                //
                var invoices = user.BitPay.GetInvoices();
                var orangeInvoice = invoices.First();
                Assert.Equal(10.00m, orangeInvoice.Price);
                Assert.Equal("CAD", orangeInvoice.Currency);
                Assert.Equal("orange", orangeInvoice.ItemDesc);


                Assert.IsType<RedirectToActionResult>(publicApps
                    .ViewPointOfSale(appId, PosViewType.Cart, 0, null, null, null, null, "apple").Result);

                invoices = user.BitPay.GetInvoices();
                var appleInvoice = invoices.SingleOrDefault(invoice => invoice.ItemCode.Equals("apple"));
                Assert.NotNull(appleInvoice);
                Assert.Equal("good apple", appleInvoice.ItemDesc);


                // testing custom amount
                var action = Assert.IsType<RedirectToActionResult>(publicApps
                    .ViewPointOfSale(appId, PosViewType.Cart, 6.6m, null, null, null, null, "donation").Result);
                Assert.Equal(nameof(InvoiceController.Checkout), action.ActionName);
                invoices = user.BitPay.GetInvoices();
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
                    Logs.Tester.LogInformation($"Testing for {test.Code}");
                    vmpos = Assert.IsType<UpdatePointOfSaleViewModel>(Assert
                        .IsType<ViewResult>(apps.UpdatePointOfSale(appId).Result).Model);
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
                    Assert.IsType<RedirectToActionResult>(apps.UpdatePointOfSale(appId, vmpos).Result);
                    publicApps = user.GetController<AppsPublicController>();
                    vmview = Assert.IsType<ViewPointOfSaleViewModel>(Assert
                        .IsType<ViewResult>(publicApps.ViewPointOfSale(appId, PosViewType.Cart).Result).Model);
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
                vmpos = Assert.IsType<UpdatePointOfSaleViewModel>(Assert
                    .IsType<ViewResult>(apps.UpdatePointOfSale(appId).Result).Model);
                vmpos.Title = "hello";
                vmpos.Currency = "BTC";
                vmpos.Template = @"
inventoryitem:
  price: 1.0
  title: good apple
  inventory: 1
noninventoryitem:
  price: 10.0";
                Assert.IsType<RedirectToActionResult>(apps.UpdatePointOfSale(appId, vmpos).Result);

                //inventoryitem has 1 item available
                await tester.WaitForEvent<AppInventoryUpdaterHostedService.UpdateAppInventory>(() =>
                {
                    Assert.IsType<RedirectToActionResult>(publicApps
                        .ViewPointOfSale(appId, PosViewType.Cart, 1, null, null, null, null, "inventoryitem").Result);
                    return Task.CompletedTask;
                });
                
                //we already bought all available stock so this should fail
                await Task.Delay(100);
                Assert.IsType<RedirectToActionResult>(publicApps
                    .ViewPointOfSale(appId, PosViewType.Cart, 1, null, null, null, null, "inventoryitem").Result);

                //inventoryitem has unlimited items available
                Assert.IsType<RedirectToActionResult>(publicApps
                    .ViewPointOfSale(appId, PosViewType.Cart, 1, null, null, null, null, "noninventoryitem").Result);
                Assert.IsType<RedirectToActionResult>(publicApps
                    .ViewPointOfSale(appId, PosViewType.Cart, 1, null, null, null, null, "noninventoryitem").Result);

                //verify invoices where created
                invoices = user.BitPay.GetInvoices();
                Assert.Equal(2, invoices.Count(invoice => invoice.ItemCode.Equals("noninventoryitem")));
                var inventoryItemInvoice =
                    Assert.Single(invoices.Where(invoice => invoice.ItemCode.Equals("inventoryitem")));
                Assert.NotNull(inventoryItemInvoice);

                //let's mark the inventoryitem invoice as invalid, thsi should return the item to back in stock
                var controller = tester.PayTester.GetController<InvoiceController>(user.UserId, user.StoreId);
                var appService = tester.PayTester.GetService<AppService>();
                var eventAggregator = tester.PayTester.GetService<EventAggregator>();
                Assert.IsType<JsonResult>(await controller.ChangeInvoiceState(inventoryItemInvoice.Id, "invalid"));
                //check that item is back in stock
                TestUtils.Eventually(() =>
                {
                    vmpos = Assert.IsType<UpdatePointOfSaleViewModel>(Assert
                        .IsType<ViewResult>(apps.UpdatePointOfSale(appId).Result).Model);
                    Assert.Equal(1,
                        appService.Parse(vmpos.Template, "BTC").Single(item => item.Id == "inventoryitem").Inventory);
                }, 10000);


                //test payment methods option

                vmpos = Assert.IsType<UpdatePointOfSaleViewModel>(Assert
                    .IsType<ViewResult>(apps.UpdatePointOfSale(appId).Result).Model);
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
                Assert.IsType<RedirectToActionResult>(apps.UpdatePointOfSale(appId, vmpos).Result);
                Assert.IsType<RedirectToActionResult>(publicApps
                    .ViewPointOfSale(appId, PosViewType.Cart, 1, null, null, null, null, "btconly").Result);
                Assert.IsType<RedirectToActionResult>(publicApps
                    .ViewPointOfSale(appId, PosViewType.Cart, 1, null, null, null, null, "normal").Result);
                invoices = user.BitPay.GetInvoices();
                var normalInvoice = invoices.Single(invoice => invoice.ItemCode == "normal");
                var btcOnlyInvoice = invoices.Single(invoice => invoice.ItemCode == "btconly");
                Assert.Single(btcOnlyInvoice.CryptoInfo);
                Assert.Equal("BTC",
                    btcOnlyInvoice.CryptoInfo.First().CryptoCode);
                Assert.Equal(PaymentTypes.BTCLike.ToString(),
                    btcOnlyInvoice.CryptoInfo.First().PaymentType);

                Assert.Equal(2, normalInvoice.CryptoInfo.Length);
                Assert.Contains(
                    normalInvoice.CryptoInfo,
                    s => PaymentTypes.BTCLike.ToString() == s.PaymentType && new[] { "BTC", "LTC" }.Contains(
                             s.CryptoCode));
            }
        }

        [Fact]
        [Trait("Fast", "Fast")]
        [Trait("Altcoins", "Altcoins")]
        public void CanCalculateCryptoDue2()
        {
#pragma warning disable CS0618
            var dummy = new Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, Network.RegTest).ToString();
            var networkProvider = new BTCPayNetworkProvider(ChainName.Regtest);
            var paymentMethodHandlerDictionary = new PaymentMethodHandlerDictionary(new IPaymentMethodHandler[]
            {
                new BitcoinLikePaymentHandler(null, networkProvider, null, null, null),
                new LightningLikePaymentHandler(null, null, networkProvider, null, null, null),
            });
            var networkBTC = networkProvider.GetNetwork("BTC");
            var networkLTC = networkProvider.GetNetwork("LTC");
            InvoiceEntity invoiceEntity = new InvoiceEntity();
            invoiceEntity.Networks = networkProvider;
            invoiceEntity.Payments = new System.Collections.Generic.List<PaymentEntity>();
            invoiceEntity.Price = 100;
            PaymentMethodDictionary paymentMethods = new PaymentMethodDictionary();
            paymentMethods.Add(new PaymentMethod() { Network = networkBTC, CryptoCode = "BTC", Rate = 10513.44m, }
                .SetPaymentMethodDetails(
                    new BTCPayServer.Payments.Bitcoin.BitcoinLikeOnChainPaymentMethod()
                    {
                        NextNetworkFee = Money.Coins(0.00000100m),
                        DepositAddress = dummy
                    }));
            paymentMethods.Add(new PaymentMethod() { Network = networkLTC, CryptoCode = "LTC", Rate = 216.79m }
                .SetPaymentMethodDetails(
                    new BTCPayServer.Payments.Bitcoin.BitcoinLikeOnChainPaymentMethod()
                    {
                        NextNetworkFee = Money.Coins(0.00010000m),
                        DepositAddress = dummy
                    }));
            invoiceEntity.SetPaymentMethods(paymentMethods);

            var btc = invoiceEntity.GetPaymentMethod(new PaymentMethodId("BTC", PaymentTypes.BTCLike));
            var accounting = btc.Calculate();

            invoiceEntity.Payments.Add(
                new PaymentEntity()
                {
                    Accounted = true,
                    CryptoCode = "BTC",
                    NetworkFee = 0.00000100m,
                    Network = networkProvider.GetNetwork("BTC"),
                }
                    .SetCryptoPaymentData(new BitcoinLikePaymentData()
                    {
                        Network = networkProvider.GetNetwork("BTC"),
                        Output = new TxOut() { Value = Money.Coins(0.00151263m) }
                    }));
            accounting = btc.Calculate();
            invoiceEntity.Payments.Add(
                new PaymentEntity()
                {
                    Accounted = true,
                    CryptoCode = "BTC",
                    NetworkFee = 0.00000100m,
                    Network = networkProvider.GetNetwork("BTC")
                }
                    .SetCryptoPaymentData(new BitcoinLikePaymentData()
                    {
                        Network = networkProvider.GetNetwork("BTC"),
                        Output = new TxOut() { Value = accounting.Due }
                    }));
            accounting = btc.Calculate();
            Assert.Equal(Money.Zero, accounting.Due);
            Assert.Equal(Money.Zero, accounting.DueUncapped);

            var ltc = invoiceEntity.GetPaymentMethod(new PaymentMethodId("LTC", PaymentTypes.BTCLike));
            accounting = ltc.Calculate();

            Assert.Equal(Money.Zero, accounting.Due);
            // LTC might have over paid due to BTC paying above what it should (round 1 satoshi up)
            Assert.True(accounting.DueUncapped < Money.Zero);

            var paymentMethod = InvoiceWatcher.GetNearestClearedPayment(paymentMethods, out var accounting2);
            Assert.Equal(btc.CryptoCode, paymentMethod.CryptoCode);
#pragma warning restore CS0618
        }

        [Fact]
        [Trait("Fast", "Fast")]
        [Trait("Altcoins", "Altcoins")]
        public void CanParseDerivationScheme()
        {
            var testnetNetworkProvider = new BTCPayNetworkProvider(ChainName.Testnet);
            var regtestNetworkProvider = new BTCPayNetworkProvider(ChainName.Regtest);
            var mainnetNetworkProvider = new BTCPayNetworkProvider(ChainName.Mainnet);
            var testnetParser = new DerivationSchemeParser(testnetNetworkProvider.GetNetwork<BTCPayNetwork>("BTC"));
            var mainnetParser = new DerivationSchemeParser(mainnetNetworkProvider.GetNetwork<BTCPayNetwork>("BTC"));
            NBXplorer.DerivationStrategy.DerivationStrategyBase result;
            //  Passing electrum stuff
            // Passing a native segwit from mainnet to a testnet parser, means the testnet parser will try to convert it into segwit
            result = testnetParser.Parse(
                "zpub6nL6PUGurpU3DfPDSZaRS6WshpbNc9ctCFFzrCn54cssnheM31SZJZUcFHKtjJJNhAueMbh6ptFMfy1aeiMQJr3RJ4DDt1hAPx7sMTKV48t");
            Assert.Equal(
                "tpubD93CJNkmGjLXnsBqE2zGDqfEh1Q8iJ8wueordy3SeWt1RngbbuxXCsqASuVWFywmfoCwUE1rSfNJbaH4cBNcbp8WcyZgPiiRSTazLGL8U9w",
                result.ToString());
            result = mainnetParser.Parse(
                "zpub6nL6PUGurpU3DfPDSZaRS6WshpbNc9ctCFFzrCn54cssnheM31SZJZUcFHKtjJJNhAueMbh6ptFMfy1aeiMQJr3RJ4DDt1hAPx7sMTKV48t");
            Assert.Equal(
                "xpub68fZn8w5ZTP5X4zymr1B1vKsMtJUiudtN2DZHQzJJc87gW1tXh7S4SALCsQijUzXstg2reVyuZYFuPnTDKXNiNgDZNpNiC4BrVzaaGEaRHj",
                result.ToString());
            // P2SH
            result = testnetParser.Parse(
                "upub57Wa4MvRPNyAipy1MCpERxcFpHR2ZatyikppkyeWkoRL6QJvLVMo39jYdcaJVxyvBURyRVmErBEA5oGicKBgk1j72GAXSPFH5tUDoGZ8nEu");
            Assert.Equal(
                "tpubD6NzVbkrYhZ4YWjDJUACG9E8fJx2NqNY1iynTiPKEjJrzzRKAgha3nNnwGXr2BtvCJKJHW4nmG7rRqc2AGGy2AECgt16seMyV2FZivUmaJg-[p2sh]",
                result.ToString());

            result = mainnetParser.Parse(
                "ypub6QqdH2c5z79681jUgdxjGJzGW9zpL4ryPCuhtZE4GpvrJoZqM823XQN6iSQeVbbbp2uCRQ9UgpeMcwiyV6qjvxTWVcxDn2XEAnioMUwsrQ5");
            Assert.Equal(
                "xpub661MyMwAqRbcGiYMrHB74DtmLBrNPSsUU6PV7ALAtpYyFhkc6TrUuLhxhET4VgwgQPnPfvYvEAHojf7QmQRj8imudHFoC7hju4f9xxri8wR-[p2sh]",
                result.ToString());

            // if prefix not recognize, assume it is segwit
            result = testnetParser.Parse(
                "xpub661MyMwAqRbcGeVGU5e5KBcau1HHEUGf9Wr7k4FyLa8yRPNQrrVa7Ndrgg8Afbe2UYXMSL6tJBFd2JewwWASsePPLjkcJFL1tTVEs3UQ23X");
            Assert.Equal(
                "tpubD6NzVbkrYhZ4YSg7vGdAX6wxE8NwDrmih9SR6cK7gUtsAg37w5LfFpJgviCxC6bGGT4G3uckqH5fiV9ZLN1gm5qgQLVuymzFUR5ed7U7ksu",
                result.ToString());
            ////////////////

            var tpub =
                "tpubD6NzVbkrYhZ4Wc65tjhmcKdWFauAo7bGLRTxvggygkNyp6SMGutJp7iociwsinU33jyNBp1J9j2hJH5yQsayfiS3LEU2ZqXodAcnaygra8o";

            result = testnetParser.Parse(tpub);
            Assert.Equal(tpub, result.ToString());
            testnetParser.HintScriptPubKey = BitcoinAddress
                .Create("tb1q4s33amqm8l7a07zdxcunqnn3gcsjcfz3xc573l", testnetParser.Network).ScriptPubKey;
            result = testnetParser.Parse(tpub);
            Assert.Equal(tpub, result.ToString());

            testnetParser.HintScriptPubKey = BitcoinAddress
                .Create("2N2humNio3YTApSfY6VztQ9hQwDnhDvaqFQ", testnetParser.Network).ScriptPubKey;
            result = testnetParser.Parse(tpub);
            Assert.Equal($"{tpub}-[p2sh]", result.ToString());

            testnetParser.HintScriptPubKey = BitcoinAddress
                .Create("mwD8bHS65cdgUf6rZUUSoVhi3wNQFu1Nfi", testnetParser.Network).ScriptPubKey;
            result = testnetParser.Parse(tpub);
            Assert.Equal($"{tpub}-[legacy]", result.ToString());

            testnetParser.HintScriptPubKey = BitcoinAddress
                .Create("2N2humNio3YTApSfY6VztQ9hQwDnhDvaqFQ", testnetParser.Network).ScriptPubKey;
            result = testnetParser.Parse($"{tpub}-[legacy]");
            Assert.Equal($"{tpub}-[p2sh]", result.ToString());

            result = testnetParser.Parse(tpub);
            Assert.Equal($"{tpub}-[p2sh]", result.ToString());

            var regtestParser = new DerivationSchemeParser(regtestNetworkProvider.GetNetwork<BTCPayNetwork>("BTC"));
            var parsed =
                regtestParser.Parse(
                    "xpub6DG1rMYXiQtCc6CfdLFD9CtxqhzzRh7j6Sq6EdE9abgYy3cfDRrniLLv2AdwqHL1exiLnnKR5XXcaoiiexf3Y9R6J6rxkJtqJHzNzMW9QMZ-[p2sh]");
            Assert.Equal(
                "tpubDDdeNbNDRgqestPX5XEJM8ELAq6eR5cne5RPbBHHvWSSiLHNHehsrn1kGCijMnHFSsFFQMqHcdMfGzDL3pWHRasPMhcGRqZ4tFankQ3i4ok-[p2sh]",
                parsed.ToString());

            // Let's make sure we can't generate segwit with dogecoin
            regtestParser = new DerivationSchemeParser(regtestNetworkProvider.GetNetwork<BTCPayNetwork>("DOGE"));
            parsed = regtestParser.Parse(
                "xpub6DG1rMYXiQtCc6CfdLFD9CtxqhzzRh7j6Sq6EdE9abgYy3cfDRrniLLv2AdwqHL1exiLnnKR5XXcaoiiexf3Y9R6J6rxkJtqJHzNzMW9QMZ-[p2sh]");
            Assert.Equal(
                "tpubDDdeNbNDRgqestPX5XEJM8ELAq6eR5cne5RPbBHHvWSSiLHNHehsrn1kGCijMnHFSsFFQMqHcdMfGzDL3pWHRasPMhcGRqZ4tFankQ3i4ok-[legacy]",
                parsed.ToString());

            regtestParser = new DerivationSchemeParser(regtestNetworkProvider.GetNetwork<BTCPayNetwork>("DOGE"));
            parsed = regtestParser.Parse(
                "tpubDDdeNbNDRgqestPX5XEJM8ELAq6eR5cne5RPbBHHvWSSiLHNHehsrn1kGCijMnHFSsFFQMqHcdMfGzDL3pWHRasPMhcGRqZ4tFankQ3i4ok-[p2sh]");
            Assert.Equal(
                "tpubDDdeNbNDRgqestPX5XEJM8ELAq6eR5cne5RPbBHHvWSSiLHNHehsrn1kGCijMnHFSsFFQMqHcdMfGzDL3pWHRasPMhcGRqZ4tFankQ3i4ok-[legacy]",
                parsed.ToString());
            
            //let's test output descriptor parsing support

            
            //we don't support every descriptor, only the ones which represent an HD wallet with stndard derivation paths
            Assert.Throws<FormatException>(() => mainnetParser.ParseOutputDescriptor("pk(0279be667ef9dcbbac55a06295ce870b07029bfcdb2dce28d959f2815b16f81798)"));
            Assert.Throws<FormatException>(() => mainnetParser.ParseOutputDescriptor("pkh(02c6047f9441ed7d6d3045406e95c07cd85c778e4b8cef3ca7abac09b95c709ee5)"));
            Assert.Throws<FormatException>(() => mainnetParser.ParseOutputDescriptor("wpkh(02f9308a019258c31049344f85f89d5229b531c845836f99b08601f113bce036f9)"));
            Assert.Throws<FormatException>(() => mainnetParser.ParseOutputDescriptor("sh(wpkh(03fff97bd5755eeea420453a14355235d382f6472f8568a18b2f057a1460297556))"));
            Assert.Throws<FormatException>(() => mainnetParser.ParseOutputDescriptor("combo(0279be667ef9dcbbac55a06295ce870b07029bfcdb2dce28d959f2815b16f81798)"));
            Assert.Throws<FormatException>(() => mainnetParser.ParseOutputDescriptor("sh(wsh(pkh(02e493dbf1c10d80f3581e4904930b1404cc6c13900ee0758474fa94abe8c4cd13)))"));
            Assert.Throws<FormatException>(() => mainnetParser.ParseOutputDescriptor("multi(1,022f8bde4d1a07209355b4a7250a5c5128e88b84bddc619ab7cba8d569b240efe4,025cbdf0646e5db4eaa398f365f2ea7a0e3d419b7e0330e39ce92bddedcac4f9bc)"));
            Assert.Throws<FormatException>(() => mainnetParser.ParseOutputDescriptor("sh(multi(2,022f01e5e15cca351daff3843fb70f3c2f0a1bdd05e5af888a67784ef3e10a2a01,03acd484e2f0c7f65309ad178a9f559abde09796974c57e714c35f110dfc27ccbe))"));
            Assert.Throws<FormatException>(() => mainnetParser.ParseOutputDescriptor("sh(sortedmulti(2,03acd484e2f0c7f65309ad178a9f559abde09796974c57e714c35f110dfc27ccbe,022f01e5e15cca351daff3843fb70f3c2f0a1bdd05e5af888a67784ef3e10a2a01))"));

            //let's see what we actually support now
            
            //standard legacy hd wallet
            var parsedDescriptor = mainnetParser.ParseOutputDescriptor(
                "pkh([d34db33f/44'/0'/0']xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL/0/*)");
            Assert.Equal(KeyPath.Parse("44'/0'/0'"),Assert.Single(parsedDescriptor.Item2).KeyPath);
            Assert.Equal( HDFingerprint.Parse("d34db33f"),Assert.Single(parsedDescriptor.Item2).MasterFingerprint);
            Assert.Equal("xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL-[legacy]",parsedDescriptor.Item1.ToString() );
            
            //masterfingerprint and key path are optional
            parsedDescriptor = mainnetParser.ParseOutputDescriptor(
                "pkh([d34db33f]xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL/0/*)");
            Assert.Equal("xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL-[legacy]",parsedDescriptor.Item1.ToString() );
            //a master fingerprint must always be present if youre providing rooted path
            Assert.Throws<ParsingException>(() => mainnetParser.ParseOutputDescriptor("pkh([44'/0'/0']xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL/1/*)"));
           
            
            parsedDescriptor = mainnetParser.ParseOutputDescriptor(
                "pkh(xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL/0/*)");
            Assert.Equal("xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL-[legacy]",parsedDescriptor.Item1.ToString() );

            //but a different deriv path from standard (0/*) is not supported
            Assert.Throws<FormatException>(() => mainnetParser.ParseOutputDescriptor("pkh(xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL/1/*)"));

            //p2sh-segwit hd wallet
             parsedDescriptor = mainnetParser.ParseOutputDescriptor(
                "sh(wpkh([d34db33f/49'/0'/0']xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL/0/*))");
            Assert.Equal(KeyPath.Parse("49'/0'/0'"),Assert.Single(parsedDescriptor.Item2).KeyPath);
            Assert.Equal( HDFingerprint.Parse("d34db33f"),Assert.Single(parsedDescriptor.Item2).MasterFingerprint);
            Assert.Equal("xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL-[p2sh]",parsedDescriptor.Item1.ToString() );

            //segwit hd wallet
            parsedDescriptor = mainnetParser.ParseOutputDescriptor(
                "wpkh([d34db33f/84'/0'/0']xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL/0/*)");
            Assert.Equal(KeyPath.Parse("84'/0'/0'"),Assert.Single(parsedDescriptor.Item2).KeyPath);
            Assert.Equal( HDFingerprint.Parse("d34db33f"),Assert.Single(parsedDescriptor.Item2).MasterFingerprint);
            Assert.Equal("xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL",parsedDescriptor.Item1.ToString() );

            //multisig tests

            //legacy
            parsedDescriptor = mainnetParser.ParseOutputDescriptor(
                "sh(multi(1,[d34db33f/45'/0]xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL/0/*,[d34db33f/45'/0]xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL/0/*))");
            Assert.Equal(2, parsedDescriptor.Item2.Length);
            var strat =   Assert.IsType<MultisigDerivationStrategy>(Assert.IsType<P2SHDerivationStrategy>(parsedDescriptor.Item1).Inner);
            Assert.True(strat.IsLegacy);
            Assert.Equal(1,strat.RequiredSignatures);
            Assert.Equal(2,strat.Keys.Count());
            Assert.False(strat.LexicographicOrder);
            Assert.Equal("1-of-xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL-xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL-[legacy]-[keeporder]",parsedDescriptor.Item1.ToString() );

            //segwit
            parsedDescriptor = mainnetParser.ParseOutputDescriptor(
                "wsh(multi(1,[d34db33f/48'/0'/0'/2']xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL/0/*,[d34db33f/48'/0'/0'/2']xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL/0/*))");
            Assert.Equal(2, parsedDescriptor.Item2.Length);
            strat =   Assert.IsType<MultisigDerivationStrategy>(Assert.IsType<P2WSHDerivationStrategy>(parsedDescriptor.Item1).Inner);
            Assert.False(strat.IsLegacy);
            Assert.Equal(1,strat.RequiredSignatures);
            Assert.Equal(2,strat.Keys.Count());
            Assert.False(strat.LexicographicOrder);
            Assert.Equal("1-of-xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL-xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL-[keeporder]",parsedDescriptor.Item1.ToString() );


            //segwit-p2sh
            parsedDescriptor = mainnetParser.ParseOutputDescriptor(
                "sh(wsh(multi(1,[d34db33f/48'/0'/0'/2']xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL/0/*,[d34db33f/48'/0'/0'/2']xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL/0/*)))");
            Assert.Equal(2, parsedDescriptor.Item2.Length);
            strat =   Assert.IsType<MultisigDerivationStrategy>(Assert.IsType<P2WSHDerivationStrategy>(Assert.IsType<P2SHDerivationStrategy>(parsedDescriptor.Item1).Inner).Inner);
            Assert.False(strat.IsLegacy);
            Assert.Equal(1,strat.RequiredSignatures);
            Assert.Equal(2,strat.Keys.Count());
            Assert.False(strat.LexicographicOrder);
            Assert.Equal("1-of-xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL-xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL-[keeporder]-[p2sh]",parsedDescriptor.Item1.ToString() );

            //sorted
            parsedDescriptor = mainnetParser.ParseOutputDescriptor(
                "sh(sortedmulti(1,[d34db33f/48'/0'/0'/1']xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL/0/*,[d34db33f/48'/0'/0'/1']xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL/0/*))");
            Assert.Equal("1-of-xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL-xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL-[legacy]",parsedDescriptor.Item1.ToString() );
        }
    }
}
