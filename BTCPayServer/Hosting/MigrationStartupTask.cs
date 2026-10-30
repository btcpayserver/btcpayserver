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
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Storage.Models;
using BTCPayServer.Storage.Services.Providers.FileSystemStorage.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
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
                    // If it is null, then it's the first run: let's skip all the migrations by setting flags to true
                    settings = new MigrationSettings() { MigratedInvoiceTextSearchPages = int.MaxValue, MigratedTransactionLabels = int.MaxValue };
                    foreach (var prop in settings.GetType().GetProperties().Where(p => p.CanWrite && p.PropertyType == typeof(bool)))
                    {
                        prop.SetValue(settings, true);
                    }
                    if (await settingsRepository.GetSettingAsync<StorageSettings>() is null)
                    {
                        await settingsRepository.UpdateSetting(new StorageSettings
                        {
                            Provider = StorageProvider.FileSystem,
                            Configuration = JObject.FromObject(new FileSystemStorageConfiguration())
                        });
                    }
                    await settingsRepository.UpdateSetting(new ThemeSettings());
                    await settingsRepository.UpdateSetting(settings);
                }

                if (!settings.MigrateToStoreConfig)
                {
                    await MigrateToStoreConfig();
                    settings.MigrateToStoreConfig = true;
                    await settingsRepository.UpdateSetting(settings);
                }
                if (!settings.MigrateBlockExplorerLinks)
                {
                    await MigrateBlockExplorerLinks();
                    settings.MigrateBlockExplorerLinks = true;
                    await settingsRepository.UpdateSetting(settings);
                }
                if (!settings.MigrateStoreExcludedPaymentMethods)
                {
                    await MigrateStoreExcludedPaymentMethods();
                    settings.MigrateStoreExcludedPaymentMethods = true;
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

        private async Task MigrateStoreExcludedPaymentMethods()
        {
            await using var ctx = dbContextFactory.CreateContext();
            var stores = await ctx.Stores.ToArrayAsync();
            foreach (var store in stores)
            {
                if (store.StoreBlob is null)
                    continue;
                var blob = JsonParser.ParseObject(store.StoreBlob);
                var array = blob["excludedPaymentMethods"] as JArray;
                if (array is null || array.Count == 0)
                    continue;
                var newArray = new JArray(array.Select(a => MigrationExtensions.TryMigratePaymentMethodId(a.Value<string>())).ToArray());
                if (array.ToString() == newArray.ToString())
                    continue;
                blob["excludedPaymentMethods"] = newArray;
                store.StoreBlob = blob.ToString();
            }
            await ctx.SaveChangesAsync();
        }

        private async Task MigrateBlockExplorerLinks()
        {
            await using var ctx = dbContextFactory.CreateContext();
            var settings = await ctx.Settings.Where(s => s.Id == "BTCPayServer.Services.PoliciesSettings").FirstOrDefaultAsync();
            if (settings is null)
                return;
            var obj = JsonParser.ParseObject(settings.Value);
            var arr = obj["BlockExplorerLinks"] as JArray;
            if (arr is null or { Count: 0 })
                return;
            foreach (var item in arr.OfType<JObject>())
            {
                var cryptoCode = item["CryptoCode"]?.Value<string>();
                if (cryptoCode is null)
                    continue;
                item.Remove("CryptoCode");
                item["PaymentMethodId"] = PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode).ToString();
            }
            settings.Value = obj.ToString();
            await ctx.SaveChangesAsync();
        }

        private async Task MigrateToStoreConfig()
        {
            await using var ctx = dbContextFactory.CreateContext();
            var stores = await ctx.Stores.ToArrayAsync();
            foreach (var store in stores)
            {
                if (string.IsNullOrEmpty(store.DerivationStrategies))
                    continue;
                var strategies = JsonParser.ParseObject(store.DerivationStrategies);
                foreach (var s in strategies.Properties().ToList())
                {
                    var ls = s;
                    var pmi = PaymentMethodId.Parse(s.Name);
                    MigrationExtensions.RenameProperty(ref ls, pmi.ToString());
                    if (ls.Value is JObject conf)
                    {
                        if (IsLN(pmi))
                        {
                            conf.RenameProperty("LightningConnectionString", "connectionString");
                            conf.Remove("DisableBOLT11PaymentOption"); // Old
                            conf.RenameProperty("InternalNodeRef", "internalNodeRef");
                            conf.Remove("CryptoCode");
                            conf.RemoveIfValue("internalNodeRef", null as string);
                            conf.RemoveIfValue("connectionString", null as string);
                        }
                        else if (IsLNURL(pmi))
                        {
                            conf.RenameProperty("LUD12Enabled", "lud12Enabled");
                            conf.RenameProperty("UseBech32Scheme", "useBech32Scheme");
                            conf.RemoveIfValue("lud12Enabled", false);
                            conf.RemoveIfValue("useBech32Scheme", false);
                            conf.Remove("CryptoCode");
                        }
                        else if (IsChain(pmi))
                        {
                            conf.RemoveIfValue("label", null as string);
                            conf.RemoveIfValue("isHotWallet", false);
                        }
                    }
                }
                store.DerivationStrategies = strategies.ToString(Formatting.None);
            }
            await ctx.SaveChangesAsync();
        }

        private bool IsLN(PaymentMethodId paymentMethodId)
        {
            return handlers.TryGetValue(paymentMethodId, out var v) && v is LightningLikePaymentHandler;
        }
        private bool IsChain(PaymentMethodId paymentMethodId)
        {
            return handlers.TryGetValue(paymentMethodId, out var v) && v is BitcoinLikePaymentHandler;
        }
        private bool IsLNURL(PaymentMethodId paymentMethodId)
        {
            return handlers.TryGetValue(paymentMethodId, out var v) && v is LNURLPayPaymentHandler;
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
                        const string baseline = "20231020135844_AddBoltcardsTable";
                        var applied = (await db.Database.GetAppliedMigrationsAsync(cts.Token)).ToArray();
                        if (!applied.Contains(baseline) && applied.Any(m => string.CompareOrdinal(m, baseline) < 0))
                        {
                            throw new ConfigException(
                                "This database requires an intermediate upgrade. " +
                                "Upgrade to BTCPay Server 1.12.x or 1.13.x first, then upgrade to this version.");
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
