using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Plugins.Crowdfund;
using BTCPayServer.Plugins.PointOfSale;
using BTCPayServer.Plugins.PointOfSale.Models;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Ganss.Xss;
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
        private readonly HtmlSanitizer _HtmlSanitizer;
        public CurrencyNameTable Currencies => _Currencies;
        public AppService(
            IEnumerable<AppBaseType> apps,
            ApplicationDbContextFactory contextFactory,
            InvoiceRepository invoiceRepository,
            CurrencyNameTable currencies,
            DisplayFormatter displayFormatter,
            StoreRepository storeRepository,
            HtmlSanitizer htmlSanitizer)
        {
            _appTypes = apps.ToDictionary(a => a.Type, a => a);
            _ContextFactory = contextFactory;
            _InvoiceRepository = invoiceRepository;
            _Currencies = currencies;
            _storeRepository = storeRepository;
            _HtmlSanitizer = htmlSanitizer;
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
            var appData = await GetApp(appId, null);
            if (appData is null)
                return null;
            var appType = GetAppType(appData.AppType);
            if (appType is null)
                return null;
            return appType.GetInfo(appData);
        }

        public async Task<IEnumerable<ItemStats>> GetItemStats(AppData appData)
        {
            if (GetAppType(appData.AppType) is not IHasItemStatsAppType salesType)
                throw new InvalidOperationException("This app isn't a SalesAppBaseType");
            var paidInvoices = await GetInvoicesForApp(_InvoiceRepository, appData,
                null, new[]
                {
                    InvoiceState.ToString(InvoiceStatusLegacy.Paid),
                    InvoiceState.ToString(InvoiceStatusLegacy.Confirmed),
                    InvoiceState.ToString(InvoiceStatusLegacy.Complete)
                });
            return await salesType.GetItemStats(appData, paidInvoices);
        }

        public static Task<SalesStats> GetSalesStatswithPOSItems(ViewPointOfSaleViewModel.Item[] items,
            InvoiceEntity[] paidInvoices, int numberOfDays)
        {
            var series = paidInvoices
                .Aggregate(new List<InvoiceStatsItem>(), AggregateInvoiceEntitiesForStats(items))
                .GroupBy(entity => entity.Date)
                .Select(entities => new SalesStatsItem
                {
                    Date = entities.Key,
                    Label = entities.Key.ToString("MMM dd", CultureInfo.InvariantCulture),
                    SalesCount = entities.Count()
                });

            // fill up the gaps
            foreach (var i in Enumerable.Range(0, numberOfDays))
            {
                var date = (DateTimeOffset.UtcNow - TimeSpan.FromDays(i)).Date;
                if (!series.Any(e => e.Date == date))
                {
                    series = series.Append(new SalesStatsItem
                    {
                        Date = date,
                        Label = date.ToString("MMM dd", CultureInfo.InvariantCulture)
                    });
                }
            }

            return Task.FromResult(new SalesStats
            {
                SalesCount = series.Sum(i => i.SalesCount),
                Series = series.OrderBy(i => i.Label)
            });
        }

        public async Task<SalesStats> GetSalesStats(AppData app, int numberOfDays = 7)
        {
            if (GetAppType(app.AppType) is not IHasSaleStatsAppType salesType)
                throw new InvalidOperationException("This app isn't a SalesAppBaseType");
            var paidInvoices = await GetInvoicesForApp(_InvoiceRepository, app, DateTimeOffset.UtcNow - TimeSpan.FromDays(numberOfDays),
                new[]
                {
                    InvoiceState.ToString(InvoiceStatusLegacy.Paid),
                    InvoiceState.ToString(InvoiceStatusLegacy.Confirmed),
                    InvoiceState.ToString(InvoiceStatusLegacy.Complete)
                });

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
                if (data is { Cart.Length: > 0 })
                {
                    foreach (var lineItem in data.Cart)
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
                                Date = e.InvoiceTime.Date
                            });
                        }
                    }
                }
                else
                {
                    res.Add(new InvoiceStatsItem
                    {
                        ItemCode = e.Metadata.ItemCode ?? typeof(PosViewType).DisplayName(PosViewType.Light.ToString()),
                        FiatPrice = e.PaidAmount.Net,
                        Date = e.InvoiceTime.Date
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
                Status = status ?? new[]{
                    InvoiceState.ToString(InvoiceStatusLegacy.New),
                    InvoiceState.ToString(InvoiceStatusLegacy.Paid),
                    InvoiceState.ToString(InvoiceStatusLegacy.Confirmed),
                    InvoiceState.ToString(InvoiceStatusLegacy.Complete)},
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
                    string posViewStyle = (settings.EnableShoppingCart ? PosViewType.Cart : settings.DefaultView).ToString();
                    style = typeof(PosViewType).DisplayName(posViewStyle);
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
        public static ViewPointOfSaleViewModel.Item[] Parse(string template, bool includeDisabled = true)
        {
            if (string.IsNullOrWhiteSpace(template))
                return Array.Empty<ViewPointOfSaleViewModel.Item>();

            return  JsonConvert.DeserializeObject<ViewPointOfSaleViewModel.Item[]>(template, _defaultSerializer)!.Where(item => includeDisabled || !item.Disabled).ToArray();
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

    public class ItemStats
    {
        public string ItemCode { get; set; }
        public string Title { get; set; }
        public int SalesCount { get; set; }
        public decimal Total { get; set; }
        public string TotalFormatted { get; set; }
    }

    public class SalesStats
    {
        public int SalesCount { get; set; }
        public IEnumerable<SalesStatsItem> Series { get; set; }
    }

    public class SalesStatsItem
    {
        public DateTime Date { get; set; }
        public string Label { get; set; }
        public int SalesCount { get; set; }
    }
}
