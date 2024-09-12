using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Payments;
using Dapper;
using Microsoft.EntityFrameworkCore;
using NBitcoin.Altcoins;
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
