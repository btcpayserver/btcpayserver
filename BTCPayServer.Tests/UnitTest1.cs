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
using BTCPayServer.Rating;
using ExchangeSharp;

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
        public void CanPayUsingBIP70()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
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
        public void CanUseLightMoney()
        {
            var light = LightMoney.MilliSatoshis(1);
            Assert.Equal("0.00000000001", light.ToString());

            light = LightMoney.MilliSatoshis(200000);
            Assert.Equal(200m, light.ToDecimal(LightMoneyUnit.Satoshi));
            Assert.Equal(0.00000001m * 200m, light.ToDecimal(LightMoneyUnit.BTC));
        }

        [Fact]
        public void CanSetLightningServer()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
                var user = tester.NewAccount();
                user.GrantAccess();
                var storeController = user.GetController<StoresController>();
                Assert.IsType<ViewResult>(storeController.UpdateStore());
                Assert.IsType<ViewResult>(storeController.AddLightningNode(user.StoreId, "BTC"));

                var testResult = storeController.AddLightningNode(user.StoreId, new LightningNodeViewModel()
                {
                    Url = tester.MerchantCharge.Client.Uri.AbsoluteUri
                }, "test", "BTC").GetAwaiter().GetResult();
                Assert.DoesNotContain("Error", ((LightningNodeViewModel)Assert.IsType<ViewResult>(testResult).Model).StatusMessage, StringComparison.OrdinalIgnoreCase);
                Assert.True(storeController.ModelState.IsValid);

                Assert.IsType<RedirectToActionResult>(storeController.AddLightningNode(user.StoreId, new LightningNodeViewModel()
                {
                    Url = tester.MerchantCharge.Client.Uri.AbsoluteUri
                }, "save", "BTC").GetAwaiter().GetResult());

                var storeVm = Assert.IsType<Models.StoreViewModels.StoreViewModel>(Assert.IsType<ViewResult>(storeController.UpdateStore()).Model);
                Assert.Single(storeVm.LightningNodes.Where(l => !string.IsNullOrEmpty(l.Address)));
            }
        }

        [Fact]
        public void CanParseLightningURL()
        {
            LightningConnectionString conn = null;
            Assert.True(LightningConnectionString.TryParse("/test/a", out conn));
            Assert.Equal("unix://test/a", conn.ToString());
            Assert.Equal("unix://test/a", conn.ToUri(true).AbsoluteUri);
            Assert.Equal("unix://test/a", conn.ToUri(false).AbsoluteUri);
            Assert.Equal(LightningConnectionType.CLightning, conn.ConnectionType);

            Assert.True(LightningConnectionString.TryParse("unix://test/a", out conn));
            Assert.Equal("unix://test/a", conn.ToString());
            Assert.Equal("unix://test/a", conn.ToUri(true).AbsoluteUri);
            Assert.Equal("unix://test/a", conn.ToUri(false).AbsoluteUri);
            Assert.Equal(LightningConnectionType.CLightning, conn.ConnectionType);

            Assert.True(LightningConnectionString.TryParse("unix://test/a", out conn));
            Assert.Equal("unix://test/a", conn.ToString());
            Assert.Equal("unix://test/a", conn.ToUri(true).AbsoluteUri);
            Assert.Equal("unix://test/a", conn.ToUri(false).AbsoluteUri);
            Assert.Equal(LightningConnectionType.CLightning, conn.ConnectionType);

            Assert.True(LightningConnectionString.TryParse("tcp://test/a", out conn));
            Assert.Equal("tcp://test/a", conn.ToString());
            Assert.Equal("tcp://test/a", conn.ToUri(true).AbsoluteUri);
            Assert.Equal("tcp://test/a", conn.ToUri(false).AbsoluteUri);
            Assert.Equal(LightningConnectionType.CLightning, conn.ConnectionType);

            Assert.True(LightningConnectionString.TryParse("http://aaa:bbb@test/a", out conn));
            Assert.Equal("http://aaa:bbb@test/a", conn.ToString());
            Assert.Equal("http://aaa:bbb@test/a", conn.ToUri(true).AbsoluteUri);
            Assert.Equal("http://test/a", conn.ToUri(false).AbsoluteUri);
            Assert.Equal(LightningConnectionType.Charge, conn.ConnectionType);
            Assert.Equal("aaa", conn.Username);
            Assert.Equal("bbb", conn.Password);

            Assert.False(LightningConnectionString.TryParse("lol://aaa:bbb@test/a", out conn));
            Assert.False(LightningConnectionString.TryParse("https://test/a", out conn));
            Assert.False(LightningConnectionString.TryParse("unix://dwewoi:dwdwqd@test/a", out conn));
        }

        [Fact]
        public void CanSendLightningPayment2()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
                tester.PrepareLightning();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterLightningNode("BTC", LightningConnectionType.CLightning);
                user.RegisterDerivationScheme("BTC");

                var invoice = user.BitPay.CreateInvoice(new Invoice()
                {
                    Price = 0.01m,
                    Currency = "USD",
                    PosData = "posData",
                    OrderId = "orderId",
                    ItemDesc = "Some description"
                });

                tester.SendLightningPayment(invoice);

                Eventually(() =>
                {
                    var localInvoice = user.BitPay.GetInvoice(invoice.Id);
                    Assert.Equal("complete", localInvoice.Status);
                    Assert.Equal("False", localInvoice.ExceptionStatus.ToString());
                });
            }
        }

        [Fact]
        public void CanSendLightningPayment()
        {

            using (var tester = ServerTester.Create())
            {
                tester.Start();
                tester.PrepareLightning();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterLightningNode("BTC", LightningConnectionType.Charge);
                user.RegisterDerivationScheme("BTC");

                var invoice = user.BitPay.CreateInvoice(new Invoice()
                {
                    Price = 0.01m,
                    Currency = "USD",
                    PosData = "posData",
                    OrderId = "orderId",
                    ItemDesc = "Some description"
                });

                tester.SendLightningPayment(invoice);

                Eventually(() =>
                {
                    var localInvoice = user.BitPay.GetInvoice(invoice.Id);
                    Assert.Equal("complete", localInvoice.Status);
                    Assert.Equal("False", localInvoice.ExceptionStatus.ToString());
                });


                Task.WaitAll(Enumerable.Range(0, 5)
                    .Select(_ => CanSendLightningPaymentCore(tester, user))
                    .ToArray());
            }
        }

        async Task CanSendLightningPaymentCore(ServerTester tester, TestAccount user)
        {
            await Task.Delay(TimeSpan.FromSeconds(RandomUtils.GetUInt32() % 5));
            var invoice = await user.BitPay.CreateInvoiceAsync(new Invoice()
            {
                Price = 0.01m,
                Currency = "USD",
                PosData = "posData",
                OrderId = "orderId",
                ItemDesc = "Some description"
            });
            await tester.SendLightningPaymentAsync(invoice);
            await EventuallyAsync(async () =>
            {
                var localInvoice = await user.BitPay.GetInvoiceAsync(invoice.Id);
                Assert.Equal("complete", localInvoice.Status);
                Assert.Equal("False", localInvoice.ExceptionStatus.ToString());
            });
        }

        [Fact]
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

        private void AssertSearchInvoice(TestAccount acc, bool expected, string invoiceId, string filter)
        {
            var result = (Models.InvoicingModels.InvoicesModel)((ViewResult)acc.GetController<InvoiceController>().ListInvoices(filter).Result).Model;
            Assert.Equal(expected, result.Invoices.Any(i => i.InvoiceId == invoiceId));
        }

        [Fact]
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
                var invoiceAddress = BitcoinAddress.Create(invoice.BitcoinAddress, user.SupportedNetwork.NBitcoinNetwork);

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
                var replaced = tester.ExplorerNode.SignRawTransaction(tx);
                tester.ExplorerNode.SendRawTransaction(replaced);
                var test = tester.ExplorerClient.GetUTXOs(user.DerivationScheme, null);
                Eventually(() =>
                {
                    invoice = user.BitPay.GetInvoice(invoice.Id);
                    Assert.Equal(payment2, invoice.BtcPaid);
                    Assert.Equal("False", invoice.ExceptionStatus.ToString());
                });
            }
        }

        [Fact]
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
                var bitflyer = CreateInvoice(tester, user, "bitflyer");
                var bitflyer2 = CreateInvoice(tester, user, "bitflyer");
                Assert.Equal(bitflyer, bitflyer2); // Should be equal because cache
                rates.Add(bitflyer);

                foreach (var rate in rates)
                {
                    Assert.Single(rates.Where(r => r == rate));
                }
            }
        }

        private static decimal CreateInvoice(ServerTester tester, TestAccount user, string exchange)
        {
            var storeController = user.GetController<StoresController>();
            var vm = (RatesViewModel)((ViewResult)storeController.Rates()).Model;
            vm.PreferredExchange = exchange;
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
            return invoice2.CryptoInfo[0].Rate;
        }

        [Fact]
        public void CanTweakRate()
        {
            using (var tester = ServerTester.Create())
            {
                tester.PayTester.MockRates = false;
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


                var storeController = user.GetController<StoresController>();
                var vm = (RatesViewModel)((ViewResult)storeController.Rates()).Model;
                Assert.Equal(1.0, vm.RateMultiplier);
                vm.RateMultiplier = 0.5;
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

                Assert.True(invoice2.BtcPrice.Almost(invoice1.BtcPrice * 2, 0.00001m));
            }
        }

        [Fact]
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
                Assert.Equal(1.0, rateVm.RateMultiplier);
                Assert.Null(rateVm.TestRateRules);

                rateVm.PreferredExchange = "bitflyer";
                Assert.IsType<RedirectToActionResult>(store.Rates(rateVm, "Save").Result);
                rateVm = Assert.IsType<RatesViewModel>(Assert.IsType<ViewResult>(store.Rates()).Model);
                Assert.Equal("bitflyer", rateVm.PreferredExchange);

                rateVm.ScriptTest = "BTC_JPY,BTC_CAD";
                rateVm.RateMultiplier = 1.1;
                store = user.GetController<StoresController>();
                rateVm = Assert.IsType<RatesViewModel>(Assert.IsType<ViewResult>(store.Rates(rateVm, "Test").Result).Model);
                Assert.NotNull(rateVm.TestRateRules);
                Assert.Equal(2, rateVm.TestRateRules.Count);
                Assert.False(rateVm.TestRateRules[0].Error);
                Assert.StartsWith("(bitflyer(BTC_JPY)) * 1.10 =", rateVm.TestRateRules[0].Rule, StringComparison.OrdinalIgnoreCase);
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
                Assert.Contains("(bitflyer(BTC_JPY)) * 1.10 = ", rateVm.TestRateRules[0].Rule, StringComparison.OrdinalIgnoreCase);

                rateVm.ScriptTest = "BTC_USD,BTC_CAD,DOGE_USD,DOGE_CAD";
                rateVm.Script = "DOGE_X = bittrex(DOGE_BTC) * BTC_X;\n" +
                                "X_CAD = quadrigacx(X_CAD);\n" +
                                 "X_X = gdax(X_X);";
                rateVm.RateMultiplier = 0.5;
                rateVm = Assert.IsType<RatesViewModel>(Assert.IsType<ViewResult>(store.Rates(rateVm, "Test").Result).Model);
                Assert.True(rateVm.TestRateRules.All(t => !t.Error));
                Assert.IsType<RedirectToActionResult>(store.Rates(rateVm, "Save").Result);
                store = user.GetController<StoresController>();
                rateVm = Assert.IsType<RatesViewModel>(Assert.IsType<ViewResult>(store.Rates()).Model);
                Assert.Equal(0.5, rateVm.RateMultiplier);
                Assert.True(rateVm.ShowScripting);
                Assert.Contains("DOGE_X", rateVm.Script, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
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
            }
        }

        [Fact]
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
        }

        [Fact]
        public void CanSetPaymentMethodLimits()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
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
                vmpos.Template =
                    "apple:\n" +
                    "  price: 5.0\n" +
                    "  title: good apple\n" +
                    "orange:\n" +
                    "  price: 10.0\n";
                Assert.IsType<RedirectToActionResult>(apps.UpdatePointOfSale(appId, vmpos).Result);
                vmpos = Assert.IsType<UpdatePointOfSaleViewModel>(Assert.IsType<ViewResult>(apps.UpdatePointOfSale(appId).Result).Model);
                Assert.Equal("hello", vmpos.Title);
                var vmview = Assert.IsType<ViewPointOfSaleViewModel>(Assert.IsType<ViewResult>(apps.ViewPointOfSale(appId).Result).Model);
                Assert.Equal("hello", vmview.Title);
                Assert.Equal(2, vmview.Items.Length);
                Assert.Equal("good apple", vmview.Items[0].Title);
                Assert.Equal("orange", vmview.Items[1].Title);
                Assert.Equal(10.0m, vmview.Items[1].Price.Value);
                Assert.Equal("$5.00", vmview.Items[0].Price.Formatted);
                Assert.IsType<RedirectResult>(apps.ViewPointOfSale(appId, 0, "orange").Result);
                var invoice = user.BitPay.GetInvoices().First();
                Assert.Equal(10.00m, invoice.Price);
                Assert.Equal("CAD", invoice.Currency);
                Assert.Equal("orange", invoice.ItemDesc);
            }
        }

        [Fact]
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
                var redirectToAction = Assert.IsType<RedirectToActionResult>(apps.CreateApp(vm).Result);
                Assert.Equal(nameof(apps.ListApps), redirectToAction.ActionName);
                var appList = Assert.IsType<ListAppsViewModel>(Assert.IsType<ViewResult>(apps.ListApps().Result).Model);
                var appList2 = Assert.IsType<ListAppsViewModel>(Assert.IsType<ViewResult>(apps2.ListApps().Result).Model);
                Assert.Single(appList.Apps);
                Assert.Empty(appList2.Apps);
                Assert.Equal("test", appList.Apps[0].AppName);
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

                cashCow.SendToAddress(invoiceAddress, invoice.BtcDue + Money.Coins(1));

                Eventually(() =>
                {
                    var localInvoice = user.BitPay.GetInvoice(invoice.Id, Facade.Merchant);
                    Assert.Equal("paid", localInvoice.Status);
                    Assert.Equal(Money.Zero, localInvoice.BtcDue);
                    Assert.Equal("paidOver", (string)((JValue)localInvoice.ExceptionStatus).Value);
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
        public void CheckQuadrigacxRateProvider()
        {
            var quadri = new QuadrigacxRateProvider();
            var rates = quadri.GetRatesAsync().GetAwaiter().GetResult();
            Assert.NotEmpty(rates);
            Assert.NotEqual(0.0m, rates.First().Value);
            Assert.NotEqual(0.0m, rates.GetRate(QuadrigacxRateProvider.QuadrigacxName, CurrencyPair.Parse("BTC_CAD")).Value);
            Assert.NotEqual(0.0m, rates.GetRate(QuadrigacxRateProvider.QuadrigacxName, CurrencyPair.Parse("BTC_USD")).Value);
            Assert.NotEqual(0.0m, rates.GetRate(QuadrigacxRateProvider.QuadrigacxName, CurrencyPair.Parse("LTC_CAD")).Value);
            Assert.Null(rates.GetRate(QuadrigacxRateProvider.QuadrigacxName, CurrencyPair.Parse("LTC_USD")));
        }

        [Fact]
        public void CanQueryDirectProviders()
        {
            var provider = new BTCPayNetworkProvider(NetworkType.Mainnet);
            var factory = CreateBTCPayRateFactory(provider);
            
            foreach (var result in factory
                .DirectProviders
                .Select(p => (ExpectedName: p.Key, ResultAsync: p.Value.GetRatesAsync()))
                .ToList())
            {
                var exchangeRates = result.ResultAsync.Result;
                Assert.NotNull(exchangeRates);
                Assert.NotEmpty(exchangeRates);
                Assert.NotEmpty(exchangeRates.ByExchange[result.ExpectedName]);

                // This check if the currency pair is using right currency pair
                Assert.Contains(exchangeRates.ByExchange[result.ExpectedName], 
                        e => ( e.CurrencyPair == new CurrencyPair("BTC", "USD") ||
                               e.CurrencyPair == new CurrencyPair("BTC", "EUR") ||
                               e.CurrencyPair == new CurrencyPair("BTC", "USDT"))
                               && e.Value > 1.0m // 1BTC will always be more than 1USD
                               );
            }
        }

        [Fact]
        public void CanGetRateCryptoCurrenciesByDefault()
        {
            var provider = new BTCPayNetworkProvider(NetworkType.Mainnet);
            var factory = CreateBTCPayRateFactory(provider);

            var pairs =
                    provider.GetAll()
                    .Select(c => new CurrencyPair(c.CryptoCode, "USD"))
                    .ToHashSet();

            var rules = new StoreBlob().GetDefaultRateRules(provider);
            var result = factory.FetchRates(pairs, rules);
            foreach (var value in result)
            {
                var rateResult = value.Value.GetAwaiter().GetResult();
                Assert.NotNull(rateResult.Value);
            }
        }

        private static BTCPayRateProviderFactory CreateBTCPayRateFactory(BTCPayNetworkProvider provider)
        {
            return new BTCPayRateProviderFactory(new MemoryCacheOptions() { ExpirationScanFrequency = TimeSpan.FromSeconds(1.0) }, provider, null, new CoinAverageSettings());
        }

        [Fact]
        public void CheckRatesProvider()
        {
            var provider = new BTCPayNetworkProvider(NetworkType.Mainnet);
            var coinAverage = new CoinAverageRateProvider();
            var rates = coinAverage.GetRatesAsync().GetAwaiter().GetResult();
            Assert.NotNull(rates.GetRate("coinaverage", new CurrencyPair("BTC", "JPY")));
            var ratesBitpay = new BitpayRateProvider(new Bitpay(new Key(), new Uri("https://bitpay.com/"))).GetRatesAsync().GetAwaiter().GetResult();
            Assert.NotNull(ratesBitpay.GetRate("bitpay", new CurrencyPair("BTC", "JPY")));

            RateRules.TryParse("X_X = coinaverage(X_X);", out var rateRules);

            var factory = CreateBTCPayRateFactory(provider);
            factory.DirectProviders.Clear();
            factory.CacheSpan = TimeSpan.FromSeconds(10);

            var fetchedRate = factory.FetchRate(CurrencyPair.Parse("BTC_USD"), rateRules).GetAwaiter().GetResult();
            Assert.False(fetchedRate.Cached);
            fetchedRate = factory.FetchRate(CurrencyPair.Parse("BTC_USD"), rateRules).GetAwaiter().GetResult();
            Assert.True(fetchedRate.Cached);

            Thread.Sleep(11000);
            fetchedRate = factory.FetchRate(CurrencyPair.Parse("BTC_USD"), rateRules).GetAwaiter().GetResult();
            Assert.False(fetchedRate.Cached);
            fetchedRate = factory.FetchRate(CurrencyPair.Parse("BTC_USD"), rateRules).GetAwaiter().GetResult();
            Assert.True(fetchedRate.Cached);
            // Should cache at exchange level so this should hit the cache
            var fetchedRate2 = factory.FetchRate(CurrencyPair.Parse("LTC_USD"), rateRules).GetAwaiter().GetResult();
            Assert.True(fetchedRate.Cached);
            Assert.NotEqual(fetchedRate.Value.Value, fetchedRate2.Value.Value);

            // Should cache at exchange level this should not hit the cache as it is different exchange
            RateRules.TryParse("X_X = bittrex(X_X);", out rateRules);
            fetchedRate = factory.FetchRate(CurrencyPair.Parse("BTC_USD"), rateRules).GetAwaiter().GetResult();
            Assert.False(fetchedRate.Cached);

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
