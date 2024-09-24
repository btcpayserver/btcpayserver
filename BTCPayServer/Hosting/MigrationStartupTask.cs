using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Fido2;
using BTCPayServer.Fido2.Models;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.PayoutProcessors;
using BTCPayServer.Payouts;
using BTCPayServer.Plugins.Crowdfund;
using BTCPayServer.Plugins.PointOfSale;
using BTCPayServer.Plugins.PointOfSale.Models;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using BTCPayServer.Storage.Models;
using BTCPayServer.Storage.Services.Providers.FileSystemStorage.Configuration;
using Fido2NetLib.Objects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PeterO.Cbor;
using YamlDotNet.RepresentationModel;
using LightningAddressData = BTCPayServer.Data.LightningAddressData;

namespace BTCPayServer.Hosting
{
    public class MigrationStartupTask : IStartupTask
    {

        private readonly ApplicationDbContextFactory _DBContextFactory;
        private readonly StoreRepository _StoreRepository;
        private readonly PaymentMethodHandlerDictionary _handlers;
        private readonly SettingsRepository _Settings;
        private readonly AppService _appService;
        private readonly PayoutMethodHandlerDictionary _payoutHandlers;
        private readonly BTCPayNetworkJsonSerializerSettings _btcPayNetworkJsonSerializerSettings;
        private readonly LightningAddressService _lightningAddressService;
        private readonly ILogger<MigrationStartupTask> _logger;
        private readonly LightningClientFactoryService _lightningClientFactoryService;
        private readonly IFileService _fileService;

        public IOptions<LightningNetworkOptions> LightningOptions { get; }

        public MigrationStartupTask(
            PaymentMethodHandlerDictionary handlers,
            StoreRepository storeRepository,
            ApplicationDbContextFactory dbContextFactory,
            IOptions<LightningNetworkOptions> lightningOptions,
            SettingsRepository settingsRepository,
            AppService appService,
            PayoutMethodHandlerDictionary payoutHandlers,
            BTCPayNetworkJsonSerializerSettings btcPayNetworkJsonSerializerSettings,
            LightningAddressService lightningAddressService,
            ILogger<MigrationStartupTask> logger,
            IFileService fileService,
            LightningClientFactoryService lightningClientFactoryService)
        {
            _handlers = handlers;
            _DBContextFactory = dbContextFactory;
            _StoreRepository = storeRepository;
            _Settings = settingsRepository;
            _appService = appService;
            _payoutHandlers = payoutHandlers;
            _btcPayNetworkJsonSerializerSettings = btcPayNetworkJsonSerializerSettings;
            _lightningAddressService = lightningAddressService;
            _logger = logger;
            _fileService = fileService;
            _lightningClientFactoryService = lightningClientFactoryService;
            LightningOptions = lightningOptions;
        }
        public async Task ExecuteAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await Migrate(cancellationToken);
                var settings = (await _Settings.GetSettingAsync<MigrationSettings>());
                if (settings is null)
                {
                    // If it is null, then it's the first run: let's skip all the migrations by setting flags to true
                    settings = new MigrationSettings() { MigratedInvoiceTextSearchPages = int.MaxValue, MigratedTransactionLabels = int.MaxValue };
                    foreach (var prop in settings.GetType().GetProperties().Where(p => p.CanWrite && p.PropertyType == typeof(bool)))
                    {
                        prop.SetValue(settings, true);
                    }
                    // Ensure these checks still get run
                    settings.FileSystemStorageAsDefault = false;
                    await _Settings.UpdateSetting(settings);
                    await _Settings.UpdateSetting(new ThemeSettings());
                }

