using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Npgsql;

namespace BTCPayServer.Hosting
{
    public class MigrationStartupTask(
        IEnumerable<IMigrationExecutor> migrationExecutors,
        PaymentMethodHandlerDictionary handlers,
        ApplicationDbContextFactory dbContextFactory,
        SettingsRepository settingsRepository,
        ILogger<MigrationStartupTask> logger)
        : IStartupTask
    {
        public async Task ExecuteAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await Migrate(cancellationToken);
                var settings = (await settingsRepository.GetSettingAsync<MigrationSettings>());
                if (settings is null)
                {
                    // First-run defaults are initialization, not an upgrade migration.
                    settings = new MigrationSettings { MigrateOldDerivationSchemes = true };
                    await settingsRepository.UpdateSetting(new ThemeSettings());
                    await settingsRepository.UpdateSetting(settings);
                }

                if (!settings.MigrateOldDerivationSchemes)
                {
                    await MigrateOldDerivationSchemes();
                    settings.MigrateOldDerivationSchemes = true;
                    await settingsRepository.UpdateSetting(settings);
                }

                foreach (var executor in migrationExecutors)
                {
                    await executor.Execute(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error on the MigrationStartupTask");
                throw;
            }
        }

        private async Task MigrateOldDerivationSchemes()
        {
            await using var ctx = dbContextFactory.CreateContext();
            var stores = await ctx.Stores.ToArrayAsync();
            foreach (var store in stores)
            {
                if (string.IsNullOrEmpty(store.DerivationStrategies))
                    continue;
                JObject strategies = JsonParser.ParseObject(store.DerivationStrategies);
                if (strategies is null)
                    continue;
                foreach (var prop in strategies.Properties())
                {
                    var h = handlers.TryGet(new PaymentMethodId(prop.Name));
                    if (h is not BitcoinLikePaymentHandler bh ||
                        prop.Value is not JValue { Type: JTokenType.String, Value: string v })
                        continue;
                    var settings = DerivationSchemeSettings.Parse(v, bh.Network);
                    prop.Value = JToken.FromObject(settings, bh.Serializer);
                    store.DerivationStrategies = strategies.ToString();
                }
            }
            await ctx.SaveChangesAsync();
        }

        private async Task Migrate(CancellationToken cancellationToken)
        {
            TimeSpan cancellationTimeout = TimeSpan.FromDays(1.0);
            using (CancellationTokenSource timeout = new CancellationTokenSource((int)cancellationTimeout.TotalMilliseconds))
            using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken))
            {
retry:
                try
                {
                    logger.LogInformation("Running the migration scripts...");
                    await using var db = dbContextFactory.CreateContext(o => o.CommandTimeout(((int)cancellationTimeout.TotalSeconds) + 1));
                    try
                    {
                        await db.Database.MigrateAsync(timeout.Token);
                    }
                    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.DuplicateTable)
                    {
                        // Diagnose unsupported upgrades only after failure, without another query on normal startup.
                        const string baseline = "20240924065254_monitoredinvoices";
                        var applied = (await db.Database.GetAppliedMigrationsAsync(cts.Token)).ToArray();
                        if (!applied.Contains(baseline) && applied.Any(m => string.CompareOrdinal(m, baseline) < 0))
                        {
                            throw new ConfigException(
                                "This database requires an intermediate upgrade. " +
                                "Upgrade to BTCPay Server 2.0.x first, then upgrade to this version.");
                        }
                        throw;
                    }
                    logger.LogInformation("All migration scripts ran successfully");
                }
                // Starting up
                catch (ConfigException) { throw; }
                catch (Exception ex) when (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        logger.LogWarning(ex, "Error while running migration scripts, retrying...");
                        await Task.Delay(1000, cts.Token);
                    }
                    catch { }
                    goto retry;
                }
            }
        }
    }
}
