using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using ExchangeSharp;
using Ganss.XSS;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using NBitcoin.DataEncoders;
using Newtonsoft.Json.Linq;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;
using static BTCPayServer.Models.AppViewModels.ViewCrowdfundViewModel;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Services.Apps
{
    public class AppService
    {
        readonly ApplicationDbContextFactory _ContextFactory;
        private readonly InvoiceRepository _InvoiceRepository;
        readonly CurrencyNameTable _Currencies;
        private readonly StoreRepository _storeRepository;
        private readonly HtmlSanitizer _HtmlSanitizer;
        public CurrencyNameTable Currencies => _Currencies;
        public AppService(ApplicationDbContextFactory contextFactory,
                          InvoiceRepository invoiceRepository,
                          CurrencyNameTable currencies,
                          StoreRepository storeRepository,
                          HtmlSanitizer htmlSanitizer)
        {
            _ContextFactory = contextFactory;
            _InvoiceRepository = invoiceRepository;
            _Currencies = currencies;
            _storeRepository = storeRepository;
            _HtmlSanitizer = htmlSanitizer;
        }

        public async Task<object> GetAppInfo(string appId)
        {
            var app = await GetApp(appId, AppType.Crowdfund, true);
            if (app != null)
            {
                return await GetInfo(app);
            }
            return null;
        }

        private async Task<ViewCrowdfundViewModel> GetInfo(AppData appData)
        {
            var settings = appData.GetSettings<CrowdfundSettings>();
            var resetEvery = settings.StartDate.HasValue ? settings.ResetEvery : CrowdfundResetEvery.Never;
            DateTime? lastResetDate = null;
            DateTime? nextResetDate = null;
            if (resetEvery != CrowdfundResetEvery.Never)
            {
                lastResetDate = settings.StartDate.Value;

                nextResetDate = lastResetDate.Value;
                while (DateTime.UtcNow >= nextResetDate)
                {
                    lastResetDate = nextResetDate;
                    switch (resetEvery)
                    {
                        case CrowdfundResetEvery.Hour:
                            nextResetDate = lastResetDate.Value.AddHours(settings.ResetEveryAmount);
                            break;
                        case CrowdfundResetEvery.Day:
                            nextResetDate = lastResetDate.Value.AddDays(settings.ResetEveryAmount);
                            break;
                        case CrowdfundResetEvery.Month:
                            nextResetDate = lastResetDate.Value.AddMonths(settings.ResetEveryAmount);
                            break;
                        case CrowdfundResetEvery.Year:
                            nextResetDate = lastResetDate.Value.AddYears(settings.ResetEveryAmount);
                            break;
                    }
                }
            }

            var invoices = await GetInvoicesForApp(appData, lastResetDate);
            var completeInvoices = invoices.Where(IsComplete).ToArray();
            var pendingInvoices = invoices.Where(IsPending).ToArray();
            var paidInvoices = invoices.Where(IsPaid).ToArray();

            var pendingPayments = GetContributionsByPaymentMethodId(settings.TargetCurrency, pendingInvoices, !settings.EnforceTargetAmount);
            var currentPayments = GetContributionsByPaymentMethodId(settings.TargetCurrency, completeInvoices, !settings.EnforceTargetAmount);

            var perkCount = paidInvoices
                .Where(entity => !string.IsNullOrEmpty(entity.Metadata.ItemCode))
                .GroupBy(entity => entity.Metadata.ItemCode)
                .ToDictionary(entities => entities.Key, entities => entities.Count());

            Dictionary<string, decimal> perkValue = new();
            if (settings.DisplayPerksValue)
            {
                perkValue = paidInvoices
                    .Where(entity => entity.Currency.Equals(settings.TargetCurrency, StringComparison.OrdinalIgnoreCase) &&
                                     !string.IsNullOrEmpty(entity.Metadata.ItemCode))
                    .GroupBy(entity => entity.Metadata.ItemCode)
                    .ToDictionary(entities => entities.Key, entities =>
                        entities.Sum(entity => entity.GetPayments(true).Sum(pay =>
                        {
                            var paymentMethodId = pay.GetPaymentMethodId();
                            var value = pay.GetCryptoPaymentData().GetValue() - pay.NetworkFee;
                            var rate = entity.GetPaymentMethod(paymentMethodId).Rate;
                            return rate * value;
                        })));
            }
            
            var perks = Parse(settings.PerksTemplate, settings.TargetCurrency);
            if (settings.SortPerksByPopularity)
            {
                var ordered = perkCount.OrderByDescending(pair => pair.Value);
                var newPerksOrder = ordered
                    .Select(keyValuePair => perks.SingleOrDefault(item => item.Id == keyValuePair.Key))
                    .Where(matchingPerk => matchingPerk != null)
                    .ToList();
                var remainingPerks = perks.Where(item => !newPerksOrder.Contains(item));
                newPerksOrder.AddRange(remainingPerks);
                perks = newPerksOrder.ToArray();
            }

            return new ViewCrowdfundViewModel
            {
                Title = settings.Title,
                Tagline = settings.Tagline,
                Description = settings.Description,
                CustomCSSLink = settings.CustomCSSLink,
                MainImageUrl = settings.MainImageUrl,
                EmbeddedCSS = settings.EmbeddedCSS,
                StoreId = appData.StoreDataId,
                AppId = appData.Id,
                StartDate = settings.StartDate?.ToUniversalTime(),
                EndDate = settings.EndDate?.ToUniversalTime(),
                TargetAmount = settings.TargetAmount,
                TargetCurrency = settings.TargetCurrency,
                EnforceTargetAmount = settings.EnforceTargetAmount,
                Perks = perks,
                Enabled = settings.Enabled,
                DisqusEnabled = settings.DisqusEnabled,
                SoundsEnabled = settings.SoundsEnabled,
                DisqusShortname = settings.DisqusShortname,
                AnimationsEnabled = settings.AnimationsEnabled,
                ResetEveryAmount = settings.ResetEveryAmount,
                ResetEvery = Enum.GetName(typeof(CrowdfundResetEvery), settings.ResetEvery),
                DisplayPerksRanking = settings.DisplayPerksRanking,
                PerkCount = perkCount,
                PerkValue = perkValue,
                NeverReset = settings.ResetEvery == CrowdfundResetEvery.Never,
                Sounds = settings.Sounds,
                AnimationColors = settings.AnimationColors,
                CurrencyData = _Currencies.GetCurrencyData(settings.TargetCurrency, true),
                CurrencyDataPayments = Enumerable.DistinctBy(currentPayments.Select(pair => pair.Key)
                        .Concat(pendingPayments.Select(pair => pair.Key))
                        .Select(id => _Currencies.GetCurrencyData(id.CryptoCode, true)), data => data.Code)
                    .ToDictionary(data => data.Code, data => data),
                Info = new CrowdfundInfo
                {
                    TotalContributors = paidInvoices.Length,
                    ProgressPercentage = (currentPayments.TotalCurrency / settings.TargetAmount) * 100,
                    PendingProgressPercentage = (pendingPayments.TotalCurrency / settings.TargetAmount) * 100,
                    LastUpdated = DateTime.UtcNow,
                    PaymentStats = currentPayments.ToDictionary(c => c.Key.ToString(), c => c.Value.Value),
                    PendingPaymentStats = pendingPayments.ToDictionary(c => c.Key.ToString(), c => c.Value.Value),
                    LastResetDate = lastResetDate,
                    NextResetDate = nextResetDate,
                    CurrentPendingAmount = pendingPayments.TotalCurrency,
                    CurrentAmount = currentPayments.TotalCurrency
                }
            };
        }

        private static bool IsPending(InvoiceEntity entity)
        {
            return !(entity.Status == InvoiceStatusLegacy.Complete || entity.Status == InvoiceStatusLegacy.Confirmed);
        }

        private static bool IsComplete(InvoiceEntity entity)
        {
            return entity.Status == InvoiceStatusLegacy.Complete || entity.Status == InvoiceStatusLegacy.Confirmed;
        }

        public async Task<IEnumerable<ItemStats>> GetPerkStats(AppData appData)
        {
            var settings = appData.GetSettings<CrowdfundSettings>();
            var invoices = await GetInvoicesForApp(appData);
            var paidInvoices = invoices.Where(IsPaid).ToArray();
            var currencyData = _Currencies.GetCurrencyData(settings.TargetCurrency, true);
            var perks = Parse(settings.PerksTemplate, settings.TargetCurrency);
            var perkCount = paidInvoices
                .Where(entity => !string.IsNullOrEmpty(entity.Metadata.ItemCode) &&
                                 entity.Currency.Equals(settings.TargetCurrency, StringComparison.OrdinalIgnoreCase))
                .GroupBy(entity => entity.Metadata.ItemCode)
                .Select(entities =>
                {
                    var total = entities
                        .Sum(entity => entity.GetPayments(true)
                            .Sum(pay => {
                                var paymentMethodId = pay.GetPaymentMethodId();
                                var value = pay.GetCryptoPaymentData().GetValue() - pay.NetworkFee;
                                var rate = entity.GetPaymentMethod(paymentMethodId).Rate;
                                return rate * value;
                            }));
                    var itemCode = entities.Key;
                    var perk = perks.First(p => p.Id == itemCode);
                    return new ItemStats
                    {
                        ItemCode = itemCode,
                        Title = perk.Title,
                        SalesCount = entities.Count(), 
                        Total = total,
                        TotalFormatted = $"{total.ShowMoney(currencyData.Divisibility)} {settings.TargetCurrency}"
                    };
                })
                .OrderByDescending(stats => stats.SalesCount);
            
            return perkCount;
        }

        public async Task<SalesStats> GetSalesStats(AppData appData, int numberOfDays = 7)
        {
            var invoices = await GetInvoicesForApp(appData);
            var paidInvoices = invoices.Where(IsPaid).ToArray();
            var series = paidInvoices
                .Where(entity => !string.IsNullOrEmpty(entity.Metadata.ItemCode) &&
                                 entity.InvoiceTime > DateTimeOffset.UtcNow - TimeSpan.FromDays(numberOfDays))
                .GroupBy(entity => entity.InvoiceTime.Date)
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
            
            return new SalesStats
            {
                SalesCount = paidInvoices.Length,
                Series = series.OrderBy(i => i.Label)
            };
        }

        private static bool IsPaid(InvoiceEntity entity)
        {
            return entity.Status == InvoiceStatusLegacy.Complete || entity.Status == InvoiceStatusLegacy.Confirmed || entity.Status == InvoiceStatusLegacy.Paid;
        }

        public static string GetCrowdfundOrderId(string appId) => $"crowdfund-app_{appId}";
        public static string GetAppInternalTag(string appId) => $"APP#{appId}";
        public static string[] GetAppInternalTags(InvoiceEntity invoice)
        {
            return invoice.GetInternalTags("APP#");
        }
        
        private async Task<InvoiceEntity[]> GetInvoicesForApp(AppData appData, DateTime? startDate = null)
        {
            var invoices = await _InvoiceRepository.GetInvoices(new InvoiceQuery()
            {
                StoreId = new[] { appData.StoreData.Id },
                OrderId = appData.TagAllInvoices ? null : new[] { GetCrowdfundOrderId(appData.Id) },
                Status = new[]{
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

        public async Task<StoreData[]> GetOwnedStores(string userId)
        {
            using var ctx = _ContextFactory.CreateContext();
            return await ctx.UserStore
                .Where(us => us.ApplicationUserId == userId && us.Role == StoreRoles.Owner)
                .Select(u => u.StoreData)
                .ToArrayAsync();
        }

        public async Task<bool> DeleteApp(AppData appData)
        {
            using var ctx = _ContextFactory.CreateContext();
            ctx.Apps.Add(appData);
            ctx.Entry(appData).State = EntityState.Deleted;
            return await ctx.SaveChangesAsync() == 1;
        }

        public async Task<ListAppsViewModel.ListAppViewModel[]> GetAllApps(string userId, bool allowNoUser = false, string storeId = null)
        {
            using var ctx = _ContextFactory.CreateContext();
            var listApps = await ctx.UserStore
                .Where(us =>
                    (allowNoUser && string.IsNullOrEmpty(userId) || us.ApplicationUserId == userId) &&
                    (storeId == null || us.StoreDataId == storeId))
                .Join(ctx.Apps, us => us.StoreDataId, app => app.StoreDataId,
                    (us, app) =>
                        new ListAppsViewModel.ListAppViewModel()
                        {
                            IsOwner = us.Role == StoreRoles.Owner,
                            StoreId = us.StoreDataId,
                            StoreName = us.StoreData.StoreName,
                            AppName = app.Name,
                            AppType = app.AppType,
                            Id = app.Id
                        })
                .ToArrayAsync();

            foreach (ListAppsViewModel.ListAppViewModel app in listApps)
            {
                app.ViewStyle = await GetAppViewStyleAsync(app.Id, app.AppType);
            }

            return listApps;
        }

        public async Task<string> GetAppViewStyleAsync(string appId, string appType)
        {
            AppType appTypeEnum = Enum.Parse<AppType>(appType);
            AppData appData = await GetApp(appId, appTypeEnum, false);
            var settings = appData.GetSettings<UIAppsController.PointOfSaleSettings>();

            string style;
            switch (appTypeEnum)
            {
                case AppType.PointOfSale:
                    string posViewStyle = (settings.EnableShoppingCart ? PosViewType.Cart : settings.DefaultView).ToString();
                    style = typeof(PosViewType).DisplayName(posViewStyle);
                    break;
                case AppType.Crowdfund:
                    style = string.Empty;
                    break;
                default:
                    style = string.Empty;
                    break;
            }

            return style;
        }

        public async Task<List<AppData>> GetApps(string[] appIds, bool includeStore = false)
        {
            using var ctx = _ContextFactory.CreateContext();
            var query = ctx.Apps
                .Where(us => appIds.Contains(us.Id));

            if (includeStore)
            {
                query = query.Include(data => data.StoreData);
            }
            return await query.ToListAsync();
        }

        public async Task<AppData> GetApp(string appId, AppType? appType, bool includeStore = false)
        {
            using var ctx = _ContextFactory.CreateContext();
            var query = ctx.Apps
                .Where(us => us.Id == appId &&
                             (appType == null || us.AppType == appType.ToString()));

            if (includeStore)
            {
                query = query.Include(data => data.StoreData);
            }
            return await query.FirstOrDefaultAsync();
        }

        public Task<StoreData> GetStore(AppData app)
        {
            return _storeRepository.FindStore(app.StoreDataId);
        }

        public string SerializeTemplate(ViewPointOfSaleViewModel.Item[] items)
        {
            var mappingNode = new YamlMappingNode();
            foreach (var item in items)
            {
                var itemNode = new YamlMappingNode();
                itemNode.Add("title", new YamlScalarNode(item.Title));
                if (item.Price.Type != ViewPointOfSaleViewModel.Item.ItemPrice.ItemPriceType.Topup)
                    itemNode.Add("price", new YamlScalarNode(item.Price.Value.ToStringInvariant()));
                if (!string.IsNullOrEmpty(item.Description))
                {
                    itemNode.Add("description", new YamlScalarNode(item.Description)
                    {
                        Style = ScalarStyle.DoubleQuoted
                    });
                }
                if (!string.IsNullOrEmpty(item.Image))
                {
                    itemNode.Add("image", new YamlScalarNode(item.Image));
                }
                itemNode.Add("price_type", new YamlScalarNode(item.Price.Type.ToStringLowerInvariant()));
                itemNode.Add("disabled", new YamlScalarNode(item.Disabled.ToStringLowerInvariant()));
                if (item.Inventory.HasValue)
                {
                    itemNode.Add("inventory", new YamlScalarNode(item.Inventory.ToString()));
                }

                if (!string.IsNullOrEmpty(item.BuyButtonText))
                {
                    itemNode.Add("buyButtonText", new YamlScalarNode(item.BuyButtonText));
                }

                if (item.PaymentMethods?.Any() is true)
                {
                    itemNode.Add("payment_methods", new YamlSequenceNode(item.PaymentMethods.Select(s => new YamlScalarNode(s))));
                }
                mappingNode.Add(item.Id, itemNode);
            }

            var serializer = new SerializerBuilder().Build();
            return serializer.Serialize(mappingNode);
        }
        public ViewPointOfSaleViewModel.Item[] Parse(string template, string currency)
        {
            if (string.IsNullOrWhiteSpace(template))
                return Array.Empty<ViewPointOfSaleViewModel.Item>();
            using var input = new StringReader(template);
            YamlStream stream = new YamlStream();
            stream.Load(input);
            var root = (YamlMappingNode)stream.Documents[0].RootNode;
            return root
                .Children
                .Select(kv => new PosHolder(_HtmlSanitizer) { Key = _HtmlSanitizer.Sanitize((kv.Key as YamlScalarNode)?.Value), Value = kv.Value as YamlMappingNode })
                .Where(kv => kv.Value != null)
                .Select(c =>
                {
                    ViewPointOfSaleViewModel.Item.ItemPrice price = new ViewPointOfSaleViewModel.Item.ItemPrice();
                    var pValue = c.GetDetail("price")?.FirstOrDefault();

                    switch (c.GetDetailString("custom") ?? c.GetDetailString("price_type")?.ToLowerInvariant())
                    {
                        case "topup":
                        case null when pValue is null:
                            price.Type = ViewPointOfSaleViewModel.Item.ItemPrice.ItemPriceType.Topup;
                            break;
                        case "true":
                        case "minimum":
                            price.Type = ViewPointOfSaleViewModel.Item.ItemPrice.ItemPriceType.Minimum;
                            if (pValue != null)
                            {
                                price.Value = decimal.Parse(pValue.Value.Value, CultureInfo.InvariantCulture);
                                price.Formatted = Currencies.FormatCurrency(pValue.Value.Value, currency);
                            }
                            break;
                        case "fixed":
                        case "false":
                        case null:
                            price.Type = ViewPointOfSaleViewModel.Item.ItemPrice.ItemPriceType.Fixed;
                            price.Value = decimal.Parse(pValue.Value.Value, CultureInfo.InvariantCulture);
                            price.Formatted = Currencies.FormatCurrency(pValue.Value.Value, currency);
                            break;
                    }

                    return new ViewPointOfSaleViewModel.Item()
                    {
                        Description = c.GetDetailString("description"),
                        Id = c.Key,
                        Image = c.GetDetailString("image"),
                        Title = c.GetDetailString("title") ?? c.Key,
                        Price = price,
                        BuyButtonText = c.GetDetailString("buyButtonText"),
                        Inventory =
                            string.IsNullOrEmpty(c.GetDetailString("inventory"))
                                ? (int?)null
                                : int.Parse(c.GetDetailString("inventory"), CultureInfo.InvariantCulture),
                        PaymentMethods = c.GetDetailStringList("payment_methods"),
                        Disabled = c.GetDetailString("disabled") == "true"
                    };
                })
                .ToArray();
        }

        public ViewPointOfSaleViewModel.Item[] GetPOSItems(string template, string currency)
        {
            return Parse(template, currency).Where(c => !c.Disabled).ToArray();
        }

        public Contributions GetContributionsByPaymentMethodId(string currency, InvoiceEntity[] invoices, bool softcap)
        {
            var contributions = invoices
                .Where(p => p.Currency.Equals(currency, StringComparison.OrdinalIgnoreCase))
                .SelectMany(p =>
                {
                    var contribution = new Contribution();
                    contribution.PaymentMethodId = new PaymentMethodId(p.Currency, PaymentTypes.BTCLike);
                    contribution.CurrencyValue = p.Price;
                    contribution.Value = contribution.CurrencyValue;

                    // For hardcap, we count newly created invoices as part of the contributions
                    if (!softcap && p.Status == InvoiceStatusLegacy.New)
                        return new[] { contribution };

                    // If the user get a donation via other mean, he can register an invoice manually for such amount
                    // then mark the invoice as complete
                    var payments = p.GetPayments(true);
                    if (payments.Count == 0 &&
                        p.ExceptionStatus == InvoiceExceptionStatus.Marked &&
                        p.Status == InvoiceStatusLegacy.Complete)
                        return new[] { contribution };

                    contribution.CurrencyValue = 0m;
                    contribution.Value = 0m;

                    // If an invoice has been marked invalid, remove the contribution
                    if (p.ExceptionStatus == InvoiceExceptionStatus.Marked &&
                        p.Status == InvoiceStatusLegacy.Invalid)
                        return new[] { contribution };


                    // Else, we just sum the payments
                    return payments
                             .Select(pay =>
                             {
                                 var paymentMethodContribution = new Contribution();
                                 paymentMethodContribution.PaymentMethodId = pay.GetPaymentMethodId();
                                 paymentMethodContribution.Value = pay.GetCryptoPaymentData().GetValue() - pay.NetworkFee;
                                 var rate = p.GetPaymentMethod(paymentMethodContribution.PaymentMethodId).Rate;
                                 paymentMethodContribution.CurrencyValue = rate * paymentMethodContribution.Value;
                                 return paymentMethodContribution;
                             })
                             .ToArray();
                })
                .GroupBy(p => p.PaymentMethodId)
                .ToDictionary(p => p.Key, p => new Contribution()
                {
                    PaymentMethodId = p.Key,
                    Value = p.Select(v => v.Value).Sum(),
                    CurrencyValue = p.Select(v => v.CurrencyValue).Sum()
                });
            return new Contributions(contributions);
        }

        private class PosHolder
        {
            private readonly HtmlSanitizer _htmlSanitizer;

            public PosHolder(HtmlSanitizer htmlSanitizer)
            {
                _htmlSanitizer = htmlSanitizer;
            }

            public string Key { get; set; }
            public YamlMappingNode Value { get; set; }

            public IEnumerable<PosScalar> GetDetail(string field)
            {
                var res = Value.Children
                                 .Where(kv => kv.Value != null)
                                 .Select(kv => new PosScalar { Key = (kv.Key as YamlScalarNode)?.Value, Value = kv.Value as YamlScalarNode })
                                 .Where(cc => cc.Key == field);
                return res;
            }

            public string GetDetailString(string field)
            {
                var raw = GetDetail(field).FirstOrDefault()?.Value?.Value;
                return raw is null ? null : _htmlSanitizer.Sanitize(raw);
            }
            public string[] GetDetailStringList(string field)
            {
                if (!Value.Children.ContainsKey(field) || !(Value.Children[field] is YamlSequenceNode sequenceNode))
                {
                    return null;
                }
                return sequenceNode.Children.Select(node => (node as YamlScalarNode)?.Value).Where(s => s != null).Select(s => _htmlSanitizer.Sanitize(s)).ToArray();
            }
        }
        private class PosScalar
        {
            public string Key { get; set; }
            public YamlScalarNode Value { get; set; }
        }

        public async Task<AppData> GetAppDataIfOwner(string userId, string appId, AppType? type = null)
        {
            if (userId == null || appId == null)
                return null;
            using var ctx = _ContextFactory.CreateContext();
            var app = await ctx.UserStore
                            .Where(us => us.ApplicationUserId == userId && us.Role == StoreRoles.Owner)
                            .SelectMany(us => us.StoreData.Apps.Where(a => a.Id == appId))
               .FirstOrDefaultAsync();
            if (app == null)
                return null;
            if (type != null && type.Value.ToString() != app.AppType)
                return null;
            return app;
        }

        public async Task UpdateOrCreateApp(AppData app)
        {
            using var ctx = _ContextFactory.CreateContext();
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

        private static bool TryParseJson(string json, out JObject result)
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

        public static bool TryParsePosCartItems(string posData, out Dictionary<string, int> cartItems)
        {
            cartItems = null;
            if (!TryParseJson(posData, out var posDataObj) ||
                !posDataObj.TryGetValue("cart", out var cartObject))
                return false;
            cartItems = cartObject.Select(token => (JObject)token)
                .ToDictionary(o => o.GetValue("id", StringComparison.InvariantCulture)?.ToString(),
                    o => int.Parse(o.GetValue("count", StringComparison.InvariantCulture)?.ToString() ?? string.Empty, CultureInfo.InvariantCulture));
            return true;
        }
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
