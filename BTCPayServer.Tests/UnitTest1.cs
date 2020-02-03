using BTCPayServer.Tests.Logging;
using System.Linq;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBitcoin.Payment;
using NBitpayClient;
using System;
using System.Threading;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;
using BTCPayServer.Services.Invoices;
using Newtonsoft.Json;
using System.IO;
using Newtonsoft.Json.Linq;
using BTCPayServer.Controllers;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;
using BTCPayServer.Services.Rates;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Generic;
using BTCPayServer.Models.StoreViewModels;
using System.Threading.Tasks;
using System.Globalization;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.HostedServices;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Stores;
using System.Net.Http;
using System.Text;
using BTCPayServer.Models;
using BTCPayServer.Rating;
using BTCPayServer.Validation;
using ExchangeSharp;
using System.Security.Cryptography.X509Certificates;
using BTCPayServer.Lightning;
using BTCPayServer.Models.WalletViewModels;
using System.Security.Claims;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Models.ServerViewModels;
using BTCPayServer.Security;
using NBXplorer.Models;
using RatesViewModel = BTCPayServer.Models.StoreViewModels.RatesViewModel;
using NBitpayClient.Extensions;
using BTCPayServer.Services;
using System.Text.RegularExpressions;
using BTCPayServer.Events;
using BTCPayServer.Configuration;
using System.Security;
using System.Runtime.CompilerServices;
using System.Net;
using BTCPayServer.Models.AccountViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NBXplorer.DerivationStrategy;
using BTCPayServer.U2F.Models;
using BTCPayServer.Security.Bitpay;
using MemoryCache = Microsoft.Extensions.Caching.Memory.MemoryCache;

namespace BTCPayServer.Tests
{
    public class UnitTest1
    {
        public const int TestTimeout = 60_000;
        public UnitTest1(ITestOutputHelper helper)
        {
            Logs.Tester = new XUnitLog(helper) { Name = "Tests" };
            Logs.LogProvider = new XUnitLogProvider(helper);
        }

        [Fact]
        [Trait("Fast", "Fast")]
        public async Task CheckNoDeadLink()
        {
            var views = Path.Combine(TestUtils.TryGetSolutionDirectoryInfo().FullName, "BTCPayServer", "Views");
            var viewFiles = Directory.EnumerateFiles(views, "*.cshtml", SearchOption.AllDirectories).ToArray();
            Assert.NotEmpty(viewFiles);
            Regex regex = new Regex("href=\"(http.*?)[\"#]");
            var httpClient = new HttpClient();
            List<Task> checkLinks = new List<Task>();
            foreach (var file in viewFiles)
            {
                checkLinks.Add(CheckLinks(regex, httpClient, file));
            }
            await Task.WhenAll(checkLinks);
        }

        private static async Task CheckLinks(Regex regex, HttpClient httpClient, string file)
        {
            List<Task> checkLinks = new List<Task>();
            var text = await File.ReadAllTextAsync(file);
            foreach (var match in regex.Matches(text).OfType<Match>())
            {
                checkLinks.Add(AssertLinkNotDead(httpClient, match, file));
            }
            await Task.WhenAll(checkLinks);
        }

        private static async Task AssertLinkNotDead(HttpClient httpClient, Match match, string file)
        {
            var url = match.Groups[1].Value;
            try
            {
                Assert.Equal(HttpStatusCode.OK, (await httpClient.GetAsync(url)).StatusCode);
                Logs.Tester.LogInformation($"OK: {url} ({file})");
            }
            catch
            {
                Logs.Tester.LogInformation($"FAILED: {url} ({file})");
                throw;
            }
        }

        [Fact]
        [Trait("Fast", "Fast")]
        public void CanHandleUriValidation()
        {
            var attribute = new UriAttribute();
            Assert.True(attribute.IsValid("http://localhost"));
            Assert.True(attribute.IsValid("http://localhost:1234"));
            Assert.True(attribute.IsValid("https://localhost"));
            Assert.True(attribute.IsValid("https://127.0.0.1"));
            Assert.True(attribute.IsValid("http://127.0.0.1"));
            Assert.True(attribute.IsValid("http://127.0.0.1:1234"));
            Assert.True(attribute.IsValid("http://gozo.com"));
            Assert.True(attribute.IsValid("https://gozo.com"));
            Assert.True(attribute.IsValid("https://gozo.com:1234"));
            Assert.True(attribute.IsValid("https://gozo.com:1234/test.css"));
            Assert.True(attribute.IsValid("https://gozo.com:1234/test.png"));
            Assert.False(attribute.IsValid("Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud e"));
            Assert.False(attribute.IsValid(2));
            Assert.False(attribute.IsValid("http://"));
            Assert.False(attribute.IsValid("httpdsadsa.com"));
        }

        [Fact]
        [Trait("Fast", "Fast")]
        public void CanCalculateCryptoDue2()
        {
#pragma warning disable CS0618
            var dummy = new Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, Network.RegTest).ToString();
            var networkProvider = new BTCPayNetworkProvider(NetworkType.Regtest);
            var paymentMethodHandlerDictionary = new PaymentMethodHandlerDictionary(new IPaymentMethodHandler[]
            {
                new BitcoinLikePaymentHandler(null, networkProvider, null, null),
                new LightningLikePaymentHandler(null, null, networkProvider, null),
            });
            InvoiceEntity invoiceEntity = new InvoiceEntity();
            invoiceEntity.Networks = networkProvider;
            invoiceEntity.Payments = new System.Collections.Generic.List<PaymentEntity>();
            invoiceEntity.ProductInformation = new ProductInformation() {Price = 100};
            PaymentMethodDictionary paymentMethods = new PaymentMethodDictionary();
            paymentMethods.Add(new PaymentMethod() {CryptoCode = "BTC", Rate = 10513.44m,}.SetPaymentMethodDetails(
                new BTCPayServer.Payments.Bitcoin.BitcoinLikeOnChainPaymentMethod()
                {
                    NextNetworkFee = Money.Coins(0.00000100m), DepositAddress = dummy
                }));
            paymentMethods.Add(new PaymentMethod() {CryptoCode = "LTC", Rate = 216.79m}.SetPaymentMethodDetails(
                new BTCPayServer.Payments.Bitcoin.BitcoinLikeOnChainPaymentMethod()
                {
                    NextNetworkFee = Money.Coins(0.00010000m), DepositAddress = dummy
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
                        Output = new TxOut() {Value = Money.Coins(0.00151263m)}
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
                        Output = new TxOut() {Value = accounting.Due}
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
        public void CanParseTorrc()
        {
            var nl = "\n";
            var input = "# For the hidden service BTCPayServer" + nl +
                        "HiddenServiceDir /var/lib/tor/hidden_services/BTCPayServer" + nl +
                        "# Redirecting to nginx" + nl +
                        "HiddenServicePort 80 172.19.0.10:81";
            nl = Environment.NewLine;
            var expected = "HiddenServiceDir /var/lib/tor/hidden_services/BTCPayServer" + nl +
                           "HiddenServicePort 80 172.19.0.10:81" + nl;
            Assert.True(Torrc.TryParse(input, out var torrc));
            Assert.Equal(expected, torrc.ToString());
            nl = "\r\n";
            input = "# For the hidden service BTCPayServer" + nl +
                        "HiddenServiceDir /var/lib/tor/hidden_services/BTCPayServer" + nl +
                        "# Redirecting to nginx" + nl +
                        "HiddenServicePort 80 172.19.0.10:81";

            Assert.True(Torrc.TryParse(input, out torrc));
            Assert.Equal(expected, torrc.ToString());

            input = "# For the hidden service BTCPayServer" + nl +
                        "HiddenServiceDir /var/lib/tor/hidden_services/BTCPayServer" + nl +
                        "# Redirecting to nginx" + nl +
                        "HiddenServicePort 80 172.19.0.10:80" + nl +
                        "HiddenServiceDir /var/lib/tor/hidden_services/Woocommerce" + nl +
                        "# Redirecting to nginx" + nl +
                        "HiddenServicePort 80 172.19.0.11:80";
            nl = Environment.NewLine;
            expected = "HiddenServiceDir /var/lib/tor/hidden_services/BTCPayServer" + nl +
                           "HiddenServicePort 80 172.19.0.10:80" + nl +
                           "HiddenServiceDir /var/lib/tor/hidden_services/Woocommerce" + nl +
                           "HiddenServicePort 80 172.19.0.11:80" + nl;
            Assert.True(Torrc.TryParse(input, out torrc));
            Assert.Equal(expected, torrc.ToString());
        }

        [Fact]
        [Trait("Fast", "Fast")]
        public void CanCalculateCryptoDue()
        {
            var networkProvider = new BTCPayNetworkProvider(NetworkType.Regtest);
            var paymentMethodHandlerDictionary = new PaymentMethodHandlerDictionary(new IPaymentMethodHandler[]
            {
                new BitcoinLikePaymentHandler(null, networkProvider, null, null),
                new LightningLikePaymentHandler(null, null, networkProvider, null),
            });
            var entity = new InvoiceEntity();
            entity.Networks = networkProvider;
#pragma warning disable CS0618
            entity.Payments = new System.Collections.Generic.List<PaymentEntity>();
            entity.SetPaymentMethod(new PaymentMethod()
            {
                CryptoCode = "BTC", Rate = 5000, NextNetworkFee = Money.Coins(0.1m)
            });
            entity.ProductInformation = new ProductInformation() {Price = 5000};

            var paymentMethod = entity.GetPaymentMethods().TryGet("BTC", PaymentTypes.BTCLike);
            var accounting = paymentMethod.Calculate();
            Assert.Equal(Money.Coins(1.1m), accounting.Due);
            Assert.Equal(Money.Coins(1.1m), accounting.TotalDue);

            entity.Payments.Add(new PaymentEntity()
            {
                Output = new TxOut(Money.Coins(0.5m), new Key()),
                Accounted = true,
                NetworkFee = 0.1m
            });

            accounting = paymentMethod.Calculate();
            //Since we need to spend one more txout, it should be 1.1 - 0,5 + 0.1
            Assert.Equal(Money.Coins(0.7m), accounting.Due);
            Assert.Equal(Money.Coins(1.2m), accounting.TotalDue);

            entity.Payments.Add(new PaymentEntity()
            {
                Output = new TxOut(Money.Coins(0.2m), new Key()),
                Accounted = true,
                NetworkFee = 0.1m
            });

            accounting = paymentMethod.Calculate();
            Assert.Equal(Money.Coins(0.6m), accounting.Due);
            Assert.Equal(Money.Coins(1.3m), accounting.TotalDue);

            entity.Payments.Add(new PaymentEntity()
            {
                Output = new TxOut(Money.Coins(0.6m), new Key()),
                Accounted = true,
                NetworkFee = 0.1m
            });

            accounting = paymentMethod.Calculate();
            Assert.Equal(Money.Zero, accounting.Due);
            Assert.Equal(Money.Coins(1.3m), accounting.TotalDue);

            entity.Payments.Add(
                new PaymentEntity()
                {
                    Output = new TxOut(Money.Coins(0.2m), new Key()), 
                    Accounted = true
                });

            accounting = paymentMethod.Calculate();
            Assert.Equal(Money.Zero, accounting.Due);
            Assert.Equal(Money.Coins(1.3m), accounting.TotalDue);

            entity = new InvoiceEntity();
            entity.Networks = networkProvider;
            entity.ProductInformation = new ProductInformation() {Price = 5000};
            PaymentMethodDictionary paymentMethods = new PaymentMethodDictionary();
            paymentMethods.Add(
                new PaymentMethod()
                {
                    CryptoCode = "BTC", 
                    Rate = 1000, 
                    NextNetworkFee = Money.Coins(0.1m)
                });
            paymentMethods.Add(
                new PaymentMethod()
                {
                    CryptoCode = "LTC", 
                    Rate = 500, 
                    NextNetworkFee = Money.Coins(0.01m)
                });
            entity.SetPaymentMethods(paymentMethods);
            entity.Payments = new List<PaymentEntity>();
            paymentMethod = entity.GetPaymentMethod(new PaymentMethodId("BTC", PaymentTypes.BTCLike));
            accounting = paymentMethod.Calculate();
            Assert.Equal(Money.Coins(5.1m), accounting.Due);

            paymentMethod = entity.GetPaymentMethod(new PaymentMethodId("LTC", PaymentTypes.BTCLike));
            accounting = paymentMethod.Calculate();

            Assert.Equal(Money.Coins(10.01m), accounting.TotalDue);

            entity.Payments.Add(new PaymentEntity()
            {
                CryptoCode = "BTC",
                Output = new TxOut(Money.Coins(1.0m), new Key()),
                Accounted = true,
                NetworkFee = 0.1m
            });

            paymentMethod = entity.GetPaymentMethod(new PaymentMethodId("BTC", PaymentTypes.BTCLike));
            accounting = paymentMethod.Calculate();
            Assert.Equal(Money.Coins(4.2m), accounting.Due);
            Assert.Equal(Money.Coins(1.0m), accounting.CryptoPaid);
            Assert.Equal(Money.Coins(1.0m), accounting.Paid);
            Assert.Equal(Money.Coins(5.2m), accounting.TotalDue);
            Assert.Equal(2, accounting.TxRequired);

            paymentMethod = entity.GetPaymentMethod(new PaymentMethodId("LTC", PaymentTypes.BTCLike));
            accounting = paymentMethod.Calculate();
            Assert.Equal(Money.Coins(10.01m + 0.1m * 2 - 2.0m /* 8.21m */), accounting.Due);
            Assert.Equal(Money.Coins(0.0m), accounting.CryptoPaid);
            Assert.Equal(Money.Coins(2.0m), accounting.Paid);
            Assert.Equal(Money.Coins(10.01m + 0.1m * 2), accounting.TotalDue);

            entity.Payments.Add(new PaymentEntity()
            {
                CryptoCode = "LTC",
                Output = new TxOut(Money.Coins(1.0m), new Key()),
                Accounted = true,
                NetworkFee = 0.01m
            });

            paymentMethod = entity.GetPaymentMethod(new PaymentMethodId("BTC", PaymentTypes.BTCLike));
            accounting = paymentMethod.Calculate();
            Assert.Equal(Money.Coins(4.2m - 0.5m + 0.01m / 2), accounting.Due);
            Assert.Equal(Money.Coins(1.0m), accounting.CryptoPaid);
            Assert.Equal(Money.Coins(1.5m), accounting.Paid);
            Assert.Equal(Money.Coins(5.2m + 0.01m / 2), accounting.TotalDue); // The fee for LTC added
            Assert.Equal(2, accounting.TxRequired);

            paymentMethod = entity.GetPaymentMethod(new PaymentMethodId("LTC", PaymentTypes.BTCLike));
            accounting = paymentMethod.Calculate();
            Assert.Equal(Money.Coins(8.21m - 1.0m + 0.01m), accounting.Due);
            Assert.Equal(Money.Coins(1.0m), accounting.CryptoPaid);
            Assert.Equal(Money.Coins(3.0m), accounting.Paid);
            Assert.Equal(Money.Coins(10.01m + 0.1m * 2 + 0.01m), accounting.TotalDue);
            Assert.Equal(2, accounting.TxRequired);

            var remaining = Money.Coins(4.2m - 0.5m + 0.01m / 2);
            entity.Payments.Add(new PaymentEntity()
            {
                CryptoCode = "BTC",
                Output = new TxOut(remaining, new Key()),
                Accounted = true,
                NetworkFee = 0.1m
            });

            paymentMethod = entity.GetPaymentMethod(new PaymentMethodId("BTC", PaymentTypes.BTCLike));
            accounting = paymentMethod.Calculate();
            Assert.Equal(Money.Zero, accounting.Due);
            Assert.Equal(Money.Coins(1.0m) + remaining, accounting.CryptoPaid);
            Assert.Equal(Money.Coins(1.5m) + remaining, accounting.Paid);
            Assert.Equal(Money.Coins(5.2m + 0.01m / 2), accounting.TotalDue);
            Assert.Equal(accounting.Paid, accounting.TotalDue);
            Assert.Equal(2, accounting.TxRequired);

            paymentMethod = entity.GetPaymentMethod(new PaymentMethodId("LTC", PaymentTypes.BTCLike));
            accounting = paymentMethod.Calculate();
            Assert.Equal(Money.Zero, accounting.Due);
            Assert.Equal(Money.Coins(1.0m), accounting.CryptoPaid);
            Assert.Equal(Money.Coins(3.0m) + remaining * 2, accounting.Paid);
            // Paying 2 BTC fee, LTC fee removed because fully paid
            Assert.Equal(Money.Coins(10.01m + 0.1m * 2 + 0.1m * 2 /* + 0.01m no need to pay this fee anymore */),
                accounting.TotalDue);
            Assert.Equal(1, accounting.TxRequired);
            Assert.Equal(accounting.Paid, accounting.TotalDue);
#pragma warning restore CS0618
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async Task CanUseTestWebsiteUI()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var response = await tester.PayTester.HttpClient.GetAsync("");
                Assert.True(response.IsSuccessStatusCode);
            }
        }

        [Fact]
        [Trait("Fast", "Fast")]
        public void CanAcceptInvoiceWithTolerance()
        {
            var networkProvider = new BTCPayNetworkProvider(NetworkType.Regtest);
            var paymentMethodHandlerDictionary = new PaymentMethodHandlerDictionary(new IPaymentMethodHandler[]
            {
                new BitcoinLikePaymentHandler(null, networkProvider, null, null),
                new LightningLikePaymentHandler(null, null, networkProvider, null),
            });
            var entity = new InvoiceEntity();
            entity.Networks = networkProvider;
#pragma warning disable CS0618
            entity.Payments = new List<PaymentEntity>();
            entity.SetPaymentMethod(new PaymentMethod()
            {
                CryptoCode = "BTC", Rate = 5000, NextNetworkFee = Money.Coins(0.1m)
            });
            entity.ProductInformation = new ProductInformation() {Price = 5000};
            entity.PaymentTolerance = 0;


            var paymentMethod = entity.GetPaymentMethods().TryGet("BTC", PaymentTypes.BTCLike);
            var accounting = paymentMethod.Calculate();
            Assert.Equal(Money.Coins(1.1m), accounting.Due);
            Assert.Equal(Money.Coins(1.1m), accounting.TotalDue);
            Assert.Equal(Money.Coins(1.1m), accounting.MinimumTotalDue);

                entity.PaymentTolerance = 10;
                accounting = paymentMethod.Calculate();
                Assert.Equal(Money.Coins(0.99m), accounting.MinimumTotalDue);

                entity.PaymentTolerance = 100;
                accounting = paymentMethod.Calculate();
                Assert.Equal(Money.Satoshis(1), accounting.MinimumTotalDue);

        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async Task CanAcceptInvoiceWithTolerance2()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");

                // Set tolerance to 50%
                var stores = user.GetController<StoresController>();
                var vm = Assert.IsType<StoreViewModel>(Assert.IsType<ViewResult>(stores.UpdateStore()).Model);
                Assert.Equal(0.0, vm.PaymentTolerance);
                vm.PaymentTolerance = 50.0;
                Assert.IsType<RedirectToActionResult>(stores.UpdateStore(vm).Result);

                var invoice = user.BitPay.CreateInvoice(new Invoice()
                {
                    Buyer = new Buyer() { email = "test@fwf.com" },
                    Price = 5000.0m,
                    Currency = "USD",
                    PosData = "posData",
                    OrderId = "orderId",
                    ItemDesc = "Some description",
                    FullNotifications = true
                }, Facade.Merchant);

                // Pays 75%
                var invoiceAddress = BitcoinAddress.Create(invoice.CryptoInfo[0].Address, tester.ExplorerNode.Network);
                tester.ExplorerNode.SendToAddress(invoiceAddress, Money.Satoshis((decimal)invoice.BtcDue.Satoshi * 0.75m));

                TestUtils.Eventually(() =>
                {
                    var localInvoice = user.BitPay.GetInvoice(invoice.Id, Facade.Merchant);
                    Assert.Equal("paid", localInvoice.Status);
                });
            }
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async Task CanThrowBitpay404Error()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");

                var invoice = user.BitPay.CreateInvoice(new Invoice()
                {
                    Buyer = new Buyer() { email = "test@fwf.com" },
                    Price = 5000.0m,
                    Currency = "USD",
                    PosData = "posData",
                    OrderId = "orderId",
                    ItemDesc = "Some description",
                    FullNotifications = true
                }, Facade.Merchant);

                try
                {
                    var throwsBitpay404Error = user.BitPay.GetInvoice(invoice.Id + "123");
                }
                catch (BitPayException ex)
                {
                    Assert.Equal("Object not found", ex.Errors.First());
                }
            }
        }

