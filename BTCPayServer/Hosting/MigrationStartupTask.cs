using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Logging;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NBitcoin.DataEncoders;
using NBXplorer;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace BTCPayServer.Hosting
{
    public class MigrationStartupTask : IStartupTask
    {
        private readonly ApplicationDbContextFactory _DBContextFactory;
        private readonly StoreRepository _StoreRepository;
        private readonly BTCPayNetworkProvider _NetworkProvider;
        private readonly SettingsRepository _Settings;
        private readonly UserManager<ApplicationUser> _userManager;

        public IOptions<LightningNetworkOptions> LightningOptions { get; }

        public MigrationStartupTask(
            BTCPayNetworkProvider networkProvider,
            StoreRepository storeRepository,
            ApplicationDbContextFactory dbContextFactory,
            UserManager<ApplicationUser> userManager,
            IOptions<LightningNetworkOptions> lightningOptions,
            SettingsRepository settingsRepository)
        {
            _DBContextFactory = dbContextFactory;
            _StoreRepository = storeRepository;
            _NetworkProvider = networkProvider;
            _Settings = settingsRepository;
            _userManager = userManager;
            LightningOptions = lightningOptions;
        }
        public async Task ExecuteAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await Migrate(cancellationToken);
                var settings = (await _Settings.GetSettingAsync<MigrationSettings>()) ?? new MigrationSettings();
                if (!settings.PaymentMethodCriteria)
                {
                    await MigratePaymentMethodCriteria();
                    settings.PaymentMethodCriteria = true;
                    await _Settings.UpdateSetting(settings);
                }
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
                if (!settings.ConvertNetworkFeeProperty)
                {
                    await ConvertNetworkFeeProperty();
                    settings.ConvertNetworkFeeProperty = true;
                    await _Settings.UpdateSetting(settings);
                }
                if (!settings.ConvertCrowdfundOldSettings)
                {
                    await ConvertCrowdfundOldSettings();
                    settings.ConvertCrowdfundOldSettings = true;
                    await _Settings.UpdateSetting(settings);
                }
                if (!settings.ConvertWalletKeyPathRoots)
                {
                    await ConvertConvertWalletKeyPathRoots();
                    settings.ConvertWalletKeyPathRoots = true;
                    await _Settings.UpdateSetting(settings);
                }
                if (!settings.CheckedFirstRun)
                {
                    var themeSettings = await _Settings.GetSettingAsync<ThemeSettings>() ?? new ThemeSettings();
                    var admin = await _userManager.GetUsersInRoleAsync(Roles.ServerAdmin);
                    themeSettings.FirstRun = admin.Count == 0;
                    await _Settings.UpdateSetting(themeSettings);
                    settings.CheckedFirstRun = true;
                    await _Settings.UpdateSetting(settings);
                }

                if (!settings.TransitionToStoreBlobAdditionalData)
                {
                    await TransitionToStoreBlobAdditionalData();
                    settings.TransitionToStoreBlobAdditionalData = true;
                    await _Settings.UpdateSetting(settings);
                }

                if (!settings.TransitionInternalNodeConnectionString)
                {
                    await TransitionInternalNodeConnectionString();
                    settings.TransitionInternalNodeConnectionString = true;
                    await _Settings.UpdateSetting(settings);
                }
            }
            catch (Exception ex)
            {
                Logs.PayServer.LogError(ex, "Error on the MigrationStartupTask");
                throw;
            }
        }

        private async Task TransitionInternalNodeConnectionString()
        {
            var nodes = LightningOptions.Value.InternalLightningByCryptoCode.Values.Select(c => c.ToString()).ToHashSet();
            await using var ctx = _DBContextFactory.CreateContext();
            foreach (var store in await ctx.Stores.AsQueryable().ToArrayAsync())
            {
#pragma warning disable CS0618 // Type or member is obsolete
                if (!string.IsNullOrEmpty(store.DerivationStrategy))
                {
                    var noLabel = store.DerivationStrategy.Split('-')[0];
                    JObject jObject = new JObject();
                    jObject.Add("BTC", new JObject()
                    {
                        new JProperty("signingKey", noLabel),
                        new JProperty("accountDerivation", store.DerivationStrategy),
                        new JProperty("accountOriginal", store.DerivationStrategy),
                        new JProperty("accountKeySettings", new JArray()
                        {
                            new JObject()
                            {
                                new JProperty("accountKey", noLabel)
                            }
                        })
                    });
                    store.DerivationStrategies = jObject.ToString();
                    store.DerivationStrategy = null;
                }
                if (string.IsNullOrEmpty(store.DerivationStrategies))
                    continue;

                var strats = JObject.Parse(store.DerivationStrategies);
                bool updated = false;
                foreach (var prop in strats.Properties().Where(p => p.Name.EndsWith("LightningLike", StringComparison.OrdinalIgnoreCase)))
                {
                    var method = ((JObject)prop.Value);
                    var lightningCharge = method.Property("LightningChargeUrl", StringComparison.OrdinalIgnoreCase);
                    var ln = method.Property("LightningConnectionString", StringComparison.OrdinalIgnoreCase);
                    if (lightningCharge != null)
                    {
                        var chargeUrl = lightningCharge.Value.Value<string>();
                        var usr = method.Property("Username", StringComparison.OrdinalIgnoreCase)?.Value.Value<string>();
                        var pass = method.Property("Password", StringComparison.OrdinalIgnoreCase)?.Value.Value<string>();
                        updated = true;
                        if (chargeUrl != null)
                        {
                            var fullUri = new UriBuilder(chargeUrl)
                            {
                                UserName = usr,
                                Password = pass
                            }.Uri.AbsoluteUri;
                            var newStr = $"type=charge;server={fullUri};allowinsecure=true";
                            if (ln is null)
                            {
                                ln = new JProperty("LightningConnectionString", newStr);
                                method.Add(ln);
                            }
                            else
                            {
                                ln.Value = newStr;
                            }
                        }
                        foreach (var p in new[] { "Username", "Password", "LightningChargeUrl" })
                            method.Property(p, StringComparison.OrdinalIgnoreCase)?.Remove();
                    }

                    var paymentId = method.Property("PaymentId", StringComparison.OrdinalIgnoreCase);
                    if (paymentId != null)
                    {
                        paymentId.Remove();
                        updated = true;
                    }

                    if (ln is null)
                        continue;
                    if (nodes.Contains(ln.Value.Value<string>()))
                    {
                        updated = true;
                        ln.Value = null;
                        if (!(method.Property("InternalNodeRef", StringComparison.OrdinalIgnoreCase) is JProperty internalNode))
                        {
                            internalNode = new JProperty("InternalNodeRef", null);
                            method.Add(internalNode);
                        }
                        internalNode.Value = new JValue(LightningSupportedPaymentMethod.InternalNode);
                    }
                }

                if (updated)
                    store.DerivationStrategies = strats.ToString();
#pragma warning restore CS0618 // Type or member is obsolete
            }
            await ctx.SaveChangesAsync();
        }

        private async Task TransitionToStoreBlobAdditionalData()
        {
            await using var ctx = _DBContextFactory.CreateContext();
            foreach (var store in await ctx.Stores.AsQueryable().ToArrayAsync())
            {
                var blob = store.GetStoreBlob();
                blob.AdditionalData.Remove("walletKeyPathRoots");
                blob.AdditionalData.Remove("onChainMinValue");
                blob.AdditionalData.Remove("lightningMaxValue");
                blob.AdditionalData.Remove("networkFeeDisabled");
                blob.AdditionalData.Remove("rateRules");
                store.SetStoreBlob(blob);
            }

            await ctx.SaveChangesAsync();
        }

        private async Task Migrate(CancellationToken cancellationToken)
        {
            using (CancellationTokenSource timeout = new CancellationTokenSource(10_000))
            using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken))
            {
retry:
                try
                {
                    await _DBContextFactory.CreateContext().Database.MigrateAsync();
                }
                // Starting up
                catch when (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(1000, cts.Token);
                    }
                    catch { }
                    goto retry;
                }
            }
        }

        private async Task ConvertConvertWalletKeyPathRoots()
        {
            bool save = false;
            using (var ctx = _DBContextFactory.CreateContext())
            {
                foreach (var store in await ctx.Stores.AsQueryable().ToArrayAsync())
                {
#pragma warning disable CS0618 // Type or member is obsolete
                    var blob = store.GetStoreBlob();

                    if (blob.AdditionalData.TryGetValue("walletKeyPathRoots", out var walletKeyPathRootsJToken))
                    {
                        var walletKeyPathRoots = walletKeyPathRootsJToken.ToObject<Dictionary<string, string>>();

                        if (!(walletKeyPathRoots?.Any() is true))
                            continue;
                        foreach (var scheme in store.GetSupportedPaymentMethods(_NetworkProvider)
                            .OfType<DerivationSchemeSettings>())
                        {
                            if (walletKeyPathRoots.TryGetValue(scheme.PaymentId.ToString().ToLowerInvariant(),
                                out var root))
                            {
                                scheme.AccountKeyPath = new NBitcoin.KeyPath(root);
                                store.SetSupportedPaymentMethod(scheme);
                                save = true;
                            }
                        }

                        blob.AdditionalData.Remove("walletKeyPathRoots");
                        store.SetStoreBlob(blob);
                    }
#pragma warning restore CS0618 // Type or member is obsolete
                }
                if (save)
                    await ctx.SaveChangesAsync();
            }
        }

        private async Task ConvertCrowdfundOldSettings()
        {
            using (var ctx = _DBContextFactory.CreateContext())
            {
                foreach (var app in await ctx.Apps.Where(a => a.AppType == "Crowdfund").ToArrayAsync())
                {
                    var settings = app.GetSettings<Services.Apps.CrowdfundSettings>();
#pragma warning disable CS0618 // Type or member is obsolete
                    if (settings.UseAllStoreInvoices)
#pragma warning restore CS0618 // Type or member is obsolete
                    {
                        app.TagAllInvoices = true;
                    }
                }
                await ctx.SaveChangesAsync();
            }
        }

        private async Task MigratePaymentMethodCriteria()
        {
            using (var ctx = _DBContextFactory.CreateContext())
            {
                foreach (var store in await ctx.Stores.AsQueryable().ToArrayAsync())
                {
                    var blob = store.GetStoreBlob();

                    CurrencyValue onChainMinValue = null;
                    CurrencyValue lightningMaxValue = null;
                    if (blob.AdditionalData.TryGetValue("onChainMinValue", out var onChainMinValueJToken))
                    {
                        CurrencyValue.TryParse(onChainMinValueJToken.Value<string>(), out onChainMinValue);
                        blob.AdditionalData.Remove("onChainMinValue");
                    }
                    if (blob.AdditionalData.TryGetValue("lightningMaxValue", out var lightningMaxValueJToken))
                    {
                        CurrencyValue.TryParse(lightningMaxValueJToken.Value<string>(), out lightningMaxValue);
                        blob.AdditionalData.Remove("lightningMaxValue");
                    }
                    blob.PaymentMethodCriteria =  store.GetEnabledPaymentIds(_NetworkProvider).Select(paymentMethodId=>
                    {
                        var matchedFromBlob =
                            blob.PaymentMethodCriteria?.SingleOrDefault(criteria => criteria.PaymentMethod == paymentMethodId && criteria.Value != null);
                        return matchedFromBlob switch
                        {
                            null when paymentMethodId.PaymentType == LightningPaymentType.Instance &&
                                      lightningMaxValue != null => new PaymentMethodCriteria()
                            {
                                Above = false, PaymentMethod = paymentMethodId, Value = lightningMaxValue
                            },
                            null when paymentMethodId.PaymentType == BitcoinPaymentType.Instance &&
                                      onChainMinValue != null => new PaymentMethodCriteria()
                            {
                                Above = true, PaymentMethod = paymentMethodId, Value = onChainMinValue
                            },
                            _ => new PaymentMethodCriteria()
                            {
                                PaymentMethod = paymentMethodId,
                                Above = matchedFromBlob?.Above ?? true,
                                Value = matchedFromBlob?.Value
                            }
                        };
                    }).ToList();

                    store.SetStoreBlob(blob);
                }

                await ctx.SaveChangesAsync();
            }
        }

        private async Task ConvertNetworkFeeProperty()
        {
            using (var ctx = _DBContextFactory.CreateContext())
            {
                foreach (var store in await ctx.Stores.AsQueryable().ToArrayAsync())
                {
                    var blob = store.GetStoreBlob();
                    if (blob.AdditionalData.TryGetValue("networkFeeDisabled", out var networkFeeModeJToken))
                    {
                        var networkFeeMode = networkFeeModeJToken.ToObject<bool?>();
                        if (networkFeeMode != null)
                        {
                            blob.NetworkFeeMode = networkFeeMode.Value ? NetworkFeeMode.Never : NetworkFeeMode.Always;
                        }

                        blob.AdditionalData.Remove("networkFeeDisabled");
                        store.SetStoreBlob(blob);
                    }
                }
                await ctx.SaveChangesAsync();
            }
        }

        private async Task ConvertMultiplierToSpread()
        {
            using (var ctx = _DBContextFactory.CreateContext())
            {
                foreach (var store in await ctx.Stores.AsQueryable().ToArrayAsync())
                {
                    var blob = store.GetStoreBlob();
                    decimal multiplier = 1.0m;
                    if (blob.AdditionalData.TryGetValue("rateRules", out var rateRulesJToken))
                    {
                        var rateRules = new Serializer(null).ToObject<List<RateRule_Obsolete>>(rateRulesJToken.ToString());
                        if (rateRules != null && rateRules.Count != 0)
                        {
                            foreach (var rule in rateRules)
                            {
                                multiplier = rule.Apply(null, multiplier);
                            }
                        }

                        blob.AdditionalData.Remove("rateRules");
                        blob.Spread = Math.Min(1.0m, Math.Max(0m, -(multiplier - 1.0m)));
                        store.SetStoreBlob(blob);
                    }
                }
                await ctx.SaveChangesAsync();
            }
        }

        public class RateRule_Obsolete
        {
            public RateRule_Obsolete()
            {
                RuleName = "Multiplier";
            }
            public string RuleName { get; set; }

            public double Multiplier { get; set; }

            public decimal Apply(BTCPayNetworkBase network, decimal rate)
            {
                return rate * (decimal)Multiplier;
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
                foreach (var store in await ctx.Stores.AsQueryable().ToArrayAsync())
                {
                    foreach (var method in store.GetSupportedPaymentMethods(_NetworkProvider).OfType<Payments.Lightning.LightningSupportedPaymentMethod>())
                    {
                        var lightning = method.GetExternalLightningUrl();
                        if (lightning?.IsLegacy is true)
                        {
                            method.SetLightningUrl(lightning);
                            store.SetSupportedPaymentMethod(method);
                        }
                    }
                }
                await ctx.SaveChangesAsync();
            }
        }
    }
}