                if (!settings.PaymentMethodCriteria)
                {
                    await MigratePaymentMethodCriteria();
                    settings.PaymentMethodCriteria = true;
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

                if (!settings.MigrateU2FToFIDO2)
                {
                    await MigrateU2FToFIDO2();
                    settings.MigrateU2FToFIDO2 = true;
                    await _Settings.UpdateSetting(settings);
                }
                if (!settings.MigrateHotwalletProperty)
                {
                    await MigrateHotwalletProperty();
                    settings.MigrateHotwalletProperty = true;
                    await _Settings.UpdateSetting(settings);
                }
                if (!settings.MigrateAppCustomOption)
                {
                    await MigrateAppCustomOption();
                    settings.MigrateAppCustomOption = true;
                    await _Settings.UpdateSetting(settings);
                }
                if (!settings.MigratePayoutDestinationId)
                {
                    await MigratePayoutDestinationId();
                    settings.MigratePayoutDestinationId = true;
                    await _Settings.UpdateSetting(settings);
                }
                if (!settings.AddInitialUserBlob)
                {
                    await AddInitialUserBlob();
                    settings.AddInitialUserBlob = true;
                    await _Settings.UpdateSetting(settings);
                }
                if (!settings.LighingAddressSettingRename)
                {
                    await MigrateLighingAddressSettingRename();
                    settings.LighingAddressSettingRename = true;
                    await _Settings.UpdateSetting(settings);
                }
                if (!settings.LighingAddressDatabaseMigration)
                {
                    await MigrateLighingAddressDatabaseMigration();
                    settings.LighingAddressDatabaseMigration = true;
                }
                if (!settings.AddStoreToPayout)
                {
                    await MigrateAddStoreToPayout();
                    settings.AddStoreToPayout = true;
                    await _Settings.UpdateSetting(settings);
                }
                if (!settings.MigrateEmailServerDisableTLSCerts)
                {
                    await MigrateEmailServerDisableTLSCerts();
                    settings.MigrateEmailServerDisableTLSCerts = true;
                    await _Settings.UpdateSetting(settings);
                }
                if (!settings.MigrateWalletColors)
                {
                    await MigrateMigrateLabels();
                    settings.MigrateWalletColors = true;
                    await _Settings.UpdateSetting(settings);
                }
                if (!settings.FileSystemStorageAsDefault)
                {
                    var storageSettings = await _Settings.GetSettingAsync<StorageSettings>();
                    if (storageSettings is null)
                    {
                        storageSettings = new StorageSettings
                        {
                            Provider = StorageProvider.FileSystem,
                            Configuration = JObject.FromObject(new FileSystemStorageConfiguration())
                        };
                        await _Settings.UpdateSetting(storageSettings);
                    }
                    settings.FileSystemStorageAsDefault = true;
                    await _Settings.UpdateSetting(settings);
                }
                if (!settings.FixMappedDomainAppType)
                {
                    await FixMappedDomainAppType();
                    settings.FixMappedDomainAppType = true;
                }
                if (!settings.MigrateAppYmlToJson)
                {
                    await MigrateAppYmlToJson();
                    settings.MigrateAppYmlToJson = true;
                    await _Settings.UpdateSetting(settings);
                }
                if (!settings.MigrateToStoreConfig)
                {
                    await MigrateToStoreConfig();
                    settings.MigrateToStoreConfig = true;
                    await _Settings.UpdateSetting(settings);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error on the MigrationStartupTask");
                throw;
            }
        }

