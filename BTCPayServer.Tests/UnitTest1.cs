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
using BTCPayServer.Invoicing;

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

			entity.Payments.Add(new PaymentEntity() { Output = new TxOut(Money.Coins(0.5m), new Key()) });

			//Since we need to spend one more txout, it should be 1.1 - 0,5 + 0.1
			Assert.Equal(Money.Coins(0.7m), entity.GetCryptoDue());
			Assert.Equal(Money.Coins(1.2m), entity.GetTotalCryptoDue());

			entity.Payments.Add(new PaymentEntity() { Output = new TxOut(Money.Coins(0.2m), new Key()) });
			Assert.Equal(Money.Coins(0.6m), entity.GetCryptoDue());
			Assert.Equal(Money.Coins(1.3m), entity.GetTotalCryptoDue());

			entity.Payments.Add(new PaymentEntity() { Output = new TxOut(Money.Coins(0.6m), new Key()) });

			Assert.Equal(Money.Zero, entity.GetCryptoDue());
			Assert.Equal(Money.Coins(1.3m), entity.GetTotalCryptoDue());

			entity.Payments.Add(new PaymentEntity() { Output = new TxOut(Money.Coins(0.2m), new Key()) });

			Assert.Equal(Money.Zero, entity.GetCryptoDue());
			Assert.Equal(Money.Coins(1.3m), entity.GetTotalCryptoDue());
		}

		[Fact]
		public void CanPayUsingBIP70()
		{
			using(var tester = ServerTester.Create())
			{
				tester.Start();
				var user = tester.CreateAccount();
				user.GrantAccess();
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

				Assert.False(invoice.Refundable);

				var url = new BitcoinUrlBuilder(invoice.PaymentUrls.BIP72);
				var request = url.GetPaymentRequest();
				var payment = request.CreatePayment();

				Transaction tx = new Transaction();
				tx.Outputs.AddRange(request.Details.Outputs.Select(o => new TxOut(o.Amount, o.Script)));
				var cashCow = tester.ExplorerNode.CreateRPCClient();
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
		public void InvoiceFlowThroughDifferentStatesCorrectly()
		{
			using(var tester = ServerTester.Create())
			{
				tester.Start();
				var user = tester.CreateAccount();
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

				var textSearchResult = tester.PayTester.Runtime.InvoiceRepository.GetInvoices(new Invoicing.InvoiceQuery()
				{
					StoreId = user.StoreId,
					TextSearch = invoice.OrderId
				}).GetAwaiter().GetResult();

				Assert.Equal(1, textSearchResult.Length);

				textSearchResult = tester.PayTester.Runtime.InvoiceRepository.GetInvoices(new Invoicing.InvoiceQuery()
				{
					StoreId = user.StoreId,
					TextSearch = invoice.Id
				}).GetAwaiter().GetResult();

				Assert.Equal(1, textSearchResult.Length);

				invoice = user.BitPay.GetInvoice(invoice.Id, Facade.Merchant);
				Assert.Equal(Money.Coins(0), invoice.BtcPaid);
				Assert.Equal("new", invoice.Status);
				Assert.Equal("false", invoice.ExceptionStatus);

				Assert.Equal(1, user.BitPay.GetInvoices(invoice.InvoiceTime.DateTime).Length);
				Assert.Equal(0, user.BitPay.GetInvoices(invoice.InvoiceTime.DateTime + TimeSpan.FromDays(1)).Length);
				Assert.Equal(1, user.BitPay.GetInvoices(invoice.InvoiceTime.DateTime - TimeSpan.FromDays(5)).Length);
				Assert.Equal(1, user.BitPay.GetInvoices(invoice.InvoiceTime.DateTime - TimeSpan.FromDays(5), invoice.InvoiceTime.DateTime).Length);
				Assert.Equal(0, user.BitPay.GetInvoices(invoice.InvoiceTime.DateTime - TimeSpan.FromDays(5), invoice.InvoiceTime.DateTime - TimeSpan.FromDays(1)).Length);


				var firstPayment = Money.Coins(0.04m);

				var txFee = Money.Zero;

				var rate = user.BitPay.GetRates();

				var cashCow = tester.ExplorerNode.CreateRPCClient();
				var invoiceAddress = BitcoinAddress.Create(invoice.BitcoinAddress, cashCow.Network);
				cashCow.SendToAddress(invoiceAddress, firstPayment);

				Money secondPayment = Money.Zero;

				Eventually(() =>
				{
					var localInvoice = user.BitPay.GetInvoice(invoice.Id, Facade.Merchant);
					Assert.Equal("paidPartial", localInvoice.Status);
					Assert.Equal(firstPayment, localInvoice.BtcPaid);
					txFee = localInvoice.BtcDue - invoice.BtcDue;
					Assert.Equal("paidPartial", localInvoice.ExceptionStatus);
					secondPayment = localInvoice.BtcDue;
				});

				cashCow.SendToAddress(invoiceAddress, secondPayment);

				Eventually(() =>
				{
					var localInvoice = user.BitPay.GetInvoice(invoice.Id, Facade.Merchant);
					Assert.Equal("paid", localInvoice.Status);
					Assert.Equal(firstPayment + secondPayment, localInvoice.BtcPaid);
					Assert.Equal(Money.Zero, localInvoice.BtcDue);
					Assert.Equal("false", localInvoice.ExceptionStatus);
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
					Assert.Equal("paidOver", localInvoice.Status);
					Assert.Equal(Money.Zero, localInvoice.BtcDue);
					Assert.Equal("paidOver", localInvoice.ExceptionStatus);
				});

				cashCow.Generate(1);

				Eventually(() =>
				{
					var localInvoice = user.BitPay.GetInvoice(invoice.Id, Facade.Merchant);
					Assert.Equal("confirmed", localInvoice.Status);
					Assert.Equal(Money.Zero, localInvoice.BtcDue);
					Assert.Equal("paidOver", localInvoice.ExceptionStatus);
				});
			}
		}

		private void Eventually(Action act)
		{
			CancellationTokenSource cts = new CancellationTokenSource(10000);
			while(true)
			{
				try
				{
					act();
					break;
				}
				catch(XunitException) when(!cts.Token.IsCancellationRequested)
				{
					cts.Token.WaitHandle.WaitOne(500);
				}
			}
		}
	}
}
