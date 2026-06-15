using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Lightning;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using Dapper;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using Xunit;
using static BTCPayServer.Payments.Lightning.LightningInstanceListener;

namespace BTCPayServer.Tests
{
    [Trait("Integration", "Integration")]
    public class LightningListenerTests(ITestOutputHelper helper) : UnitTestBase(helper)
    {
        [Fact]
        public async Task DoesNotSilentlyDropInvoiceWaitingForAmount()
        {
            var tester = CreateDBTester();
            await tester.MigrateUntil();
            var invoiceRepository = tester.GetInvoiceRepository();

            await using (var ctx = tester.CreateContext())
            {
                var conn = ctx.Database.GetDbConnection();
                await conn.ExecuteAsync(
                    "INSERT INTO \"Invoices\" (\"Id\", \"Created\", \"Status\", \"Currency\", \"Blob2\") VALUES ('inv1', NOW(), 'New', 'USD', '{}')");
            }

            var networkProvider = CreateNetworkProvider();
            var network = networkProvider.GetNetwork<BTCPayNetwork>("BTC");
            var paymentMethodId = PaymentMethodId.Parse("BTC-LN");
            var handlers = new PaymentMethodHandlerDictionary(Array.Empty<IPaymentMethodHandler>());
            var paymentService = new PaymentService(new EventAggregator(BTCPayLogs), tester.CreateContextFactory(), handlers, invoiceRepository);

            LightningInstanceListener NewListener(FakeLightningClient client) => new LightningInstanceListener(
                invoiceRepository,
                new EventAggregator(BTCPayLogs),
                new FakeLightningClientFactory(client),
                network,
                handlers,
                "dummy",
                paymentService,
                BTCPayLogs);

            // An unpaid invoice must be retried later, not evicted.
            var unpaidListener = NewListener(new FakeLightningClient());
            var unpaidNotification = new LightningInvoice { Id = "ln-unpaid", Status = LightningInvoiceStatus.Unpaid };
            unpaidListener.AddListenedInvoice(new ListenedInvoice(
                DateTimeOffset.UtcNow.AddMinutes(10),
                new LigthningPaymentPromptDetails { InvoiceId = "ln-unpaid" },
                new PaymentPrompt { PaymentMethodId = paymentMethodId },
                network,
                "inv1"));
            var unpaidState = await unpaidListener.AddPayment(unpaidNotification, "inv1", paymentMethodId);
            Assert.Equal(RecordedState.RetryLaterUnpaid, unpaidState);
            Assert.False(unpaidListener.Empty);

            // The Lightning node reports the invoice as Paid, but withholds the settled amount.
            var fakeClient = new FakeLightningClient();
            var listener = NewListener(fakeClient);
            var listenedInvoice = new ListenedInvoice(
                DateTimeOffset.UtcNow - TimeSpan.FromMinutes(1), // already expired
                new LigthningPaymentPromptDetails { InvoiceId = "ln-missing-amount" },
                new PaymentPrompt { PaymentMethodId = paymentMethodId },
                network,
                "inv1");
            listener.AddListenedInvoice(listenedInvoice);

            fakeClient.NextInvoice = new LightningInvoice
            {
                Id = "ln-missing-amount",
                Status = LightningInvoiceStatus.Paid,
                PaidAt = DateTimeOffset.UtcNow,
                Amount = null,
                AmountReceived = null,
            };

            var missingAmountState = await listener.AddPayment(fakeClient.NextInvoice, "inv1", paymentMethodId);
            Assert.Equal(RecordedState.RetryLaterMissingAmount, missingAmountState);
            Assert.False(listener.Empty);
            Assert.Equal(0, fakeClient.GetInvoiceCalls);

            // RemoveExpiredInvoices must give the invoice one last poll before giving up on it,
            // instead of silently dropping it the moment it expires.
            await listener.RemoveExpiredInvoices(CancellationToken.None);

            Assert.Equal(1, fakeClient.GetInvoiceCalls);
            Assert.True(listener.Empty);
        }

        private class FakeLightningClientFactory : LightningClientFactoryService
        {
            private readonly ILightningClient _client;

            public FakeLightningClientFactory(ILightningClient client)
                : base(null, Array.Empty<Func<HttpClient, ILightningConnectionStringHandler>>(), Array.Empty<ILightningConnectionStringHandler>())
            {
                _client = client;
            }

            public override ILightningClient Create(string lightningConnectionString, BTCPayNetwork network) => _client;
        }

        private class FakeLightningClient : ILightningClient
        {
            public LightningInvoice NextInvoice { get; set; }
            public int GetInvoiceCalls { get; private set; }

            public Task<LightningInvoice> GetInvoice(string invoiceId, CancellationToken cancellation)
            {
                GetInvoiceCalls++;
                return Task.FromResult(NextInvoice);
            }

            public Task<LightningInvoice> GetInvoice(uint256 paymentHash, CancellationToken cancellation) => throw new NotImplementedException();
            public Task<LightningInvoice[]> ListInvoices(CancellationToken cancellation) => throw new NotImplementedException();
            public Task<LightningInvoice[]> ListInvoices(ListInvoicesParams request, CancellationToken cancellation) => throw new NotImplementedException();
            public Task<LightningPayment> GetPayment(string paymentHash, CancellationToken cancellation) => throw new NotImplementedException();
            public Task<LightningPayment[]> ListPayments(CancellationToken cancellation) => throw new NotImplementedException();
            public Task<LightningPayment[]> ListPayments(ListPaymentsParams request, CancellationToken cancellation) => throw new NotImplementedException();
            public Task<LightningInvoice> CreateInvoice(LightMoney amount, string description, TimeSpan expiry, CancellationToken cancellation) => throw new NotImplementedException();
            public Task<LightningInvoice> CreateInvoice(CreateInvoiceParams createInvoiceRequest, CancellationToken cancellation) => throw new NotImplementedException();
            public Task<ILightningInvoiceListener> Listen(CancellationToken cancellation) => throw new NotImplementedException();
            public Task<LightningNodeInformation> GetInfo(CancellationToken cancellation) => throw new NotImplementedException();
            public Task<LightningNodeBalance> GetBalance(CancellationToken cancellation) => throw new NotImplementedException();
            public Task<PayResponse> Pay(PayInvoiceParams payParams, CancellationToken cancellation) => throw new NotImplementedException();
            public Task<PayResponse> Pay(string bolt11, PayInvoiceParams payParams, CancellationToken cancellation) => throw new NotImplementedException();
            public Task<PayResponse> Pay(string bolt11, CancellationToken cancellation) => throw new NotImplementedException();
            public Task<OpenChannelResponse> OpenChannel(OpenChannelRequest openChannelRequest, CancellationToken cancellation) => throw new NotImplementedException();
            public Task<BitcoinAddress> GetDepositAddress(CancellationToken cancellation) => throw new NotImplementedException();
            public Task<ConnectionResult> ConnectTo(NodeInfo nodeInfo, CancellationToken cancellation) => throw new NotImplementedException();
            public Task CancelInvoice(string invoiceId, CancellationToken cancellation) => throw new NotImplementedException();
            public Task<LightningChannel[]> ListChannels(CancellationToken cancellation) => throw new NotImplementedException();
        }
    }
}
