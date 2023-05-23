#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Abstractions.Services;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Plugins.Crowdfund.Controllers;
using BTCPayServer.Plugins.Crowdfund.Models;
using BTCPayServer.Plugins.PointOfSale;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using Ganss.XSS;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Plugins.Crowdfund
{
    public class CrowdfundPlugin : BaseBTCPayServerPlugin
    {
        public override string Identifier => "BTCPayServer.Plugins.Crowdfund";
        public override string Name => "Crowdfund";
        public override string Description => "Create a self-hosted funding campaign, similar to Kickstarter or Indiegogo. Funds go directly to the creator’s wallet without any fees.";

        public override void Execute(IServiceCollection services)
        {
            services.AddSingleton<IUIExtension>(new UIExtension("Crowdfund/NavExtension", "header-nav"));
            services.AddSingleton<CrowdfundAppType>();
            services.AddSingleton<AppBaseType, CrowdfundAppType>();

            base.Execute(services);
        }
    }

    public class CrowdfundAppType : AppBaseType, IHasSaleStatsAppType, IHasItemStatsAppType
    {
        private readonly LinkGenerator _linkGenerator;
        private readonly IOptions<BTCPayServerOptions> _options;
        private readonly DisplayFormatter _displayFormatter;
        private readonly CurrencyNameTable _currencyNameTable;
        private readonly HtmlSanitizer _htmlSanitizer;
        private readonly InvoiceRepository _invoiceRepository;
        public const string AppType = "Crowdfund";

        public CrowdfundAppType(
            LinkGenerator linkGenerator,
            IOptions<BTCPayServerOptions> options,
            InvoiceRepository invoiceRepository,
            DisplayFormatter displayFormatter,
            CurrencyNameTable currencyNameTable,
            HtmlSanitizer htmlSanitizer)
        {
            Description = Type = AppType;
            _linkGenerator = linkGenerator;
            _options = options;
            _displayFormatter = displayFormatter;
            _currencyNameTable = currencyNameTable;
            _htmlSanitizer = htmlSanitizer;
            _invoiceRepository = invoiceRepository;
        }

        public override Task<string> ConfigureLink(AppData app)
        {
            return Task.FromResult(_linkGenerator.GetPathByAction(nameof(UICrowdfundController.UpdateCrowdfund),
                "UICrowdfund", new { appId = app.Id }, _options.Value.RootPath)!);
        }

        public Task<SalesStats> GetSalesStats(AppData app, InvoiceEntity[] paidInvoices, int numberOfDays)
        {
            var cfS = app.GetSettings<CrowdfundSettings>();
            var items = AppService.Parse( cfS.PerksTemplate);
            return AppService.GetSalesStatswithPOSItems(items, paidInvoices, numberOfDays);
        }

        public Task<IEnumerable<ItemStats>> GetItemStats(AppData appData, InvoiceEntity[] paidInvoices)
        {
            var settings = appData.GetSettings<CrowdfundSettings>();
            var perks = AppService.Parse( settings.PerksTemplate);
            var perkCount = paidInvoices
                .Where(entity => entity.Currency.Equals(settings.TargetCurrency, StringComparison.OrdinalIgnoreCase) &&
                                 // we need the item code to know which perk it is and group by that
                                 !string.IsNullOrEmpty(entity.Metadata.ItemCode))
                .GroupBy(entity => entity.Metadata.ItemCode)
                .Select(entities =>
                {
                    var total = entities
                        .Sum(entity => entity.GetPayments(true)
                            .Sum(pay =>
                            {
                                var paymentMethodId = pay.GetPaymentMethodId();
                                var value = pay.GetCryptoPaymentData().GetValue() - pay.NetworkFee;
                                var rate = entity.GetPaymentMethod(paymentMethodId).Rate;
                                return rate * value;
                            }));
                    var itemCode = entities.Key;
                    var perk = perks.FirstOrDefault(p => p.Id == itemCode);
                    return new ItemStats
                    {
                        ItemCode = itemCode,
                        Title = perk?.Title ?? itemCode,
                        SalesCount = entities.Count(),
                        Total = total,
                        TotalFormatted = _displayFormatter.Currency(total, settings.TargetCurrency)
                    };
                })
                .OrderByDescending(stats => stats.SalesCount);

            return Task.FromResult<IEnumerable<ItemStats>>(perkCount);
        }

        public override async Task<object?> GetInfo(AppData appData)
        {
            var settings = appData.GetSettings<CrowdfundSettings>();
            var resetEvery = settings.StartDate.HasValue ? settings.ResetEvery : CrowdfundResetEvery.Never;
            DateTime? lastResetDate = null;
            DateTime? nextResetDate = null;
            if (resetEvery != CrowdfundResetEvery.Never && settings.StartDate is not null)
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

            var invoices = await AppService.GetInvoicesForApp(_invoiceRepository, appData, lastResetDate);
            var completeInvoices = invoices.Where(IsComplete).ToArray();
            var pendingInvoices = invoices.Where(IsPending).ToArray();
            var paidInvoices = invoices.Where(IsPaid).ToArray();

            var pendingPayments = _invoiceRepository.GetContributionsByPaymentMethodId(settings.TargetCurrency, pendingInvoices, !settings.EnforceTargetAmount);
            var currentPayments = _invoiceRepository.GetContributionsByPaymentMethodId(settings.TargetCurrency, completeInvoices, !settings.EnforceTargetAmount);

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

            var perks = AppService.Parse( settings.PerksTemplate, false);
            if (settings.SortPerksByPopularity)
            {
                var ordered = perkCount.OrderByDescending(pair => pair.Value);
                var newPerksOrder = ordered
                    .Select(keyValuePair => perks.SingleOrDefault(item => item.Id == keyValuePair.Key))
                    .Where(matchingPerk => matchingPerk != null)
                    .ToList();
                var remainingPerks = perks.Where(item => !newPerksOrder.Contains(item));
                newPerksOrder.AddRange(remainingPerks);
                perks = newPerksOrder.ToArray()!;
            }

            var store = appData.StoreData;
            var storeBlob = store.GetStoreBlob();

            return new ViewCrowdfundViewModel
            {
                Title = settings.Title,
                Tagline = settings.Tagline,
                Description = settings.Description,
                CustomCSSLink = settings.CustomCSSLink,
                MainImageUrl = settings.MainImageUrl,
                EmbeddedCSS = settings.EmbeddedCSS,
                StoreName = store.StoreName,
                CssFileId = storeBlob.CssFileId,
                LogoFileId = storeBlob.LogoFileId,
                BrandColor = storeBlob.BrandColor,
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
                CurrencyData = _currencyNameTable.GetCurrencyData(settings.TargetCurrency, true),
                CurrencyDataPayments = currentPayments.Select(pair => pair.Key)
                    .Concat(pendingPayments.Select(pair => pair.Key))
                    .Select(id => _currencyNameTable.GetCurrencyData(id.CryptoCode, true)).DistinctBy(data => data.Code)
                    .ToDictionary(data => data.Code, data => data),
                Info = new ViewCrowdfundViewModel.CrowdfundInfo
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

        public override Task SetDefaultSettings(AppData appData, string defaultCurrency)
        {
            var emptyCrowdfund = new CrowdfundSettings { TargetCurrency = defaultCurrency };
            appData.SetSettings(emptyCrowdfund);
            return Task.CompletedTask;
        }

        public override Task<string> ViewLink(AppData app)
        {
            return Task.FromResult(_linkGenerator.GetPathByAction(nameof(UICrowdfundController.ViewCrowdfund),
                "UICrowdfund", new { appId = app.Id }, _options.Value.RootPath)!);
        }

        private static bool IsPaid(InvoiceEntity entity)
        {
            return entity.Status == InvoiceStatusLegacy.Complete || entity.Status == InvoiceStatusLegacy.Confirmed || entity.Status == InvoiceStatusLegacy.Paid;
        }

        private static bool IsPending(InvoiceEntity entity)
        {
            return !(entity.Status == InvoiceStatusLegacy.Complete || entity.Status == InvoiceStatusLegacy.Confirmed);
        }

        private static bool IsComplete(InvoiceEntity entity)
        {
            return entity.Status == InvoiceStatusLegacy.Complete || entity.Status == InvoiceStatusLegacy.Confirmed;
        }
    }
}
