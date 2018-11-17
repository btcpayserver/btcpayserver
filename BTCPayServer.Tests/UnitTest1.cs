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
using BTCPayServer.Authentication;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore.Extensions;
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
using BTCPayServer.Models.ServerViewModels;
using BTCPayServer.Security;
using NBXplorer.Models;
using RatesViewModel = BTCPayServer.Models.StoreViewModels.RatesViewModel;

namespace BTCPayServer.Tests
{
    public class UnitTest1
    {
        public UnitTest1(ITestOutputHelper helper)
        {
            Logs.Tester = new XUnitLog(helper) { Name = "Tests" };
            Logs.LogProvider = new XUnitLogProvider(helper);
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
            var dummy = new Key().PubKey.GetAddress(Network.RegTest).ToString();
#pragma warning disable CS0618
            InvoiceEntity invoiceEntity = new InvoiceEntity();
            invoiceEntity.Payments = new System.Collections.Generic.List<PaymentEntity>();
            invoiceEntity.ProductInformation = new ProductInformation() { Price = 100 };
            PaymentMethodDictionary paymentMethods = new PaymentMethodDictionary();
            paymentMethods.Add(new PaymentMethod()
            {
                CryptoCode = "BTC",
                Rate = 10513.44m,
            }.SetPaymentMethodDetails(new BTCPayServer.Payments.Bitcoin.BitcoinLikeOnChainPaymentMethod()
            {
                TxFee = Money.Coins(0.00000100m),
                DepositAddress = dummy
            }));
            paymentMethods.Add(new PaymentMethod()
            {
                CryptoCode = "LTC",
                Rate = 216.79m
            }.SetPaymentMethodDetails(new BTCPayServer.Payments.Bitcoin.BitcoinLikeOnChainPaymentMethod()
            {
                TxFee = Money.Coins(0.00010000m),
                DepositAddress = dummy
            }));
            invoiceEntity.SetPaymentMethods(paymentMethods);

            var btc = invoiceEntity.GetPaymentMethod(new PaymentMethodId("BTC", PaymentTypes.BTCLike), null);
            var accounting = btc.Calculate();

            invoiceEntity.Payments.Add(new PaymentEntity() { Accounted = true, CryptoCode = "BTC" }.SetCryptoPaymentData(new BitcoinLikePaymentData()
            {
                Output = new TxOut() { Value = Money.Coins(0.00151263m) }
            }));
            accounting = btc.Calculate();
            invoiceEntity.Payments.Add(new PaymentEntity() { Accounted = true, CryptoCode = "BTC" }.SetCryptoPaymentData(new BitcoinLikePaymentData()
            {
                Output = new TxOut() { Value = accounting.Due }
            }));
            accounting = btc.Calculate();
            Assert.Equal(Money.Zero, accounting.Due);
            Assert.Equal(Money.Zero, accounting.DueUncapped);

            var ltc = invoiceEntity.GetPaymentMethod(new PaymentMethodId("LTC", PaymentTypes.BTCLike), null);
            accounting = ltc.Calculate();

            Assert.Equal(Money.Zero, accounting.Due);
            // LTC might have over paid due to BTC paying above what it should (round 1 satoshi up)
            Assert.True(accounting.DueUncapped < Money.Zero);

            var paymentMethod = InvoiceWatcher.GetNearestClearedPayment(paymentMethods, out var accounting2, null);
            Assert.Equal(btc.CryptoCode, paymentMethod.CryptoCode);
#pragma warning restore CS0618
        }

