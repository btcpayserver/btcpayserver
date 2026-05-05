using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Payments;
using BTCPayServer.Services;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NBitcoin;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests
{
    [Trait("Integration", "Integration")]
    public class DatabaseTests(ITestOutputHelper helper) : UnitTestBase(helper)
    {
        [Fact]
        public async Task CanConcurrentlyModifyWalletObject()
        {
            var tester = CreateDBTester();
            await tester.MigrateUntil();
            var walletRepo = tester.GetWalletRepository();

            var wid = new WalletObjectId(new WalletId("AAA", "ddd"), "a", "b");
            var all = Enumerable.Range(0, 10)
#pragma warning disable CS0618 // Type or member is obsolete
                .Select(i => walletRepo.ModifyWalletObjectData(wid, o => { o["idx"] = i; }))
#pragma warning restore CS0618 // Type or member is obsolete
                .ToArray();
            foreach (var task in all)
            {
                await task;
            }
        }

        [Fact]
        public async Task CanQueryMonitoredInvoices()
        {
            var tester = CreateDBTester();
            await tester.MigrateUntil();
            var invoiceRepository = tester.GetInvoiceRepository();
            await using var ctx = tester.CreateContext();
            var conn = ctx.Database.GetDbConnection();

            async Task AddPrompt(string invoiceId, string paymentMethodId, bool activated = true)
            {
                var prompt = new JObject();
                if (!activated)
                    prompt["inactive"] = true;
                prompt["currency"] = "USD";
                var query = """
                UPDATE "Invoices" SET "Blob2" = jsonb_set('{"prompts": {}}'::JSONB || COALESCE("Blob2",'{}'), ARRAY['prompts','@paymentMethodId'], '@prompt'::JSONB)
                WHERE "Id" = '@invoiceId'
                """;
                query = query.Replace("@paymentMethodId", paymentMethodId);
                query = query.Replace("@prompt", prompt.ToString());
                query = query.Replace("@invoiceId", invoiceId);
                Assert.Equal(1, await conn.ExecuteAsync(query));
            }

            await conn.ExecuteAsync("""
                INSERT INTO "Invoices" ("Id", "Created", "Status","Currency") VALUES
                ('BTCOnly', NOW(), 'New', 'USD'),
                ('LTCOnly', NOW(), 'New', 'USD'),
                ('LTCAndBTC', NOW(), 'New', 'USD'),
                ('LTCAndBTCLazy', NOW(), 'New', 'USD')
                """);
            foreach (var invoiceId in new[] { "LTCOnly", "LTCAndBTCLazy", "LTCAndBTC" })
            {
                await AddPrompt(invoiceId, "LTC-CHAIN");
            }
            foreach (var invoiceId in new[] { "BTCOnly", "LTCAndBTC" })
            {
                await AddPrompt(invoiceId, "BTC-CHAIN");
            }
            await AddPrompt("LTCAndBTCLazy", "BTC-CHAIN", false);

            var btc = PaymentMethodId.Parse("BTC-CHAIN");
            var ltc = PaymentMethodId.Parse("LTC-CHAIN");
            var invoices = await invoiceRepository.GetMonitoredInvoices(btc);
            Assert.Equal(2, invoices.Length);
            foreach (var invoiceId in new[] { "BTCOnly", "LTCAndBTC" })
            {
                Assert.Contains(invoices, i => i.Id == invoiceId);
            }
            invoices = await invoiceRepository.GetMonitoredInvoices(btc, true);
            Assert.Equal(3, invoices.Length);
            foreach (var invoiceId in new[] { "BTCOnly", "LTCAndBTC", "LTCAndBTCLazy" })
            {
                Assert.Contains(invoices, i => i.Id == invoiceId);
            }

            invoices = await invoiceRepository.GetMonitoredInvoices(ltc);
            Assert.Equal(3, invoices.Length);
            foreach (var invoiceId in new[] { "LTCAndBTC", "LTCAndBTC", "LTCAndBTCLazy" })
            {
                Assert.Contains(invoices, i => i.Id == invoiceId);
            }

            await conn.ExecuteAsync("""
                INSERT INTO "Payments" ("Id", "InvoiceDataId", "PaymentMethodId", "Status", "Blob2", "Created", "Amount", "Currency") VALUES
                ('1','LTCAndBTC', 'LTC-CHAIN', 'Processing', '{}'::JSONB, NOW(), 123, 'USD'),
                ('2','LTCAndBTC', 'BTC-CHAIN', 'Processing', '{}'::JSONB, NOW(), 123, 'USD'),
                ('3','LTCAndBTC', 'BTC-CHAIN', 'Processing', '{}'::JSONB, NOW(), 123, 'USD'),
                ('4','LTCAndBTC', 'BTC-CHAIN', 'Settled', '{}'::JSONB, NOW(), 123, 'USD');

                INSERT INTO "AddressInvoices" ("InvoiceDataId", "Address", "PaymentMethodId") VALUES
                ('LTCAndBTC', 'BTC1', 'BTC-CHAIN'),
                ('LTCAndBTC', 'BTC2', 'BTC-CHAIN'),
                ('LTCAndBTC', 'LTC1', 'LTC-CHAIN');
                """);

            var invoice = Assert.Single(await invoiceRepository.GetMonitoredInvoices(ltc), i => i.Id == "LTCAndBTC");
            var payment = Assert.Single(invoice.GetPayments(false));
            Assert.Equal("1", payment.Id);

            foreach (var includeNonActivated in new[] { true, false })
            {
                invoices = await invoiceRepository.GetMonitoredInvoices(btc, includeNonActivated);
                invoice = Assert.Single(invoices, i => i.Id == "LTCAndBTC");
                var payments = invoice.GetPayments(false);
                Assert.Equal(3, payments.Count);

                foreach (var paymentId in new[] { "2", "3", "4" })
                {
                    Assert.Contains(payments, p => p.Id == paymentId);
                }
                Assert.Equal(2, invoice.Addresses.Count);
                foreach (var addr in new[] { "BTC1", "BTC2" })
                {
                    Assert.Contains(invoice.Addresses, p => p.Address == addr);
                }
                if (!includeNonActivated)
                    Assert.DoesNotContain(invoices, i => i.Id == "LTCAndBTCLazy");
                else
                    Assert.Contains(invoices, i => i.Id == "LTCAndBTCLazy");
            }
        }

        [Fact]
        public async Task CanOnlyMarkMatchingPendingTransactionAsBroadcast()
        {
            var tester = CreateDBTester();
            await tester.MigrateUntil();
            const string storeId = "TestStore";

            await using (var ctx = tester.CreateContext())
            {
                await ctx.Database.GetDbConnection().ExecuteAsync("""
                    INSERT INTO "Stores" ("Id", "SpeedPolicy") VALUES (@storeId, 0);
                    """, new { storeId });
            }

            var networkProvider = CreateNetworkProvider();
            var network = networkProvider.GetNetwork<BTCPayNetwork>("BTC").NBitcoinNetwork;
            var service = new PendingTransactionService(
                networkProvider,
                tester.CreateContextFactory(),
                new EventAggregator(new BTCPayServer.Logging.Logs()),
                NullLogger<PendingTransactionService>.Instance);
            var requestBaseUrl = RequestBaseUrl.FromUrl("https://example.com");
            var psbtA = CreatePendingTransactionPSBT(network, 1, Money.Satoshis(10_000));
            var psbtB = CreatePendingTransactionPSBT(network, 2, Money.Satoshis(20_000));
            var pendingA = await service.CreatePendingTransaction(storeId, "BTC", psbtA, requestBaseUrl);
            var pendingB = await service.CreatePendingTransaction(storeId, "BTC", psbtB, requestBaseUrl);

            await service.Broadcasted(new PendingTransactionService.PendingTransactionFullId("BTC", storeId, pendingA.Id), psbtB.GetGlobalTransaction());

            await using (var ctx = tester.CreateContext())
            {
                var reloadedPendingA = await ctx.PendingTransactions.SingleAsync(p => p.Id == pendingA.Id);
                Assert.Equal(PendingTransactionState.Pending, reloadedPendingA.State);
            }

            var malleatedTransaction = psbtA.GetGlobalTransaction().Clone();
            malleatedTransaction.Inputs[0].ScriptSig = new Script(Op.GetPushOp(new byte[] { 1, 2, 3 }));
            Assert.NotEqual(psbtA.GetGlobalTransaction().GetHash(), malleatedTransaction.GetHash());
            await service.Broadcasted(new PendingTransactionService.PendingTransactionFullId("BTC", storeId, pendingA.Id), malleatedTransaction);

            await using (var ctx = tester.CreateContext())
            {
                var reloadedPendingA = await ctx.PendingTransactions.SingleAsync(p => p.Id == pendingA.Id);
                var reloadedPendingB = await ctx.PendingTransactions.SingleAsync(p => p.Id == pendingB.Id);
                Assert.Equal(PendingTransactionState.Broadcast, reloadedPendingA.State);
                Assert.Equal(PendingTransactionState.Pending, reloadedPendingB.State);
            }
        }

        private static PSBT CreatePendingTransactionPSBT(Network network, uint prevTxNonce, Money amount)
        {
            var tx = Transaction.Create(network);
            tx.Version = 2;
            tx.LockTime = LockTime.Zero;
            tx.Inputs.Add(new TxIn(new OutPoint(uint256.Parse($"{prevTxNonce:x64}"), 0))
            {
                Sequence = Sequence.Final
            });
            tx.Outputs.Add(amount, new Key().GetScriptPubKey(ScriptPubKeyType.Legacy));
            return PSBT.FromTransaction(tx, network);
        }

        [Fact]
        public async Task CanMigrateInvoiceAddresses()
        {
            var tester = CreateDBTester();
            await tester.MigrateUntil("20240919085726_refactorinvoiceaddress");
            await using var ctx = tester.CreateContext();
            var conn = ctx.Database.GetDbConnection();
            await conn.ExecuteAsync("INSERT INTO \"Invoices\" (\"Id\", \"Created\") VALUES ('i', NOW())");
            await conn.ExecuteAsync(
                "INSERT INTO \"AddressInvoices\" VALUES ('aaa#BTC', 'i'),('bbb','i'),('ccc#BTC_LNU', 'i'),('ddd#XMR_MoneroLike', 'i'),('eee#ZEC_ZcashLike', 'i')");
            await tester.CompleteMigrations();
            foreach (var v in new[] { ("aaa", "BTC-CHAIN"), ("bbb", "BTC-CHAIN"), ("ddd", "XMR-CHAIN") , ("eee", "ZEC-CHAIN") })
            {
                var ok = await conn.ExecuteScalarAsync<bool>("SELECT 't'::BOOLEAN FROM \"AddressInvoices\" WHERE \"Address\"=@a AND \"PaymentMethodId\"=@b", new { a = v.Item1, b = v.Item2 });
                Assert.True(ok);
            }
            var notok = await conn.ExecuteScalarAsync<bool>("SELECT 't'::BOOLEAN FROM \"AddressInvoices\" WHERE \"Address\"='ccc'");
            Assert.False(notok);
        }


    }
}