        private async Task MigrateToStoreConfig()
        {
            await using var ctx = _DBContextFactory.CreateContext();
            var stores = await ctx.Stores.ToArrayAsync();
            foreach (var store in stores)
            {
                if (string.IsNullOrEmpty(store.DerivationStrategies))
                    continue;
                var strategies = JObject.Parse(store.DerivationStrategies);
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


        private async Task FixMappedDomainAppType()
        {
            await using var ctx = _DBContextFactory.CreateContext();
            var setting = await ctx.Settings.FirstOrDefaultAsync(s => s.Id == "BTCPayServer.Services.PoliciesSettings");
            if (setting?.Value is null)
                return;
            string MapToString(int v)
            {
                return v switch
                {
                    0 => "PointOfSale",
                    1 => "Crowdfund",
                    _ => throw new NotSupportedException()
                };
            }
            var data = JObject.Parse(setting.Value);
            if (data["RootAppType"]?.Type is JTokenType.Integer)
            {
                var v = data["RootAppType"].Value<int>();
                data["RootAppType"] = new JValue(MapToString(v));
            }
            var arr = data["DomainToAppMapping"] as JArray;
            if (arr != null)
            {
                foreach (var map in arr)
                {
                    if (map["AppType"]?.Type is JTokenType.Integer)
                    {
                        var v = map["AppType"].Value<int>();
                        map["AppType"] = new JValue(MapToString(v));
                    }
                }
            }
            setting.Value = data.ToString();
            await ctx.SaveChangesAsync();
        }
        static async Task<string> GetMigrationState(ApplicationDbContext postgresContext)
        {
            var o = (await postgresContext.Settings.FromSqlRaw("SELECT \"Id\", \"Value\" FROM \"Settings\" WHERE \"Id\"='MigrationData'").AsNoTracking().FirstOrDefaultAsync())?.Value;
            if (o is null)
                return null;
            return JObject.Parse(o)["state"]?.Value<string>();
        }
        static async Task UpdateSequenceInvoiceSearch(ApplicationDbContext postgresContext)
        {
            await postgresContext.Database.ExecuteSqlRawAsync("SELECT SETVAL('\"InvoiceSearches_Id_seq\"', (SELECT max(\"Id\") FROM \"InvoiceSearches\"));");
        }
        private async Task MigrateAppYmlToJson()
        {
            await using var ctx = _DBContextFactory.CreateContext();
            var apps = await ctx.Apps.Where(data => CrowdfundAppType.AppType == data.AppType || PointOfSaleAppType.AppType  == data.AppType)
                .ToListAsync();
            foreach (var app  in apps)
            {
                switch (app.AppType)
                {
                   case CrowdfundAppType.AppType :
                       var cfSettings = app.GetSettings<CrowdfundSettings>();
                       if (!string.IsNullOrEmpty(cfSettings?.PerksTemplate))
                       {
                           cfSettings.PerksTemplate = AppService.SerializeTemplate(ParsePOSYML(cfSettings?.PerksTemplate));
                           app.SetSettings(cfSettings);
                       }
                       break;
                   case PointOfSaleAppType.AppType:
                       var pSettings = app.GetSettings<PointOfSaleSettings>();
                       if (!string.IsNullOrEmpty(pSettings?.Template))
                       {
                           pSettings.Template = AppService.SerializeTemplate(ParsePOSYML(pSettings?.Template));
                           app.SetSettings(pSettings);
                       }
                       break;
                }
            }

            await ctx.SaveChangesAsync();
            
        }
        public static ViewPointOfSaleViewModel.Item[] ParsePOSYML(string yaml)
        {
            var items = new List<ViewPointOfSaleViewModel.Item>();
            var stream = new YamlStream();
            if (string.IsNullOrEmpty(yaml))
                return items.ToArray();
            
            stream.Load(new StringReader(yaml));

            if(stream.Documents.FirstOrDefault()?.RootNode is not YamlMappingNode root)
                return items.ToArray();
            foreach (var posItem in root.Children)
            {
                var trimmedKey = ((YamlScalarNode)posItem.Key).Value?.Trim();
                if (string.IsNullOrEmpty(trimmedKey))
                {
                    continue;
                }

                var currentItem = new ViewPointOfSaleViewModel.Item
                {
                    Id = trimmedKey, Title = trimmedKey, PriceType = ViewPointOfSaleViewModel.ItemPriceType.Fixed
                };
                var itemSpecs = (YamlMappingNode)posItem.Value;
                foreach (var spec in itemSpecs)
                {
                    if (spec.Key is not YamlScalarNode {Value: string keyString} || string.IsNullOrEmpty(keyString))
                        continue;
                    var scalarValue = spec.Value as YamlScalarNode;
                    switch (keyString)
                    {
                        case "title":
                            currentItem.Title = scalarValue?.Value ?? trimmedKey;
                            break;
                        case "inventory":
                            if (int.TryParse(scalarValue?.Value, out var inv))
                            {
                                currentItem.Inventory = inv;
                            }
                            break;
                        case "description":
                            currentItem.Description = scalarValue?.Value;
                            break;
                        case "image":
                            currentItem.Image = scalarValue?.Value;
                            break;
                        case "payment_methods" when spec.Value is YamlSequenceNode pmSequenceNode:

                            currentItem.PaymentMethods = pmSequenceNode.Children
                                .Select(node => (node as YamlScalarNode)?.Value?.Trim())
                                .Where(node => !string.IsNullOrEmpty(node)).ToArray();
                            break;
                        case "price_type":
                        case "custom":
                            if (bool.TryParse(scalarValue?.Value, out var customBoolValue))
                            {
                                if (customBoolValue)
                                {
                                    currentItem.PriceType = currentItem.Price is null or 0
                                        ? ViewPointOfSaleViewModel.ItemPriceType.Topup
                                        : ViewPointOfSaleViewModel.ItemPriceType.Minimum;
                                }
                                else
                                {
                                    currentItem.PriceType = ViewPointOfSaleViewModel.ItemPriceType.Fixed;
                                }
                            }
                            else if (Enum.TryParse<ViewPointOfSaleViewModel.ItemPriceType>(scalarValue?.Value, true,
                                         out var customPriceType))
                            {
                                currentItem.PriceType = customPriceType;
                            }

                            break;
                        case "price":
                            if (decimal.TryParse(scalarValue?.Value, out var price))
                            {
                                currentItem.Price = price;
                            }

                            break;

                        case "buybuttontext":
                            currentItem.BuyButtonText = scalarValue?.Value;
                            break;

                        case "disabled":
                            if (bool.TryParse(scalarValue?.Value, out var disabled))
                            {
                                currentItem.Disabled = disabled;
                            }

                            break;
                    }
                }

                items.Add(currentItem);
            }

            return items.ToArray();
        }

#pragma warning disable CS0612 // Type or member is obsolete

        static WalletBlobInfo GetBlobInfo(WalletData walletData)
        {
            if (walletData.Blob == null || walletData.Blob.Length == 0)
            {
                return new WalletBlobInfo();
            }
            var blobInfo = JsonConvert.DeserializeObject<WalletBlobInfo>(ZipUtils.Unzip(walletData.Blob));
            return blobInfo;
        }

        private async Task MigrateMigrateLabels()
        {
            await using var ctx = _DBContextFactory.CreateContext();
            var wallets = await ctx.Wallets.AsNoTracking().ToArrayAsync();
            foreach (var wallet in wallets)
            {
                var blob = GetBlobInfo(wallet);
                HashSet<string> labels = new HashSet<string>(blob.LabelColors.Count);
                foreach (var label in blob.LabelColors)
                {
                    var labelId = label.Key;
                    if (labelId.StartsWith("{", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            labelId = JObject.Parse(label.Key)["value"].Value<string>();
                        }
                        catch
                        {
                        }
                    }
                    if (!labels.Add(labelId))
                        continue;
                    var obj = new JObject();
                    obj.Add("color", label.Value);
                    ctx.WalletObjects.Add(new WalletObjectData()
                    {
                        WalletId = wallet.Id,
                        Type = WalletObjectData.Types.Label,
                        Id = labelId,
                        Data = obj.ToString()
                    });
                }
            }
            await ctx.SaveChangesAsync();
        }
#pragma warning restore CS0612 // Type or member is obsolete

        // In the past, if a server was considered local network, then we would disable TLS checks.
        // Now we don't do it anymore, as we have an explicit flag (DisableCertificateCheck) to control the behavior.
        // But we need to migrate old users that relied on the behavior before.
        private async Task MigrateEmailServerDisableTLSCerts()
        {
            await using var ctx = _DBContextFactory.CreateContext();
            var serverEmailSettings = await _Settings.GetSettingAsync<Services.Mails.EmailSettings>();
            if (serverEmailSettings?.Server is String server)
            {
                serverEmailSettings.DisableCertificateCheck = Extensions.IsLocalNetwork(server);
                if (serverEmailSettings.DisableCertificateCheck)
                    await _Settings.UpdateSetting(serverEmailSettings);
            }
            var stores = await ctx.Stores.ToArrayAsync();
            foreach (var store in stores)
            {
                var storeBlob = store.GetStoreBlob();
                if (storeBlob.EmailSettings?.Server is String storeServer)
                {
                    storeBlob.EmailSettings.DisableCertificateCheck = Extensions.IsLocalNetwork(storeServer);
                    if (storeBlob.EmailSettings.DisableCertificateCheck)
                        store.SetStoreBlob(storeBlob);
                }
            }
            await ctx.SaveChangesAsync();
        }

        private async Task MigrateLighingAddressDatabaseMigration()
        {
            await using var ctx = _DBContextFactory.CreateContext();

            var lightningAddressSettings =
                await _Settings.GetSettingAsync<UILNURLController.LightningAddressSettings>(
                    nameof(UILNURLController.LightningAddressSettings));

            if (lightningAddressSettings is null)
            {
                return;
            }

            var storeids = lightningAddressSettings.StoreToItemMap.Keys.ToArray();
            var existingStores = (await ctx.Stores.Where(data => storeids.Contains(data.Id)).Select(data => data.Id).ToArrayAsync()).ToHashSet();

            foreach (var storeMap in lightningAddressSettings.StoreToItemMap)
            {
                if (!existingStores.Contains(storeMap.Key))
                    continue;
                foreach (var storeitem in storeMap.Value)
                {
                    if (lightningAddressSettings.Items.TryGetValue(storeitem, out var val))
                    {
                        await _lightningAddressService.Set(
                            new LightningAddressData()
                            {
                                StoreDataId = storeMap.Key,
                                Username = storeitem
                            }
                            .SetBlob(new LightningAddressDataBlob()
                            {
                                Max = val.Max,
                                Min = val.Min,
                                CurrencyCode = val.CurrencyCode
                            }), ctx);
                    }
                }
            }

            await ctx.SaveChangesAsync();
            await _Settings.UpdateSetting<object>(null, nameof(UILNURLController.LightningAddressSettings));
        }

        private async Task MigrateLighingAddressSettingRename()
        {
            var old = await _Settings.GetSettingAsync<UILNURLController.LightningAddressSettings>("BTCPayServer.LNURLController+LightningAddressSettings");
            if (old is not null)
            {
                await _Settings.UpdateSetting(old, nameof(UILNURLController.LightningAddressSettings));
            }
        }

        private async Task MigrateAddStoreToPayout()
        {
            await using var ctx = _DBContextFactory.CreateContext();

            await ctx.Database.ExecuteSqlRawAsync(@"
WITH cte AS (
SELECT DISTINCT p.""Id"", pp.""StoreId"" FROM ""Payouts"" p
JOIN ""PullPayments"" pp  ON pp.""Id"" = p.""PullPaymentDataId""
WHERE p.""StoreDataId"" IS NULL
)
UPDATE ""Payouts"" p
SET ""StoreDataId""=cte.""StoreId""
FROM cte
WHERE cte.""Id""=p.""Id""
");
        }

        private async Task AddInitialUserBlob()
        {
            await using var ctx = _DBContextFactory.CreateContext();
            foreach (var user in await ctx.Users.AsQueryable().ToArrayAsync())
            {
                user.SetBlob(new UserBlob() { ShowInvoiceStatusChangeHint = true });
            }
            await ctx.SaveChangesAsync();
        }

        private async Task MigratePayoutDestinationId()
        {
            await using var ctx = _DBContextFactory.CreateContext();
            foreach (var payoutData in await ctx.Payouts.AsQueryable().ToArrayAsync())
            {
                var pmi = payoutData.GetPayoutMethodId();
                if (pmi is null)
                {
                    continue;
                }
                var handler = _payoutHandlers
                    .TryGet(pmi);
                if (handler is null)
                {
                    continue;
                }
                var claim = await handler?.ParseClaimDestination(payoutData.GetBlob(_btcPayNetworkJsonSerializerSettings).Destination, default);
                payoutData.DedupId = claim.destination?.Id;
            }
            await ctx.SaveChangesAsync();
        }

        private async Task MigrateAppCustomOption()
        {
            await using var ctx = _DBContextFactory.CreateContext();
            foreach (var app in await ctx.Apps.Include(data => data.StoreData).AsQueryable().ToArrayAsync())
            {
                switch (app.AppType)
                {
                    case CrowdfundAppType.AppType:
                        var settings1 = app.GetSettings<CrowdfundSettings>();
                        if (string.IsNullOrEmpty(settings1.TargetCurrency))
                        {
                            settings1.TargetCurrency = app.StoreData.GetStoreBlob().DefaultCurrency;
                            app.SetSettings(settings1);
                        }
                        break;

                    case PointOfSaleAppType.AppType:

                        var settings2 = app.GetSettings<PointOfSaleSettings>();
                        if (string.IsNullOrEmpty(settings2.Currency))
                        {
                            settings2.Currency = app.StoreData.GetStoreBlob().DefaultCurrency;
                            app.SetSettings(settings2);
                        }
                        break;
                }
            }
            await ctx.SaveChangesAsync();
        }

        private async Task MigrateHotwalletProperty()
        {
            await using var ctx = _DBContextFactory.CreateContext();
            foreach (var store in await ctx.Stores.AsQueryable().ToArrayAsync())
            {
                foreach (var (id, paymentMethod) in store.GetPaymentMethodConfigs<DerivationSchemeSettings>(_handlers))
                {
                    paymentMethod.IsHotWallet = paymentMethod.Source == "NBXplorer";
                    if (paymentMethod.IsHotWallet)
                    {
                        paymentMethod.Source = "NBXplorerGenerated";
                        store.SetPaymentMethodConfig(_handlers[id], paymentMethod);
                    }
                }
            }
            await ctx.SaveChangesAsync();
        }

        private async Task MigrateU2FToFIDO2()
        {
            await using var ctx = _DBContextFactory.CreateContext();
            var u2fDevices = await ctx.U2FDevices.ToListAsync();
            foreach (U2FDevice u2FDevice in u2fDevices)
            {
                var fido2 = new Fido2Credential()
                {
                    ApplicationUserId = u2FDevice.ApplicationUserId,
                    Name = u2FDevice.Name,
                    Type = Fido2Credential.CredentialType.FIDO2
                };
                fido2.SetBlob(new Fido2CredentialBlob()
                {
                    SignatureCounter = (uint)u2FDevice.Counter,
                    PublicKey = CreatePublicKeyFromU2fRegistrationData(u2FDevice.PublicKey).EncodeToBytes(),
                    UserHandle = u2FDevice.KeyHandle,
                    Descriptor = new PublicKeyCredentialDescriptor(u2FDevice.KeyHandle),
                    CredType = "u2f"
                });

                await ctx.AddAsync(fido2);

                ctx.Remove(u2FDevice);
            }
            await ctx.SaveChangesAsync();
        }
        //from https://github.com/abergs/fido2-net-lib/blob/0fa7bb4b4a1f33f46c5f7ca4ee489b47680d579b/Test/ExistingU2fRegistrationDataTests.cs#L70
        private static CBORObject CreatePublicKeyFromU2fRegistrationData(byte[] publicKeyData)
        {
            if (publicKeyData.Length != 65)
            {
                throw new ArgumentException("u2f public key must be 65 bytes", nameof(publicKeyData));
            }
            var x = new byte[32];
            var y = new byte[32];
            Buffer.BlockCopy(publicKeyData, 1, x, 0, 32);
            Buffer.BlockCopy(publicKeyData, 33, y, 0, 32);


            var coseKey = CBORObject.NewMap();

            coseKey.Add(COSE.KeyCommonParameter.KeyType, COSE.KeyType.EC2);
            coseKey.Add(COSE.KeyCommonParameter.Alg, -7);

            coseKey.Add(COSE.KeyTypeParameter.Crv, COSE.EllipticCurve.P256);

            coseKey.Add(COSE.KeyTypeParameter.X, x);
            coseKey.Add(COSE.KeyTypeParameter.Y, y);

            return coseKey;
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
                foreach (var prop in strats.Properties().Where(p => IsLN(PaymentMethodId.Parse(p.Name))))
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
                        internalNode.Value = new JValue(LightningPaymentMethodConfig.InternalNode);
                    }
                }

                if (updated)
                    store.DerivationStrategies = strats.ToString();
#pragma warning restore CS0618 // Type or member is obsolete
            }
            await ctx.SaveChangesAsync();
        }

