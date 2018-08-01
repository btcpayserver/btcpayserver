using System;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using BTCPayServer.Logging;

namespace BTCPayServer.HostedServices
{
    public class MigratorHostedService : BaseAsyncService
    {
        private ApplicationDbContextFactory _DBContextFactory;
        private StoreRepository _StoreRepository;
        private BTCPayNetworkProvider _NetworkProvider;
        private SettingsRepository _Settings;
        public MigratorHostedService(
            BTCPayNetworkProvider networkProvider,
            StoreRepository storeRepository,
            ApplicationDbContextFactory dbContextFactory,
            SettingsRepository settingsRepository)
        {
            _DBContextFactory = dbContextFactory;
            _StoreRepository = storeRepository;
            _NetworkProvider = networkProvider;
            _Settings = settingsRepository;
        }
        internal override Task[] InitializeTasks()
        {
            return new[]
            {
                Update()
            };
        }

        private async Task Update()
        {
            try
            {
                var settings = (await _Settings.GetSettingAsync<MigrationSettings>()) ?? new MigrationSettings();
                if (!settings.DeprecatedLightningConnectionStringCheck)
                {
                    await DeprecatedLightningConnectionStringCheck();
                    settings.DeprecatedLightningConnectionStringCheck = true;
                    await _Settings.UpdateSetting(settings);
                }
                if (!settings.UnreachableStoreCheck)
                {
                    await UnreachableStoreCheck();
                    settings.UnreachableStoreCheck = true;
                    await _Settings.UpdateSetting(settings);
                }
                if (!settings.ConvertMultiplierToSpread)
                {
                    await ConvertMultiplierToSpread();
                    settings.ConvertMultiplierToSpread = true;
                    await _Settings.UpdateSetting(settings);
                }
            }
            catch (Exception ex)
            {
                Logs.PayServer.LogError(ex, "Error on the MigratorHostedService");
                throw;
            }
        }

        private async Task ConvertMultiplierToSpread()
        {
            using (var ctx = _DBContextFactory.CreateContext())
            {
                foreach (var store in await ctx.Stores.ToArrayAsync())
                {
                    var blob = store.GetStoreBlob();
#pragma warning disable CS0612 // Type or member is obsolete
                    decimal multiplier = 1.0m;
                    if (blob.RateRules != null && blob.RateRules.Count != 0)
                    {
                        foreach (var rule in blob.RateRules)
                        {
                            multiplier = rule.Apply(null, multiplier);
                        }
                    }
                    blob.RateRules = null;
                    blob.Spread = Math.Min(1.0m, Math.Max(0m, -(multiplier - 1.0m)));
                    store.SetStoreBlob(blob);
#pragma warning restore CS0612 // Type or member is obsolete
                }
                await ctx.SaveChangesAsync();
            }
        }

        private Task UnreachableStoreCheck()
        {
            return _StoreRepository.CleanUnreachableStores();
        }

        private async Task DeprecatedLightningConnectionStringCheck()
        {
            using (var ctx = _DBContextFactory.CreateContext())
            {
                foreach (var store in await ctx.Stores.ToArrayAsync())
                {
                    foreach (var method in store.GetSupportedPaymentMethods(_NetworkProvider).OfType<Payments.Lightning.LightningSupportedPaymentMethod>())
                    {
                        var lightning = method.GetLightningUrl();
                        if (lightning.IsLegacy)
                        {
                            method.SetLightningUrl(lightning);
                            store.SetSupportedPaymentMethod(method.PaymentId, method);
                        }
                    }
                }
                await ctx.SaveChangesAsync();
            }
        }
    }
}