        [Fact]
        [Trait("Fast", "Fast")]
        public void CanCalculateCryptoDue()
        {
            var entity = new InvoiceEntity();
#pragma warning disable CS0618
            entity.Payments = new System.Collections.Generic.List<PaymentEntity>();
            entity.SetPaymentMethod(new PaymentMethod() { CryptoCode = "BTC", Rate = 5000, TxFee = Money.Coins(0.1m) });
            entity.ProductInformation = new ProductInformation() { Price = 5000 };

            var paymentMethod = entity.GetPaymentMethods(null).TryGet("BTC", PaymentTypes.BTCLike);
            var accounting = paymentMethod.Calculate();
            Assert.Equal(Money.Coins(1.1m), accounting.Due);
            Assert.Equal(Money.Coins(1.1m), accounting.TotalDue);

            entity.Payments.Add(new PaymentEntity() { Output = new TxOut(Money.Coins(0.5m), new Key()), Accounted = true });

            accounting = paymentMethod.Calculate();
            //Since we need to spend one more txout, it should be 1.1 - 0,5 + 0.1
            Assert.Equal(Money.Coins(0.7m), accounting.Due);
            Assert.Equal(Money.Coins(1.2m), accounting.TotalDue);

            entity.Payments.Add(new PaymentEntity() { Output = new TxOut(Money.Coins(0.2m), new Key()), Accounted = true });

            accounting = paymentMethod.Calculate();
            Assert.Equal(Money.Coins(0.6m), accounting.Due);
            Assert.Equal(Money.Coins(1.3m), accounting.TotalDue);

            entity.Payments.Add(new PaymentEntity() { Output = new TxOut(Money.Coins(0.6m), new Key()), Accounted = true });

            accounting = paymentMethod.Calculate();
            Assert.Equal(Money.Zero, accounting.Due);
            Assert.Equal(Money.Coins(1.3m), accounting.TotalDue);

            entity.Payments.Add(new PaymentEntity() { Output = new TxOut(Money.Coins(0.2m), new Key()), Accounted = true });

            accounting = paymentMethod.Calculate();
            Assert.Equal(Money.Zero, accounting.Due);
            Assert.Equal(Money.Coins(1.3m), accounting.TotalDue);

            entity = new InvoiceEntity();
            entity.ProductInformation = new ProductInformation() { Price = 5000 };
            PaymentMethodDictionary paymentMethods = new PaymentMethodDictionary();
            paymentMethods.Add(new PaymentMethod()
            {
                CryptoCode = "BTC",
                Rate = 1000,
                TxFee = Money.Coins(0.1m)
            });
            paymentMethods.Add(new PaymentMethod()
            {
                CryptoCode = "LTC",
                Rate = 500,
                TxFee = Money.Coins(0.01m)
            });
            entity.SetPaymentMethods(paymentMethods);
            entity.Payments = new List<PaymentEntity>();
            paymentMethod = entity.GetPaymentMethod(new PaymentMethodId("BTC", PaymentTypes.BTCLike), null);
            accounting = paymentMethod.Calculate();
            Assert.Equal(Money.Coins(5.1m), accounting.Due);

            paymentMethod = entity.GetPaymentMethod(new PaymentMethodId("LTC", PaymentTypes.BTCLike), null);
            accounting = paymentMethod.Calculate();
            Assert.Equal(Money.Coins(10.01m), accounting.TotalDue);

            entity.Payments.Add(new PaymentEntity() { CryptoCode = "BTC", Output = new TxOut(Money.Coins(1.0m), new Key()), Accounted = true });

            paymentMethod = entity.GetPaymentMethod(new PaymentMethodId("BTC", PaymentTypes.BTCLike), null);
            accounting = paymentMethod.Calculate();
            Assert.Equal(Money.Coins(4.2m), accounting.Due);
            Assert.Equal(Money.Coins(1.0m), accounting.CryptoPaid);
            Assert.Equal(Money.Coins(1.0m), accounting.Paid);
            Assert.Equal(Money.Coins(5.2m), accounting.TotalDue);
            Assert.Equal(2, accounting.TxRequired);

            paymentMethod = entity.GetPaymentMethod(new PaymentMethodId("LTC", PaymentTypes.BTCLike), null);
            accounting = paymentMethod.Calculate();
            Assert.Equal(Money.Coins(10.01m + 0.1m * 2 - 2.0m /* 8.21m */), accounting.Due);
            Assert.Equal(Money.Coins(0.0m), accounting.CryptoPaid);
            Assert.Equal(Money.Coins(2.0m), accounting.Paid);
            Assert.Equal(Money.Coins(10.01m + 0.1m * 2), accounting.TotalDue);

            entity.Payments.Add(new PaymentEntity() { CryptoCode = "LTC", Output = new TxOut(Money.Coins(1.0m), new Key()), Accounted = true });


            paymentMethod = entity.GetPaymentMethod(new PaymentMethodId("BTC", PaymentTypes.BTCLike), null);
            accounting = paymentMethod.Calculate();
            Assert.Equal(Money.Coins(4.2m - 0.5m + 0.01m / 2), accounting.Due);
            Assert.Equal(Money.Coins(1.0m), accounting.CryptoPaid);
            Assert.Equal(Money.Coins(1.5m), accounting.Paid);
            Assert.Equal(Money.Coins(5.2m + 0.01m / 2), accounting.TotalDue); // The fee for LTC added
            Assert.Equal(2, accounting.TxRequired);

            paymentMethod = entity.GetPaymentMethod(new PaymentMethodId("LTC", PaymentTypes.BTCLike), null);
            accounting = paymentMethod.Calculate();
            Assert.Equal(Money.Coins(8.21m - 1.0m + 0.01m), accounting.Due);
            Assert.Equal(Money.Coins(1.0m), accounting.CryptoPaid);
            Assert.Equal(Money.Coins(3.0m), accounting.Paid);
            Assert.Equal(Money.Coins(10.01m + 0.1m * 2 + 0.01m), accounting.TotalDue);
            Assert.Equal(2, accounting.TxRequired);

            var remaining = Money.Coins(4.2m - 0.5m + 0.01m / 2);
            entity.Payments.Add(new PaymentEntity() { CryptoCode = "BTC", Output = new TxOut(remaining, new Key()), Accounted = true });

            paymentMethod = entity.GetPaymentMethod(new PaymentMethodId("BTC", PaymentTypes.BTCLike), null);
            accounting = paymentMethod.Calculate();
            Assert.Equal(Money.Zero, accounting.Due);
            Assert.Equal(Money.Coins(1.0m) + remaining, accounting.CryptoPaid);
            Assert.Equal(Money.Coins(1.5m) + remaining, accounting.Paid);
            Assert.Equal(Money.Coins(5.2m + 0.01m / 2), accounting.TotalDue);
            Assert.Equal(accounting.Paid, accounting.TotalDue);
            Assert.Equal(2, accounting.TxRequired);

            paymentMethod = entity.GetPaymentMethod(new PaymentMethodId("LTC", PaymentTypes.BTCLike), null);
            accounting = paymentMethod.Calculate();
            Assert.Equal(Money.Zero, accounting.Due);
            Assert.Equal(Money.Coins(1.0m), accounting.CryptoPaid);
            Assert.Equal(Money.Coins(3.0m) + remaining * 2, accounting.Paid);
            // Paying 2 BTC fee, LTC fee removed because fully paid
            Assert.Equal(Money.Coins(10.01m + 0.1m * 2 + 0.1m * 2 /* + 0.01m no need to pay this fee anymore */), accounting.TotalDue);
            Assert.Equal(1, accounting.TxRequired);
            Assert.Equal(accounting.Paid, accounting.TotalDue);
#pragma warning restore CS0618
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public void CanAcceptInvoiceWithTolerance()
        {
            var entity = new InvoiceEntity();
#pragma warning disable CS0618
            entity.Payments = new List<PaymentEntity>();
            entity.SetPaymentMethod(new PaymentMethod() { CryptoCode = "BTC", Rate = 5000, TxFee = Money.Coins(0.1m) });
            entity.ProductInformation = new ProductInformation() { Price = 5000 };
            entity.PaymentTolerance = 0;


            var paymentMethod = entity.GetPaymentMethods(null).TryGet("BTC", PaymentTypes.BTCLike);
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
        public void CanAcceptInvoiceWithTolerance2()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
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

                Eventually(() =>
                {
                    var localInvoice = user.BitPay.GetInvoice(invoice.Id, Facade.Merchant);
                    Assert.Equal("paid", localInvoice.Status);
                });
            }
        }

