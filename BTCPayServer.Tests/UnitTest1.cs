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
        public void CanCalculateCryptoDue()
        {
            var entity = new InvoiceEntity();
            entity.TxFee = Money.Coins(0.1m);
            entity.Rate = 5000;
            entity.Payments = new System.Collections.Generic.List<PaymentEntity>();
            entity.ProductInformation = new ProductInformation() { Price = 5000 };

            Assert.Equal(Money.Coins(1.1m), entity.GetCryptoDue());
            Assert.Equal(Money.Coins(1.1m), entity.GetTotalCryptoDue());

            entity.Payments.Add(new PaymentEntity() { Output = new TxOut(Money.Coins(0.5m), new Key()), Accounted = true });

            //Since we need to spend one more txout, it should be 1.1 - 0,5 + 0.1
            Assert.Equal(Money.Coins(0.7m), entity.GetCryptoDue());
            Assert.Equal(Money.Coins(1.2m), entity.GetTotalCryptoDue());

            entity.Payments.Add(new PaymentEntity() { Output = new TxOut(Money.Coins(0.2m), new Key()), Accounted = true });
            Assert.Equal(Money.Coins(0.6m), entity.GetCryptoDue());
            Assert.Equal(Money.Coins(1.3m), entity.GetTotalCryptoDue());

            entity.Payments.Add(new PaymentEntity() { Output = new TxOut(Money.Coins(0.6m), new Key()), Accounted = true });

            Assert.Equal(Money.Zero, entity.GetCryptoDue());
            Assert.Equal(Money.Coins(1.3m), entity.GetTotalCryptoDue());

            entity.Payments.Add(new PaymentEntity() { Output = new TxOut(Money.Coins(0.2m), new Key()), Accounted = true });

            Assert.Equal(Money.Zero, entity.GetCryptoDue());
            Assert.Equal(Money.Coins(1.3m), entity.GetTotalCryptoDue());
        }

        [Fact]
        public void CanPayUsingBIP70()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
                var user = tester.NewAccount();
                user.GrantAccess();
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
                    tester.SimulateCallback(url.Address);
                    var localInvoice = user.BitPay.GetInvoice(invoice.Id, Facade.Merchant);
                    Assert.Equal("paid", localInvoice.Status);
                    Assert.True(localInvoice.Refundable);
                });
            }
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
                    var invoice = acc.BitPay.CreateInvoice(new Invoice()
                    {
                        Price = 5.0,
                        Currency = "USD",
                        PosData = "posData",
                        OrderId = "orderId",
                        NotificationURL = callbackServer.GetUri().AbsoluteUri,
                        ItemDesc = "Some description",
                        FullNotifications = true
                    });
                    BitcoinUrlBuilder url = new BitcoinUrlBuilder(invoice.PaymentUrls.BIP21);
                    tester.ExplorerNode.SendToAddress(url.Address, url.Amount);
                    Thread.Sleep(5000);
                    tester.SimulateCallback(url.Address);
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
                Assert.Contains(nameof(PairingResult.ReusedKey), store2.StatusMessage);
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
                var invoice = user.BitPay.CreateInvoice(new Invoice()
                {
                    Price = 5000.0,
                    Currency = "USD"
                }, Facade.Merchant);

                var payment1 = Money.Coins(0.04m);
                var payment2 = Money.Coins(0.08m);
                var tx1 = new uint256(tester.ExplorerNode.SendCommand("sendtoaddress", new object[]
                {
                    invoice.BitcoinAddress.ToString(),
                    payment1.ToString(),
                    null, //comment
                    null, //comment_to
                    false, //subtractfeefromamount
                    true, //replaceable
                }).ResultString);
                var invoiceAddress = BitcoinAddress.Create(invoice.BitcoinAddress, tester.Network);

                Eventually(() =>
                {
                    tester.SimulateCallback(invoiceAddress);
                    invoice = user.BitPay.GetInvoice(invoice.Id);
                    Assert.Equal(payment1, invoice.BtcPaid);
                    invoiceAddress = BitcoinAddress.Create(invoice.BitcoinAddress, tester.Network);
                });

                var tx = tester.ExplorerNode.GetRawTransaction(new uint256(tx1));
                foreach (var input in tx.Inputs)
                {
                    input.ScriptSig = Script.Empty; //Strip signatures
                }
                var change = tx.Outputs.First(o => o.Value != payment1);
                var output = tx.Outputs.First(o => o.Value == payment1);
                output.Value = payment2;
                output.ScriptPubKey = invoiceAddress.ScriptPubKey;
                change.Value -= (payment2 - payment1) * 2; //Add more fees
                var replaced = tester.ExplorerNode.SignRawTransaction(tx);
                tester.ExplorerNode.SendRawTransaction(replaced);
                var test = tester.ExplorerClient.Sync(user.DerivationScheme, null);
                Eventually(() =>
                {
                    tester.SimulateCallback(invoiceAddress);
                    invoice = user.BitPay.GetInvoice(invoice.Id);
                    Assert.Equal(payment2, invoice.BtcPaid);
                });
            }
        }

        [Fact]
        public void InvoiceFlowThroughDifferentStatesCorrectly()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
                var user = tester.NewAccount();
                Assert.False(user.BitPay.TestAccess(Facade.Merchant));
                user.GrantAccess();
                Assert.True(user.BitPay.TestAccess(Facade.Merchant));
                var invoice = user.BitPay.CreateInvoice(new Invoice()
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
                var repo = tester.PayTester.GetService<InvoiceRepository>();
                var ctx = tester.PayTester.GetService<ApplicationDbContextFactory>().CreateContext();

                Eventually(() =>
                {
                    var textSearchResult = tester.PayTester.Runtime.InvoiceRepository.GetInvoices(new InvoiceQuery()
                    {
                        StoreId = user.StoreId,
                        TextSearch = invoice.OrderId
                    }).GetAwaiter().GetResult();
                    Assert.Equal(1, textSearchResult.Length);
                    textSearchResult = tester.PayTester.Runtime.InvoiceRepository.GetInvoices(new InvoiceQuery()
                    {
                        StoreId = user.StoreId,
                        TextSearch = invoice.Id
                    }).GetAwaiter().GetResult();

                    Assert.Equal(1, textSearchResult.Length);
                });

                invoice = user.BitPay.GetInvoice(invoice.Id, Facade.Merchant);
                Assert.Equal(Money.Coins(0), invoice.BtcPaid);
                Assert.Equal("new", invoice.Status);
                Assert.Equal(false, (bool)((JValue)invoice.ExceptionStatus).Value);

                Assert.Equal(1, user.BitPay.GetInvoices(invoice.InvoiceTime.DateTime).Length);
                Assert.Equal(0, user.BitPay.GetInvoices(invoice.InvoiceTime.DateTime + TimeSpan.FromDays(1)).Length);
                Assert.Equal(1, user.BitPay.GetInvoices(invoice.InvoiceTime.DateTime - TimeSpan.FromDays(5)).Length);
                Assert.Equal(1, user.BitPay.GetInvoices(invoice.InvoiceTime.DateTime - TimeSpan.FromDays(5), invoice.InvoiceTime.DateTime).Length);
                Assert.Equal(0, user.BitPay.GetInvoices(invoice.InvoiceTime.DateTime - TimeSpan.FromDays(5), invoice.InvoiceTime.DateTime - TimeSpan.FromDays(1)).Length);


                var firstPayment = Money.Coins(0.04m);

                var txFee = Money.Zero;

                var rate = user.BitPay.GetRates();

                var cashCow = tester.ExplorerNode;

                var invoiceAddress = BitcoinAddress.Create(invoice.BitcoinAddress, cashCow.Network);
                var iii = ctx.AddressInvoices.ToArray();
                Assert.True(IsMapped(invoice, ctx));
                cashCow.SendToAddress(invoiceAddress, firstPayment);

                var invoiceEntity = repo.GetInvoice(null, invoice.Id, true).GetAwaiter().GetResult();
                Assert.Equal(1, invoiceEntity.HistoricalAddresses.Length);
                Assert.Null(invoiceEntity.HistoricalAddresses[0].UnAssigned);

                Money secondPayment = Money.Zero;

                Eventually(() =>
                {
                    tester.SimulateCallback(invoiceAddress);
                    var localInvoice = user.BitPay.GetInvoice(invoice.Id, Facade.Merchant);
                    Assert.Equal("new", localInvoice.Status);
                    Assert.Equal(firstPayment, localInvoice.BtcPaid);
                    txFee = localInvoice.BtcDue - invoice.BtcDue;
                    Assert.Equal("paidPartial", localInvoice.ExceptionStatus);
                    Assert.NotEqual(localInvoice.BitcoinAddress, invoice.BitcoinAddress); //New address
                    Assert.True(IsMapped(invoice, ctx));
                    Assert.True(IsMapped(localInvoice, ctx));

                    invoiceEntity = repo.GetInvoice(null, invoice.Id, true).GetAwaiter().GetResult();
                    var historical1 = invoiceEntity.HistoricalAddresses.FirstOrDefault(h => h.Address == invoice.BitcoinAddress.ToString());
                    Assert.NotNull(historical1.UnAssigned);
                    var historical2 = invoiceEntity.HistoricalAddresses.FirstOrDefault(h => h.Address == localInvoice.BitcoinAddress.ToString());
                    Assert.Null(historical2.UnAssigned);
                    invoiceAddress = BitcoinAddress.Create(localInvoice.BitcoinAddress, cashCow.Network);
                    secondPayment = localInvoice.BtcDue;
                });

                cashCow.SendToAddress(invoiceAddress, secondPayment);

                Eventually(() =>
                {
                    tester.SimulateCallback(invoiceAddress);
                    var localInvoice = user.BitPay.GetInvoice(invoice.Id, Facade.Merchant);
                    Assert.Equal("paid", localInvoice.Status);
                    Assert.Equal(firstPayment + secondPayment, localInvoice.BtcPaid);
                    Assert.Equal(Money.Zero, localInvoice.BtcDue);
                    Assert.Equal(localInvoice.BitcoinAddress, invoiceAddress.ToString()); //no new address generated
                    Assert.True(IsMapped(localInvoice, ctx));
                    Assert.Equal(false, (bool)((JValue)localInvoice.ExceptionStatus).Value);
                });

                cashCow.Generate(1); //The user has medium speed settings, so 1 conf is enough to be confirmed

                Eventually(() =>
                {
                    tester.SimulateCallback();
                    var localInvoice = user.BitPay.GetInvoice(invoice.Id, Facade.Merchant);
                    Assert.Equal("confirmed", localInvoice.Status);
                });

                cashCow.Generate(5); //Now should be complete

                Eventually(() =>
                {
                    tester.SimulateCallback();
                    var localInvoice = user.BitPay.GetInvoice(invoice.Id, Facade.Merchant);
                    Assert.Equal("complete", localInvoice.Status);
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
                    tester.SimulateCallback(invoiceAddress);
                    var localInvoice = user.BitPay.GetInvoice(invoice.Id, Facade.Merchant);
                    Assert.Equal("paid", localInvoice.Status);
                    Assert.Equal(Money.Zero, localInvoice.BtcDue);
                    Assert.Equal("paidOver", (string)((JValue)localInvoice.ExceptionStatus).Value);
                });

                cashCow.Generate(1);

                Eventually(() =>
                {
                    tester.SimulateCallback();
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
            var coinAverage = new CoinAverageRateProvider();
            var jpy = coinAverage.GetRateAsync("JPY").GetAwaiter().GetResult();
            var jpy2 = new BitpayRateProvider(new Bitpay(new Key(), new Uri("https://bitpay.com/"))).GetRateAsync("JPY").GetAwaiter().GetResult();

            var cached = new CachedRateProvider(coinAverage, new MemoryCache(new MemoryCacheOptions() { ExpirationScanFrequency = TimeSpan.FromSeconds(1.0) }));
            cached.CacheSpan = TimeSpan.FromSeconds(10);
            var a = cached.GetRateAsync("JPY").GetAwaiter().GetResult();
            var b = cached.GetRateAsync("JPY").GetAwaiter().GetResult();
            //Manually check that cache get hit after 10 sec
            var c = cached.GetRateAsync("JPY").GetAwaiter().GetResult();
        }

        private static bool IsMapped(Invoice invoice, ApplicationDbContext ctx)
        {
            var h = BitcoinAddress.Create(invoice.BitcoinAddress).ScriptPubKey.Hash.ToString();
            return ctx.AddressInvoices.FirstOrDefault(i => i.InvoiceDataId == invoice.Id && i.Address == h) != null;
        }

        private void Eventually(Action act)
        {
            CancellationTokenSource cts = new CancellationTokenSource(10000);
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
    }
}
