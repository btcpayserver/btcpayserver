using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Payments;
using BTCPayServer.Services;
using Dapper;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using NBitcoin.Altcoins;
using NBitpayClient;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests
{
    [Trait("Integration", "Integration")]
    public class DatabaseTests : UnitTestBase
    {

        public DatabaseTests(ITestOutputHelper helper):base(helper)
        {
        }

        [Fact]
        public async Task CanConcurrentlyModifyWalletObject()
        {
            var tester = CreateDBTester();
            await tester.MigrateUntil();
            var walletRepo = tester.GetWalletRepository();

            var wid = new WalletObjectId(new WalletId("AAA", "ddd"), "a", "b");
            var all = Enumerable.Range(0, 10)
#pragma warning disable CS0618 // Type or member is obsolete
                .Select(i => walletRepo.ModifyWalletObjectData(wid, (o) => { o["idx"] = i; }))
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
            using var ctx = tester.CreateContext();
            var conn = ctx.Database.GetDbConnection();

            async Task AddPrompt(string invoiceId, string paymentMethodId, bool activated = true)
            {
                JObject prompt = new JObject();
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
            foreach (var invoiceId in new string[] { "LTCOnly", "LTCAndBTCLazy", "LTCAndBTC" })
            {
                await AddPrompt(invoiceId, "LTC-CHAIN", true);
            }
            foreach (var invoiceId in new string[] { "BTCOnly", "LTCAndBTC" })
            {
                await AddPrompt(invoiceId, "BTC-CHAIN", true);
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
        public async Task CanMigrateInvoiceAddresses()
        {
            var tester = CreateDBTester();
            await tester.MigrateUntil("20240919085726_refactorinvoiceaddress");
            using var ctx = tester.CreateContext();
            var conn = ctx.Database.GetDbConnection();
            await conn.ExecuteAsync("INSERT INTO \"Invoices\" (\"Id\", \"Created\") VALUES ('i', NOW())");
            await conn.ExecuteAsync(
                "INSERT INTO \"AddressInvoices\" VALUES ('aaa#BTC', 'i'),('bbb','i'),('ccc#BTC_LNU', 'i'),('ddd#XMR_MoneroLike', 'i'),('eee#ZEC_ZcashLike', 'i')");
            await tester.ContinueMigration();
            foreach (var v in new[] { ("aaa", "BTC-CHAIN"), ("bbb", "BTC-CHAIN"), ("ddd", "XMR-CHAIN") , ("eee", "ZEC-CHAIN") })
            {
                var ok = await conn.ExecuteScalarAsync<bool>("SELECT 't'::BOOLEAN FROM \"AddressInvoices\" WHERE \"Address\"=@a AND \"PaymentMethodId\"=@b", new { a = v.Item1, b = v.Item2 });
                Assert.True(ok);
            }
            var notok = await conn.ExecuteScalarAsync<bool>("SELECT 't'::BOOLEAN FROM \"AddressInvoices\" WHERE \"Address\"='ccc'");
            Assert.False(notok);
        }

        [Fact]
        public async Task CanMigratePayoutsAndPullPayments()
        {
            var tester = CreateDBTester();
            await tester.MigrateUntil("20240827034505_migratepayouts");

            using var ctx = tester.CreateContext();
            var conn = ctx.Database.GetDbConnection();
            await conn.ExecuteAsync("INSERT INTO \"Stores\"(\"Id\", \"SpeedPolicy\") VALUES (@store, 0)", new { store = "store1" });
            var param = new
            {
                Id = "pp1",
                StoreId = "store1",
                Blob = "{\"Name\": \"CoinLottery\", \"View\": {\"Email\": null, \"Title\": \"\", \"Description\": \"\", \"EmbeddedCSS\": null, \"CustomCSSLink\": null}, \"Limit\": \"10.00\", \"Period\": null, \"Currency\": \"GBP\", \"Description\": \"\", \"Divisibility\": 0, \"MinimumClaim\": \"0\", \"AutoApproveClaims\": false, \"SupportedPaymentMethods\": [\"BTC\", \"BTC_LightningLike\"]}"
            };
            await conn.ExecuteAsync("INSERT INTO \"PullPayments\"(\"Id\", \"StoreId\", \"Blob\", \"StartDate\", \"Archived\") VALUES (@Id, @StoreId, @Blob::JSONB, NOW(), 'f')", param);
            var parameters = new[]
            {
                new
                {
                    Id = "p1",
                    StoreId = "store1",
                    PullPaymentDataId = "pp1",
                    PaymentMethodId = "BTC",
                    Blob = "{\"Amount\": \"10.0\", \"Revision\": 0, \"Destination\": \"address\", \"CryptoAmount\": \"0.00012225\", \"MinimumConfirmation\": 1}"
                },
                new
                {
                    Id = "p2",
                    StoreId = "store1",
                    PullPaymentDataId = "pp1",
                    PaymentMethodId = "BTC_LightningLike",
                    Blob = "{\"Amount\": \"10.0\", \"Revision\": 0, \"Destination\": \"address\", \"CryptoAmount\": null, \"MinimumConfirmation\": 1}"
                },
                new
                {
                    Id = "p3",
                    StoreId = "store1",
                    PullPaymentDataId = null as string,
                    PaymentMethodId = "BTC_LightningLike",
                    Blob = "{\"Amount\": \"10.0\", \"Revision\": 0, \"Destination\": \"address\", \"CryptoAmount\": null, \"MinimumConfirmation\": 1}"
                },
                new
                {
                    Id = "p4",
                    StoreId = "store1",
                    PullPaymentDataId = null as string,
                    PaymentMethodId = "BTC_LightningLike",
                    Blob = "{\"Amount\": \"-10.0\", \"Revision\": 0, \"Destination\": \"address\", \"CryptoAmount\": null, \"MinimumConfirmation\": 1}"
                }
            };
            await conn.ExecuteAsync("INSERT INTO \"Payouts\"(\"Id\", \"StoreDataId\", \"PullPaymentDataId\", \"PaymentMethodId\", \"Blob\", \"State\", \"Date\") VALUES (@Id, @StoreId, @PullPaymentDataId, @PaymentMethodId, @Blob::JSONB, 'state', NOW())", parameters);
            await tester.ContinueMigration();

            var migrated = await conn.ExecuteScalarAsync<bool>("SELECT 't'::BOOLEAN FROM \"PullPayments\" WHERE \"Id\"='pp1' AND \"Limit\"=10.0 AND \"Currency\"='GBP' AND \"Blob\"->>'SupportedPayoutMethods'='[\"BTC-CHAIN\", \"BTC-LN\"]'");
            Assert.True(migrated);

            migrated = await conn.ExecuteScalarAsync<bool>("SELECT 't'::BOOLEAN FROM \"Payouts\" WHERE \"Id\"='p1' AND \"Amount\"= 0.00012225 AND \"OriginalAmount\"=10.0 AND \"OriginalCurrency\"='GBP' AND \"PayoutMethodId\"='BTC-CHAIN'");
            Assert.True(migrated);

            migrated = await conn.ExecuteScalarAsync<bool>("SELECT 't'::BOOLEAN FROM \"Payouts\" WHERE \"Id\"='p2' AND \"Amount\" IS NULL AND \"OriginalAmount\"=10.0 AND \"OriginalCurrency\"='GBP' AND \"PayoutMethodId\"='BTC-LN'");
            Assert.True(migrated);

            migrated = await conn.ExecuteScalarAsync<bool>("SELECT 't'::BOOLEAN FROM \"Payouts\" WHERE \"Id\"='p3' AND \"Amount\" IS NULL AND \"OriginalAmount\"=10.0 AND \"OriginalCurrency\"='BTC'");
            Assert.True(migrated);

            migrated = await conn.ExecuteScalarAsync<bool>("SELECT 't'::BOOLEAN FROM \"Payouts\" WHERE \"Id\"='p4' AND \"Amount\" IS NULL AND \"OriginalAmount\"=-10.0 AND \"OriginalCurrency\"='BTC' AND \"PayoutMethodId\"='TOPUP'");
            Assert.True(migrated);
        }
    }
}