        private bool IsLN(PaymentMethodId paymentMethodId)
        {
            return _handlers.TryGetValue(paymentMethodId, out var v) && v is LightningLikePaymentHandler;
        }
        private bool IsChain(PaymentMethodId paymentMethodId)
        {
            return _handlers.TryGetValue(paymentMethodId, out var v) && v is BitcoinLikePaymentHandler;
        }
        private bool IsLNURL(PaymentMethodId paymentMethodId)
        {
            return _handlers.TryGetValue(paymentMethodId, out var v) && v is LNURLPayPaymentHandler;
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
            TimeSpan cancellationTimeout = TimeSpan.FromDays(1.0);
            using (CancellationTokenSource timeout = new CancellationTokenSource((int)cancellationTimeout.TotalMilliseconds))
            using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken))
            {
retry:
                try
                {
                    _logger.LogInformation("Running the migration scripts...");
                    var db = _DBContextFactory.CreateContext(o => o.CommandTimeout(((int)cancellationTimeout.TotalSeconds) + 1));
                    await db.Database.MigrateAsync(timeout.Token);
                    _logger.LogInformation("All migration scripts ran successfully");
                }
                // Starting up
                catch (ConfigException) { throw; }
                catch (Exception ex) when (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        _logger.LogWarning(ex, "Error while running migration scripts, retrying...");
                        await Task.Delay(1000, cts.Token);
                    }
                    catch { }
                    goto retry;
                }
            }
        }

        void MigrateDerivationSettings(DerivationSchemeSettings s, BTCPayNetwork network)
        {
            if (network == null || s.AccountKeySettings is not (null or { Length: 1 }))
                return;
            s.AccountKeySettings = s.AccountDerivation.GetExtPubKeys().Select(e => new AccountKeySettings()
            {
                AccountKey = e.GetWif(network.NBitcoinNetwork),
            }).ToArray();
#pragma warning disable CS0618 // Type or member is obsolete
            s.AccountKeySettings[0].AccountKeyPath = s.AccountKeyPath;
            s.AccountKeySettings[0].RootFingerprint = s.RootFingerprint;
            s.ExplicitAccountKey = null;
            s.AccountKeyPath = null;
            s.RootFingerprint = null;
#pragma warning restore CS0618 // Type or member is obsolete
        }

        private async Task MigratePaymentMethodCriteria()
        {
            using var ctx = _DBContextFactory.CreateContext();
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
                blob.PaymentMethodCriteria = store.GetPaymentMethodConfigs().Select(c => c.Key).Select(paymentMethodId =>
               {
                   var matchedFromBlob =
                       blob.PaymentMethodCriteria?.SingleOrDefault(criteria => criteria.PaymentMethod == paymentMethodId && criteria.Value != null);
                   return matchedFromBlob switch
                   {
                       null when _handlers.TryGet(paymentMethodId) is LightningLikePaymentHandler &&
                                 lightningMaxValue != null => new PaymentMethodCriteria()
                                 {
                                     Above = false,
                                     PaymentMethod = paymentMethodId,
                                     Value = lightningMaxValue
                                 },
                       null when _handlers.TryGet(paymentMethodId) is BitcoinLikePaymentHandler &&
                                 onChainMinValue != null => new PaymentMethodCriteria()
                                 {
                                     Above = true,
                                     PaymentMethod = paymentMethodId,
                                     Value = onChainMinValue
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
    }
}
