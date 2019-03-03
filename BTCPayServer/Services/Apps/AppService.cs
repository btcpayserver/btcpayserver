﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Models;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Rating;
using BTCPayServer.Security;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using Ganss.XSS;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NBitpayClient;
using YamlDotNet.RepresentationModel;
using static BTCPayServer.Controllers.AppsController;

namespace BTCPayServer.Services.Apps
{
    public class AppService
    {
        ApplicationDbContextFactory _ContextFactory;
        private readonly InvoiceRepository _InvoiceRepository;
        CurrencyNameTable _Currencies;
        private readonly RateFetcher _RateFetcher;
        private readonly HtmlSanitizer _HtmlSanitizer;
        private readonly BTCPayNetworkProvider _Networks;
        public CurrencyNameTable Currencies => _Currencies;
        public AppService(ApplicationDbContextFactory contextFactory,
                          InvoiceRepository invoiceRepository,
                          BTCPayNetworkProvider networks,
                          CurrencyNameTable currencies,
                          RateFetcher rateFetcher,
                          HtmlSanitizer htmlSanitizer)
        {
            _ContextFactory = contextFactory;
            _InvoiceRepository = invoiceRepository;
            _Currencies = currencies;
            _RateFetcher = rateFetcher;
            _HtmlSanitizer = htmlSanitizer;
            _Networks = networks;
        }

        public async Task<object> GetAppInfo(string appId)
        {
            var app = await GetApp(appId, AppType.Crowdfund, true);
            return await GetInfo(app);
        }
        private async Task<ViewCrowdfundViewModel> GetInfo(AppData appData, string statusMessage = null)
        {
            var settings = appData.GetSettings<CrowdfundSettings>();
            var resetEvery = settings.StartDate.HasValue ? settings.ResetEvery : CrowdfundResetEvery.Never;
            DateTime? lastResetDate = null;
            DateTime? nextResetDate = null;
            if (resetEvery != CrowdfundResetEvery.Never)
            {
                lastResetDate = settings.StartDate.Value;

                nextResetDate = lastResetDate.Value;
                while (DateTime.Now >= nextResetDate)
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
            var completeInvoices = invoices.Where(entity => entity.Status == InvoiceStatus.Complete || entity.Status == InvoiceStatus.Confirmed).ToArray();
            var pendingInvoices = invoices.Where(entity => !(entity.Status == InvoiceStatus.Complete || entity.Status == InvoiceStatus.Confirmed)).ToArray();

            var rateRules = appData.StoreData.GetStoreBlob().GetRateRules(_Networks);

            var pendingPaymentStats = GetCurrentContributionAmountStats(pendingInvoices, !settings.EnforceTargetAmount);
            var paymentStats = GetCurrentContributionAmountStats(completeInvoices, !settings.EnforceTargetAmount);

            var currentAmount = await GetCurrentContributionAmount(
                paymentStats,
                settings.TargetCurrency, rateRules);
            var currentPendingAmount = await GetCurrentContributionAmount(
                pendingPaymentStats,
                settings.TargetCurrency, rateRules);

            var perkCount = invoices
                .Where(entity => !string.IsNullOrEmpty(entity.ProductInformation.ItemCode))
                .GroupBy(entity => entity.ProductInformation.ItemCode)
                .ToDictionary(entities => entities.Key, entities => entities.Count());

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
            return new ViewCrowdfundViewModel()
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
                StatusMessage = statusMessage,
                Perks = perks,
                Enabled = settings.Enabled,
                DisqusEnabled = settings.DisqusEnabled,
                SoundsEnabled = settings.SoundsEnabled,
                DisqusShortname = settings.DisqusShortname,
                AnimationsEnabled = settings.AnimationsEnabled,
                ResetEveryAmount = settings.ResetEveryAmount,
                DisplayPerksRanking = settings.DisplayPerksRanking,
                PerkCount = perkCount,
                NeverReset = settings.ResetEvery == CrowdfundResetEvery.Never,
                CurrencyData = _Currencies.GetCurrencyData(settings.TargetCurrency, true),
                Info = new ViewCrowdfundViewModel.CrowdfundInfo()
                {
                    TotalContributors = invoices.Length,
                    CurrentPendingAmount = currentPendingAmount,
                    CurrentAmount = currentAmount,
                    ProgressPercentage = (currentAmount / settings.TargetAmount) * 100,
                    PendingProgressPercentage = (currentPendingAmount / settings.TargetAmount) * 100,
                    LastUpdated = DateTime.Now,
                    PaymentStats = paymentStats,
                    PendingPaymentStats = pendingPaymentStats,
                    LastResetDate = lastResetDate,
                    NextResetDate = nextResetDate
                }
            };
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
                Status = new string[]{
                    InvoiceState.ToString(InvoiceStatus.New),
                    InvoiceState.ToString(InvoiceStatus.Paid),
                    InvoiceState.ToString(InvoiceStatus.Confirmed),
                    InvoiceState.ToString(InvoiceStatus.Complete)},
                StartDate = startDate
            });

