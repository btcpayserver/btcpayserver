using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Hosting;
using BTCPayServer.Payments;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BTCPayServer.Tests;

[Trait("Integration", "Integration")]
public class ConsolidatedMigrationTests(ITestOutputHelper helper) : UnitTestBase(helper)
{
    private const string Baseline = "20240924065254_monitoredinvoices";
    private const string NextMigration = "20241029163147_AddingPendingTransactionsTable";

    [Fact]
    public async Task CanInitializeConsolidatedDatabase()
    {
        var tester = CreateDBTester();
        await tester.MigrateUntil(Baseline);
        await using var ctx = tester.CreateContext();
        await ctx.Database.GetDbConnection().ExecuteAsync("""DELETE FROM "__EFMigrationsHistory" """);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var settings = new SettingsRepository(tester.CreateContextFactory(), tester.CreateEventAggregator(), cache);
        var startup = CreateStartup(tester, settings);
        await startup.ExecuteAsync();
        Assert.NotNull(await settings.GetSettingAsync<ThemeSettings>());
        Assert.True((await settings.GetSettingAsync<MigrationSettings>()).MigrateOldDerivationSchemes);

        Assert.Equal(Baseline, ctx.Database.GetMigrations().First());
        Assert.Empty(await ctx.Database.GetPendingMigrationsAsync());
        Assert.Equal(7, await ctx.StoreRoles.CountAsync());
        // Initialization is safe to repeat, without duplicating schema or seed rows.
        await startup.ExecuteAsync();
        Assert.Equal(7, await ctx.StoreRoles.CountAsync());
        Assert.Equal("Default", await ctx.Database.GetDbConnection().QuerySingleAsync<string>(
            "SELECT source FROM lang_dictionaries WHERE dict_id = 'English'"));
    }

    [Fact]
    public async Task CanUpgradeFromConsolidatedBaseline()
    {
        var tester = CreateDBTester();
        await tester.MigrateUntil(NextMigration);
        await using var ctx = tester.CreateContext();
        var conn = ctx.Database.GetDbConnection();
        // The baseline must include the English dictionary used by language fallbacks.
        Assert.Equal("Default", await conn.QuerySingleAsync<string>(
            "SELECT source FROM lang_dictionaries WHERE dict_id = 'English'"));
        // MigrateUntil marks later migrations as applied to stop at the cutoff.
        await conn.ExecuteAsync("""
            DELETE FROM "__EFMigrationsHistory" WHERE "MigrationId" > @baseline;
            INSERT INTO "Stores" ("Id", "SpeedPolicy", "StoreBlob")
            VALUES ('store', 0, '{"defaultCurrency":"JPY"}');
            INSERT INTO "Invoices" ("Id", "StoreDataId", "Created", "Currency", "Amount", "Status", "Blob2")
            VALUES ('invoice', 'store', NOW(), 'JPY', 1234, 'New', '{"metadata":{"orderId":"keep"}}');
            INSERT INTO "Settings" ("Id", "Value") VALUES
            ('BTCPayServer.Services.MigrationSettings', '{"MigrateToStoreConfig":true}'),
            ('BTCPayServer.Services.ThemeSettings', '{"LogoUrl":"https://example.com/logo.png"}');
            """, new { baseline = Baseline });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var settings = new SettingsRepository(tester.CreateContextFactory(), tester.CreateEventAggregator(), cache);
        await CreateStartup(tester, settings).ExecuteAsync();

        Assert.Empty(await ctx.Database.GetPendingMigrationsAsync());
        Assert.Equal(1234m, await conn.ExecuteScalarAsync<decimal>("""SELECT "Amount" FROM "Invoices" WHERE "Id"='invoice'"""));
        Assert.Equal("keep", await conn.ExecuteScalarAsync<string>("""SELECT "Blob2"->'metadata'->>'orderId' FROM "Invoices" WHERE "Id"='invoice'"""));
        Assert.Equal("JPY", await conn.ExecuteScalarAsync<string>("""SELECT "StoreBlob"->>'defaultCurrency' FROM "Stores" WHERE "Id"='store'"""));
        Assert.Equal("https://example.com/logo.png", await conn.ExecuteScalarAsync<string>("""SELECT "Value"->>'LogoUrl' FROM "Settings" WHERE "Id"='BTCPayServer.Services.ThemeSettings'"""));
        Assert.True((await settings.GetSettingAsync<MigrationSettings>()).MigrateOldDerivationSchemes);
    }

    [Theory]
    [InlineData("20231020135844_AddBoltcardsTable")]
    [InlineData("20240923065254_refactorpayments")]
    public async Task RejectsPre20Database(string oldMigration)
    {
        var tester = CreateDBTester();
        await tester.MigrateUntil(Baseline);
        await using var ctx = tester.CreateContext();
        await ctx.Database.GetDbConnection().ExecuteAsync("""
            DELETE FROM "__EFMigrationsHistory";
            INSERT INTO "__EFMigrationsHistory" VALUES (@oldMigration, '8.0.0');
            CREATE TABLE "AddressInvoices" ("Address" TEXT);
            """, new { oldMigration });
        var error = await Assert.ThrowsAsync<ConfigException>(() => CreateStartup(tester).ExecuteAsync());
        Assert.Contains("2.0.x", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetriesUnrelatedDuplicateTableErrors(bool baselineApplied)
    {
        var tester = CreateDBTester();
        await tester.MigrateUntil(baselineApplied ? NextMigration : Baseline);
        await using var ctx = tester.CreateContext();
        await ctx.Database.GetDbConnection().ExecuteAsync(baselineApplied
            ? """
              DELETE FROM "__EFMigrationsHistory" WHERE "MigrationId" > @baseline;
              CREATE TABLE "PendingTransactions" ("Id" TEXT);
              """
            : """
              DELETE FROM "__EFMigrationsHistory";
              CREATE TABLE "AddressInvoices" ("Address" TEXT);
              """, new { baseline = Baseline });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var logger = new RetryLogger(cts.Cancel);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateStartup(tester, logger: logger).ExecuteAsync(cts.Token));
        Assert.True(logger.Retries > 0);
    }

    private static MigrationStartupTask CreateStartup(DatabaseTester tester, SettingsRepository settings = null, ILogger<MigrationStartupTask> logger = null)
        => new(Array.Empty<IMigrationExecutor>(),
            new PaymentMethodHandlerDictionary(Array.Empty<IPaymentMethodHandler>()),
            tester.CreateContextFactory(), settings, logger ?? NullLogger<MigrationStartupTask>.Instance);

    private class RetryLogger(Action onRetry) : ILogger<MigrationStartupTask>
    {
        public int Retries { get; private set; }
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Retries++;
                onRetry();
            }
        }
    }
}