        [Fact]
        [Trait("Fast", "Fast")]
        public void RoundupCurrenciesCorrectly()
        {
            foreach (var test in new[]
            {
                (0.0005m, "$0.0005 (USD)"),
                (0.001m, "$0.001 (USD)"),
                (0.01m, "$0.01 (USD)"),
                (0.1m, "$0.10 (USD)"),
            })
            {
                var actual = new CurrencyNameTable().DisplayFormatCurrency(test.Item1, "USD");
                Assert.Equal(test.Item2, actual);
            }
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public void CanPayUsingBIP70()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");
                Assert.True(user.BitPay.TestAccess(Facade.Merchant));
                var invoice = user.BitPay.CreateInvoice(new Invoice()
                {
                    Buyer = new Buyer() { email = "test@fwf.com" },
                    Price = 5000.0m,
                    Currency = "USD",
                    PosData = "posData",
                    OrderId = "orderId",
                    //RedirectURL = redirect + "redirect",
                    //NotificationURL = CallbackUri + "/notification",
                    ItemDesc = "Some description",
                    FullNotifications = true
                }, Facade.Merchant);

                Assert.False(invoice.Refundable);

                var url = new BitcoinUrlBuilder(invoice.PaymentUrls.BIP72);
                var request = url.GetPaymentRequest();
                var payment = request.CreatePayment();

                Transaction tx = new Transaction();
                tx.Outputs.AddRange(request.Details.Outputs.Select(o => new TxOut(o.Amount, o.Script)));
                var cashCow = tester.ExplorerNode;
                tx = cashCow.FundRawTransaction(tx).Transaction;
                tx = cashCow.SignRawTransaction(tx);

                payment.Transactions.Add(tx);

                payment.RefundTo.Add(new PaymentOutput(Money.Coins(1.0m), new Key().ScriptPubKey));
                var ack = payment.SubmitPayment();
                Assert.NotNull(ack);

                Eventually(() =>
                {
                    var localInvoice = user.BitPay.GetInvoice(invoice.Id, Facade.Merchant);
                    Assert.Equal("paid", localInvoice.Status);
                    Assert.True(localInvoice.Refundable);
                });
            }
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async Task CanSetLightningServer()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
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
                Assert.DoesNotContain("Error", ((LightningNodeViewModel)Assert.IsType<ViewResult>(testResult).Model).StatusMessage, StringComparison.OrdinalIgnoreCase);
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

        [Fact]
        [Trait("Integration", "Integration")]
        public async Task CanSendLightningPaymentCLightning()
        {
            await ProcessLightningPayment(LightningConnectionType.CLightning);
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async Task CanSendLightningPaymentCharge()
        {
            await ProcessLightningPayment(LightningConnectionType.Charge);
        }

        [Fact]
        [Trait("Integration", "Integration")]
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
                tester.Start();
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
            await tester.SendLightningPaymentAsync(invoice);
            await EventuallyAsync(async () =>
            {
                var localInvoice = await user.BitPay.GetInvoiceAsync(invoice.Id);
                Assert.Equal("complete", localInvoice.Status);
                Assert.Equal("False", localInvoice.ExceptionStatus.ToString());
            });
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public void CanUseServerInitiatedPairingCode()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
                var acc = tester.NewAccount();
                acc.Register();
                acc.CreateStore();

                var controller = acc.GetController<StoresController>();
                var token = (RedirectToActionResult)controller.CreateToken(new Models.StoreViewModels.CreateTokenViewModel()
                {
                    Facade = Facade.Merchant.ToString(),
                    Label = "bla",
                    PublicKey = null
                }).GetAwaiter().GetResult();

                var pairingCode = (string)token.RouteValues["pairingCode"];

                acc.BitPay.AuthorizeClient(new PairingCode(pairingCode)).GetAwaiter().GetResult();
                Assert.True(acc.BitPay.TestAccess(Facade.Merchant));
            }
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public void CanSendIPN()
        {
            using (var callbackServer = new CustomServer())
            {
                using (var tester = ServerTester.Create())
                {
                    tester.Start();
                    var acc = tester.NewAccount();
                    acc.GrantAccess();
                    acc.RegisterDerivationScheme("BTC");
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
                    BitcoinUrlBuilder url = new BitcoinUrlBuilder(invoice.PaymentUrls.BIP21);
                    tester.ExplorerNode.SendToAddress(url.Address, url.Amount);
                    Thread.Sleep(5000);
                    callbackServer.ProcessNextRequest((ctx) =>
                    {
                        var ipn = new StreamReader(ctx.Request.Body).ReadToEnd();
                        JsonConvert.DeserializeObject<InvoicePaymentNotification>(ipn); //can deserialize
                    });
                    var invoice2 = acc.BitPay.GetInvoice(invoice.Id);
                    Assert.NotNull(invoice2);
                }
            }
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public void CantPairTwiceWithSamePubkey()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
                var acc = tester.NewAccount();
                acc.Register();
                acc.CreateStore();
                var store = acc.GetController<StoresController>();
                var pairingCode = acc.BitPay.RequestClientAuthorization("test", Facade.Merchant);
                Assert.IsType<RedirectToActionResult>(store.Pair(pairingCode.ToString(), acc.StoreId).GetAwaiter().GetResult());

                pairingCode = acc.BitPay.RequestClientAuthorization("test1", Facade.Merchant);
                acc.CreateStore();
                var store2 = acc.GetController<StoresController>();
                store2.Pair(pairingCode.ToString(), store2.StoreData.Id).GetAwaiter().GetResult();
                Assert.Contains(nameof(PairingResult.ReusedKey), store2.StatusMessage, StringComparison.CurrentCultureIgnoreCase);
            }
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public void CanSolveTheDogesRatesOnKraken()
        {
            var provider = new BTCPayNetworkProvider(NetworkType.Mainnet);
            var factory = CreateBTCPayRateFactory();
            var fetcher = new RateFetcher(factory);

            Assert.True(RateRules.TryParse("X_X=kraken(X_BTC) * kraken(BTC_X)", out var rule));
            foreach(var pair in new[] { "DOGE_USD", "DOGE_CAD", "DASH_CAD", "DASH_USD", "DASH_EUR" })
            {
                var result = fetcher.FetchRate(CurrencyPair.Parse(pair), rule).GetAwaiter().GetResult();
                Assert.NotNull(result.BidAsk);
                Assert.Empty(result.Errors);
            }
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public void CanRescanWallet()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
                var acc = tester.NewAccount();
                acc.GrantAccess();
                acc.RegisterDerivationScheme("BTC", true);
                var btcDerivationScheme = acc.DerivationScheme;
                acc.RegisterDerivationScheme("LTC", true);

                var walletController = tester.PayTester.GetController<WalletsController>(acc.UserId);
                WalletId walletId = new WalletId(acc.StoreId, "LTC");
                var rescan = Assert.IsType<RescanWalletModel>(Assert.IsType<ViewResult>(walletController.WalletRescan(walletId).Result).Model);
                Assert.False(rescan.Ok);
                Assert.True(rescan.IsFullySync);
                Assert.True(rescan.IsSegwit);
                Assert.False(rescan.IsSupportedByCurrency);
                Assert.False(rescan.IsServerAdmin);

                walletId = new WalletId(acc.StoreId, "BTC");
                var serverAdminClaim = new[] { new Claim(Policies.CanModifyServerSettings.Key, "true") };
                walletController = tester.PayTester.GetController<WalletsController>(acc.UserId, additionalClaims: serverAdminClaim);
                rescan = Assert.IsType<RescanWalletModel>(Assert.IsType<ViewResult>(walletController.WalletRescan(walletId).Result).Model);
                Assert.True(rescan.Ok);
                Assert.True(rescan.IsFullySync);
                Assert.True(rescan.IsSupportedByCurrency);
                Assert.True(rescan.IsServerAdmin);

                rescan.GapLimit = 100;

                // Sending a coin
                var txId = tester.ExplorerNode.SendToAddress(btcDerivationScheme.Derive(new KeyPath("0/90")).ScriptPubKey, Money.Coins(1.0m));
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
            }
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public void CanListInvoices()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
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
                Eventually(() =>
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
            }
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public void CanGetRates()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
                var acc = tester.NewAccount();
                acc.GrantAccess();
                acc.RegisterDerivationScheme("BTC");
                acc.RegisterDerivationScheme("LTC");