            // Old invoices may have invoices which were not tagged
            invoices = invoices.Where(inv => inv.Version < InvoiceEntity.InternalTagSupport_Version ||
                                             inv.InternalTags.Contains(GetAppInternalTag(appData.Id))).ToArray();
            return invoices;
        }

        public async Task<StoreData[]> GetOwnedStores(string userId)
        {
            using (var ctx = _ContextFactory.CreateContext())
            {
                return await ctx.UserStore
                    .Where(us => us.ApplicationUserId == userId && us.Role == StoreRoles.Owner)
                    .Select(u => u.StoreData)
                    .ToArrayAsync();
            }
        }

        public async Task<bool> DeleteApp(AppData appData)
        {
            using (var ctx = _ContextFactory.CreateContext())
            {
                ctx.Apps.Add(appData);
                ctx.Entry<AppData>(appData).State = EntityState.Deleted;
                return await ctx.SaveChangesAsync() == 1;
            }
        }

        public async Task<ListAppsViewModel.ListAppViewModel[]> GetAllApps(string userId, bool allowNoUser = false)
        {
            using (var ctx = _ContextFactory.CreateContext())
            {
                return await ctx.UserStore
                    .Where(us => (allowNoUser && string.IsNullOrEmpty(userId)) || us.ApplicationUserId == userId)
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
            }
        }


        public async Task<AppData> GetApp(string appId, AppType appType, bool includeStore = false)
        {
            using (var ctx = _ContextFactory.CreateContext())
            {
                var query = ctx.Apps
                    .Where(us => us.Id == appId &&
                                 us.AppType == appType.ToString());

                if (includeStore)
                {
                    query = query.Include(data => data.StoreData);
                }
                return await query.FirstOrDefaultAsync();
            }
        }

        public async Task<StoreData> GetStore(AppData app)
        {
            using (var ctx = _ContextFactory.CreateContext())
            {
                return await ctx.Stores.FirstOrDefaultAsync(s => s.Id == app.StoreDataId);
            }
        }


        public ViewPointOfSaleViewModel.Item[] Parse(string template, string currency)
        {
            if (string.IsNullOrWhiteSpace(template))
                return Array.Empty<ViewPointOfSaleViewModel.Item>();
            var input = new StringReader(template);
            YamlStream stream = new YamlStream();
            stream.Load(input);
            var root = (YamlMappingNode)stream.Documents[0].RootNode;
            return root
                .Children
                .Select(kv => new PosHolder { Key = (kv.Key as YamlScalarNode)?.Value, Value = kv.Value as YamlMappingNode })
                .Where(kv => kv.Value != null)
                .Select(c => new ViewPointOfSaleViewModel.Item()
                {
                    Description = _HtmlSanitizer.Sanitize(c.GetDetailString("description")),
                    Id = c.Key,
                    Image = _HtmlSanitizer.Sanitize(c.GetDetailString("image")),
                    Title = _HtmlSanitizer.Sanitize(c.GetDetailString("title") ?? c.Key),
                    Price = c.GetDetail("price")
                             .Select(cc => new ViewPointOfSaleViewModel.Item.ItemPrice()
                             {
                                 Value = decimal.Parse(cc.Value.Value, CultureInfo.InvariantCulture),
                                 Formatted = Currencies.FormatCurrency(cc.Value.Value, currency)
                             }).Single(),
                    Custom = c.GetDetailString("custom") == "true"
                })
                .ToArray();
        }

        public async Task<decimal> GetCurrentContributionAmount(Dictionary<string, decimal> stats, string primaryCurrency, RateRules rateRules)
        {
            var result = new List<decimal>();

            var ratesTask = _RateFetcher.FetchRates(
                stats.Keys
                    .Select((x) => new CurrencyPair(primaryCurrency, PaymentMethodId.Parse(x).CryptoCode))
                    .Distinct()
                    .ToHashSet(),
                rateRules).Select(async rateTask =>
                {
                    var (key, value) = rateTask;
                    var tResult = await value;
                    var rate = tResult.BidAsk?.Bid;
                    if (rate == null)
                        return;

                    foreach (var stat in stats)
                    {
                        if (string.Equals(PaymentMethodId.Parse(stat.Key).CryptoCode, key.Right,
                            StringComparison.InvariantCultureIgnoreCase))
                        {
                            result.Add((1m / rate.Value) * stat.Value);
                        }
                    }
                });

            await Task.WhenAll(ratesTask);

            return result.Sum();
        }

        public Dictionary<string, decimal> GetCurrentContributionAmountStats(InvoiceEntity[] invoices, bool softcap)
        {
            return invoices
                .SelectMany(p =>
                {
                    // For hardcap, we count newly created invoices as part of the contributions
                    if (!softcap && p.Status == InvoiceStatus.New)
                        return new[] { (Key: p.ProductInformation.Currency, Value: p.ProductInformation.Price) };

                    // If the user get a donation via other mean, he can register an invoice manually for such amount
                    // then mark the invoice as complete
                    var payments = p.GetPayments();
                    if (payments.Count == 0 &&
                        p.ExceptionStatus == InvoiceExceptionStatus.Marked &&
                        p.Status == InvoiceStatus.Complete)
                        return new[] { (Key: p.ProductInformation.Currency, Value: p.ProductInformation.Price) };

                    // If an invoice has been marked invalid, remove the contribution
                    if (p.ExceptionStatus == InvoiceExceptionStatus.Marked &&
                        p.Status == InvoiceStatus.Invalid)
                        return new[] { (Key: p.ProductInformation.Currency, Value: 0m) };

                    // Else, we just sum the payments
                    return payments
                             .Select(pay => (Key: pay.GetPaymentMethodId().ToString(), Value: pay.GetCryptoPaymentData().GetValue() - pay.NetworkFee))
                             .ToArray();
                })
                .GroupBy(p => p.Key)
                .ToDictionary(p => p.Key, p => p.Select(v => v.Value).Sum());
        }

        private class PosHolder
        {
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

                return GetDetail(field).FirstOrDefault()?.Value?.Value;
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
            using (var ctx = _ContextFactory.CreateContext())
            {
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
        }
    }
}