        [Fact]
        [Trait("Fast", "Fast")]
        public void RoundupCurrenciesCorrectly()
        {
            foreach (var test in new[]
            {
                (0.0005m, "$0.0005 (USD)", "USD"),
                (0.001m, "$0.001 (USD)", "USD"),
                (0.01m, "$0.01 (USD)", "USD"),
                (0.1m, "$0.10 (USD)", "USD"),
                (0.1m, "0,10 € (EUR)", "EUR"),
                (1000m, "¥1,000 (JPY)", "JPY"),
                (1000.0001m, "₹ 1,000.00 (INR)", "INR")
            })
            {
                var actual = new CurrencyNameTable().DisplayFormatCurrency(test.Item1, test.Item3);
                actual = actual.Replace("￥", "¥"); // Hack so JPY test pass on linux as well
                Assert.Equal(test.Item2, actual);
            }
        }

        [Fact]
        [Trait("Fast", "Fast")]
        public async Task CanEnumerateTorServices()
        {
            var tor = new TorServices(new BTCPayNetworkProvider(NetworkType.Regtest), new BTCPayServerOptions()
            {
                TorrcFile = TestUtils.GetTestDataFullPath("Tor/torrc")
            });
            await tor.Refresh();

            Assert.Single(tor.Services.Where(t => t.ServiceType == TorServiceType.BTCPayServer));
            Assert.Single(tor.Services.Where(t => t.ServiceType == TorServiceType.P2P));
            Assert.Single(tor.Services.Where(t => t.ServiceType == TorServiceType.RPC));
            Assert.True(tor.Services.Where(t => t.ServiceType == TorServiceType.Other).Count() > 1);
        }

        [Fact(Timeout = 60 * 2 * 1000)]
        [Trait("Integration", "Integration")]
        [Trait("Lightning", "Lightning")]
        public async Task CanSetLightningServer()
        {
            using (var tester = ServerTester.Create())
            {
                tester.ActivateLightning();
                await tester.StartAsync();
                await tester.EnsureChannelsSetup();
                var user = tester.NewAccount();
                user.GrantAccess();
                var storeController = user.GetController<StoresController>();
                Assert.IsType<ViewResult>(storeController.UpdateStore());
                Assert.IsType<ViewResult>(storeController.AddLightningNode(user.StoreId, "BTC"));

                var testResult = storeController.AddLightningNode(user.StoreId, new LightningNodeViewModel()
                {
                    ConnectionString = "type=charge;server=" + tester.MerchantCharge.Client.Uri.AbsoluteUri,
                    SkipPortTest = true // We can't test this as the IP can't be resolved by the test host :(
                }, "test", "BTC").GetAwaiter().GetResult();
                Assert.False(storeController.TempData.ContainsKey(WellKnownTempData.ErrorMessage));
                storeController.TempData.Clear();
                Assert.True(storeController.ModelState.IsValid);

                Assert.IsType<RedirectToActionResult>(storeController.AddLightningNode(user.StoreId, new LightningNodeViewModel()
                {
                    ConnectionString = "type=charge;server=" + tester.MerchantCharge.Client.Uri.AbsoluteUri
                }, "save", "BTC").GetAwaiter().GetResult());

                // Make sure old connection string format does not work
                Assert.IsType<ViewResult>(storeController.AddLightningNode(user.StoreId, new LightningNodeViewModel()
                {
                    ConnectionString = tester.MerchantCharge.Client.Uri.AbsoluteUri
                }, "save", "BTC").GetAwaiter().GetResult());

                var storeVm = Assert.IsType<Models.StoreViewModels.StoreViewModel>(Assert.IsType<ViewResult>(storeController.UpdateStore()).Model);
                Assert.Single(storeVm.LightningNodes.Where(l => !string.IsNullOrEmpty(l.Address)));
            }
        }

        [Fact(Timeout = 60 * 2 * 1000)]
        [Trait("Integration", "Integration")]
        [Trait("Lightning", "Lightning")]
        public async Task CanSendLightningPaymentCLightning()
        {
            await ProcessLightningPayment(LightningConnectionType.CLightning);
        }

        [Fact(Timeout = 60 * 2 * 1000)]
        [Trait("Integration", "Integration")]
        [Trait("Lightning", "Lightning")]
        public async Task CanSendLightningPaymentCharge()
        {
            await ProcessLightningPayment(LightningConnectionType.Charge);
        }

        [Fact(Timeout = 60 * 2 * 1000)]
        [Trait("Integration", "Integration")]
        [Trait("Lightning", "Lightning")]
        public async Task CanSendLightningPaymentLnd()
        {
            await ProcessLightningPayment(LightningConnectionType.LndREST);
        }

        async Task ProcessLightningPayment(LightningConnectionType type)
        {
            // For easier debugging and testing
            // LightningLikePaymentHandler.LIGHTNING_TIMEOUT = int.MaxValue;

            using (var tester = ServerTester.Create())
            {
                tester.ActivateLightning();
                await tester.StartAsync();
                await tester.EnsureChannelsSetup();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterLightningNode("BTC", type);
                user.RegisterDerivationScheme("BTC");

                await CanSendLightningPaymentCore(tester, user);

                await Task.WhenAll(Enumerable.Range(0, 5)
                    .Select(_ => CanSendLightningPaymentCore(tester, user))
                    .ToArray());
            }
        }