                var rateController = acc.GetController<RateController>();
                var GetBaseCurrencyRatesResult = JObject.Parse(((JsonResult)rateController.GetBaseCurrencyRates("BTC", acc.StoreId)
                    .GetAwaiter().GetResult()).Value.ToJson()).ToObject<DataWrapper<Rate[]>>();
                Assert.NotNull(GetBaseCurrencyRatesResult);
                Assert.NotNull(GetBaseCurrencyRatesResult.Data);
                Assert.Equal(2, GetBaseCurrencyRatesResult.Data.Length);
                Assert.Single(GetBaseCurrencyRatesResult.Data.Where(o => o.Code == "LTC"));

                var GetRatesResult = JObject.Parse(((JsonResult)rateController.GetRates(null, acc.StoreId)
                    .GetAwaiter().GetResult()).Value.ToJson()).ToObject<DataWrapper<Rate[]>>();
                Assert.NotNull(GetRatesResult);
                Assert.NotNull(GetRatesResult.Data);
                Assert.Equal(2, GetRatesResult.Data.Length);

                var GetCurrencyPairRateResult = JObject.Parse(((JsonResult)rateController.GetCurrencyPairRate("BTC", "LTC", acc.StoreId)
                    .GetAwaiter().GetResult()).Value.ToJson()).ToObject<DataWrapper<Rate>>();

                Assert.NotNull(GetCurrencyPairRateResult);
                Assert.NotNull(GetCurrencyPairRateResult.Data);
                Assert.Equal("LTC", GetCurrencyPairRateResult.Data.Code);

