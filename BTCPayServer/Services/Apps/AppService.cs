using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Plugins.Crowdfund;
using BTCPayServer.Plugins.PointOfSale;
using BTCPayServer.Plugins.PointOfSale.Models;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Dapper;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using NBitcoin.DataEncoders;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Services.Apps
{
    public class AppService
    {
        private readonly Dictionary<string, AppBaseType> _appTypes;
        static AppService()
        {
            _defaultSerializer = new JsonSerializerSettings()
            {
                ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver(),
                Formatting = Formatting.None
            };
        }

        private static JsonSerializerSettings _defaultSerializer;

        readonly ApplicationDbContextFactory _ContextFactory;
        private readonly InvoiceRepository _InvoiceRepository;
        readonly CurrencyNameTable _Currencies;
        private readonly DisplayFormatter _displayFormatter;
        private readonly StoreRepository _storeRepository;
        public CurrencyNameTable Currencies => _Currencies;

        public AppService(
            IEnumerable<AppBaseType> apps,
            ApplicationDbContextFactory contextFactory,
            InvoiceRepository invoiceRepository,
            CurrencyNameTable currencies,
            DisplayFormatter displayFormatter,
            StoreRepository storeRepository)
        {
            _appTypes = apps.ToDictionary(a => a.Type, a => a);
            _ContextFactory = contextFactory;
            _InvoiceRepository = invoiceRepository;
            _Currencies = currencies;
            _storeRepository = storeRepository;
            _displayFormatter = displayFormatter;
        }
#nullable enable
        public Dictionary<string, string> GetAvailableAppTypes()
        {
            return _appTypes.ToDictionary(app => app.Key, app => app.Value.Description);
        }

        public AppBaseType? GetAppType(string appType)
        {
            _appTypes.TryGetValue(appType, out var a);
            return a;
        }

        public async Task<object?> GetInfo(string appId)
        {
            var appData = await GetApp(appId, null, includeStore: true);
            if (appData is null)
                return null;
            var appType = GetAppType(appData.AppType);
            if (appType is null)
                return null;
            return await appType.GetInfo(appData);
        }

        public async Task<IEnumerable<AppItemStats>> GetItemStats(AppData appData)
        {
            if (GetAppType(appData.AppType) is not IHasItemStatsAppType salesType)
                throw new InvalidOperationException("This app isn't a SalesAppBaseType");
            var paidInvoices = await GetInvoicesForApp(_InvoiceRepository, appData, null,
            [
                InvoiceStatus.Processing.ToString(),
                InvoiceStatus.Settled.ToString()
            ]);
            return await salesType.GetItemStats(appData, paidInvoices);
        }

        public static Task<AppSalesStats> GetSalesStatswithPOSItems(ViewPointOfSaleViewModel.Item[] items,
            InvoiceEntity[] paidInvoices, int numberOfDays)
        {
            var series = paidInvoices
                .Aggregate([], AggregateInvoiceEntitiesForStats(items))
                .GroupBy(entity => entity.Date)
                .Select(entities => new AppSalesStatsItem
                {
                    Date = entities.Key,
                    Label = entities.Key.ToString("MMM dd", CultureInfo.InvariantCulture),
                    SalesCount = entities.Count()
                }).ToList();

            // fill up the gaps
            foreach (var i in Enumerable.Range(0, numberOfDays))
            {
                var date = (DateTimeOffset.UtcNow - TimeSpan.FromDays(i)).Date;
                if (series.All(e => e.Date != date))
                {
                    series.Add(new AppSalesStatsItem
                    {
                        Date = date,
                        Label = date.ToString("MMM dd", CultureInfo.InvariantCulture)
                    });
                }
            }

            return Task.FromResult(new AppSalesStats
            {
                SalesCount = series.Sum(i => i.SalesCount),
                Series = series.OrderBy(i => i.Date)
            });
        }

        public async Task<AppSalesStats> GetSalesStats(AppData app, int numberOfDays = 7)
        {
            if (GetAppType(app.AppType) is not IHasSaleStatsAppType salesType)
                throw new InvalidOperationException("This app isn't a SalesAppBaseType");
            var paidInvoices = await GetInvoicesForApp(_InvoiceRepository, app, DateTimeOffset.UtcNow - TimeSpan.FromDays(numberOfDays));

            return await salesType.GetSalesStats(app, paidInvoices, numberOfDays);
        }

        public class InvoiceStatsItem
        {
            public string ItemCode { get; set; } = string.Empty;
            public decimal FiatPrice { get; set; }
            public DateTime Date { get; set; }
        }

        public static Func<List<InvoiceStatsItem>, InvoiceEntity, List<InvoiceStatsItem>> AggregateInvoiceEntitiesForStats(ViewPointOfSaleViewModel.Item[] items)
        {
            return (res, e) =>
            {
                // flatten single items from POS data
                var data = e.Metadata.PosData?.ToObject<PosAppData>();
                var hasCart = data is { Cart.Length: > 0 };
                var hasAmounts = data is { Amounts.Length: > 0 };
                var date = e.InvoiceTime.Date;
                var itemCode = e.Metadata.ItemCode ?? typeof(Plugins.PointOfSale.PosViewType).DisplayName(Plugins.PointOfSale.PosViewType.Light.ToString());
                if (hasCart)
                {
                    foreach (var lineItem in data!.Cart)
                    {
                        var item = items.FirstOrDefault(p => p.Id == lineItem.Id);
                        if (item == null)
                            continue;

                        for (var i = 0; i < lineItem.Count; i++)
                        {
                            res.Add(new InvoiceStatsItem
                            {
                                ItemCode = item.Id,
                                FiatPrice = lineItem.Price,
                                Date = date
                            });
                        }
                    }
                }
                if (hasAmounts)
                {
                    res.AddRange(data!.Amounts.Select(amount => new InvoiceStatsItem { ItemCode = itemCode, FiatPrice = amount, Date = date }));
                }
                // no further info, just add the total amount
                if (!hasCart && !hasAmounts)
                {
                    res.Add(new InvoiceStatsItem
                    {
                        ItemCode = itemCode,
                        FiatPrice = e.PaidAmount.Net,
                        Date = date
                    });
                }
                return res;
            };
        }

        public static string GetAppSearchTerm(AppData app) => GetAppSearchTerm(app.AppType, app.Id);
        public static string GetAppSearchTerm(string appType, string appId) =>
            appType switch
            {
                CrowdfundAppType.AppType => $"crowdfund-app_{appId}",
                PointOfSaleAppType.AppType => $"pos-app_{appId}",
                _ => $"{appType}_{appId}"
            };

        public static string GetAppInternalTag(string appId) => $"APP#{appId}";
        public static string[] GetAppInternalTags(InvoiceEntity invoice)
        {
            return invoice.GetInternalTags("APP#");
        }
        
        public static string GetRandomOrderId(int length = 16)
        {
            return Encoders.Base58.EncodeData(RandomUtils.GetBytes(length));
        }

        public static async Task<InvoiceEntity[]> GetInvoicesForApp(InvoiceRepository invoiceRepository, AppData appData, DateTimeOffset? startDate = null, string[]? status = null)
        {
            var invoices = await invoiceRepository.GetInvoices(new InvoiceQuery
            {
                StoreId = new[] { appData.StoreDataId },
                TextSearch = appData.TagAllInvoices ? null : GetAppSearchTerm(appData),
                Status = status,
                StartDate = startDate
            });

            // Old invoices may have invoices which were not tagged
            invoices = invoices.Where(inv => appData.TagAllInvoices || inv.Version < InvoiceEntity.InternalTagSupport_Version ||
                                             inv.InternalTags.Contains(GetAppInternalTag(appData.Id))).ToArray();
            return invoices;
        }

        public async Task<bool> DeleteApp(AppData appData)
        {
            await using var ctx = _ContextFactory.CreateContext();
            ctx.Apps.Add(appData);
            ctx.Entry(appData).State = EntityState.Deleted;
            return await ctx.SaveChangesAsync() == 1;
        }

        public async Task<bool> SetArchived(AppData appData, bool archived)
        {
            await using var ctx = _ContextFactory.CreateContext();
            appData.Archived = archived;
            ctx.Entry(appData).State = EntityState.Modified;
            return await ctx.SaveChangesAsync() == 1;
        }

        public async Task<ListAppsViewModel.ListAppViewModel[]> GetAllApps(string? userId, bool allowNoUser = false, string? storeId = null, bool includeArchived = false)
        {
            await using var ctx = _ContextFactory.CreateContext();
            var types = GetAvailableAppTypes().Select(at => at.Key).ToHashSet();
            var listApps = (await ctx.UserStore
                .Where(us =>
                    (allowNoUser && string.IsNullOrEmpty(userId) || us.ApplicationUserId == userId) &&
                    (storeId == null || us.StoreDataId == storeId))
                .Include(store => store.StoreRole)
                .Include(store => store.StoreData)
                .Join(ctx.Apps, us => us.StoreDataId, app => app.StoreDataId, (us, app) => new { us, app })
                .Where(b => types.Contains(b.app.AppType) && (!b.app.Archived || b.app.Archived == includeArchived))
                .OrderBy(b => b.app.Created)
                .ToArrayAsync()).Select(arg => new ListAppsViewModel.ListAppViewModel
            {
                Role = StoreRepository.ToStoreRole(arg.us.StoreRole),
                StoreId = arg.us.StoreDataId,
                StoreName = arg.us.StoreData.StoreName,
                AppName = arg.app.Name,
                AppType = arg.app.AppType,
                Id = arg.app.Id,
                Created = arg.app.Created,
                Archived = arg.app.Archived,
                App = arg.app
            }).ToArray();

            // allowNoUser can lead to apps being included twice, unify them with distinct
            if (allowNoUser)
            {
                listApps = listApps.DistinctBy(a => a.Id).ToArray();
            }

            foreach (ListAppsViewModel.ListAppViewModel app in listApps)
            {
                app.ViewStyle = GetAppViewStyle(app.App, app.AppType);
            }

            return listApps;
        }

        public string GetAppViewStyle(AppData app, string appType)
        {
            string style;
            switch (appType)
            {
                case PointOfSaleAppType.AppType:
                    var settings = app.GetSettings<PointOfSaleSettings>();
                    string posViewStyle = (settings.EnableShoppingCart ? Plugins.PointOfSale.PosViewType.Cart : settings.DefaultView).ToString();
                    style = typeof(Plugins.PointOfSale.PosViewType).DisplayName(posViewStyle);
                    break;

                default:
                    style = string.Empty;
                    break;
            }

            return style;
        }

        public async Task<List<AppData>> GetApps(string[] appIds, bool includeStore = false, bool includeArchived = false)
        {
            await using var ctx = _ContextFactory.CreateContext();
            var types = GetAvailableAppTypes().Select(at => at.Key);
            var query = ctx.Apps
                .Where(app => appIds.Contains(app.Id))
                .Where(app => types.Contains(app.AppType) && (!app.Archived || app.Archived == includeArchived));
            if (includeStore)
            {
                query = query.Include(data => data.StoreData);
            }
            return await query.ToListAsync();
        }

        public async Task<List<AppData>> GetApps(string appType)
        {
            await using var ctx = _ContextFactory.CreateContext();
            var query = ctx.Apps
                .Where(app => app.AppType == appType);
            return await query.ToListAsync();
        }

        public async Task<AppData?> GetApp(string appId, string? appType, bool includeStore = false, bool includeArchived = false)
        {
            await using var ctx = _ContextFactory.CreateContext();
            var types = GetAvailableAppTypes().Select(at => at.Key);
            var query = ctx.Apps
                .Where(us => us.Id == appId && (appType == null || us.AppType == appType))
                .Where(app => types.Contains(app.AppType) && (!app.Archived || app.Archived == includeArchived));
            if (includeStore)
            {
                query = query.Include(data => data.StoreData);
            }
            return await query.FirstOrDefaultAsync();
        }

        public Task<StoreData?> GetStore(AppData app)
        {
            return _storeRepository.FindStore(app.StoreDataId);
        }

        public static string SerializeTemplate(ViewPointOfSaleViewModel.Item[] items)
        {
               return JsonConvert.SerializeObject(items, Formatting.Indented, _defaultSerializer);
        }
        public static ViewPointOfSaleViewModel.Item[] Parse(string template, bool includeDisabled = true, bool throws = false)
        {
            if (string.IsNullOrWhiteSpace(template)) return [];
            var allItems = JsonConvert.DeserializeObject<ViewPointOfSaleViewModel.Item[]>(template, _defaultSerializer)!;
            // ensure all items have an id, which is also unique
            var itemsWithoutId = allItems.Where(i => string.IsNullOrEmpty(i.Id)).ToList();
            if (itemsWithoutId.Any() && throws) throw new ArgumentException($"Missing ID for item \"{itemsWithoutId.First().Title}\".");
            // find items with duplicate IDs
            var duplicateIds = allItems.GroupBy(i => i.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (duplicateIds.Any() && throws) throw new ArgumentException($"Duplicate ID \"{duplicateIds.First()}\".");
            return allItems.Where(item => (includeDisabled || !item.Disabled) && !itemsWithoutId.Contains(item) && !duplicateIds.Contains(item.Id)).ToArray();
        }
#nullable restore
#nullable enable
        public async Task<AppData?> GetAppDataIfOwner(string userId, string appId, string? type = null)
        {
            if (userId == null || appId == null)
                return null;
            await using var ctx = _ContextFactory.CreateContext();
            var app = await ctx.UserStore
                .Include(store => store.StoreRole)
                .Where(us => us.ApplicationUserId == userId && us.StoreRole.Permissions.Contains(Policies.CanModifyStoreSettings))
                .SelectMany(us => us.StoreData.Apps.Where(a => a.Id == appId))
                .FirstOrDefaultAsync();
            if (app == null)
                return null;
            if (type != null && type != app.AppType)
                return null;
            return app;
        }

        public async Task<AppData?> GetAppData(string userId, string appId, string? type = null)
        {
            if (userId == null || appId == null)
                return null;
            await using var ctx = _ContextFactory.CreateContext();
            var app = await ctx.UserStore
                .Where(us => us.ApplicationUserId == userId && us.StoreData != null && us.StoreData.UserStores.Any(u => u.ApplicationUserId == userId))
                .SelectMany(us => us.StoreData.Apps.Where(a => a.Id == appId))
                .FirstOrDefaultAsync();
            if (app == null)
                return null;
            if (type != null && type != app.AppType)
                return null;
            return app;
        }

        record AppSettingsWithXmin(string apptype, string settings, uint xmin);
        public record InventoryChange(string ItemId, int Delta);
        public async Task UpdateInventory(string appId, InventoryChange[] changes)
        {
            await using var ctx = _ContextFactory.CreateContext();
            // We use xmin to make sure we don't override changes made by another process
retry:
            var connection = ctx.Database.GetDbConnection();
            var row = connection.QueryFirstOrDefault<AppSettingsWithXmin>(
                "SELECT \"AppType\" AS apptype, \"Settings\" AS settings, xmin FROM \"Apps\" WHERE \"Id\"=@appId", new { appId }
                );
            if (row?.settings is null)
                return;
            var templatePath = row.apptype switch
            {
                CrowdfundAppType.AppType => "PerksTemplate",
                _ => "Template"
            };
            var settings = JObject.Parse(row.settings);
            if (!settings.TryGetValue(templatePath, out var template))
                return;

            var items = template.Type switch
            {
                JTokenType.String => JArray.Parse(template.Value<string>()!),
                JTokenType.Array => (JArray)template,
                _ => null
            };
            if (items is null)
                return;
            bool hasChange = false;
            foreach (var change in changes)
            {
                var item = items.FirstOrDefault(i => i["id"]?.Value<string>() == change.ItemId && i["inventory"]?.Type is JTokenType.Integer);
                if (item is null)
                    continue;
                var inventory = item["inventory"]!.Value<int>();
                inventory += change.Delta;
                item["inventory"] = inventory;
                hasChange = true;
            }
            if (!hasChange)
                return;
            settings[templatePath] = items.ToString(Formatting.None);
            var updated = await connection.ExecuteAsync("UPDATE \"Apps\" SET \"Settings\"=@v::JSONB WHERE \"Id\"=@appId AND xmin=@xmin", new { appId, xmin = (int)row.xmin, v = settings.ToString(Formatting.None) }) == 1;
            // If we can't update, it means someone else updated the row, so we need to retry
            if (!updated)
                goto retry;
        }

        public async Task UpdateOrCreateApp(AppData app)
        {
            await using var ctx = _ContextFactory.CreateContext();
            if (string.IsNullOrEmpty(app.Id))
            {
                app.Id = Encoders.Base58.EncodeData(RandomUtils.GetBytes(20));
                app.Created = DateTimeOffset.UtcNow;
                await ctx.Apps.AddAsync(app);
            }
            else
            {
                ctx.Apps.Update(app);
                ctx.Entry(app).Property(data => data.Created).IsModified = false;
                ctx.Entry(app).Property(data => data.Id).IsModified = false;
                ctx.Entry(app).Property(data => data.AppType).IsModified = false;
            }
            await ctx.SaveChangesAsync();
        }

        private static bool TryParseJson(string json, [MaybeNullWhen(false)] out JObject result)
        {
            result = null;
            try
            {
                result = JObject.Parse(json);
                return true;
            }
            catch
            {
                return false;
            }
        }
#nullable enable
        
        public static bool TryParsePosCartItems(JObject? posData, [MaybeNullWhen(false)] out List<PosCartItem> cartItems)
        {
            cartItems = null;
            if (posData is null)
                return false;
            if (!posData.TryGetValue("cart", out var cartObject) || cartObject is null)
                return false;
            try
            {
                cartItems = new List<PosCartItem>();
                foreach (var o in cartObject.OfType<JObject>())
                {
                    var id = o.GetValue("id", StringComparison.InvariantCulture)?.ToString();
                    if (id == null)
                        continue;
                    var countStr = o.GetValue("count", StringComparison.InvariantCulture)?.ToString() ?? string.Empty;
                    var price = o.GetValue("price") switch
                    {
                        JValue v => v.Value<decimal>(),
                        // Don't crash on legacy format
                        JObject v2 => v2["value"]?.Value<decimal>() ?? 0m,
                        _ => 0m
                    };
                    if (int.TryParse(countStr, out var count))
                    {
                        cartItems.Add(new PosCartItem { Id = id, Count = count, Price = price });
                    }
                }
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public async Task SetDefaultSettings(AppData appData, string defaultCurrency)
        {
            var app = GetAppType(appData.AppType);
            if (app is null)
            {
                appData.SetSettings(null);
            }
            else
            {
                await app.SetDefaultSettings(appData, defaultCurrency);
            }
        }

        public async Task<string?> ViewLink(AppData app)
        {
            var appType = GetAppType(app.AppType);
            return await appType?.ViewLink(app)!;
        }
#nullable restore
    }

    public class PosCartItem
    {
        public string Id { get; set; }
        public int Count { get; set; }
        public decimal Price { get; set; }
    }
}