        async Task CanSendLightningPaymentCore(ServerTester tester, TestAccount user)
        {
            var invoice = await user.BitPay.CreateInvoiceAsync(new Invoice()
            {
                Price = 0.01m,
                Currency = "USD",
                PosData = "posData",
                OrderId = "orderId",
                ItemDesc = "Some description"
            });
            await Task.Delay(TimeSpan.FromMilliseconds(1000)); // Give time to listen the new invoices
            Logs.Tester.LogInformation($"Trying to send Lightning payment to {invoice.Id}");
            await tester.SendLightningPaymentAsync(invoice);
            Logs.Tester.LogInformation($"Lightning payment to {invoice.Id} is sent");
            await TestUtils.EventuallyAsync(async () =>
            {
                var localInvoice = await user.BitPay.GetInvoiceAsync(invoice.Id);
                Assert.Equal("complete", localInvoice.Status);
                // C-Lightning may overpay for privacy
                Assert.Contains(localInvoice.ExceptionStatus.ToString(), new[] { "False", "paidOver" });
            });
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanUseServerInitiatedPairingCode()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var acc = tester.NewAccount();
                acc.Register();
                acc.CreateStore();

                var controller = acc.GetController<StoresController>();
                var token = (RedirectToActionResult)await controller.CreateToken2(new Models.StoreViewModels.CreateTokenViewModel()
                {
                    Label = "bla",
                    PublicKey = null,
                    StoreId = acc.StoreId
                });

                var pairingCode = (string)token.RouteValues["pairingCode"];

                acc.BitPay.AuthorizeClient(new PairingCode(pairingCode)).GetAwaiter().GetResult();
                Assert.True(acc.BitPay.TestAccess(Facade.Merchant));
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanSendIPN()
        {
            using (var callbackServer = new CustomServer())
            {
                using (var tester = ServerTester.Create())
                {
                    await tester.StartAsync();
                    var acc = tester.NewAccount();
                    acc.GrantAccess();
                    acc.RegisterDerivationScheme("BTC");
                    acc.ModifyStore(s => s.SpeedPolicy = SpeedPolicy.LowSpeed);
                    var invoice = acc.BitPay.CreateInvoice(new Invoice()
                    {
                        Price = 5.0m,
                        Currency = "USD",
                        PosData = "posData",
                        OrderId = "orderId",
                        NotificationURL = callbackServer.GetUri().AbsoluteUri,
                        ItemDesc = "Some description",
                        FullNotifications = true,
                        ExtendedNotifications = true
                    });
                    BitcoinUrlBuilder url = new BitcoinUrlBuilder(invoice.PaymentUrls.BIP21, tester.NetworkProvider.BTC.NBitcoinNetwork);
                    bool receivedPayment = false;
                    bool paid = false;
                    bool confirmed = false;
                    bool completed = false;
                    while (!completed || !confirmed)
                    {
                        var request = await callbackServer.GetNextRequest();
                        if (request.ContainsKey("event"))
                        {
                            var evtName = request["event"]["name"].Value<string>();
                            switch (evtName)
                            {
                                case InvoiceEvent.Created:
                                    tester.ExplorerNode.SendToAddress(url.Address, url.Amount);
                                    break;
                                case InvoiceEvent.ReceivedPayment:
                                    receivedPayment = true;
                                    break;
                                case InvoiceEvent.PaidInFull:
                                    Assert.True(receivedPayment);
                                    tester.ExplorerNode.Generate(6);
                                    paid = true;
                                    break;
                                case InvoiceEvent.Confirmed:
                                    Assert.True(paid);
                                    confirmed = true;
                                    break;
                                case InvoiceEvent.Completed:
                                    Assert.True(paid); //TODO: Fix, out of order event mean we can receive invoice_confirmed after invoice_complete
                                    completed = true;
                                    break;
                                default:
                                    Assert.False(true, $"{evtName} was not expected");
                                    break;
                            }
                        }
                    }
                    var invoice2 = acc.BitPay.GetInvoice(invoice.Id);
                    Assert.NotNull(invoice2);
                }
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CantPairTwiceWithSamePubkey()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var acc = tester.NewAccount();
                acc.Register();
                acc.CreateStore();
                var store = acc.GetController<StoresController>();
                var pairingCode = acc.BitPay.RequestClientAuthorization("test", Facade.Merchant);
                Assert.IsType<RedirectToActionResult>(store.Pair(pairingCode.ToString(), acc.StoreId).GetAwaiter().GetResult());

                pairingCode = acc.BitPay.RequestClientAuthorization("test1", Facade.Merchant);
                acc.CreateStore();
                var store2 = acc.GetController<StoresController>();
                await store2.Pair(pairingCode.ToString(), store2.CurrentStore.Id);
                Assert.Contains(nameof(PairingResult.ReusedKey), (string)store2.TempData[WellKnownTempData.ErrorMessage], StringComparison.CurrentCultureIgnoreCase);
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public void CanSolveTheDogesRatesOnKraken()
        {
            var provider = new BTCPayNetworkProvider(NetworkType.Mainnet);
            var factory = CreateBTCPayRateFactory();
            var fetcher = new RateFetcher(factory);

            Assert.True(RateRules.TryParse("X_X=kraken(X_BTC) * kraken(BTC_X)", out var rule));
            foreach (var pair in new[] { "DOGE_USD", "DOGE_CAD", "DASH_CAD", "DASH_USD", "DASH_EUR" })
            {
                var result = fetcher.FetchRate(CurrencyPair.Parse(pair), rule, default).GetAwaiter().GetResult();
                Assert.NotNull(result.BidAsk);
                Assert.Empty(result.Errors);
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanRescanWallet()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var acc = tester.NewAccount();
                acc.GrantAccess();
                acc.RegisterDerivationScheme("BTC", true);
                var btcDerivationScheme = acc.DerivationScheme;

                var walletController = acc.GetController<WalletsController>();

                var walletId = new WalletId(acc.StoreId, "BTC");
                acc.IsAdmin = true;
                walletController = acc.GetController<WalletsController>();
                
                var rescan = Assert.IsType<RescanWalletModel>(Assert.IsType<ViewResult>(walletController.WalletRescan(walletId).Result).Model);
                Assert.True(rescan.Ok);
                Assert.True(rescan.IsFullySync);
                Assert.True(rescan.IsSupportedByCurrency);
                Assert.True(rescan.IsServerAdmin);

                rescan.GapLimit = 100;

                // Sending a coin
                var txId = tester.ExplorerNode.SendToAddress(btcDerivationScheme.GetDerivation(new KeyPath("0/90")).ScriptPubKey, Money.Coins(1.0m));
                tester.ExplorerNode.Generate(1);
                var transactions = Assert.IsType<ListTransactionsViewModel>(Assert.IsType<ViewResult>(walletController.WalletTransactions(walletId).Result).Model);
                Assert.Empty(transactions.Transactions);

                Assert.IsType<RedirectToActionResult>(walletController.WalletRescan(walletId, rescan).Result);

                while (true)
                {
                    rescan = Assert.IsType<RescanWalletModel>(Assert.IsType<ViewResult>(walletController.WalletRescan(walletId).Result).Model);
                    if (rescan.Progress == null && rescan.LastSuccess != null)
                    {
                        if (rescan.LastSuccess.Found == 0)
                            continue;
                        // Scan over
                        break;
                    }
                    else
                    {
                        Assert.Null(rescan.TimeOfScan);
                        Assert.NotNull(rescan.RemainingTime);
                        Assert.NotNull(rescan.Progress);
                        Thread.Sleep(100);
                    }
                }
                Assert.Null(rescan.PreviousError);
                Assert.NotNull(rescan.TimeOfScan);
                Assert.Equal(1, rescan.LastSuccess.Found);
                transactions = Assert.IsType<ListTransactionsViewModel>(Assert.IsType<ViewResult>(walletController.WalletTransactions(walletId).Result).Model);
                var tx = Assert.Single(transactions.Transactions);
                Assert.Equal(tx.Id, txId.ToString());

                // Hijack the test to see if we can add label and comments
                Assert.IsType<RedirectToActionResult>(await walletController.ModifyTransaction(walletId, tx.Id, addcomment: "hello-pouet"));
                Assert.IsType<RedirectToActionResult>(await walletController.ModifyTransaction(walletId, tx.Id, addlabel: "test"));
                Assert.IsType<RedirectToActionResult>(await walletController.ModifyTransaction(walletId, tx.Id, addlabelclick: "test2"));
                Assert.IsType<RedirectToActionResult>(await walletController.ModifyTransaction(walletId, tx.Id, addcomment: "hello"));

                transactions = Assert.IsType<ListTransactionsViewModel>(Assert.IsType<ViewResult>(walletController.WalletTransactions(walletId).Result).Model);
                tx = Assert.Single(transactions.Transactions);

                Assert.Equal("hello", tx.Comment);
                Assert.Contains("test", tx.Labels.Select(l => l.Value));
                Assert.Contains("test2", tx.Labels.Select(l => l.Value));
                Assert.Equal(2, tx.Labels.GroupBy(l => l.Color).Count());

                Assert.IsType<RedirectToActionResult>(await walletController.ModifyTransaction(walletId, tx.Id, removelabel: "test2"));

                transactions = Assert.IsType<ListTransactionsViewModel>(Assert.IsType<ViewResult>(walletController.WalletTransactions(walletId).Result).Model);
                tx = Assert.Single(transactions.Transactions);

                Assert.Equal("hello", tx.Comment);
                Assert.Contains("test", tx.Labels.Select(l => l.Value));
                Assert.DoesNotContain("test2", tx.Labels.Select(l => l.Value));
                Assert.Single(tx.Labels.GroupBy(l => l.Color));

                var walletInfo = await tester.PayTester.GetService<WalletRepository>().GetWalletInfo(walletId);
                Assert.Single(walletInfo.LabelColors); // the test2 color should have been removed
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanListInvoices()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var acc = tester.NewAccount();
                acc.GrantAccess();
                acc.RegisterDerivationScheme("BTC");
                // First we try payment with a merchant having only BTC
                var invoice = acc.BitPay.CreateInvoice(new Invoice()
                {
                    Price = 500,
                    Currency = "USD",
                    PosData = "posData",
                    OrderId = "orderId",
                    ItemDesc = "Some description",
                    FullNotifications = true
                }, Facade.Merchant);

                var cashCow = tester.ExplorerNode;
                var invoiceAddress = BitcoinAddress.Create(invoice.CryptoInfo[0].Address, cashCow.Network);
                var firstPayment = invoice.CryptoInfo[0].TotalDue - Money.Satoshis(10);
                cashCow.SendToAddress(invoiceAddress, firstPayment);
                TestUtils.Eventually(() =>
                {
                    invoice = acc.BitPay.GetInvoice(invoice.Id);
                    Assert.Equal(firstPayment, invoice.CryptoInfo[0].Paid);
                });


                AssertSearchInvoice(acc, true, invoice.Id, $"storeid:{acc.StoreId}");
                AssertSearchInvoice(acc, false, invoice.Id, $"storeid:blah");
                AssertSearchInvoice(acc, true, invoice.Id, $"{invoice.Id}");
                AssertSearchInvoice(acc, true, invoice.Id, $"exceptionstatus:paidPartial");
                AssertSearchInvoice(acc, false, invoice.Id, $"exceptionstatus:paidOver");
                AssertSearchInvoice(acc, true, invoice.Id, $"unusual:true");
                AssertSearchInvoice(acc, false, invoice.Id, $"unusual:false");

                var time = invoice.InvoiceTime;
                AssertSearchInvoice(acc, true, invoice.Id, $"startdate:{time.ToString("yyyy-MM-dd HH:mm:ss")}");
                AssertSearchInvoice(acc, true, invoice.Id, $"enddate:{time.ToStringLowerInvariant()}");
                AssertSearchInvoice(acc, false, invoice.Id, $"startdate:{time.AddSeconds(1).ToString("yyyy-MM-dd HH:mm:ss")}");
                AssertSearchInvoice(acc, false, invoice.Id, $"enddate:{time.AddSeconds(-1).ToString("yyyy-MM-dd HH:mm:ss")}");
            }
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async Task CanGetRates()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var acc = tester.NewAccount();
                acc.GrantAccess();
                acc.RegisterDerivationScheme("BTC");

                var rateController = acc.GetController<RateController>();
                var GetBaseCurrencyRatesResult = JObject.Parse(((JsonResult)rateController.GetBaseCurrencyRates("BTC", default)
                    .GetAwaiter().GetResult()).Value.ToJson()).ToObject<DataWrapper<Rate[]>>();
                Assert.NotNull(GetBaseCurrencyRatesResult);
                Assert.NotNull(GetBaseCurrencyRatesResult.Data);
                var rate = Assert.Single(GetBaseCurrencyRatesResult.Data);
                Assert.Equal("BTC", rate.Code);

                var GetRatesResult = JObject.Parse(((JsonResult)rateController.GetRates(null, default)
                    .GetAwaiter().GetResult()).Value.ToJson()).ToObject<DataWrapper<Rate[]>>();
                // We don't have any default currencies, so this should be failing
                Assert.Null(GetRatesResult?.Data);

                var store = acc.GetController<StoresController>();
                var ratesVM = (RatesViewModel)(Assert.IsType<ViewResult>(store.Rates()).Model);
                ratesVM.DefaultCurrencyPairs = "BTC_USD,LTC_USD";
                await store.Rates(ratesVM);
                store = acc.GetController<StoresController>();
                rateController = acc.GetController<RateController>();
                GetRatesResult = JObject.Parse(((JsonResult)rateController.GetRates(null, default)
                    .GetAwaiter().GetResult()).Value.ToJson()).ToObject<DataWrapper<Rate[]>>();
                // Now we should have a result
                Assert.NotNull(GetRatesResult);
                Assert.NotNull(GetRatesResult.Data);
                Assert.Equal(2, GetRatesResult.Data.Length);

                var GetCurrencyPairRateResult = JObject.Parse(((JsonResult)rateController.GetCurrencyPairRate("BTC", "LTC", default)
                    .GetAwaiter().GetResult()).Value.ToJson()).ToObject<DataWrapper<Rate>>();

                Assert.NotNull(GetCurrencyPairRateResult);
                Assert.NotNull(GetCurrencyPairRateResult.Data);
                Assert.Equal("LTC", GetCurrencyPairRateResult.Data.Code);

                // Should be OK because the request is signed, so we can know the store
                var rates = acc.BitPay.GetRates();
                HttpClient client = new HttpClient();
                // Unauthentified requests should also be ok
                var response = await client.GetAsync($"http://127.0.0.1:{tester.PayTester.Port}/api/rates?storeId={acc.StoreId}");
                response.EnsureSuccessStatusCode();
                response = await client.GetAsync($"http://127.0.0.1:{tester.PayTester.Port}/rates?storeId={acc.StoreId}");
                response.EnsureSuccessStatusCode();
            }
        }

        private void AssertSearchInvoice(TestAccount acc, bool expected, string invoiceId, string filter)
        {
            var result = (Models.InvoicingModels.InvoicesModel)((ViewResult)acc.GetController<InvoiceController>().ListInvoices(filter).Result).Model;
            Assert.Equal(expected, result.Invoices.Any(i => i.InvoiceId == invoiceId));
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanRBFPayment()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");
                user.SetNetworkFeeMode(NetworkFeeMode.Always);
                var invoice = user.BitPay.CreateInvoice(new Invoice()
                {
                    Price = 5000.0m,
                    Currency = "USD"
                }, Facade.Merchant);
                var payment1 = invoice.BtcDue + Money.Coins(0.0001m);
                var payment2 = invoice.BtcDue;

                var tx1 = new uint256(tester.ExplorerNode.SendCommand("sendtoaddress", new object[]
                {
                    invoice.BitcoinAddress,
                    payment1.ToString(),
                    null, //comment
                    null, //comment_to
                    false, //subtractfeefromamount
                    true, //replaceable
                }).ResultString);
                Logs.Tester.LogInformation($"Let's send a first payment of {payment1} for the {invoice.BtcDue} invoice ({tx1})");
                var invoiceAddress = BitcoinAddress.Create(invoice.BitcoinAddress, user.SupportedNetwork.NBitcoinNetwork);

                Logs.Tester.LogInformation($"The invoice should be paidOver");
                TestUtils.Eventually(() =>
                {
                    invoice = user.BitPay.GetInvoice(invoice.Id);
                    Assert.Equal(payment1, invoice.BtcPaid);
                    Assert.Equal("paid", invoice.Status);
                    Assert.Equal("paidOver", invoice.ExceptionStatus.ToString());
                    invoiceAddress = BitcoinAddress.Create(invoice.BitcoinAddress, user.SupportedNetwork.NBitcoinNetwork);
                });

                var tx = tester.ExplorerNode.GetRawTransaction(new uint256(tx1));
                foreach (var input in tx.Inputs)
                {
                    input.ScriptSig = Script.Empty; //Strip signatures
                }
                var output = tx.Outputs.First(o => o.Value == payment1);
                output.Value = payment2;
                output.ScriptPubKey = invoiceAddress.ScriptPubKey;

                using (var cts = new CancellationTokenSource(10000))
                using (var listener = tester.ExplorerClient.CreateWebsocketNotificationSession())
                {
                    listener.ListenAllDerivationSchemes();
                    var replaced = tester.ExplorerNode.SignRawTransaction(tx);
                    Thread.Sleep(1000); // Make sure the replacement has a different timestamp
                    var tx2 = tester.ExplorerNode.SendRawTransaction(replaced);
                    Logs.Tester.LogInformation($"Let's RBF with a payment of {payment2} ({tx2}), waiting for NBXplorer to pick it up");
                    Assert.Equal(tx2, ((NewTransactionEvent)listener.NextEvent(cts.Token)).TransactionData.TransactionHash);
                }
                Logs.Tester.LogInformation($"The invoice should now not be paidOver anymore");
                TestUtils.Eventually(() =>
                {
                    invoice = user.BitPay.GetInvoice(invoice.Id);
                    Assert.Equal(payment2, invoice.BtcPaid);
                    Assert.Equal("False", invoice.ExceptionStatus.ToString());
                });
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Fast", "Fast")]
        public void CanParseFilter()
        {
            var filter = "storeid:abc, status:abed, blabhbalh ";
            var search = new SearchString(filter);
            Assert.Equal("storeid:abc, status:abed, blabhbalh", search.ToString());
            Assert.Equal("blabhbalh", search.TextSearch);
            Assert.Single(search.Filters["storeid"]);
            Assert.Single(search.Filters["status"]);
            Assert.Equal("abc", search.Filters["storeid"].First());
            Assert.Equal("abed", search.Filters["status"].First());

            filter = "status:abed, status:abed2";
            search = new SearchString(filter);
            Assert.Equal("", search.TextSearch);
            Assert.Equal("status:abed, status:abed2", search.ToString());
            Assert.Throws<KeyNotFoundException>(() => search.Filters["test"]);
            Assert.Equal(2, search.Filters["status"].Count);
            Assert.Equal("abed", search.Filters["status"].First());
            Assert.Equal("abed2", search.Filters["status"].Skip(1).First());

            filter = "StartDate:2019-04-25 01:00 AM, hekki";
            search = new SearchString(filter);
            Assert.Equal("2019-04-25 01:00 AM", search.Filters["startdate"].First());
            Assert.Equal("hekki", search.TextSearch);
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Fast", "Fast")]
        public void CanParseFingerprint()
        {
            Assert.True(SSH.SSHFingerprint.TryParse("4e343c6fc6cfbf9339c02d06a151e1dd", out var unused));
            Assert.Equal("4e:34:3c:6f:c6:cf:bf:93:39:c0:2d:06:a1:51:e1:dd", unused.ToString());
            Assert.True(SSH.SSHFingerprint.TryParse("4e:34:3c:6f:c6:cf:bf:93:39:c0:2d:06:a1:51:e1:dd", out unused));
            Assert.True(SSH.SSHFingerprint.TryParse("SHA256:Wl7CdRgT4u5T7yPMsxSrlFP+HIJJWwidGkzphJ8di5w", out unused));
            Assert.True(SSH.SSHFingerprint.TryParse("SHA256:Wl7CdRgT4u5T7yPMsxSrlFP+HIJJWwidGkzphJ8di5w=", out unused));
            Assert.True(SSH.SSHFingerprint.TryParse("Wl7CdRgT4u5T7yPMsxSrlFP+HIJJWwidGkzphJ8di5w=", out unused));
            Assert.Equal("SHA256:Wl7CdRgT4u5T7yPMsxSrlFP+HIJJWwidGkzphJ8di5w", unused.ToString());

            Assert.True(SSH.SSHFingerprint.TryParse("Wl7CdRgT4u5T7yPMsxSrlFP+HIJJWwidGkzphJ8di5w=", out var f1));
            Assert.True(SSH.SSHFingerprint.TryParse("SHA256:Wl7CdRgT4u5T7yPMsxSrlFP+HIJJWwidGkzphJ8di5w", out var f2));
            Assert.Equal(f1.ToString(), f2.ToString());
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async void CheckCORSSetOnBitpayAPI()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                foreach (var req in new[]
                {
                    "invoices/",
                    "invoices",
                    "rates",
                    "tokens"
                }.Select(async path =>
                {
                    using (HttpClient client = new HttpClient())
                    {
                        HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Options, tester.PayTester.ServerUri.AbsoluteUri + path);
                        message.Headers.Add("Access-Control-Request-Headers", "test");
                        var response = await client.SendAsync(message);
                        response.EnsureSuccessStatusCode();
                        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var val));
                        Assert.Equal("*", val.FirstOrDefault());
                        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Headers", out val));
                        Assert.Equal("test", val.FirstOrDefault());
                    }
                }).ToList())
                {
                    await req;
                }
                HttpClient client2 = new HttpClient();
                HttpRequestMessage message2 = new HttpRequestMessage(HttpMethod.Options, tester.PayTester.ServerUri.AbsoluteUri + "rates");
                var response2 = await client2.SendAsync(message2);
                Assert.True(response2.Headers.TryGetValues("Access-Control-Allow-Origin", out var val2));
                Assert.Equal("*", val2.FirstOrDefault());
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task TestAccessBitpayAPI()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                Assert.False(user.BitPay.TestAccess(Facade.Merchant));
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");

                Assert.True(user.BitPay.TestAccess(Facade.Merchant));

                // Test request pairing code client side
                var storeController = user.GetController<StoresController>();
                storeController.CreateToken(user.StoreId, new CreateTokenViewModel()
                {
                    Label = "test2",
                    StoreId = user.StoreId
                }).GetAwaiter().GetResult();
                Assert.NotNull(storeController.GeneratedPairingCode);


                var k = new Key();
                var bitpay = new Bitpay(k, tester.PayTester.ServerUri);
                bitpay.AuthorizeClient(new PairingCode(storeController.GeneratedPairingCode)).Wait();
                Assert.True(bitpay.TestAccess(Facade.Merchant));
                Assert.True(bitpay.TestAccess(Facade.PointOfSale));
                // Same with new instance
                bitpay = new Bitpay(k, tester.PayTester.ServerUri);
                Assert.True(bitpay.TestAccess(Facade.Merchant));
                Assert.True(bitpay.TestAccess(Facade.PointOfSale));

                // Can generate API Key
                var repo = tester.PayTester.GetService<TokenRepository>();
                Assert.Empty(repo.GetLegacyAPIKeys(user.StoreId).GetAwaiter().GetResult());
                Assert.IsType<RedirectToActionResult>(user.GetController<StoresController>().GenerateAPIKey(user.StoreId).GetAwaiter().GetResult());

                var apiKey = Assert.Single(repo.GetLegacyAPIKeys(user.StoreId).GetAwaiter().GetResult());
                ///////

                // Generating a new one remove the previous
                Assert.IsType<RedirectToActionResult>(user.GetController<StoresController>().GenerateAPIKey(user.StoreId).GetAwaiter().GetResult());
                var apiKey2 = Assert.Single(repo.GetLegacyAPIKeys(user.StoreId).GetAwaiter().GetResult());
                Assert.NotEqual(apiKey, apiKey2);
                ////////

                apiKey = apiKey2;

                // Can create an invoice with this new API Key
                HttpClient client = new HttpClient();
                HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post, tester.PayTester.ServerUri.AbsoluteUri + "invoices");
                message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Encoders.Base64.EncodeData(Encoders.ASCII.DecodeData(apiKey)));
                var invoice = new Invoice()
                {
                    Price = 5000.0m,
                    Currency = "USD"
                };
                message.Content = new StringContent(JsonConvert.SerializeObject(invoice), Encoding.UTF8, "application/json");
                var result = client.SendAsync(message).GetAwaiter().GetResult();
                result.EnsureSuccessStatusCode();
                /////////////////////

                // Have error 403 with bad signature
                client = new HttpClient();
                HttpRequestMessage mess = new HttpRequestMessage(HttpMethod.Get, tester.PayTester.ServerUri.AbsoluteUri + "tokens");
                mess.Content = new StringContent(string.Empty, Encoding.UTF8, "application/json");
                mess.Headers.Add("x-signature", "3045022100caa123193afc22ef93d9c6b358debce6897c09dd9869fe6fe029c9cb43623fac022000b90c65c50ba8bbbc6ebee8878abe5659e17b9f2e1b27d95eda4423da5608fe");
                mess.Headers.Add("x-identity", "04b4d82095947262dd70f94c0a0e005ec3916e3f5f2181c176b8b22a52db22a8c436c4703f43a9e8884104854a11e1eb30df8fdf116e283807a1f1b8fe4c182b99");
                mess.Method = HttpMethod.Get;
                result = client.SendAsync(mess).GetAwaiter().GetResult();
                Assert.Equal(System.Net.HttpStatusCode.Unauthorized, result.StatusCode);
                //
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanUseExchangeSpecificRate()
        {
            using (var tester = ServerTester.Create())
            {
                tester.PayTester.MockRates = false;
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");
                List<decimal> rates = new List<decimal>();
                rates.Add(await CreateInvoice(tester, user, "coingecko"));
                var bitflyer = await CreateInvoice(tester, user, "bitflyer", "JPY");
                var bitflyer2 = await CreateInvoice(tester, user, "bitflyer", "JPY");
                Assert.Equal(bitflyer, bitflyer2); // Should be equal because cache
                rates.Add(bitflyer);

                foreach (var rate in rates)
                {
                    Assert.Single(rates.Where(r => r == rate));
                }
            }
        }