                // Should be OK because the request is signed, so we can know the store
                var rates = acc.BitPay.GetRates();
                HttpClient client = new HttpClient();
                // Unauthentified requests should also be ok
                var response = client.GetAsync($"http://127.0.0.1:{tester.PayTester.Port}/api/rates?storeId={acc.StoreId}").GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();
            }
        }

        private void AssertSearchInvoice(TestAccount acc, bool expected, string invoiceId, string filter)
        {
            var result = (Models.InvoicingModels.InvoicesModel)((ViewResult)acc.GetController<InvoiceController>().ListInvoices(filter).Result).Model;
            Assert.Equal(expected, result.Invoices.Any(i => i.InvoiceId == invoiceId));
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public void CanRBFPayment()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");
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
                Eventually(() =>
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
                using (var listener = tester.ExplorerClient.CreateNotificationSession())
                {
                    listener.ListenAllDerivationSchemes();
                    var replaced = tester.ExplorerNode.SignRawTransaction(tx);
                    Thread.Sleep(1000); // Make sure the replacement has a different timestamp
                    var tx2 = tester.ExplorerNode.SendRawTransaction(replaced);
                    Logs.Tester.LogInformation($"Let's RBF with a payment of {payment2} ({tx2}), waiting for NBXplorer to pick it up");
                    Assert.Equal(tx2, ((NewTransactionEvent)listener.NextEvent(cts.Token)).TransactionData.TransactionHash);
                }
                Logs.Tester.LogInformation($"The invoice should now not be paidOver anymore");
                Eventually(() =>
                {
                    invoice = user.BitPay.GetInvoice(invoice.Id);
                    Assert.Equal(payment2, invoice.BtcPaid);
                    Assert.Equal("False", invoice.ExceptionStatus.ToString());
                });
            }
        }

        [Fact]
        [Trait("Fast", "Fast")]
        public void CanParseFilter()
        {
            var filter = "storeid:abc status:abed blabhbalh ";
            var search = new SearchString(filter);
            Assert.Equal("storeid:abc status:abed blabhbalh", search.ToString());
            Assert.Equal("blabhbalh", search.TextSearch);
            Assert.Single(search.Filters["storeid"]);
            Assert.Single(search.Filters["status"]);
            Assert.Equal("abc", search.Filters["storeid"].First());
            Assert.Equal("abed", search.Filters["status"].First());

            filter = "status:abed status:abed2";
            search = new SearchString(filter);
            Assert.Equal("status:abed status:abed2", search.ToString());
            Assert.Throws<KeyNotFoundException>(() => search.Filters["test"]);
            Assert.Equal(2, search.Filters["status"].Count);
            Assert.Equal("abed", search.Filters["status"].First());
            Assert.Equal("abed2", search.Filters["status"].Skip(1).First());
        }

        [Fact]
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

        [Fact]
        [Trait("Integration", "Integration")]
        public void TestAccessBitpayAPI()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
                var user = tester.NewAccount();
                Assert.False(user.BitPay.TestAccess(Facade.Merchant));
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");

                Assert.True(user.BitPay.TestAccess(Facade.Merchant));

                // Test request pairing code client side
                var storeController = user.GetController<StoresController>();
                storeController.CreateToken(new CreateTokenViewModel()
                {
                    Facade = Facade.Merchant.ToString(),
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
                Assert.IsType<RedirectToActionResult>(user.GetController<StoresController>().GenerateAPIKey().GetAwaiter().GetResult());

                var apiKey = Assert.Single(repo.GetLegacyAPIKeys(user.StoreId).GetAwaiter().GetResult());
                ///////

                // Generating a new one remove the previous
                Assert.IsType<RedirectToActionResult>(user.GetController<StoresController>().GenerateAPIKey().GetAwaiter().GetResult());
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
            }
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public void CanUseExchangeSpecificRate()
        {
            using (var tester = ServerTester.Create())
            {
                tester.PayTester.MockRates = false;
                tester.Start();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");
                List<decimal> rates = new List<decimal>();
                rates.Add(CreateInvoice(tester, user, "coinaverage"));
                var bitflyer = CreateInvoice(tester, user, "bitflyer", "JPY");
                var bitflyer2 = CreateInvoice(tester, user, "bitflyer", "JPY");
                Assert.Equal(bitflyer, bitflyer2); // Should be equal because cache
                rates.Add(bitflyer);

                foreach (var rate in rates)
                {
                    Assert.Single(rates.Where(r => r == rate));
                }
            }
        }

        private static decimal CreateInvoice(ServerTester tester, TestAccount user, string exchange, string currency = "USD")
        {
            var storeController = user.GetController<StoresController>();
            var vm = (RatesViewModel)((ViewResult)storeController.Rates()).Model;
            vm.PreferredExchange = exchange;
            storeController.Rates(vm).Wait();
            var invoice2 = user.BitPay.CreateInvoice(new Invoice()
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

        [Fact]
        [Trait("Integration", "Integration")]
        public void CanTweakRate()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
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
                storeController.Rates(vm).Wait();


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

        [Fact]
        [Trait("Integration", "Integration")]
        public void CanHaveLTCOnlyStore()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
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
                Eventually(() =>
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

                Eventually(() =>
                {
                    invoice = user.BitPay.GetInvoice(invoice.Id);
                    Assert.Equal("paid", invoice.Status);
                    checkout = (Models.InvoicingModels.PaymentModel)((JsonResult)controller.GetStatus(invoice.Id, null).GetAwaiter().GetResult()).Value;
                    Assert.Equal("paid", checkout.Status);
                });

            }
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public void CanModifyRates()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");

                var store = user.GetController<StoresController>();
                var rateVm = Assert.IsType<RatesViewModel>(Assert.IsType<ViewResult>(store.Rates()).Model);
                Assert.False(rateVm.ShowScripting);
                Assert.Equal("coinaverage", rateVm.PreferredExchange);
                Assert.Equal(0.0, rateVm.Spread);
                Assert.Null(rateVm.TestRateRules);

                rateVm.PreferredExchange = "bitflyer";
                Assert.IsType<RedirectToActionResult>(store.Rates(rateVm, "Save").Result);
                rateVm = Assert.IsType<RatesViewModel>(Assert.IsType<ViewResult>(store.Rates()).Model);
                Assert.Equal("bitflyer", rateVm.PreferredExchange);

                rateVm.ScriptTest = "BTC_JPY,BTC_CAD";
                rateVm.Spread = 10;
                store = user.GetController<StoresController>();
                rateVm = Assert.IsType<RatesViewModel>(Assert.IsType<ViewResult>(store.Rates(rateVm, "Test").Result).Model);
                Assert.NotNull(rateVm.TestRateRules);
                Assert.Equal(2, rateVm.TestRateRules.Count);
                Assert.False(rateVm.TestRateRules[0].Error);
                Assert.StartsWith("(bitflyer(BTC_JPY)) * (0.9, 1.1) =", rateVm.TestRateRules[0].Rule, StringComparison.OrdinalIgnoreCase);
                Assert.True(rateVm.TestRateRules[1].Error);
                Assert.IsType<RedirectToActionResult>(store.Rates(rateVm, "Save").Result);

                Assert.IsType<RedirectToActionResult>(store.ShowRateRulesPost(true).Result);
                Assert.IsType<RedirectToActionResult>(store.Rates(rateVm, "Save").Result);
                store = user.GetController<StoresController>();
                rateVm = Assert.IsType<RatesViewModel>(Assert.IsType<ViewResult>(store.Rates()).Model);
                Assert.Equal(rateVm.DefaultScript, rateVm.Script);
                Assert.True(rateVm.ShowScripting);
                rateVm.ScriptTest = "BTC_JPY";
                rateVm = Assert.IsType<RatesViewModel>(Assert.IsType<ViewResult>(store.Rates(rateVm, "Test").Result).Model);
                Assert.True(rateVm.ShowScripting);
                Assert.Contains("(bitflyer(BTC_JPY)) * (0.9, 1.1) = ", rateVm.TestRateRules[0].Rule, StringComparison.OrdinalIgnoreCase);

                rateVm.ScriptTest = "BTC_USD,BTC_CAD,DOGE_USD,DOGE_CAD";
                rateVm.Script = "DOGE_X = bittrex(DOGE_BTC) * BTC_X;\n" +
                                "X_CAD = quadrigacx(X_CAD);\n" +
                                 "X_X = coinaverage(X_X);";
                rateVm.Spread = 50;
                rateVm = Assert.IsType<RatesViewModel>(Assert.IsType<ViewResult>(store.Rates(rateVm, "Test").Result).Model);
                Assert.True(rateVm.TestRateRules.All(t => !t.Error));
                Assert.IsType<RedirectToActionResult>(store.Rates(rateVm, "Save").Result);
                store = user.GetController<StoresController>();
                rateVm = Assert.IsType<RatesViewModel>(Assert.IsType<ViewResult>(store.Rates()).Model);
                Assert.Equal(50, rateVm.Spread);
                Assert.True(rateVm.ShowScripting);
                Assert.Contains("DOGE_X", rateVm.Script, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public void CanPayWithTwoCurrencies()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
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
                Eventually(() =>
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
                Eventually(() =>
                {
                    invoice = user.BitPay.GetInvoice(invoice.Id);
                    Assert.True(invoice.BtcPaid == firstPayment);
                });

                cashCow = tester.LTCExplorerNode;
                var ltcCryptoInfo = invoice.CryptoInfo.FirstOrDefault(c => c.CryptoCode == "LTC");
                Assert.NotNull(ltcCryptoInfo);
                invoiceAddress = BitcoinAddress.Create(ltcCryptoInfo.Address, cashCow.Network);
                var secondPayment = Money.Coins(decimal.Parse(ltcCryptoInfo.Due, CultureInfo.InvariantCulture));
                cashCow.Generate(2); // LTC is not worth a lot, so just to make sure we have money...
                cashCow.SendToAddress(invoiceAddress, secondPayment);
                Logs.Tester.LogInformation("Second payment sent to " + invoiceAddress);
                Eventually(() =>
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
            var parser = new DerivationSchemeParser(Network.TestNet);
            NBXplorer.DerivationStrategy.DerivationStrategyBase result;
            //  Passing electrum stuff
            // Native
            result = parser.Parse("zpub6nL6PUGurpU3DfPDSZaRS6WshpbNc9ctCFFzrCn54cssnheM31SZJZUcFHKtjJJNhAueMbh6ptFMfy1aeiMQJr3RJ4DDt1hAPx7sMTKV48t");
            Assert.Equal("tpubD93CJNkmGjLXnsBqE2zGDqfEh1Q8iJ8wueordy3SeWt1RngbbuxXCsqASuVWFywmfoCwUE1rSfNJbaH4cBNcbp8WcyZgPiiRSTazLGL8U9w", result.ToString());
            // P2SH
            result = parser.Parse("ypub6QqdH2c5z79681jUgdxjGJzGW9zpL4ryPCuhtZE4GpvrJoZqM823XQN6iSQeVbbbp2uCRQ9UgpeMcwiyV6qjvxTWVcxDn2XEAnioMUwsrQ5");
            Assert.Equal("tpubD6NzVbkrYhZ4YWjDJUACG9E8fJx2NqNY1iynTiPKEjJrzzRKAgha3nNnwGXr2BtvCJKJHW4nmG7rRqc2AGGy2AECgt16seMyV2FZivUmaJg-[p2sh]", result.ToString());
            result = parser.Parse("xpub661MyMwAqRbcGeVGU5e5KBcau1HHEUGf9Wr7k4FyLa8yRPNQrrVa7Ndrgg8Afbe2UYXMSL6tJBFd2JewwWASsePPLjkcJFL1tTVEs3UQ23X");
            Assert.Equal("tpubD6NzVbkrYhZ4YSg7vGdAX6wxE8NwDrmih9SR6cK7gUtsAg37w5LfFpJgviCxC6bGGT4G3uckqH5fiV9ZLN1gm5qgQLVuymzFUR5ed7U7ksu-[legacy]", result.ToString());
            ////////////////

            var tpub = "tpubD6NzVbkrYhZ4Wc65tjhmcKdWFauAo7bGLRTxvggygkNyp6SMGutJp7iociwsinU33jyNBp1J9j2hJH5yQsayfiS3LEU2ZqXodAcnaygra8o";

            result = parser.Parse(tpub);
            Assert.Equal(tpub, result.ToString());
            parser.HintScriptPubKey = BitcoinAddress.Create("tb1q4s33amqm8l7a07zdxcunqnn3gcsjcfz3xc573l", parser.Network).ScriptPubKey;
            result = parser.Parse(tpub);
            Assert.Equal(tpub, result.ToString());

            parser.HintScriptPubKey = BitcoinAddress.Create("2N2humNio3YTApSfY6VztQ9hQwDnhDvaqFQ", parser.Network).ScriptPubKey;
            result = parser.Parse(tpub);
            Assert.Equal($"{tpub}-[p2sh]", result.ToString());

            parser.HintScriptPubKey = BitcoinAddress.Create("mwD8bHS65cdgUf6rZUUSoVhi3wNQFu1Nfi", parser.Network).ScriptPubKey;
            result = parser.Parse(tpub);
            Assert.Equal($"{tpub}-[legacy]", result.ToString());

            parser.HintScriptPubKey = BitcoinAddress.Create("2N2humNio3YTApSfY6VztQ9hQwDnhDvaqFQ", parser.Network).ScriptPubKey;
            result = parser.Parse($"{tpub}-[legacy]");
            Assert.Equal($"{tpub}-[p2sh]", result.ToString());

            result = parser.Parse(tpub);
            Assert.Equal($"{tpub}-[p2sh]", result.ToString());

            parser = new DerivationSchemeParser(Network.RegTest);
            var parsed = parser.Parse("xpub6DG1rMYXiQtCc6CfdLFD9CtxqhzzRh7j6Sq6EdE9abgYy3cfDRrniLLv2AdwqHL1exiLnnKR5XXcaoiiexf3Y9R6J6rxkJtqJHzNzMW9QMZ-[p2sh]");
            Assert.Equal("tpubDDdeNbNDRgqestPX5XEJM8ELAq6eR5cne5RPbBHHvWSSiLHNHehsrn1kGCijMnHFSsFFQMqHcdMfGzDL3pWHRasPMhcGRqZ4tFankQ3i4ok-[p2sh]", parsed.ToString());

            // Let's make sure we can't generate segwit with dogecoin
            parser = new DerivationSchemeParser(NBitcoin.Altcoins.Dogecoin.Instance.Regtest);
            parsed = parser.Parse("xpub6DG1rMYXiQtCc6CfdLFD9CtxqhzzRh7j6Sq6EdE9abgYy3cfDRrniLLv2AdwqHL1exiLnnKR5XXcaoiiexf3Y9R6J6rxkJtqJHzNzMW9QMZ-[p2sh]");
            Assert.Equal("tpubDDdeNbNDRgqestPX5XEJM8ELAq6eR5cne5RPbBHHvWSSiLHNHehsrn1kGCijMnHFSsFFQMqHcdMfGzDL3pWHRasPMhcGRqZ4tFankQ3i4ok-[legacy]", parsed.ToString());

            parser = new DerivationSchemeParser(NBitcoin.Altcoins.Dogecoin.Instance.Regtest);
            parsed = parser.Parse("tpubDDdeNbNDRgqestPX5XEJM8ELAq6eR5cne5RPbBHHvWSSiLHNHehsrn1kGCijMnHFSsFFQMqHcdMfGzDL3pWHRasPMhcGRqZ4tFankQ3i4ok-[p2sh]");
            Assert.Equal("tpubDDdeNbNDRgqestPX5XEJM8ELAq6eR5cne5RPbBHHvWSSiLHNHehsrn1kGCijMnHFSsFFQMqHcdMfGzDL3pWHRasPMhcGRqZ4tFankQ3i4ok-[legacy]", parsed.ToString());
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public void CanDisablePaymentMethods()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");
                user.RegisterDerivationScheme("LTC");
                user.RegisterLightningNode("BTC", LightningConnectionType.CLightning);

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

                var derivationVM = (DerivationSchemeViewModel)Assert.IsType<ViewResult>(controller.AddDerivationScheme(user.StoreId, "BTC")).Model;
                Assert.True(derivationVM.Enabled);
                derivationVM.Enabled = false;
                Assert.IsType<RedirectToActionResult>(controller.AddDerivationScheme(user.StoreId, derivationVM, "BTC").GetAwaiter().GetResult());
                derivationVM = (DerivationSchemeViewModel)Assert.IsType<ViewResult>(controller.AddDerivationScheme(user.StoreId, "BTC")).Model;
                // Confirmation
                controller.AddDerivationScheme(user.StoreId, derivationVM, "BTC").GetAwaiter().GetResult();
                Assert.False(derivationVM.Enabled);
                derivationVM = (DerivationSchemeViewModel)Assert.IsType<ViewResult>(controller.AddDerivationScheme(user.StoreId, "BTC")).Model;
                Assert.False(derivationVM.Enabled);

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
            }
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async Task CanSetPaymentMethodLimits()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
                await tester.EnsureChannelsSetup();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");
                user.RegisterLightningNode("BTC", LightningConnectionType.Charge);
                var vm = Assert.IsType<CheckoutExperienceViewModel>(Assert.IsType<ViewResult>(user.GetController<StoresController>().CheckoutExperience()).Model);
                vm.LightningMaxValue = "2 USD";
                vm.OnChainMinValue = "5 USD";
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

                invoice = user.BitPay.CreateInvoice(new Invoice()
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

        [Fact]
        [Trait("Integration", "Integration")]
        public void CanUsePoSApp()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
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
                Assert.IsType<RedirectResult>(publicApps.ViewPointOfSale(appId, 0, null, null, null, null, "orange").Result);

                //
                var invoices = user.BitPay.GetInvoices();
                var orangeInvoice = invoices.First();
                Assert.Equal(10.00m, orangeInvoice.Price);
                Assert.Equal("CAD", orangeInvoice.Currency);
                Assert.Equal("orange", orangeInvoice.ItemDesc);

                // testing custom amount
                Assert.IsType<RedirectResult>(publicApps.ViewPointOfSale(appId, 5, null, null, null, null, "donation").Result);
                invoices = user.BitPay.GetInvoices();
                var donationInvoice = invoices.First(); // expected behavior is that new invoice should now be first
                Assert.Equal(5m, donationInvoice.Price);
                Assert.Equal("CAD", donationInvoice.Currency);
                Assert.Equal("donation", donationInvoice.ItemDesc);
            }
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public void CanCreateAndDeleteApps()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
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

        [Fact]
        [Trait("Integration", "Integration")]
        public void InvoiceFlowThroughDifferentStatesCorrectly()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");
                var invoice = user.BitPay.CreateInvoice(new Invoice()
                {
                    Price = 5000.0m,
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
                Eventually(() =>
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

                var invoiceEntity = repo.GetInvoice(null, invoice.Id, true).GetAwaiter().GetResult();
                Assert.Single(invoiceEntity.HistoricalAddresses);
                Assert.Null(invoiceEntity.HistoricalAddresses[0].UnAssigned);

                Money secondPayment = Money.Zero;

                Eventually(() =>
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

                    invoiceEntity = repo.GetInvoice(null, invoice.Id, true).GetAwaiter().GetResult();
                    var historical1 = invoiceEntity.HistoricalAddresses.FirstOrDefault(h => h.GetAddress() == invoice.BitcoinAddress);
                    Assert.NotNull(historical1.UnAssigned);
                    var historical2 = invoiceEntity.HistoricalAddresses.FirstOrDefault(h => h.GetAddress() == localInvoice.BitcoinAddress);
                    Assert.Null(historical2.UnAssigned);
                    invoiceAddress = BitcoinAddress.Create(localInvoice.BitcoinAddress, cashCow.Network);
                    secondPayment = localInvoice.BtcDue;
                });

                cashCow.SendToAddress(invoiceAddress, secondPayment);

                Eventually(() =>
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

                Eventually(() =>
                {
                    var localInvoice = user.BitPay.GetInvoice(invoice.Id, Facade.Merchant);
                    Assert.Equal("confirmed", localInvoice.Status);
                });

                cashCow.Generate(5); //Now should be complete

                Eventually(() =>
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

                Eventually(() =>
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

                Eventually(() =>
                {
                    var localInvoice = user.BitPay.GetInvoice(invoice.Id, Facade.Merchant);
                    Assert.Equal("confirmed", localInvoice.Status);
                    Assert.Equal(Money.Zero, localInvoice.BtcDue);
                    Assert.Equal("paidOver", (string)((JValue)localInvoice.ExceptionStatus).Value);
                });
            }
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public void CheckQuadrigacxRateProvider()
        {
            var quadri = new QuadrigacxRateProvider();
            var rates = quadri.GetRatesAsync().GetAwaiter().GetResult();
            Assert.NotEmpty(rates);
            Assert.NotEqual(0.0m, rates.First().BidAsk.Bid);
            Assert.NotEqual(0.0m, rates.GetRate(QuadrigacxRateProvider.QuadrigacxName, CurrencyPair.Parse("BTC_CAD")).Bid);
            Assert.NotEqual(0.0m, rates.GetRate(QuadrigacxRateProvider.QuadrigacxName, CurrencyPair.Parse("BTC_USD")).Bid);
            Assert.NotEqual(0.0m, rates.GetRate(QuadrigacxRateProvider.QuadrigacxName, CurrencyPair.Parse("LTC_CAD")).Bid);
            Assert.Null(rates.GetRate(QuadrigacxRateProvider.QuadrigacxName, CurrencyPair.Parse("LTC_USD")));
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public void CanQueryDirectProviders()
        {
            var factory = CreateBTCPayRateFactory();

            foreach (var result in factory
                .Providers
                .Where(p => p.Value is BackgroundFetcherRateProvider)
                .Select(p => (ExpectedName: p.Key, ResultAsync: p.Value.GetRatesAsync(), Fetcher: (BackgroundFetcherRateProvider)p.Value))
                .ToList())
            {
                result.Fetcher.InvalidateCache();
                var exchangeRates = result.ResultAsync.Result;
                result.Fetcher.InvalidateCache();
                Assert.NotNull(exchangeRates);
                Assert.NotEmpty(exchangeRates);
                Assert.NotEmpty(exchangeRates.ByExchange[result.ExpectedName]);

                // This check if the currency pair is using right currency pair
                Assert.Contains(exchangeRates.ByExchange[result.ExpectedName],
                        e => (e.CurrencyPair == new CurrencyPair("BTC", "USD") ||
                               e.CurrencyPair == new CurrencyPair("BTC", "EUR") ||
                               e.CurrencyPair == new CurrencyPair("BTC", "USDT"))
                               && e.BidAsk.Bid > 1.0m // 1BTC will always be more than 1USD
                               );
            }
            // Kraken emit one request only after first GetRates
            factory.Providers["kraken"].GetRatesAsync().GetAwaiter().GetResult();
        }

        [Fact]
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
            var result = fetcher.FetchRates(pairs, rules);
            foreach (var value in result)
            {
                var rateResult = value.Value.GetAwaiter().GetResult();
                Logs.Tester.LogInformation($"Testing {value.Key.ToString()}");
                Assert.True(rateResult.BidAsk != null, $"Impossible to get the rate {rateResult.EvaluatedRule}");
            }
        }

        public static RateProviderFactory CreateBTCPayRateFactory()
        {
            return new RateProviderFactory(CreateMemoryCache(), null, new CoinAverageSettings());
        }

        private static MemoryCacheOptions CreateMemoryCache()
        {
            return new MemoryCacheOptions() { ExpirationScanFrequency = TimeSpan.FromSeconds(1.0) };
        }

        class SpyRateProvider : IRateProvider
        {
            public bool Hit { get; set; }
            public Task<ExchangeRates> GetRatesAsync()
            {
                Hit = true;
                var rates = new ExchangeRates();
                rates.Add(new ExchangeRate("coinaverage", CurrencyPair.Parse("BTC_USD"), new BidAsk(5000)));
                return Task.FromResult(rates);
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

        [Fact]
        [Trait("Integration", "Integration")]
        public async Task CheckLogsRoute()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");
                
                var serverController = user.GetController<ServerController>();
                var vm = Assert.IsType<LogsViewModel>(Assert.IsType<ViewResult>(await serverController.LogsView()).Model);
            }
        } 
       
        [Fact]
        [Trait("Fast", "Fast")]
        public void CheckRatesProvider()
        {
            var spy = new SpyRateProvider();
            RateRules.TryParse("X_X = coinaverage(X_X);", out var rateRules);

            var factory = CreateBTCPayRateFactory();
            factory.Providers.Clear();
            factory.Providers.Add("coinaverage", new CachedRateProvider("coinaverage", spy, new MemoryCache(CreateMemoryCache())));
            factory.Providers.Add("bittrex", new CachedRateProvider("bittrex", spy, new MemoryCache(CreateMemoryCache())));
            factory.CacheSpan = TimeSpan.FromSeconds(1);

            var fetcher = new RateFetcher(factory);

            var fetchedRate = fetcher.FetchRate(CurrencyPair.Parse("BTC_USD"), rateRules).GetAwaiter().GetResult();
            spy.AssertHit();
            fetchedRate = fetcher.FetchRate(CurrencyPair.Parse("BTC_USD"), rateRules).GetAwaiter().GetResult();
            spy.AssertNotHit();

            Thread.Sleep(3000);
            fetchedRate = fetcher.FetchRate(CurrencyPair.Parse("BTC_USD"), rateRules).GetAwaiter().GetResult();
            spy.AssertHit();
            fetchedRate = fetcher.FetchRate(CurrencyPair.Parse("BTC_USD"), rateRules).GetAwaiter().GetResult();
            spy.AssertNotHit();
            // Should cache at exchange level so this should hit the cache
            var fetchedRate2 = fetcher.FetchRate(CurrencyPair.Parse("LTC_USD"), rateRules).GetAwaiter().GetResult();
            spy.AssertNotHit();
            Assert.Null(fetchedRate2.BidAsk);
            Assert.Equal(RateRulesErrors.RateUnavailable, fetchedRate2.Errors.First());

            // Should cache at exchange level this should not hit the cache as it is different exchange
            RateRules.TryParse("X_X = bittrex(X_X);", out rateRules);
            fetchedRate = fetcher.FetchRate(CurrencyPair.Parse("BTC_USD"), rateRules).GetAwaiter().GetResult();
            spy.AssertHit();

            factory.Providers.Clear();
            var fetch = new BackgroundFetcherRateProvider(spy);
            fetch.DoNotAutoFetchIfExpired = true;
            factory.Providers.Add("bittrex", fetch);
            fetchedRate = fetcher.FetchRate(CurrencyPair.Parse("BTC_USD"), rateRules).GetAwaiter().GetResult();
            spy.AssertHit();
            fetchedRate = fetcher.FetchRate(CurrencyPair.Parse("BTC_USD"), rateRules).GetAwaiter().GetResult();
            spy.AssertNotHit();
            fetch.UpdateIfNecessary().GetAwaiter().GetResult();
            spy.AssertNotHit();
            fetch.RefreshRate = TimeSpan.FromSeconds(1.0);
            Thread.Sleep(1020);
            fetchedRate = fetcher.FetchRate(CurrencyPair.Parse("BTC_USD"), rateRules).GetAwaiter().GetResult();
            spy.AssertNotHit();
            fetch.ValidatyTime = TimeSpan.FromSeconds(1.0);
            fetch.UpdateIfNecessary().GetAwaiter().GetResult();
            spy.AssertHit();
            fetch.GetRatesAsync().GetAwaiter().GetResult();
            Thread.Sleep(1000);
            Assert.Throws<InvalidOperationException>(() => fetch.GetRatesAsync().GetAwaiter().GetResult());
        }

        private static bool IsMapped(Invoice invoice, ApplicationDbContext ctx)
        {
            var h = BitcoinAddress.Create(invoice.BitcoinAddress, Network.RegTest).ScriptPubKey.Hash.ToString();
            return ctx.AddressInvoices.FirstOrDefault(i => i.InvoiceDataId == invoice.Id && i.GetAddress() == h) != null;
        }

        private void Eventually(Action act)
        {
            CancellationTokenSource cts = new CancellationTokenSource(20000);
            while (true)
            {
                try
                {
                    act();
                    break;
                }
                catch (XunitException) when (!cts.Token.IsCancellationRequested)
                {
                    cts.Token.WaitHandle.WaitOne(500);
                }
            }
        }

        private async Task EventuallyAsync(Func<Task> act)
        {
            CancellationTokenSource cts = new CancellationTokenSource(20000);
            while (true)
            {
                try
                {
                    await act();
                    break;
                }
                catch (XunitException) when (!cts.Token.IsCancellationRequested)
                {
                    await Task.Delay(500);
                }
            }
        }
    }
}
