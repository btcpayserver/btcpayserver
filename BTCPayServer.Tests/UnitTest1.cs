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
using BTCPayServer.Payments.Lightning.Eclair;
using System.Collections.Generic;
using BTCPayServer.Models.StoreViewModels;
using System.Threading.Tasks;
using System.Globalization;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.HostedServices;
using BTCPayServer.Payments.Lightning;

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
            var dummy = new Key().PubKey.GetAddress(Network.RegTest);
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
            entity.TxFee = Money.Coins(0.1m);
            entity.Rate = 5000;

            entity.Payments = new System.Collections.Generic.List<PaymentEntity>();
            entity.ProductInformation = new ProductInformation() { Price = 5000 };

            // Some check that handling legacy stuff does not break things
            var paymentMethod = entity.GetPaymentMethods(null, true).TryGet("BTC", PaymentTypes.BTCLike);
            paymentMethod.Calculate();
            Assert.NotNull(paymentMethod);
            Assert.Null(entity.GetPaymentMethods(null, false).TryGet("BTC", PaymentTypes.BTCLike));
            entity.SetPaymentMethod(new PaymentMethod() { ParentEntity = entity, Rate = entity.Rate, CryptoCode = "BTC", TxFee = entity.TxFee });
            Assert.NotNull(entity.GetPaymentMethods(null, false).TryGet("BTC", PaymentTypes.BTCLike));
            Assert.NotNull(entity.GetPaymentMethods(null, true).TryGet("BTC", PaymentTypes.BTCLike));
            ////////////////////

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
                    Price = 5000.0,
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
                var storeController = tester.PayTester.GetController<StoresController>(user.UserId);
                Assert.IsType<ViewResult>(storeController.UpdateStore(user.StoreId).GetAwaiter().GetResult());
                Assert.IsType<ViewResult>(storeController.AddLightningNode(user.StoreId).GetAwaiter().GetResult());

                var testResult = storeController.AddLightningNode(user.StoreId, new LightningNodeViewModel()
                {
                    CryptoCurrency = "BTC",
                    Url = tester.MerchantCharge.Client.Uri.AbsoluteUri
                }, "test").GetAwaiter().GetResult();
                Assert.DoesNotContain("Error", ((LightningNodeViewModel)Assert.IsType<ViewResult>(testResult).Model).StatusMessage, StringComparison.OrdinalIgnoreCase);
                Assert.True(storeController.ModelState.IsValid);

                Assert.IsType<RedirectToActionResult>(storeController.AddLightningNode(user.StoreId, new LightningNodeViewModel()
                {
                    CryptoCurrency = "BTC",
                    Url = tester.MerchantCharge.Client.Uri.AbsoluteUri
                }, "save").GetAwaiter().GetResult());

                var storeVm = Assert.IsType<Models.StoreViewModels.StoreViewModel>(Assert.IsType<ViewResult>(storeController.UpdateStore(user.StoreId).GetAwaiter().GetResult()).Model);
                Assert.Single(storeVm.LightningNodes);
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
                user.RegisterLightningNode("BTC");
                user.RegisterDerivationScheme("BTC");

                var invoice = user.BitPay.CreateInvoice(new Invoice()
                {
                    Price = 0.01,
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
                Price = 0.01,
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

                var controller = tester.PayTester.GetController<StoresController>(acc.UserId);
                var token = (RedirectToActionResult)controller.CreateToken(acc.StoreId, new Models.StoreViewModels.CreateTokenViewModel()
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
                        Price = 5.0,
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
                var store = acc.CreateStore();
                var pairingCode = acc.BitPay.RequestClientAuthorization("test", Facade.Merchant);
                Assert.IsType<RedirectToActionResult>(store.Pair(pairingCode.ToString(), acc.StoreId).GetAwaiter().GetResult());

                pairingCode = acc.BitPay.RequestClientAuthorization("test1", Facade.Merchant);
                var store2 = acc.CreateStore();
                store2.Pair(pairingCode.ToString(), store2.CreatedStoreId).GetAwaiter().GetResult();
                Assert.Contains(nameof(PairingResult.ReusedKey), store2.StatusMessage, StringComparison.CurrentCultureIgnoreCase);
            }
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
                    Price = 5000.0,
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
            Assert.Equal("abc", search.Filters["storeid"]);
            Assert.Equal("abed", search.Filters["status"]);
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
            }
        }


        [Fact]
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
                    Price = 5000.0,
                    Currency = "USD",
                    PosData = "posData",
                    OrderId = "orderId",
                    ItemDesc = "Some description",
                    FullNotifications = true
                }, Facade.Merchant);


                var storeController = tester.PayTester.GetController<StoresController>(user.UserId);
                var vm = (StoreViewModel)((ViewResult)storeController.UpdateStore(user.StoreId).Result).Model;
                Assert.Equal(1.0, vm.RateMultiplier);
                vm.RateMultiplier = 0.5;
                storeController.UpdateStore(user.StoreId, vm).Wait();


                var invoice2 = user.BitPay.CreateInvoice(new Invoice()
                {
                    Price = 5000.0,
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
                Assert.NotEqual(1.0, invoice.Rate);
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
                    Price = 5000.0,
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
                    Price = 5000.0,
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
                    Price = 5000.0,
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
                        StoreId = user.StoreId,
                        TextSearch = invoice.OrderId
                    }).GetAwaiter().GetResult();
                    Assert.Single(textSearchResult);
                    textSearchResult = tester.PayTester.InvoiceRepository.GetInvoices(new InvoiceQuery()
                    {
                        StoreId = user.StoreId,
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

                var rate = user.BitPay.GetRates();

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
                    Assert.NotEqual(0.0, localInvoice.Rate);
                });

                invoice = user.BitPay.CreateInvoice(new Invoice()
                {
                    Price = 5000.0,
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
        public void CheckRatesProvider()
        {
            var coinAverage = new CoinAverageRateProvider("BTC");
            var jpy = coinAverage.GetRateAsync("JPY").GetAwaiter().GetResult();
            var jpy2 = new BitpayRateProvider(new Bitpay(new Key(), new Uri("https://bitpay.com/"))).GetRateAsync("JPY").GetAwaiter().GetResult();

            var cached = new CachedRateProvider("BTC", coinAverage, new MemoryCache(new MemoryCacheOptions() { ExpirationScanFrequency = TimeSpan.FromSeconds(1.0) }));
            cached.CacheSpan = TimeSpan.FromSeconds(10);
            var a = cached.GetRateAsync("JPY").GetAwaiter().GetResult();
            var b = cached.GetRateAsync("JPY").GetAwaiter().GetResult();
            //Manually check that cache get hit after 10 sec
            var c = cached.GetRateAsync("JPY").GetAwaiter().GetResult();

            var bitstamp = new CoinAverageRateProvider("BTC") { Exchange = "bitstamp" };
            var bitstampRate = bitstamp.GetRateAsync("USD").GetAwaiter().GetResult();
            Assert.Throws<RateUnavailableException>(() => bitstamp.GetRateAsync("XXXXX").GetAwaiter().GetResult());
        }

        private static bool IsMapped(Invoice invoice, ApplicationDbContext ctx)
        {
            var h = BitcoinAddress.Create(invoice.BitcoinAddress).ScriptPubKey.Hash.ToString();
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