        private static async Task<decimal> CreateInvoice(ServerTester tester, TestAccount user, string exchange, string currency = "USD")
        {
            var storeController = user.GetController<StoresController>();
            var vm = (RatesViewModel)((ViewResult)storeController.Rates()).Model;
            vm.PreferredExchange = exchange;
            await storeController.Rates(vm);
            var invoice2 = await user.BitPay.CreateInvoiceAsync(new Invoice()
            {
                Price = 5000.0m,
                Currency = currency,
                PosData = "posData",
                OrderId = "orderId",
                ItemDesc = "Some description",
                FullNotifications = true
            }, Facade.Merchant);
            return invoice2.CryptoInfo[0].Rate;
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanUseAnyoneCanCreateInvoice()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");

                Logs.Tester.LogInformation("StoreId without anyone can create invoice = 403");
                var response = await tester.PayTester.HttpClient.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"invoices?storeId={user.StoreId}")
                {
                    Content = new StringContent("{\"Price\": 5000, \"currency\": \"USD\"}", Encoding.UTF8, "application/json"),
                });
                Assert.Equal(403, (int)response.StatusCode);

                Logs.Tester.LogInformation("No store without  anyone can create invoice = 404 because the bitpay API can't know the storeid");
                response = await tester.PayTester.HttpClient.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"invoices")
                {
                    Content = new StringContent("{\"Price\": 5000, \"currency\": \"USD\"}", Encoding.UTF8, "application/json"),
                });
                Assert.Equal(404, (int)response.StatusCode);

                user.ModifyStore(s => s.AnyoneCanCreateInvoice = true);

                Logs.Tester.LogInformation("Bad store with anyone can create invoice = 403");
                response = await tester.PayTester.HttpClient.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"invoices?storeId=badid")
                {
                    Content = new StringContent("{\"Price\": 5000, \"currency\": \"USD\"}", Encoding.UTF8, "application/json"),
                });
                Assert.Equal(403, (int)response.StatusCode);

                Logs.Tester.LogInformation("Good store with anyone can create invoice = 200");
                response = await tester.PayTester.HttpClient.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"invoices?storeId={user.StoreId}")
                {
                    Content = new StringContent("{\"Price\": 5000, \"currency\": \"USD\"}", Encoding.UTF8, "application/json"),
                });
                Assert.Equal(200, (int)response.StatusCode);
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanTweakRate()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");
                // First we try payment with a merchant having only BTC
                var invoice1 = user.BitPay.CreateInvoice(new Invoice()
                {
                    Price = 5000.0m,
                    Currency = "USD",
                    PosData = "posData",
                    OrderId = "orderId",
                    ItemDesc = "Some description",
                    FullNotifications = true
                }, Facade.Merchant);
                Assert.Equal(Money.Coins(1.0m), invoice1.BtcPrice);

                var storeController = user.GetController<StoresController>();
                var vm = (RatesViewModel)((ViewResult)storeController.Rates()).Model;
                Assert.Equal(0.0, vm.Spread);
                vm.Spread = 40;
                await storeController.Rates(vm);


                var invoice2 = user.BitPay.CreateInvoice(new Invoice()
                {
                    Price = 5000.0m,
                    Currency = "USD",
                    PosData = "posData",
                    OrderId = "orderId",
                    ItemDesc = "Some description",
                    FullNotifications = true
                }, Facade.Merchant);

                var expectedRate = 5000.0m * 0.6m;
                var expectedCoins = invoice2.Price / expectedRate;
                Assert.True(invoice2.BtcPrice.Almost(Money.Coins(expectedCoins), 0.00001m));
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
                var invoice = user.BitPay.CreateInvoice(new Invoice()
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
                var checkout = (Models.InvoicingModels.PaymentModel)((JsonResult)controller.GetStatus(invoice.Id, null).GetAwaiter().GetResult()).Value;
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
                    checkout = (Models.InvoicingModels.PaymentModel)((JsonResult)controller.GetStatus(invoice.Id, null).GetAwaiter().GetResult()).Value;
                    Assert.Equal("paid", checkout.Status);
                });

            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanModifyRates()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");

                var store = user.GetController<StoresController>();
                var rateVm = Assert.IsType<RatesViewModel>(Assert.IsType<ViewResult>(store.Rates()).Model);
                Assert.False(rateVm.ShowScripting);
                Assert.Equal(CoinGeckoRateProvider.CoinGeckoName, rateVm.PreferredExchange);
                Assert.Equal(0.0, rateVm.Spread);
                Assert.Null(rateVm.TestRateRules);

                rateVm.PreferredExchange = "bitflyer";
                Assert.IsType<RedirectToActionResult>(await store.Rates(rateVm, "Save"));
                rateVm = Assert.IsType<RatesViewModel>(Assert.IsType<ViewResult>(store.Rates()).Model);
                Assert.Equal("bitflyer", rateVm.PreferredExchange);

                rateVm.ScriptTest = "BTC_JPY,BTC_CAD";
                rateVm.Spread = 10;
                store = user.GetController<StoresController>();
                rateVm = Assert.IsType<RatesViewModel>(Assert.IsType<ViewResult>(await store.Rates(rateVm, "Test")).Model);
                Assert.NotNull(rateVm.TestRateRules);
                Assert.Equal(2, rateVm.TestRateRules.Count);
                Assert.False(rateVm.TestRateRules[0].Error);
                Assert.StartsWith("(bitflyer(BTC_JPY)) * (0.9, 1.1) =", rateVm.TestRateRules[0].Rule, StringComparison.OrdinalIgnoreCase);
                Assert.True(rateVm.TestRateRules[1].Error);
                Assert.IsType<RedirectToActionResult>(await store.Rates(rateVm, "Save"));

                Assert.IsType<RedirectToActionResult>(store.ShowRateRulesPost(true).Result);
                Assert.IsType<RedirectToActionResult>(await store.Rates(rateVm, "Save"));
                store = user.GetController<StoresController>();
                rateVm = Assert.IsType<RatesViewModel>(Assert.IsType<ViewResult>(store.Rates()).Model);
                Assert.Equal(rateVm.StoreId, user.StoreId);
                Assert.Equal(rateVm.DefaultScript, rateVm.Script);
                Assert.True(rateVm.ShowScripting);
                rateVm.ScriptTest = "BTC_JPY";
                rateVm = Assert.IsType<RatesViewModel>(Assert.IsType<ViewResult>(await store.Rates(rateVm, "Test")).Model);
                Assert.True(rateVm.ShowScripting);
                Assert.Contains("(bitflyer(BTC_JPY)) * (0.9, 1.1) = ", rateVm.TestRateRules[0].Rule, StringComparison.OrdinalIgnoreCase);

                rateVm.ScriptTest = "BTC_USD,BTC_CAD,DOGE_USD,DOGE_CAD";
                rateVm.Script = "DOGE_X = bittrex(DOGE_BTC) * BTC_X;\n" +
                                "X_CAD = quadrigacx(X_CAD);\n" +
                                 "X_X = coingecko(X_X);";
                rateVm.Spread = 50;
                rateVm = Assert.IsType<RatesViewModel>(Assert.IsType<ViewResult>(await store.Rates(rateVm, "Test")).Model);
                Assert.True(rateVm.TestRateRules.All(t => !t.Error));
                Assert.IsType<RedirectToActionResult>(await store.Rates(rateVm, "Save"));
                store = user.GetController<StoresController>();
                rateVm = Assert.IsType<RatesViewModel>(Assert.IsType<ViewResult>(store.Rates()).Model);
                Assert.Equal(50, rateVm.Spread);
                Assert.True(rateVm.ShowScripting);
                Assert.Contains("DOGE_X", rateVm.Script, StringComparison.OrdinalIgnoreCase);
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
                var invoice = user.BitPay.CreateInvoice(new Invoice()
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
                var checkout = (Models.InvoicingModels.PaymentModel)((JsonResult)controller.GetStatus(invoice.Id, null).GetAwaiter().GetResult()).Value;
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
                invoice = user.BitPay.CreateInvoice(new Invoice()
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
                checkout = (Models.InvoicingModels.PaymentModel)((JsonResult)controller.GetStatus(invoice.Id, "LTC").GetAwaiter().GetResult()).Value;
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
                invoice = user.BitPay.CreateInvoice(new Invoice()
                {
                    Price = 5000.0m,
                    Currency = "USD",
                    PosData = "posData",
                    OrderId = "orderId",
                    ItemDesc = "Some description",
                    FullNotifications = true,
                    SupportedTransactionCurrencies = new Dictionary<string, InvoiceSupportedTransactionCurrency>()
                    {
                        { "BTC", new InvoiceSupportedTransactionCurrency() { Enabled = true } }
                    }
                }, Facade.Merchant);

                Assert.Single(invoice.CryptoInfo.Where(c => c.CryptoCode == "BTC"));
                Assert.Empty(invoice.CryptoInfo.Where(c => c.CryptoCode == "LTC"));
            }
        }

        [Fact]
        [Trait("Fast", "Fast")]
        public void CanParseCurrencyValue()
        {
            Assert.True(CurrencyValue.TryParse("1.50USD", out var result));
            Assert.Equal("1.50 USD", result.ToString());
            Assert.True(CurrencyValue.TryParse("1.50 USD", out result));
            Assert.Equal("1.50 USD", result.ToString());
            Assert.True(CurrencyValue.TryParse("1.50 usd", out result));
            Assert.Equal("1.50 USD", result.ToString());
            Assert.True(CurrencyValue.TryParse("1 usd", out result));
            Assert.Equal("1 USD", result.ToString());
            Assert.True(CurrencyValue.TryParse("1usd", out result));
            Assert.Equal("1 USD", result.ToString());
            Assert.True(CurrencyValue.TryParse("1.501 usd", out result));
            Assert.Equal("1.50 USD", result.ToString());
            Assert.False(CurrencyValue.TryParse("1.501 WTFF", out result));
            Assert.False(CurrencyValue.TryParse("1,501 usd", out result));
            Assert.False(CurrencyValue.TryParse("1.501", out result));
        }

        [Fact]
        [Trait("Fast", "Fast")]
        public void CanParseDerivationScheme()
        {
            var  testnetNetworkProvider = new BTCPayNetworkProvider(NetworkType.Testnet);
            var regtestNetworkProvider = new BTCPayNetworkProvider(NetworkType.Regtest);
            var mainnetNetworkProvider = new BTCPayNetworkProvider(NetworkType.Mainnet);
            var testnetParser = new DerivationSchemeParser(testnetNetworkProvider.GetNetwork<BTCPayNetwork>("BTC"));
            var mainnetParser = new DerivationSchemeParser(mainnetNetworkProvider.GetNetwork<BTCPayNetwork>("BTC"));
            NBXplorer.DerivationStrategy.DerivationStrategyBase result;
            //  Passing electrum stuff
            // Passing a native segwit from mainnet to a testnet parser, means the testnet parser will try to convert it into segwit
            result = testnetParser.Parse("zpub6nL6PUGurpU3DfPDSZaRS6WshpbNc9ctCFFzrCn54cssnheM31SZJZUcFHKtjJJNhAueMbh6ptFMfy1aeiMQJr3RJ4DDt1hAPx7sMTKV48t");
            Assert.Equal("tpubD93CJNkmGjLXnsBqE2zGDqfEh1Q8iJ8wueordy3SeWt1RngbbuxXCsqASuVWFywmfoCwUE1rSfNJbaH4cBNcbp8WcyZgPiiRSTazLGL8U9w", result.ToString());
            result = mainnetParser.Parse("zpub6nL6PUGurpU3DfPDSZaRS6WshpbNc9ctCFFzrCn54cssnheM31SZJZUcFHKtjJJNhAueMbh6ptFMfy1aeiMQJr3RJ4DDt1hAPx7sMTKV48t");
            Assert.Equal("xpub68fZn8w5ZTP5X4zymr1B1vKsMtJUiudtN2DZHQzJJc87gW1tXh7S4SALCsQijUzXstg2reVyuZYFuPnTDKXNiNgDZNpNiC4BrVzaaGEaRHj", result.ToString());
            // P2SH
            result = testnetParser.Parse("upub57Wa4MvRPNyAipy1MCpERxcFpHR2ZatyikppkyeWkoRL6QJvLVMo39jYdcaJVxyvBURyRVmErBEA5oGicKBgk1j72GAXSPFH5tUDoGZ8nEu");
            Assert.Equal("tpubD6NzVbkrYhZ4YWjDJUACG9E8fJx2NqNY1iynTiPKEjJrzzRKAgha3nNnwGXr2BtvCJKJHW4nmG7rRqc2AGGy2AECgt16seMyV2FZivUmaJg-[p2sh]", result.ToString());

            result = mainnetParser.Parse("ypub6QqdH2c5z79681jUgdxjGJzGW9zpL4ryPCuhtZE4GpvrJoZqM823XQN6iSQeVbbbp2uCRQ9UgpeMcwiyV6qjvxTWVcxDn2XEAnioMUwsrQ5");
            Assert.Equal("xpub661MyMwAqRbcGiYMrHB74DtmLBrNPSsUU6PV7ALAtpYyFhkc6TrUuLhxhET4VgwgQPnPfvYvEAHojf7QmQRj8imudHFoC7hju4f9xxri8wR-[p2sh]", result.ToString());

            // if prefix not recognize, assume it is segwit
            result = testnetParser.Parse("xpub661MyMwAqRbcGeVGU5e5KBcau1HHEUGf9Wr7k4FyLa8yRPNQrrVa7Ndrgg8Afbe2UYXMSL6tJBFd2JewwWASsePPLjkcJFL1tTVEs3UQ23X");
            Assert.Equal("tpubD6NzVbkrYhZ4YSg7vGdAX6wxE8NwDrmih9SR6cK7gUtsAg37w5LfFpJgviCxC6bGGT4G3uckqH5fiV9ZLN1gm5qgQLVuymzFUR5ed7U7ksu", result.ToString());
            ////////////////

            var tpub = "tpubD6NzVbkrYhZ4Wc65tjhmcKdWFauAo7bGLRTxvggygkNyp6SMGutJp7iociwsinU33jyNBp1J9j2hJH5yQsayfiS3LEU2ZqXodAcnaygra8o";

            result = testnetParser.Parse(tpub);
            Assert.Equal(tpub, result.ToString());
            testnetParser.HintScriptPubKey = BitcoinAddress.Create("tb1q4s33amqm8l7a07zdxcunqnn3gcsjcfz3xc573l", testnetParser.Network).ScriptPubKey;
            result = testnetParser.Parse(tpub);
            Assert.Equal(tpub, result.ToString());

            testnetParser.HintScriptPubKey = BitcoinAddress.Create("2N2humNio3YTApSfY6VztQ9hQwDnhDvaqFQ", testnetParser.Network).ScriptPubKey;
            result = testnetParser.Parse(tpub);
            Assert.Equal($"{tpub}-[p2sh]", result.ToString());

            testnetParser.HintScriptPubKey = BitcoinAddress.Create("mwD8bHS65cdgUf6rZUUSoVhi3wNQFu1Nfi", testnetParser.Network).ScriptPubKey;
            result = testnetParser.Parse(tpub);
            Assert.Equal($"{tpub}-[legacy]", result.ToString());

            testnetParser.HintScriptPubKey = BitcoinAddress.Create("2N2humNio3YTApSfY6VztQ9hQwDnhDvaqFQ", testnetParser.Network).ScriptPubKey;
            result = testnetParser.Parse($"{tpub}-[legacy]");
            Assert.Equal($"{tpub}-[p2sh]", result.ToString());

            result = testnetParser.Parse(tpub);
            Assert.Equal($"{tpub}-[p2sh]", result.ToString());

            var regtestParser = new DerivationSchemeParser(regtestNetworkProvider.GetNetwork<BTCPayNetwork>("BTC"));
            var parsed = regtestParser.Parse("xpub6DG1rMYXiQtCc6CfdLFD9CtxqhzzRh7j6Sq6EdE9abgYy3cfDRrniLLv2AdwqHL1exiLnnKR5XXcaoiiexf3Y9R6J6rxkJtqJHzNzMW9QMZ-[p2sh]");
            Assert.Equal("tpubDDdeNbNDRgqestPX5XEJM8ELAq6eR5cne5RPbBHHvWSSiLHNHehsrn1kGCijMnHFSsFFQMqHcdMfGzDL3pWHRasPMhcGRqZ4tFankQ3i4ok-[p2sh]", parsed.ToString());

            // Let's make sure we can't generate segwit with dogecoin
            regtestParser = new DerivationSchemeParser(regtestNetworkProvider.GetNetwork<BTCPayNetwork>("DOGE"));
            parsed = regtestParser.Parse("xpub6DG1rMYXiQtCc6CfdLFD9CtxqhzzRh7j6Sq6EdE9abgYy3cfDRrniLLv2AdwqHL1exiLnnKR5XXcaoiiexf3Y9R6J6rxkJtqJHzNzMW9QMZ-[p2sh]");
            Assert.Equal("tpubDDdeNbNDRgqestPX5XEJM8ELAq6eR5cne5RPbBHHvWSSiLHNHehsrn1kGCijMnHFSsFFQMqHcdMfGzDL3pWHRasPMhcGRqZ4tFankQ3i4ok-[legacy]", parsed.ToString());

            regtestParser = new DerivationSchemeParser(regtestNetworkProvider.GetNetwork<BTCPayNetwork>("DOGE"));
            parsed = regtestParser.Parse("tpubDDdeNbNDRgqestPX5XEJM8ELAq6eR5cne5RPbBHHvWSSiLHNHehsrn1kGCijMnHFSsFFQMqHcdMfGzDL3pWHRasPMhcGRqZ4tFankQ3i4ok-[p2sh]");
            Assert.Equal("tpubDDdeNbNDRgqestPX5XEJM8ELAq6eR5cne5RPbBHHvWSSiLHNHehsrn1kGCijMnHFSsFFQMqHcdMfGzDL3pWHRasPMhcGRqZ4tFankQ3i4ok-[legacy]", parsed.ToString());
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
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");
                user.RegisterDerivationScheme("LTC");
                user.RegisterLightningNode("BTC", LightningConnectionType.CLightning);
                var btcNetwork = tester.PayTester.Networks.GetNetwork<BTCPayNetwork>("BTC");
                var invoice = user.BitPay.CreateInvoice(new Invoice()
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
                var lightningVM = (LightningNodeViewModel)Assert.IsType<ViewResult>(controller.AddLightningNode(user.StoreId, "BTC")).Model;
                Assert.True(lightningVM.Enabled);
                lightningVM.Enabled = false;
                controller.AddLightningNode(user.StoreId, lightningVM, "save", "BTC").GetAwaiter().GetResult();
                lightningVM = (LightningNodeViewModel)Assert.IsType<ViewResult>(controller.AddLightningNode(user.StoreId, "BTC")).Model;
                Assert.False(lightningVM.Enabled);

                // Only Enabling/Disabling the payment method must redirect to store page
                var derivationVM = (DerivationSchemeViewModel)Assert.IsType<ViewResult>(await controller.AddDerivationScheme(user.StoreId, "BTC")).Model;
                Assert.True(derivationVM.Enabled);
                derivationVM.Enabled = false;
                Assert.IsType<RedirectToActionResult>(controller.AddDerivationScheme(user.StoreId, derivationVM, "BTC").GetAwaiter().GetResult());
                derivationVM = (DerivationSchemeViewModel)Assert.IsType<ViewResult>(await controller.AddDerivationScheme(user.StoreId, "BTC")).Model;
                Assert.False(derivationVM.Enabled);

                // Clicking next without changing anything should send to the confirmation screen
                derivationVM = (DerivationSchemeViewModel)Assert.IsType<ViewResult>(await controller.AddDerivationScheme(user.StoreId, "BTC")).Model;
                derivationVM = (DerivationSchemeViewModel)Assert.IsType<ViewResult>(controller.AddDerivationScheme(user.StoreId, derivationVM, "BTC").GetAwaiter().GetResult()).Model;
                Assert.True(derivationVM.Confirmation);

                invoice = user.BitPay.CreateInvoice(new Invoice()
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
                derivationVM = (DerivationSchemeViewModel)Assert.IsType<ViewResult>(await controller.AddDerivationScheme(user.StoreId, "BTC")).Model;
                derivationVM.DerivationScheme = null;
                Assert.IsType<RedirectToActionResult>(controller.AddDerivationScheme(user.StoreId, derivationVM, "BTC").GetAwaiter().GetResult());

                // Setting it again should redirect to the confirmation page
                derivationVM = (DerivationSchemeViewModel)Assert.IsType<ViewResult>(await controller.AddDerivationScheme(user.StoreId, "BTC")).Model;
                derivationVM.DerivationScheme = oldScheme;
                derivationVM = (DerivationSchemeViewModel)Assert.IsType<ViewResult>(controller.AddDerivationScheme(user.StoreId, derivationVM, "BTC").GetAwaiter().GetResult()).Model;
                Assert.True(derivationVM.Confirmation);

                // Can we upload coldcard settings? (Should fail, we are giving a mainnet file to a testnet network)
                derivationVM = (DerivationSchemeViewModel)Assert.IsType<ViewResult>(await controller.AddDerivationScheme(user.StoreId, "BTC")).Model;
                string content = "{\"keystore\": {\"ckcc_xpub\": \"xpub661MyMwAqRbcGVBsTGeNZN6QGVHmMHLdSA4FteGsRrEriu4pnVZMZWnruFFFXkMnyoBjyHndD3Qwcfz4MPzBUxjSevweNFQx7SAYZATtcDw\", \"xpub\": \"ypub6WWc2gWwHbdnAAyJDnR4SPL1phRh7REqrPBfZeizaQ1EmTshieRXJC3Z5YoU4wkcdKHEjQGkh6AYEzCQC1Kz3DNaWSwdc1pc8416hAjzqyD\", \"label\": \"Coldcard Import 0x60d1af8b\", \"ckcc_xfp\": 1624354699, \"type\": \"hardware\", \"hw_type\": \"coldcard\", \"derivation\": \"m/49'/0'/0'\"}, \"wallet_type\": \"standard\", \"use_encryption\": false, \"seed_version\": 17}";
                derivationVM.ColdcardPublicFile = TestUtils.GetFormFile("wallet.json", content);
                derivationVM = (DerivationSchemeViewModel)Assert.IsType<ViewResult>(controller.AddDerivationScheme(user.StoreId, derivationVM, "BTC").GetAwaiter().GetResult()).Model;
                Assert.False(derivationVM.Confirmation); // Should fail, we are giving a mainnet file to a testnet network 

                // And with a good file? (upub)
                content = "{\"keystore\": {\"ckcc_xpub\": \"tpubD6NzVbkrYhZ4YHNiuTdTmHRmbcPRLfqgyneZFCL1mkzkUBjXriQShxTh9HL34FK2mhieasJVk9EzJrUfkFqRNQBjiXgx3n5BhPkxKBoFmaS\", \"xpub\": \"upub5DBYp1qGgsTrkzCptMGZc2x18pquLwGrBw6nS59T4NViZ4cni1mGowQzziy85K8vzkp1jVtWrSkLhqk9KDfvrGeB369wGNYf39kX8rQfiLn\", \"label\": \"Coldcard Import 0x60d1af8b\", \"ckcc_xfp\": 1624354699, \"type\": \"hardware\", \"hw_type\": \"coldcard\", \"derivation\": \"m/49'/0'/0'\"}, \"wallet_type\": \"standard\", \"use_encryption\": false, \"seed_version\": 17}";
                derivationVM = (DerivationSchemeViewModel)Assert.IsType<ViewResult>(await controller.AddDerivationScheme(user.StoreId, "BTC")).Model;
                derivationVM.ColdcardPublicFile = TestUtils.GetFormFile("wallet2.json", content);
                derivationVM.Enabled = true;
                derivationVM = (DerivationSchemeViewModel)Assert.IsType<ViewResult>(controller.AddDerivationScheme(user.StoreId, derivationVM, "BTC").GetAwaiter().GetResult()).Model;
                Assert.True(derivationVM.Confirmation);
                Assert.IsType<RedirectToActionResult>(controller.AddDerivationScheme(user.StoreId, derivationVM, "BTC").GetAwaiter().GetResult());

                // Now let's check that no data has been lost in the process
                var store = tester.PayTester.StoreRepository.FindStore(user.StoreId).GetAwaiter().GetResult();
                var onchainBTC = store.GetSupportedPaymentMethods(tester.PayTester.Networks).OfType<DerivationSchemeSettings>().First(o => o.PaymentId.IsBTCOnChain);
                DerivationSchemeSettings.TryParseFromColdcard(content, onchainBTC.Network, out var expected);
                Assert.Equal(expected.ToJson(), onchainBTC.ToJson());

                // Let's check that the root hdkey and account key path are taken into account when making a PSBT
                invoice = user.BitPay.CreateInvoice(new Invoice()
                {
                    Price = 1.5m,
                    Currency = "USD",
                    PosData = "posData",
                    OrderId = "orderId",
                    ItemDesc = "Some description",
                    FullNotifications = true
                }, Facade.Merchant);

                tester.ExplorerNode.Generate(1);
                var invoiceAddress = BitcoinAddress.Create(invoice.CryptoInfo.First(c => c.CryptoCode == "BTC").Address, tester.ExplorerNode.Network);
                tester.ExplorerNode.SendToAddress(invoiceAddress, Money.Coins(1m));
                TestUtils.Eventually(() =>
                {
                    invoice = user.BitPay.GetInvoice(invoice.Id);
                    Assert.Equal("paid", invoice.Status);
                });
                var wallet = tester.PayTester.GetController<WalletsController>();
                var psbt = wallet.CreatePSBT(btcNetwork, onchainBTC, new WalletSendModel()
                {
                   Outputs = new List<WalletSendModel.TransactionOutput>()
                   {
                       new WalletSendModel.TransactionOutput()
                       {
                           Amount = 0.5m,
                           DestinationAddress = new Key().PubKey.GetAddress(btcNetwork.NBitcoinNetwork).ToString(),
                       }
                   },
                    FeeSatoshiPerByte = 1
                }, default).GetAwaiter().GetResult();

                Assert.NotNull(psbt);

                var root = new Mnemonic("usage fever hen zero slide mammal silent heavy donate budget pulse say brain thank sausage brand craft about save attract muffin advance illegal cabbage").DeriveExtKey().AsHDKeyCache();
                var account = root.Derive(new KeyPath("m/49'/0'/0'"));
                Assert.All(psbt.PSBT.Inputs, input =>
                {
                    var keyPath = input.HDKeyPaths.Single();
                    Assert.False(keyPath.Value.KeyPath.IsHardened);
                    Assert.Equal(account.Derive(keyPath.Value.KeyPath).GetPublicKey(), keyPath.Key);
                    Assert.Equal(keyPath.Value.MasterFingerprint, onchainBTC.AccountKeySettings[0].AccountKey.GetPublicKey().GetHDFingerPrint());
                });
            }
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async Task CanSetPaymentMethodLimits()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");
                var vm = Assert.IsType<CheckoutExperienceViewModel>(Assert.IsType<ViewResult>(user.GetController<StoresController>().CheckoutExperience()).Model);
                vm.OnChainMinValue = "5 USD";
                Assert.IsType<RedirectToActionResult>(user.GetController<StoresController>().CheckoutExperience(vm).Result);

                var invoice = user.BitPay.CreateInvoice(new Invoice()
                {
                    Price = 5.5m,
                    Currency = "USD",
                    PosData = "posData",
                    OrderId = "orderId",
                    ItemDesc = "Some description",
                    FullNotifications = true
                }, Facade.Merchant);

                Assert.Single(invoice.CryptoInfo);
                Assert.Equal(PaymentTypes.BTCLike.ToString(), invoice.CryptoInfo[0].PaymentType);
            }
        }

        [Fact(Timeout = 60 * 2 * 1000)]
        [Trait("Integration", "Integration")]
        [Trait("Lightning", "Lightning")]
        public async Task CanSetPaymentMethodLimitsLightning()
        {
            using (var tester = ServerTester.Create())
            {
                tester.ActivateLightning();
                await tester.StartAsync();
                await tester.EnsureChannelsSetup();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterLightningNode("BTC", LightningConnectionType.Charge);
                var vm = Assert.IsType<CheckoutExperienceViewModel>(Assert.IsType<ViewResult>(user.GetController<StoresController>().CheckoutExperience()).Model);
                vm.LightningMaxValue = "2 USD";
                Assert.IsType<RedirectToActionResult>(user.GetController<StoresController>().CheckoutExperience(vm).Result);

                var invoice = user.BitPay.CreateInvoice(new Invoice()
                {
                    Price = 1.5m,
                    Currency = "USD",
                    PosData = "posData",
                    OrderId = "orderId",
                    ItemDesc = "Some description",
                    FullNotifications = true
                }, Facade.Merchant);

                Assert.Single(invoice.CryptoInfo);
                Assert.Equal(PaymentTypes.LightningLike.ToString(), invoice.CryptoInfo[0].PaymentType);
            }
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async Task CanUsePoSApp()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");
                var apps = user.GetController<AppsController>();
                var vm = Assert.IsType<CreateAppViewModel>(Assert.IsType<ViewResult>(apps.CreateApp().Result).Model);
                vm.Name = "test";
                vm.SelectedAppType = AppType.PointOfSale.ToString();
                Assert.IsType<RedirectToActionResult>(apps.CreateApp(vm).Result);
                var appId = Assert.IsType<ListAppsViewModel>(Assert.IsType<ViewResult>(apps.ListApps().Result).Model).Apps[0].Id;
                var vmpos = Assert.IsType<UpdatePointOfSaleViewModel>(Assert.IsType<ViewResult>(apps.UpdatePointOfSale(appId).Result).Model);
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
                vmpos = Assert.IsType<UpdatePointOfSaleViewModel>(Assert.IsType<ViewResult>(apps.UpdatePointOfSale(appId).Result).Model);
                Assert.Equal("hello", vmpos.Title);

                var publicApps = user.GetController<AppsPublicController>();
                var vmview = Assert.IsType<ViewPointOfSaleViewModel>(Assert.IsType<ViewResult>(publicApps.ViewPointOfSale(appId).Result).Model);
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
                Assert.IsType<RedirectToActionResult>(publicApps.ViewPointOfSale(appId, 0, null, null, null, null, "orange").Result);

                //
                var invoices = user.BitPay.GetInvoices();
                var orangeInvoice = invoices.First();
                Assert.Equal(10.00m, orangeInvoice.Price);
                Assert.Equal("CAD", orangeInvoice.Currency);
                Assert.Equal("orange", orangeInvoice.ItemDesc);
                
                
                Assert.IsType<RedirectToActionResult>(publicApps.ViewPointOfSale(appId, 0, null, null, null, null, "apple").Result);

                invoices = user.BitPay.GetInvoices();
                var appleInvoice = invoices.SingleOrDefault(invoice => invoice.ItemCode.Equals("apple"));
                Assert.NotNull(appleInvoice);
                Assert.Equal("good apple", appleInvoice.ItemDesc);
                

                // testing custom amount
                var action = Assert.IsType<RedirectToActionResult>(publicApps.ViewPointOfSale(appId, 6.6m, null, null, null, null, "donation").Result);
                Assert.Equal(nameof(InvoiceController.Checkout), action.ActionName);
                invoices = user.BitPay.GetInvoices();
                var donationInvoice = invoices.Single(i => i.Price == 6.6m);
                Assert.NotNull(donationInvoice);
                Assert.Equal("CAD", donationInvoice.Currency);
                Assert.Equal("donation", donationInvoice.ItemDesc);

                foreach (var test in new[]
                {
                    (Code: "EUR", ExpectedSymbol: "€", ExpectedDecimalSeparator: ",", ExpectedDivisibility: 2, ExpectedThousandSeparator: "\xa0", ExpectedPrefixed: false, ExpectedSymbolSpace: true),
                    (Code: "INR", ExpectedSymbol: "₹", ExpectedDecimalSeparator: ".", ExpectedDivisibility: 2, ExpectedThousandSeparator: ",", ExpectedPrefixed: true, ExpectedSymbolSpace: true),
                    (Code: "JPY", ExpectedSymbol: "¥", ExpectedDecimalSeparator: ".", ExpectedDivisibility: 0, ExpectedThousandSeparator: ",", ExpectedPrefixed: true, ExpectedSymbolSpace: false),
                    (Code: "BTC", ExpectedSymbol: "BTC", ExpectedDecimalSeparator: ".", ExpectedDivisibility: 8, ExpectedThousandSeparator: ",", ExpectedPrefixed: false, ExpectedSymbolSpace: true),
                })
                {
                    Logs.Tester.LogInformation($"Testing for {test.Code}");
                    vmpos = Assert.IsType<UpdatePointOfSaleViewModel>(Assert.IsType<ViewResult>(apps.UpdatePointOfSale(appId).Result).Model);
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
                    vmview = Assert.IsType<ViewPointOfSaleViewModel>(Assert.IsType<ViewResult>(publicApps.ViewPointOfSale(appId).Result).Model);
                    Assert.Equal(test.Code, vmview.CurrencyCode);
                    Assert.Equal(test.ExpectedSymbol, vmview.CurrencySymbol.Replace("￥", "¥")); // Hack so JPY test pass on linux as well);
                    Assert.Equal(test.ExpectedSymbol, vmview.CurrencyInfo.CurrencySymbol.Replace("￥", "¥")); // Hack so JPY test pass on linux as well);
                    Assert.Equal(test.ExpectedDecimalSeparator, vmview.CurrencyInfo.DecimalSeparator);
                    Assert.Equal(test.ExpectedThousandSeparator, vmview.CurrencyInfo.ThousandSeparator);
                    Assert.Equal(test.ExpectedPrefixed, vmview.CurrencyInfo.Prefixed);
                    Assert.Equal(test.ExpectedDivisibility, vmview.CurrencyInfo.Divisibility);
                    Assert.Equal(test.ExpectedSymbolSpace, vmview.CurrencyInfo.SymbolSpace);
                }
                
                
                //test inventory related features
                vmpos = Assert.IsType<UpdatePointOfSaleViewModel>(Assert.IsType<ViewResult>(apps.UpdatePointOfSale(appId).Result).Model);
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
                Assert.IsType<RedirectToActionResult>(publicApps.ViewPointOfSale(appId, 1, null, null, null, null, "inventoryitem").Result);
                //we already bought all available stock so this should fail
                await Task.Delay(100);
                Assert.IsType<RedirectToActionResult>(publicApps.ViewPointOfSale(appId, 1, null, null, null, null, "inventoryitem").Result);
                
                //inventoryitem has unlimited items available
                Assert.IsType<RedirectToActionResult>(publicApps.ViewPointOfSale(appId, 1, null, null, null, null, "noninventoryitem").Result);
                Assert.IsType<RedirectToActionResult>(publicApps.ViewPointOfSale(appId, 1, null, null, null, null, "noninventoryitem").Result);

                //verify invoices where created
                invoices = user.BitPay.GetInvoices();
                Assert.Equal(2, invoices.Count(invoice => invoice.ItemCode.Equals("noninventoryitem")));
                var inventoryItemInvoice = Assert.Single(invoices.Where(invoice => invoice.ItemCode.Equals("inventoryitem")));
                Assert.NotNull(inventoryItemInvoice);
                
                //let's mark the inventoryitem invoice as invalid, thsi should return the item to back in stock
                var controller = tester.PayTester.GetController<InvoiceController>(user.UserId, user.StoreId);
                var appService = tester.PayTester.GetService<AppService>();
                var eventAggregator = tester.PayTester.GetService<EventAggregator>();
                Assert.IsType<JsonResult>( await controller.ChangeInvoiceState(inventoryItemInvoice.Id, "invalid"));
                //check that item is back in stock
                TestUtils.Eventually(() =>
                {
                    vmpos = Assert.IsType<UpdatePointOfSaleViewModel>(Assert.IsType<ViewResult>(apps.UpdatePointOfSale(appId).Result).Model);
                    Assert.Equal(1, appService.Parse(vmpos.Template, "BTC").Single(item => item.Id == "inventoryitem").Inventory);
                }, 10000);
                
            }
        }


        [Fact]
        [Trait("Fast", "Fast")]
        public async Task CanScheduleBackgroundTasks()
        {
            BackgroundJobClient client = new BackgroundJobClient();
            MockDelay mockDelay = new MockDelay();
            client.Delay = mockDelay;
            bool[] jobs = new bool[4];
            Logs.Tester.LogInformation("Start Job[0] in 5 sec");
            client.Schedule((_) => { Logs.Tester.LogInformation("Job[0]"); jobs[0] = true; return Task.CompletedTask; }, TimeSpan.FromSeconds(5.0));
            Logs.Tester.LogInformation("Start Job[1] in 2 sec");
            client.Schedule((_) => { Logs.Tester.LogInformation("Job[1]"); jobs[1] = true; return Task.CompletedTask; }, TimeSpan.FromSeconds(2.0));
            Logs.Tester.LogInformation("Start Job[2] fails in 6 sec");
            client.Schedule((_) => { jobs[2] = true; throw new Exception("Job[2]"); }, TimeSpan.FromSeconds(6.0));
            Logs.Tester.LogInformation("Start Job[3] starts in in 7 sec");
            client.Schedule((_) => { Logs.Tester.LogInformation("Job[3]"); jobs[3] = true; return Task.CompletedTask; }, TimeSpan.FromSeconds(7.0));

            Assert.True(new[] { false, false, false, false }.SequenceEqual(jobs));
            CancellationTokenSource cts = new CancellationTokenSource();
            var processing = client.ProcessJobs(cts.Token);

            Assert.Equal(4, client.GetExecutingCount());

            var waitJobsFinish = client.WaitAllRunning(default);

            await mockDelay.Advance(TimeSpan.FromSeconds(2.0));
            Assert.True(new[] { false, true, false, false }.SequenceEqual(jobs));

            await mockDelay.Advance(TimeSpan.FromSeconds(3.0));
            Assert.True(new[] { true, true, false, false }.SequenceEqual(jobs));

            await mockDelay.Advance(TimeSpan.FromSeconds(1.0));
            Assert.True(new[] { true, true, true, false }.SequenceEqual(jobs));
            Assert.Equal(1, client.GetExecutingCount());

            Assert.False(waitJobsFinish.Wait(1));
            Assert.False(waitJobsFinish.IsCompletedSuccessfully);

            await mockDelay.Advance(TimeSpan.FromSeconds(1.0));
            Assert.True(new[] { true, true, true, true }.SequenceEqual(jobs));

            await waitJobsFinish;
            Assert.True(waitJobsFinish.IsCompletedSuccessfully);
            Assert.True(!waitJobsFinish.IsFaulted);
            Assert.Equal(0, client.GetExecutingCount());

            bool jobExecuted = false;
            Logs.Tester.LogInformation("This job will be cancelled");
            client.Schedule((_) => { jobExecuted = true; return Task.CompletedTask; }, TimeSpan.FromSeconds(1.0));
            await mockDelay.Advance(TimeSpan.FromSeconds(0.5));
            Assert.False(jobExecuted);
            TestUtils.Eventually(() => Assert.Equal(1, client.GetExecutingCount()));


            waitJobsFinish = client.WaitAllRunning(default);
            Assert.False(waitJobsFinish.Wait(100));
            cts.Cancel();
            await waitJobsFinish;
            Assert.True(waitJobsFinish.Wait(1));
            Assert.True(waitJobsFinish.IsCompletedSuccessfully);
            Assert.False(waitJobsFinish.IsFaulted);
            Assert.False(jobExecuted);

            await mockDelay.Advance(TimeSpan.FromSeconds(1.0));

            Assert.False(jobExecuted);
            Assert.Equal(0, client.GetExecutingCount());

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await processing);
            Assert.True(processing.IsCanceled);
            Assert.True(client.WaitAllRunning(default).Wait(100));
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Fast", "Fast")]
        public void PosDataParser_ParsesCorrectly()
        {
            var testCases =
                new List<(string input, Dictionary<string, object> expectedOutput)>()
                {
                    { (null, new Dictionary<string, object>())},
                    {("", new Dictionary<string, object>())},
                    {("{}", new Dictionary<string, object>())},
                    {("non-json-content", new Dictionary<string, object>(){ {string.Empty, "non-json-content"}})},
                    {("[1,2,3]", new Dictionary<string, object>(){ {string.Empty, "[1,2,3]"}})},
                    {("{ \"key\": \"value\"}", new Dictionary<string, object>(){ {"key", "value"}})},
                    {("{ \"key\": true}", new Dictionary<string, object>(){ {"key", "True"}})},
                    {("{ invalidjson file here}", new Dictionary<string, object>(){ {String.Empty, "{ invalidjson file here}"}})}
                };

            testCases.ForEach(tuple =>
            {
                Assert.Equal(tuple.expectedOutput, InvoiceController.PosDataParser.ParsePosData(tuple.input));
            });
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task PosDataParser_ParsesCorrectly_Slower()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");

                var controller = tester.PayTester.GetController<InvoiceController>(null);

                var testCases =
                    new List<(string input, Dictionary<string, object> expectedOutput)>()
                    {
                        { (null, new Dictionary<string, object>())},
                        {("", new Dictionary<string, object>())},
                        {("{}", new Dictionary<string, object>())},
                        {("non-json-content", new Dictionary<string, object>(){ {string.Empty, "non-json-content"}})},
                        {("[1,2,3]", new Dictionary<string, object>(){ {string.Empty, "[1,2,3]"}})},
                        {("{ \"key\": \"value\"}", new Dictionary<string, object>(){ {"key", "value"}})},
                        {("{ \"key\": true}", new Dictionary<string, object>(){ {"key", "True"}})},
                        {("{ invalidjson file here}", new Dictionary<string, object>(){ {String.Empty, "{ invalidjson file here}"}})}
                    };

                var tasks = new List<Task>();
                foreach (var valueTuple in testCases)
                {
                    tasks.Add(user.BitPay.CreateInvoiceAsync(new Invoice(1, "BTC")
                    {
                        PosData = valueTuple.input
                    }).ContinueWith(async task =>
                    {
                        var result = await controller.Invoice(task.Result.Id);
                        var viewModel =
                            Assert.IsType<InvoiceDetailsModel>(
                                Assert.IsType<ViewResult>(result).Model);
                        Assert.Equal(valueTuple.expectedOutput, viewModel.PosData);
                    }));
                }

                await Task.WhenAll(tasks);
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanExportInvoicesJson()
        {
            decimal GetFieldValue(string input, string fieldName)
            {
                var match = Regex.Match(input, $"\"{fieldName}\":([^,]*)");
                Assert.True(match.Success);
                return decimal.Parse(match.Groups[1].Value.Trim(), CultureInfo.InvariantCulture);
            }
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");
                user.SetNetworkFeeMode(NetworkFeeMode.Always);
                var invoice = user.BitPay.CreateInvoice(new Invoice()
                {
                    Price = 10,
                    Currency = "USD",
                    PosData = "posData",
                    OrderId = "orderId",
                    ItemDesc = "Some \", description",
                    FullNotifications = true
                }, Facade.Merchant);

                var networkFee = new FeeRate(invoice.MinerFees["BTC"].SatoshiPerBytes).GetFee(100);
                // ensure 0 invoices exported because there are no payments yet
                var jsonResult = user.GetController<InvoiceController>().Export("json").GetAwaiter().GetResult();
                var result = Assert.IsType<ContentResult>(jsonResult);
                Assert.Equal("application/json", result.ContentType);
                Assert.Equal("[]", result.Content);

                var cashCow = tester.ExplorerNode;
                var invoiceAddress = BitcoinAddress.Create(invoice.CryptoInfo[0].Address, cashCow.Network);
                // 
                var firstPayment = invoice.CryptoInfo[0].TotalDue - 3 * networkFee;
                cashCow.SendToAddress(invoiceAddress, firstPayment);
                Thread.Sleep(1000); // prevent race conditions, ordering payments
                // look if you can reduce thread sleep, this was min value for me

                // should reduce invoice due by 0 USD because payment = network fee
                cashCow.SendToAddress(invoiceAddress, networkFee);
                Thread.Sleep(1000);

                // pay remaining amount
                cashCow.SendToAddress(invoiceAddress, 4 * networkFee);
                Thread.Sleep(1000);

                TestUtils.Eventually(() =>
                {
                    var jsonResultPaid = user.GetController<InvoiceController>().Export("json").GetAwaiter().GetResult();
                    var paidresult = Assert.IsType<ContentResult>(jsonResultPaid);
                    Assert.Equal("application/json", paidresult.ContentType);

                    var parsedJson = JsonConvert.DeserializeObject<object[]>(paidresult.Content);
                    Assert.Equal(3, parsedJson.Length);

                    var invoiceDueAfterFirstPayment = (3 * networkFee).ToDecimal(MoneyUnit.BTC) * invoice.Rate;
                    var pay1str = parsedJson[0].ToString();
                    Assert.Contains("\"InvoiceItemDesc\": \"Some \\\", description\"", pay1str);
                    Assert.Equal(invoiceDueAfterFirstPayment, GetFieldValue(pay1str, "InvoiceDue"));
                    Assert.Contains("\"InvoicePrice\": 10.0", pay1str);
                    Assert.Contains("\"ConversionRate\": 5000.0", pay1str);
                    Assert.Contains($"\"InvoiceId\": \"{invoice.Id}\",", pay1str);

                    var pay2str = parsedJson[1].ToString();
                    Assert.Equal(invoiceDueAfterFirstPayment, GetFieldValue(pay2str, "InvoiceDue"));

                    var pay3str = parsedJson[2].ToString();
                    Assert.Contains("\"InvoiceDue\": 0", pay3str);
                });
            }
        }
        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanChangeNetworkFeeMode()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");
                foreach (var networkFeeMode in Enum.GetValues(typeof(NetworkFeeMode)).Cast<NetworkFeeMode>())
                {
                    Logs.Tester.LogInformation($"Trying with {nameof(networkFeeMode)}={networkFeeMode}");
                    user.SetNetworkFeeMode(networkFeeMode);
                    var invoice = user.BitPay.CreateInvoice(new Invoice()
                    {
                        Price = 10,
                        Currency = "USD",
                        PosData = "posData",
                        OrderId = "orderId",
                        ItemDesc = "Some \", description",
                        FullNotifications = true
                    }, Facade.Merchant);

                    var networkFee = Money.Satoshis(10000).ToDecimal(MoneyUnit.BTC);
                    var missingMoney = Money.Satoshis(5000).ToDecimal(MoneyUnit.BTC);
                    var cashCow = tester.ExplorerNode;
                    var invoiceAddress = BitcoinAddress.Create(invoice.CryptoInfo[0].Address, cashCow.Network);

                    // Check that for the first payment, no network fee are included
                    var due = Money.Parse(invoice.CryptoInfo[0].Due);
                    var productPartDue = (invoice.Price / invoice.Rate);
                    switch (networkFeeMode)
                    {
                        case NetworkFeeMode.MultiplePaymentsOnly:
                        case NetworkFeeMode.Never:
                            Assert.Equal(productPartDue, due.ToDecimal(MoneyUnit.BTC));
                            break;
                        case NetworkFeeMode.Always:
                            Assert.Equal(productPartDue + networkFee, due.ToDecimal(MoneyUnit.BTC));
                            break;
                        default:
                            throw new NotSupportedException(networkFeeMode.ToString());
                    }
                    var firstPayment = productPartDue - missingMoney;
                    cashCow.SendToAddress(invoiceAddress, Money.Coins(firstPayment));

                        TestUtils.Eventually(() =>
                        {
                        invoice = user.BitPay.GetInvoice(invoice.Id);
                        // Check that for the second payment, network fee are included
                        due = Money.Parse(invoice.CryptoInfo[0].Due);
                        Assert.Equal(Money.Coins(firstPayment), Money.Parse(invoice.CryptoInfo[0].Paid));
                        switch (networkFeeMode)
                        {
                            case NetworkFeeMode.MultiplePaymentsOnly:
                                Assert.Equal(missingMoney + networkFee, due.ToDecimal(MoneyUnit.BTC));
                                Assert.Equal(firstPayment + missingMoney + networkFee, Money.Parse(invoice.CryptoInfo[0].TotalDue).ToDecimal(MoneyUnit.BTC));
                                break;
                            case NetworkFeeMode.Always:
                                Assert.Equal(missingMoney + 2 * networkFee, due.ToDecimal(MoneyUnit.BTC));
                                Assert.Equal(firstPayment + missingMoney + 2 * networkFee, Money.Parse(invoice.CryptoInfo[0].TotalDue).ToDecimal(MoneyUnit.BTC));
                                break;
                            case NetworkFeeMode.Never:
                                Assert.Equal(missingMoney, due.ToDecimal(MoneyUnit.BTC));
                                Assert.Equal(firstPayment + missingMoney, Money.Parse(invoice.CryptoInfo[0].TotalDue).ToDecimal(MoneyUnit.BTC));
                                break;
                            default:
                                throw new NotSupportedException(networkFeeMode.ToString());
                        }
                    });
                    cashCow.SendToAddress(invoiceAddress, due);
                    TestUtils.Eventually(() =>
                    {
                        invoice = user.BitPay.GetInvoice(invoice.Id);
                        Assert.Equal("paid", invoice.Status);
                    });
                }
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanExportInvoicesCsv()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");
                user.SetNetworkFeeMode(NetworkFeeMode.Always);
                var invoice = user.BitPay.CreateInvoice(new Invoice()
                {
                    Price = 500,
                    Currency = "USD",
                    PosData = "posData",
                    OrderId = "orderId",
                    ItemDesc = "Some \", description",
                    FullNotifications = true
                }, Facade.Merchant);

                var cashCow = tester.ExplorerNode;
                var invoiceAddress = BitcoinAddress.Create(invoice.CryptoInfo[0].Address, cashCow.Network);
                var firstPayment = invoice.CryptoInfo[0].TotalDue - Money.Coins(0.001m);
                cashCow.SendToAddress(invoiceAddress, firstPayment);
                TestUtils.Eventually(() =>
                {
                    var exportResultPaid = user.GetController<InvoiceController>().Export("csv").GetAwaiter().GetResult();
                    var paidresult = Assert.IsType<ContentResult>(exportResultPaid);
                    Assert.Equal("application/csv", paidresult.ContentType);
                    Assert.Contains($",\"orderId\",\"{invoice.Id}\",", paidresult.Content);
                    Assert.Contains($",\"On-Chain\",\"BTC\",\"0.0991\",\"0.0001\",\"5000.0\"", paidresult.Content);
                    Assert.Contains($",\"USD\",\"5.00", paidresult.Content); // Seems hacky but some plateform does not render this decimal the same
                    Assert.Contains($"0\",\"500.0\",\"\",\"Some ``, description\",\"new (paidPartial)\"", paidresult.Content);
                });
            }
        }



        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanCreateAndDeleteApps()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                var user2 = tester.NewAccount();
                user2.GrantAccess();
                var apps = user.GetController<AppsController>();
                var apps2 = user2.GetController<AppsController>();
                var vm = Assert.IsType<CreateAppViewModel>(Assert.IsType<ViewResult>(apps.CreateApp().Result).Model);
                Assert.NotNull(vm.SelectedAppType);
                Assert.Null(vm.Name);
                vm.Name = "test";
                vm.SelectedAppType = AppType.PointOfSale.ToString();
                var redirectToAction = Assert.IsType<RedirectToActionResult>(apps.CreateApp(vm).Result);
                Assert.Equal(nameof(apps.UpdatePointOfSale), redirectToAction.ActionName);
                var appList = Assert.IsType<ListAppsViewModel>(Assert.IsType<ViewResult>(apps.ListApps().Result).Model);
                var appList2 = Assert.IsType<ListAppsViewModel>(Assert.IsType<ViewResult>(apps2.ListApps().Result).Model);
                Assert.Single(appList.Apps);
                Assert.Empty(appList2.Apps);
                Assert.Equal("test", appList.Apps[0].AppName);
                Assert.Equal(apps.CreatedAppId, appList.Apps[0].Id);
                Assert.True(appList.Apps[0].IsOwner);
                Assert.Equal(user.StoreId, appList.Apps[0].StoreId);
                Assert.IsType<NotFoundResult>(apps2.DeleteApp(appList.Apps[0].Id).Result);
                Assert.IsType<ViewResult>(apps.DeleteApp(appList.Apps[0].Id).Result);
                redirectToAction = Assert.IsType<RedirectToActionResult>(apps.DeleteAppPost(appList.Apps[0].Id).Result);
                Assert.Equal(nameof(apps.ListApps), redirectToAction.ActionName);
                appList = Assert.IsType<ListAppsViewModel>(Assert.IsType<ViewResult>(apps.ListApps().Result).Model);
                Assert.Empty(appList.Apps);
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanCreateStrangeInvoice()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");
                var invoice1 = user.BitPay.CreateInvoice(new Invoice()
                {
                    Price = 0.000000012m,
                    Currency = "USD",
                    FullNotifications = true
                }, Facade.Merchant);
                var invoice2 = user.BitPay.CreateInvoice(new Invoice()
                {
                    Price = 0.000000019m,
                    Currency = "USD"
                }, Facade.Merchant);
                Assert.Equal(0.000000012m, invoice1.Price);
                Assert.Equal(0.000000019m, invoice2.Price);

                // Should round up to 1 because 0.000000019 is unsignificant
                var invoice3 = user.BitPay.CreateInvoice(new Invoice()
                {
                    Price = 1.000000019m,
                    Currency = "USD",
                    FullNotifications = true
                }, Facade.Merchant);
                Assert.Equal(1m, invoice3.Price);

                // Should not round up at 8 digit because the 9th is insignificant
                var invoice4 = user.BitPay.CreateInvoice(new Invoice()
                {
                    Price = 1.000000019m,
                    Currency = "BTC",
                    FullNotifications = true
                }, Facade.Merchant);
                Assert.Equal(1.00000002m, invoice4.Price);

                // But not if the 9th is insignificant
                invoice4 = user.BitPay.CreateInvoice(new Invoice()
                {
                    Price = 0.000000019m,
                    Currency = "BTC",
                    FullNotifications = true
                }, Facade.Merchant);
                Assert.Equal(0.000000019m, invoice4.Price);

                var invoice = user.BitPay.CreateInvoice(new Invoice()
                {
                    Price = -0.1m,
                    Currency = "BTC",
                    FullNotifications = true
                }, Facade.Merchant);
                Assert.Equal(0.0m, invoice.Price);
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task InvoiceFlowThroughDifferentStatesCorrectly()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");
                var invoice = user.BitPay.CreateInvoice(new Invoice()
                {
                    Price = 5000.0m,
                    TaxIncluded = 1000.0m,
                    Currency = "USD",
                    PosData = "posData",
                    OrderId = "orderId",
                    ItemDesc = "Some description",
                    FullNotifications = true
                }, Facade.Merchant);
                var repo = tester.PayTester.GetService<InvoiceRepository>();
                var ctx = tester.PayTester.GetService<ApplicationDbContextFactory>().CreateContext();
                Assert.Equal(0, invoice.CryptoInfo[0].TxCount);
                Assert.True(invoice.MinerFees.ContainsKey("BTC"));
                Assert.Contains(invoice.MinerFees["BTC"].SatoshiPerBytes, new[] { 100.0m, 20.0m });
                TestUtils.Eventually(() =>
                {
                    var textSearchResult = tester.PayTester.InvoiceRepository.GetInvoices(new InvoiceQuery()
                    {
                        StoreId = new[] { user.StoreId },
                        TextSearch = invoice.OrderId
                    }).GetAwaiter().GetResult();
                    Assert.Single(textSearchResult);
                    textSearchResult = tester.PayTester.InvoiceRepository.GetInvoices(new InvoiceQuery()
                    {
                        StoreId = new[] { user.StoreId },
                        TextSearch = invoice.Id
                    }).GetAwaiter().GetResult();

                    Assert.Single(textSearchResult);
                });

                invoice = user.BitPay.GetInvoice(invoice.Id, Facade.Merchant);
                Assert.Equal(1000.0m, invoice.TaxIncluded);
                Assert.Equal(5000.0m, invoice.Price);
                Assert.Equal(Money.Coins(0), invoice.BtcPaid);
                Assert.Equal("new", invoice.Status);
                Assert.False((bool)((JValue)invoice.ExceptionStatus).Value);

                Assert.Single(user.BitPay.GetInvoices(invoice.InvoiceTime.UtcDateTime));
                Assert.Empty(user.BitPay.GetInvoices(invoice.InvoiceTime.UtcDateTime + TimeSpan.FromDays(2)));
                Assert.Single(user.BitPay.GetInvoices(invoice.InvoiceTime.UtcDateTime - TimeSpan.FromDays(5)));
                Assert.Single(user.BitPay.GetInvoices(invoice.InvoiceTime.UtcDateTime - TimeSpan.FromDays(5), invoice.InvoiceTime.DateTime + TimeSpan.FromDays(1.0)));
                Assert.Empty(user.BitPay.GetInvoices(invoice.InvoiceTime.UtcDateTime - TimeSpan.FromDays(5), invoice.InvoiceTime.DateTime - TimeSpan.FromDays(1)));


                var firstPayment = Money.Coins(0.04m);

                var txFee = Money.Zero;

                var cashCow = tester.ExplorerNode;

                var invoiceAddress = BitcoinAddress.Create(invoice.BitcoinAddress, cashCow.Network);
                var iii = ctx.AddressInvoices.ToArray();
                Assert.True(IsMapped(invoice, ctx));
                cashCow.SendToAddress(invoiceAddress, firstPayment);

                var invoiceEntity = repo.GetInvoice(invoice.Id, true).GetAwaiter().GetResult();
                Assert.Single(invoiceEntity.HistoricalAddresses);
                Assert.Null(invoiceEntity.HistoricalAddresses[0].UnAssigned);

                Money secondPayment = Money.Zero;

                TestUtils.Eventually(() =>
                {
                    var localInvoice = user.BitPay.GetInvoice(invoice.Id, Facade.Merchant);
                    Assert.Equal("new", localInvoice.Status);
                    Assert.Equal(firstPayment, localInvoice.BtcPaid);
                    txFee = localInvoice.BtcDue - invoice.BtcDue;
                    Assert.Equal("paidPartial", localInvoice.ExceptionStatus.ToString());
                    Assert.Equal(1, localInvoice.CryptoInfo[0].TxCount);
                    Assert.NotEqual(localInvoice.BitcoinAddress, invoice.BitcoinAddress); //New address
                    Assert.True(IsMapped(invoice, ctx));
                    Assert.True(IsMapped(localInvoice, ctx));

                    invoiceEntity = repo.GetInvoice(invoice.Id, true).GetAwaiter().GetResult();
                    var historical1 = invoiceEntity.HistoricalAddresses.FirstOrDefault(h => h.GetAddress() == invoice.BitcoinAddress);
                    Assert.NotNull(historical1.UnAssigned);
                    var historical2 = invoiceEntity.HistoricalAddresses.FirstOrDefault(h => h.GetAddress() == localInvoice.BitcoinAddress);
                    Assert.Null(historical2.UnAssigned);
                    invoiceAddress = BitcoinAddress.Create(localInvoice.BitcoinAddress, cashCow.Network);
                    secondPayment = localInvoice.BtcDue;
                });

                cashCow.SendToAddress(invoiceAddress, secondPayment);

                TestUtils.Eventually(() =>
                {
                    var localInvoice = user.BitPay.GetInvoice(invoice.Id, Facade.Merchant);
                    Assert.Equal("paid", localInvoice.Status);
                    Assert.Equal(2, localInvoice.CryptoInfo[0].TxCount);
                    Assert.Equal(firstPayment + secondPayment, localInvoice.BtcPaid);
                    Assert.Equal(Money.Zero, localInvoice.BtcDue);
                    Assert.Equal(localInvoice.BitcoinAddress, invoiceAddress.ToString()); //no new address generated
                    Assert.True(IsMapped(localInvoice, ctx));
                    Assert.False((bool)((JValue)localInvoice.ExceptionStatus).Value);
                });

                cashCow.Generate(1); //The user has medium speed settings, so 1 conf is enough to be confirmed

                TestUtils.Eventually(() =>
                {
                    var localInvoice = user.BitPay.GetInvoice(invoice.Id, Facade.Merchant);
                    Assert.Equal("confirmed", localInvoice.Status);
                });

                cashCow.Generate(5); //Now should be complete

                TestUtils.Eventually(() =>
                {
                    var localInvoice = user.BitPay.GetInvoice(invoice.Id, Facade.Merchant);
                    Assert.Equal("complete", localInvoice.Status);
                    Assert.NotEqual(0.0m, localInvoice.Rate);
                });

                invoice = user.BitPay.CreateInvoice(new Invoice()
                {
                    Price = 5000.0m,
                    Currency = "USD",
                    PosData = "posData",
                    OrderId = "orderId",
                    //RedirectURL = redirect + "redirect",
                    //NotificationURL = CallbackUri + "/notification",
                    ItemDesc = "Some description",
                    FullNotifications = true
                }, Facade.Merchant);
                invoiceAddress = BitcoinAddress.Create(invoice.BitcoinAddress, cashCow.Network);

                var txId = cashCow.SendToAddress(invoiceAddress, invoice.BtcDue + Money.Coins(1));

                TestUtils.Eventually(() =>
                {
                    var localInvoice = user.BitPay.GetInvoice(invoice.Id, Facade.Merchant);
                    Assert.Equal("paid", localInvoice.Status);
                    Assert.Equal(Money.Zero, localInvoice.BtcDue);
                    Assert.Equal("paidOver", (string)((JValue)localInvoice.ExceptionStatus).Value);

                    var textSearchResult = tester.PayTester.InvoiceRepository.GetInvoices(new InvoiceQuery()
                    {
                        StoreId = new[] { user.StoreId },
                        TextSearch = txId.ToString()
                    }).GetAwaiter().GetResult();
                    Assert.Single(textSearchResult);
                });

                cashCow.Generate(1);

                TestUtils.Eventually(() =>
                {
                    var localInvoice = user.BitPay.GetInvoice(invoice.Id, Facade.Merchant);
                    Assert.Equal("confirmed", localInvoice.Status);
                    Assert.Equal(Money.Zero, localInvoice.BtcDue);
                    Assert.Equal("paidOver", (string)((JValue)localInvoice.ExceptionStatus).Value);
                });
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public void CanQueryDirectProviders()
        {
            var factory = CreateBTCPayRateFactory();
            var directlySupported = factory.GetSupportedExchanges().Where(s => s.Source == RateSource.Direct).Select(s => s.Id).ToHashSet();
            var all = string.Join("\r\n", factory.GetSupportedExchanges().Select(e => e.Id).ToArray());
            foreach (var result in factory
                .Providers
                .Where(p => p.Value is BackgroundFetcherRateProvider bf && !(bf.Inner is CoinGeckoRateProvider cg && cg.UnderlyingExchange != null))
                .Select(p => (ExpectedName: p.Key, ResultAsync: p.Value.GetRatesAsync(default), Fetcher: (BackgroundFetcherRateProvider)p.Value))
                .ToList())
            {
                
                Logs.Tester.LogInformation($"Testing {result.ExpectedName}");
                result.Fetcher.InvalidateCache();
                var exchangeRates = new ExchangeRates(result.ExpectedName, result.ResultAsync.Result);
                result.Fetcher.InvalidateCache();
                Assert.NotNull(exchangeRates);
                Assert.NotEmpty(exchangeRates);
                Assert.NotEmpty(exchangeRates.ByExchange[result.ExpectedName]);
                if (result.ExpectedName == "bitbank")
                {
                    Assert.Contains(exchangeRates.ByExchange[result.ExpectedName],
                        e => e.CurrencyPair == new CurrencyPair("BTC", "JPY") && e.BidAsk.Bid > 100m); // 1BTC will always be more than 100JPY
                }
                else
                {
                    // This check if the currency pair is using right currency pair
                    Assert.Contains(exchangeRates.ByExchange[result.ExpectedName],
                        e => (e.CurrencyPair == new CurrencyPair("BTC", "USD") ||
                               e.CurrencyPair == new CurrencyPair("BTC", "EUR") ||
                               e.CurrencyPair == new CurrencyPair("BTC", "USDT") ||
                               e.CurrencyPair == new CurrencyPair("BTC", "CAD"))
                               && e.BidAsk.Bid > 1.0m // 1BTC will always be more than 1USD
                               );
                }
                // We are not showing a directly implemented exchange as directly implemented in the UI
                // we need to modify the AvailableRateProvider

                // There are some exception we stopped supporting but don't want to break backward compat
                if (result.ExpectedName != "coinaverage" && result.ExpectedName != "gdax")
                    Assert.Contains(result.ExpectedName, directlySupported);
            }
            // Kraken emit one request only after first GetRates
            factory.Providers["kraken"].GetRatesAsync(default).GetAwaiter().GetResult();
        }


        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanExportBackgroundFetcherState()
        {
            var factory = CreateBTCPayRateFactory();
            var provider = (BackgroundFetcherRateProvider)factory.Providers["kraken"];
            await provider.GetRatesAsync(default);
            var state = provider.GetState();
            Assert.Single(state.Rates, r => r.Pair == new CurrencyPair("BTC", "EUR"));
            var provider2 = new BackgroundFetcherRateProvider(provider.Inner)
            {
                RefreshRate = provider.RefreshRate,
                ValidatyTime = provider.ValidatyTime
            };
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                // Should throw
                await Assert.ThrowsAsync<OperationCanceledException>(async () => await provider2.GetRatesAsync(cts.Token));
            }
            provider2.LoadState(state);
            Assert.Equal(provider.LastRequested, provider2.LastRequested);
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                // Should not throw, as things should be cached
                await provider2.GetRatesAsync(cts.Token);
            }
            Assert.Equal(provider.NextUpdate, provider2.NextUpdate);
            Assert.NotEqual(provider.LastRequested, provider2.LastRequested);
            Assert.Equal(provider.Expiration, provider2.Expiration);

            var str = JsonConvert.SerializeObject(state);
            var state2 = JsonConvert.DeserializeObject<BackgroundFetcherState>(str);
            var str2 = JsonConvert.SerializeObject(state2);
            Assert.Equal(str, str2);
        }


        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public void CanGetRateCryptoCurrenciesByDefault()
        {
            var provider = new BTCPayNetworkProvider(NetworkType.Mainnet);
            var factory = CreateBTCPayRateFactory();
            var fetcher = new RateFetcher(factory);
            var pairs =
                    provider.GetAll()
                    .Select(c => new CurrencyPair(c.CryptoCode, "USD"))
                    .ToHashSet();

            var rules = new StoreBlob().GetDefaultRateRules(provider);
            var result = fetcher.FetchRates(pairs, rules, default);
            foreach (var value in result)
            {
                var rateResult = value.Value.GetAwaiter().GetResult();
                Logs.Tester.LogInformation($"Testing {value.Key.ToString()}");
                Assert.True(rateResult.BidAsk != null, $"Impossible to get the rate {rateResult.EvaluatedRule}");
            }
        }

        public static RateProviderFactory CreateBTCPayRateFactory()
        {
            return new RateProviderFactory(TestUtils.CreateHttpFactory());
        }

        class SpyRateProvider : IRateProvider
        {
            public bool Hit { get; set; }
            public Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken)
            {
                Hit = true;
                var rates = new List<PairRate>();
                rates.Add(new PairRate(CurrencyPair.Parse("BTC_USD"), new BidAsk(5000)));
                return Task.FromResult(rates.ToArray());
            }

            public void AssertHit()
            {
                Assert.True(Hit, "Should have hit the provider");
                Hit = false;
            }
            public void AssertNotHit()
            {
                Assert.False(Hit, "Should have not hit the provider");
                Hit = false;
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CheckLogsRoute()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");

                var serverController = user.GetController<ServerController>();
                var vm = Assert.IsType<LogsViewModel>(Assert.IsType<ViewResult>(await serverController.LogsView()).Model);
            }
        }

        [Fact]
        [Trait("Fast", "Fast")]
        public async Task CanExpandExternalConnectionString()
        {
            var unusedUri = new Uri("https://toto.com");
            Assert.True(ExternalConnectionString.TryParse("server=/test", out var connStr, out var error));
            var expanded = await connStr.Expand(new Uri("https://toto.com"), ExternalServiceTypes.Charge, NetworkType.Mainnet);
            Assert.Equal(new Uri("https://toto.com/test"), expanded.Server);
            expanded = await connStr.Expand(new Uri("http://toto.onion"), ExternalServiceTypes.Charge, NetworkType.Mainnet);
            Assert.Equal(new Uri("http://toto.onion/test"), expanded.Server);
            await Assert.ThrowsAsync<SecurityException>(() => connStr.Expand(new Uri("http://toto.com"), ExternalServiceTypes.Charge, NetworkType.Mainnet));
            await connStr.Expand(new Uri("http://toto.com"), ExternalServiceTypes.Charge, NetworkType.Testnet);

            // Make sure absolute paths are not expanded
            Assert.True(ExternalConnectionString.TryParse("server=https://tow/test", out connStr, out error));
            expanded = await connStr.Expand(new Uri("https://toto.com"), ExternalServiceTypes.Charge, NetworkType.Mainnet);
            Assert.Equal(new Uri("https://tow/test"), expanded.Server);

            // Error if directory not exists
            Assert.True(ExternalConnectionString.TryParse($"server={unusedUri};macaroondirectorypath=pouet", out connStr, out error));
            await Assert.ThrowsAsync<DirectoryNotFoundException>(() => connStr.Expand(unusedUri, ExternalServiceTypes.LNDGRPC, NetworkType.Mainnet));
            await Assert.ThrowsAsync<DirectoryNotFoundException>(() => connStr.Expand(unusedUri, ExternalServiceTypes.LNDRest, NetworkType.Mainnet));
            await connStr.Expand(unusedUri, ExternalServiceTypes.Charge, NetworkType.Mainnet);

            var macaroonDirectory = CreateDirectory();
            Assert.True(ExternalConnectionString.TryParse($"server={unusedUri};macaroondirectorypath={macaroonDirectory}", out connStr, out error));
            await connStr.Expand(unusedUri, ExternalServiceTypes.LNDGRPC, NetworkType.Mainnet);
            expanded = await connStr.Expand(unusedUri, ExternalServiceTypes.LNDRest, NetworkType.Mainnet);
            Assert.NotNull(expanded.Macaroons);
            Assert.Null(expanded.MacaroonFilePath);
            Assert.Null(expanded.Macaroons.AdminMacaroon);
            Assert.Null(expanded.Macaroons.InvoiceMacaroon);
            Assert.Null(expanded.Macaroons.ReadonlyMacaroon);

            File.WriteAllBytes($"{macaroonDirectory}/admin.macaroon", new byte[] { 0xaa });
            File.WriteAllBytes($"{macaroonDirectory}/invoice.macaroon", new byte[] { 0xab });
            File.WriteAllBytes($"{macaroonDirectory}/readonly.macaroon", new byte[] { 0xac });
            expanded = await connStr.Expand(unusedUri, ExternalServiceTypes.LNDRest, NetworkType.Mainnet);
            Assert.NotNull(expanded.Macaroons.AdminMacaroon);
            Assert.NotNull(expanded.Macaroons.InvoiceMacaroon);
            Assert.Equal("ab", expanded.Macaroons.InvoiceMacaroon.Hex);
            Assert.Equal(0xab, expanded.Macaroons.InvoiceMacaroon.Bytes[0]);
            Assert.NotNull(expanded.Macaroons.ReadonlyMacaroon);

            Assert.True(ExternalConnectionString.TryParse($"server={unusedUri};cookiefilepath={macaroonDirectory}/charge.cookie", out connStr, out error));
            File.WriteAllText($"{macaroonDirectory}/charge.cookie", "apitoken");
            expanded = await connStr.Expand(unusedUri, ExternalServiceTypes.Charge, NetworkType.Mainnet);
            Assert.Equal("apitoken", expanded.APIToken);
        }

        private string CreateDirectory([CallerMemberName] string caller = null)
        {
            var name = $"{caller}-{NBitcoin.RandomUtils.GetUInt32()}";
            Directory.CreateDirectory(name);
            return name;
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Fast", "Fast")]
        public async Task CanCreateSqlitedb()
        {
            if (File.Exists("temp.db"))
                File.Delete("temp.db");
            // This test sqlite can migrate
            var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
            builder.UseSqlite("Data Source=temp.db");
            await new ApplicationDbContext(builder.Options).Database.MigrateAsync();
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Fast", "Fast")]
        public void CheckRatesProvider()
        {
            var spy = new SpyRateProvider();
            RateRules.TryParse("X_X = bittrex(X_X);", out var rateRules);

            var factory = CreateBTCPayRateFactory();
            factory.Providers.Clear();
            var fetcher = new RateFetcher(factory);
            factory.Providers.Clear();
            var fetch = new BackgroundFetcherRateProvider(spy);
            fetch.DoNotAutoFetchIfExpired = true;
            factory.Providers.Add("bittrex", fetch);
            var fetchedRate = fetcher.FetchRate(CurrencyPair.Parse("BTC_USD"), rateRules, default).GetAwaiter().GetResult();
            spy.AssertHit();
            fetchedRate = fetcher.FetchRate(CurrencyPair.Parse("BTC_USD"), rateRules, default).GetAwaiter().GetResult();
            spy.AssertNotHit();
            fetch.UpdateIfNecessary(default).GetAwaiter().GetResult();
            spy.AssertNotHit();
            fetch.RefreshRate = TimeSpan.FromSeconds(1.0);
            Thread.Sleep(1020);
            fetchedRate = fetcher.FetchRate(CurrencyPair.Parse("BTC_USD"), rateRules, default).GetAwaiter().GetResult();
            spy.AssertNotHit();
            fetch.ValidatyTime = TimeSpan.FromSeconds(1.0);
            fetch.UpdateIfNecessary(default).GetAwaiter().GetResult();
            spy.AssertHit();
            fetch.GetRatesAsync(default).GetAwaiter().GetResult();
            Thread.Sleep(1000);
            Assert.Throws<InvalidOperationException>(() => fetch.GetRatesAsync(default).GetAwaiter().GetResult());
        }

        [Fact]
        [Trait("Fast", "Fast")]
        public void ParseDerivationSchemeSettings()
        {
            var mainnet = new BTCPayNetworkProvider(NetworkType.Mainnet).GetNetwork<BTCPayNetwork>("BTC");
            var root = new Mnemonic("usage fever hen zero slide mammal silent heavy donate budget pulse say brain thank sausage brand craft about save attract muffin advance illegal cabbage").DeriveExtKey();
            Assert.True(DerivationSchemeSettings.TryParseFromColdcard("{\"keystore\": {\"ckcc_xpub\": \"xpub661MyMwAqRbcGVBsTGeNZN6QGVHmMHLdSA4FteGsRrEriu4pnVZMZWnruFFFXkMnyoBjyHndD3Qwcfz4MPzBUxjSevweNFQx7SAYZATtcDw\", \"xpub\": \"ypub6WWc2gWwHbdnAAyJDnR4SPL1phRh7REqrPBfZeizaQ1EmTshieRXJC3Z5YoU4wkcdKHEjQGkh6AYEzCQC1Kz3DNaWSwdc1pc8416hAjzqyD\", \"label\": \"Coldcard Import 0x60d1af8b\", \"ckcc_xfp\": 1624354699, \"type\": \"hardware\", \"hw_type\": \"coldcard\", \"derivation\": \"m/49'/0'/0'\"}, \"wallet_type\": \"standard\", \"use_encryption\": false, \"seed_version\": 17}", mainnet, out var settings));
            Assert.Equal(root.GetPublicKey().GetHDFingerPrint(), settings.AccountKeySettings[0].RootFingerprint);
            Assert.Equal(settings.AccountKeySettings[0].RootFingerprint, HDFingerprint.TryParse("8bafd160", out var hd) ? hd : default);
            Assert.Equal("Coldcard Import 0x60d1af8b", settings.Label);
            Assert.Equal("49'/0'/0'", settings.AccountKeySettings[0].AccountKeyPath.ToString());
            Assert.Equal("ypub6WWc2gWwHbdnAAyJDnR4SPL1phRh7REqrPBfZeizaQ1EmTshieRXJC3Z5YoU4wkcdKHEjQGkh6AYEzCQC1Kz3DNaWSwdc1pc8416hAjzqyD", settings.AccountOriginal);
            Assert.Equal(root.Derive(new KeyPath("m/49'/0'/0'")).Neuter().PubKey.WitHash.ScriptPubKey.Hash.ScriptPubKey, settings.AccountDerivation.GetDerivation().ScriptPubKey);

            var testnet = new BTCPayNetworkProvider(NetworkType.Testnet).GetNetwork<BTCPayNetwork>("BTC");

            // Should be legacy
            Assert.True(DerivationSchemeSettings.TryParseFromColdcard("{\"keystore\": {\"ckcc_xpub\": \"tpubD6NzVbkrYhZ4YHNiuTdTmHRmbcPRLfqgyneZFCL1mkzkUBjXriQShxTh9HL34FK2mhieasJVk9EzJrUfkFqRNQBjiXgx3n5BhPkxKBoFmaS\", \"xpub\": \"tpubDDWYqT3P24znfsaGX7kZcQhNc5LAjnQiKQvUCHF2jS6dsgJBRtymopEU5uGpMaR5YChjuiExZG1X2aTbqXkp82KqH5qnqwWHp6EWis9ZvKr\", \"label\": \"Coldcard Import 0x60d1af8b\", \"ckcc_xfp\": 1624354699, \"type\": \"hardware\", \"hw_type\": \"coldcard\", \"derivation\": \"m/44'/1'/0'\"}, \"wallet_type\": \"standard\", \"use_encryption\": false, \"seed_version\": 17}", testnet, out settings));
            Assert.True(settings.AccountDerivation is DirectDerivationStrategy s && !s.Segwit);

            // Should be segwit p2sh
            Assert.True(DerivationSchemeSettings.TryParseFromColdcard("{\"keystore\": {\"ckcc_xpub\": \"tpubD6NzVbkrYhZ4YHNiuTdTmHRmbcPRLfqgyneZFCL1mkzkUBjXriQShxTh9HL34FK2mhieasJVk9EzJrUfkFqRNQBjiXgx3n5BhPkxKBoFmaS\", \"xpub\": \"upub5DSddA9NoRUyJrQ4p86nsCiTSY7kLHrSxx3joEJXjHd4HPARhdXUATuk585FdWPVC2GdjsMePHb6BMDmf7c6KG4K4RPX6LVqBLtDcWpQJmh\", \"label\": \"Coldcard Import 0x60d1af8b\", \"ckcc_xfp\": 1624354699, \"type\": \"hardware\", \"hw_type\": \"coldcard\", \"derivation\": \"m/49'/1'/0'\"}, \"wallet_type\": \"standard\", \"use_encryption\": false, \"seed_version\": 17}", testnet, out settings));
            Assert.True(settings.AccountDerivation is P2SHDerivationStrategy p && p.Inner is DirectDerivationStrategy s2 && s2.Segwit);

            // Should be segwit
            Assert.True(DerivationSchemeSettings.TryParseFromColdcard("{\"keystore\": {\"ckcc_xpub\": \"tpubD6NzVbkrYhZ4YHNiuTdTmHRmbcPRLfqgyneZFCL1mkzkUBjXriQShxTh9HL34FK2mhieasJVk9EzJrUfkFqRNQBjiXgx3n5BhPkxKBoFmaS\", \"xpub\": \"vpub5YjYxTemJ39tFRnuAhwduyxG2tKGjoEpmvqVQRPqdYrqa6YGoeSzBtHXaJUYB19zDbXs3JjbEcVWERjQBPf9bEfUUMZNMv1QnMyHV8JPqyf\", \"label\": \"Coldcard Import 0x60d1af8b\", \"ckcc_xfp\": 1624354699, \"type\": \"hardware\", \"hw_type\": \"coldcard\", \"derivation\": \"m/84'/1'/0'\"}, \"wallet_type\": \"standard\", \"use_encryption\": false, \"seed_version\": 17}", testnet, out settings));
            Assert.True(settings.AccountDerivation is DirectDerivationStrategy s3 && s3.Segwit);
        }
        
        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        [Trait("Altcoins", "Altcoins")]
        [Trait("Lightning", "Lightning")]
        public  async Task CanCreateInvoiceWithSpecificPaymentMethods()
        {
            using (var tester = ServerTester.Create())
            {
                tester.ActivateLightning();
                tester.ActivateLTC();
                await tester.StartAsync();
                await tester.EnsureChannelsSetup();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterLightningNode("BTC", LightningConnectionType.Charge);
                user.RegisterDerivationScheme("BTC");
                user.RegisterDerivationScheme("LTC");

                var invoice = await user.BitPay.CreateInvoiceAsync(new Invoice(100, "BTC"));
                Assert.Equal(2, invoice.SupportedTransactionCurrencies.Count);
               
                
                invoice = await user.BitPay.CreateInvoiceAsync(new Invoice(100, "BTC")
                {
                    SupportedTransactionCurrencies = new Dictionary<string, InvoiceSupportedTransactionCurrency>()
                    {
                        {"BTC", new InvoiceSupportedTransactionCurrency()
                        {
                            Enabled = true
                        }}
                    }
                });
                
                Assert.Single(invoice.SupportedTransactionCurrencies);
            }
        }
        

        
        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanLoginWithNoSecondaryAuthSystemsOrRequestItWhenAdded()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();

               var accountController = tester.PayTester.GetController<AccountController>();

               //no 2fa or u2f enabled, login should work
               Assert.Equal(nameof(HomeController.Index), Assert.IsType<RedirectToActionResult>(await accountController.Login(new LoginViewModel()
               {
                   Email = user.RegisterDetails.Email,
                   Password = user.RegisterDetails.Password
               })).ActionName);

               var manageController = user.GetController<ManageController>();
               
               //by default no u2f devices available
               Assert.Empty(Assert.IsType<U2FAuthenticationViewModel>(Assert.IsType<ViewResult>(await manageController.U2FAuthentication()).Model).Devices);
               var addRequest = Assert.IsType<AddU2FDeviceViewModel>(Assert.IsType<ViewResult>(manageController.AddU2FDevice("label")).Model);
               //name should match the one provided in beginning
               Assert.Equal("label",addRequest.Name);

                //sending an invalid response model back to server, should error out
                Assert.IsType<RedirectToActionResult>(await manageController.AddU2FDevice(addRequest));
                var statusModel = manageController.TempData.GetStatusMessageModel();
               Assert.Equal(StatusMessageModel.StatusSeverity.Error, statusModel.Severity);

               var contextFactory = tester.PayTester.GetService<ApplicationDbContextFactory>();

               //add a fake u2f device in db directly since emulating a u2f device is hard and annoying
               using (var context = contextFactory.CreateContext())
               {
                   var newDevice = new U2FDevice()
                   {
                       Id = Guid.NewGuid().ToString(),
                       Name = "fake",
                       Counter = 0,
                       KeyHandle = UTF8Encoding.UTF8.GetBytes("fake"),
                       PublicKey= UTF8Encoding.UTF8.GetBytes("fake"),
                       AttestationCert= UTF8Encoding.UTF8.GetBytes("fake"),
                       ApplicationUserId= user.UserId
                   };
                   await context.U2FDevices.AddAsync(newDevice);
                   await context.SaveChangesAsync();
                   
                   Assert.NotNull(newDevice.Id);
                   Assert.NotEmpty(Assert.IsType<U2FAuthenticationViewModel>(Assert.IsType<ViewResult>(await manageController.U2FAuthentication()).Model).Devices);
                   
               }

               //check if we are showing the u2f login screen now
               var secondLoginResult = Assert.IsType<ViewResult>(await accountController.Login(new LoginViewModel()
               {
                   Email = user.RegisterDetails.Email,
                   Password = user.RegisterDetails.Password
               }));

               Assert.Equal("SecondaryLogin", secondLoginResult.ViewName);
               var vm = Assert.IsType<SecondaryLoginViewModel>(secondLoginResult.Model);
               //2fa was never enabled for user so this should be empty
               Assert.Null(vm.LoginWith2FaViewModel);
               Assert.NotNull(vm.LoginWithU2FViewModel);               
            }
        }
        
        private static bool IsMapped(Invoice invoice, ApplicationDbContext ctx)
        {
            var h = BitcoinAddress.Create(invoice.BitcoinAddress, Network.RegTest).ScriptPubKey.Hash.ToString();
            return (ctx.AddressInvoices.Where(i => i.InvoiceDataId == invoice.Id).ToArrayAsync().GetAwaiter().GetResult())
                .Where(i => i.GetAddress() == h).Any();
        }
    }
}
