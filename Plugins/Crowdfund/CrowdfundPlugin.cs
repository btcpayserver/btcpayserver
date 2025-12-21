#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Abstractions.Services;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Plugins.Crowdfund.Controllers;
using BTCPayServer.Plugins.Crowdfund.Models;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using Ganss.Xss;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using static BTCPayServer.Plugins.Crowdfund.Models.ViewCrowdfundViewModel.CrowdfundInfo;

namespace BTCPayServer.Plugins.Crowdfund
{
    public class CrowdfundPlugin : BaseBTCPayServerPlugin
    {
        public override string Identifier => "BTCPayServer.Plugins.Crowdfund";
        public override string Name => "Crowdfund";
        public override string Description => "Create a self-hosted funding campaign, similar to Kickstarter or Indiegogo. Funds go directly to the creator’s wallet without any fees.";

        public override void Execute(IServiceCollection services)
        {
            services.AddUIExtension("header-nav", "Crowdfund/NavExtension");
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
        private readonly UriResolver _uriResolver;
        private readonly InvoiceRepository _invoiceRepository;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly PrettyNameProvider _prettyNameProvider;
        public const string AppType = "Crowdfund";

        public CrowdfundAppType(
            LinkGenerator linkGenerator,
            IOptions<BTCPayServerOptions> options,
            UriResolver uriResolver,
            InvoiceRepository invoiceRepository,
            PrettyNameProvider prettyNameProvider,
            DisplayFormatter displayFormatter,
            IHttpContextAccessor httpContextAccessor,
            CurrencyNameTable currencyNameTable)
        {
            Description = Type = AppType;
            _linkGenerator = linkGenerator;
            _options = options;
            _uriResolver = uriResolver;
            _displayFormatter = displayFormatter;
            _httpContextAccessor = httpContextAccessor;
            _currencyNameTable = currencyNameTable;
            _invoiceRepository = invoiceRepository;
            _prettyNameProvider = prettyNameProvider;
        }

        public override Task<string> ConfigureLink(AppData app)
        {
            return Task.FromResult(_linkGenerator.GetPathByAction(nameof(UICrowdfundController.UpdateCrowdfund),
                "UICrowdfund", new { appId = app.Id }, _options.Value.RootPath)!);
        }

        public Task<AppSalesStats> GetSalesStats(AppData app, InvoiceEntity[] paidInvoices, int numberOfDays)
        {
            var cfS = app.GetSettings<CrowdfundSettings>();
            var items = AppService.Parse(cfS.PerksTemplate);
            return AppService.GetSalesStatswithPOSItems(items, paidInvoices, numberOfDays);
        }

        public Task<IEnumerable<AppItemStats>> GetItemStats(AppData appData, InvoiceEntity[] paidInvoices)
        {
            var settings = appData.GetSettings<CrowdfundSettings>();
            var perks = AppService.Parse(settings.PerksTemplate);
            var perkCount = paidInvoices
                .Where(entity => entity.Currency.Equals(settings.TargetCurrency, StringComparison.OrdinalIgnoreCase) &&
                                 // we need the item code to know which perk it is and group by that
                                 !string.IsNullOrEmpty(entity.Metadata.ItemCode))
                .GroupBy(entity => entity.Metadata.ItemCode)
                .Select(entities =>
                {
                    var total = entities.Sum(entity => entity.PaidAmount.Net);
                    var itemCode = entities.Key;
                    var perk = perks.FirstOrDefault(p => p.Id == itemCode);
                    return new AppItemStats
                    {
                        ItemCode = itemCode,
                        Title = perk?.Title ?? itemCode,
                        SalesCount = entities.Count(),
                        Total = total,
                        TotalFormatted = _displayFormatter.Currency(total, settings.TargetCurrency)
                    };
                })
                .OrderByDescending(stats => stats.SalesCount);

            return Task.FromResult<IEnumerable<AppItemStats>>(perkCount);
        }

        public override async Task<object?> GetInfo(AppData appData)
        {
            var settings = appData.GetSettings<CrowdfundSettings>();
            var resetEvery = settings.StartDate.HasValue ? settings.ResetEvery : Services.Apps.CrowdfundResetEvery.Never;
            DateTime? lastResetDate = null;
            DateTime? nextResetDate = null;
            if (resetEvery != Services.Apps.CrowdfundResetEvery.Never && settings.StartDate is not null)
            {
                lastResetDate = settings.StartDate.Value;

                nextResetDate = lastResetDate.Value;
                while (DateTime.UtcNow >= nextResetDate)
                {
                    lastResetDate = nextResetDate;
                    switch (resetEvery)
                    {
                        case Services.Apps.CrowdfundResetEvery.Hour:
                            nextResetDate = lastResetDate.Value.AddHours(settings.ResetEveryAmount);
                            break;
                        case Services.Apps.CrowdfundResetEvery.Day:
                            nextResetDate = lastResetDate.Value.AddDays(settings.ResetEveryAmount);
                            break;
                        case Services.Apps.CrowdfundResetEvery.Month:
                            nextResetDate = lastResetDate.Value.AddMonths(settings.ResetEveryAmount);
                            break;
                        case Services.Apps.CrowdfundResetEvery.Year:
                            nextResetDate = lastResetDate.Value.AddYears(settings.ResetEveryAmount);
                            break;
                    }
                }
            }

            var invoices = await AppService.GetInvoicesForApp(_invoiceRepository, appData, lastResetDate);

            var currentPayments = _invoiceRepository.GetContributionsByPaymentMethodId(settings.TargetCurrency, invoices, !settings.EnforceTargetAmount);

            var paidInvoices = invoices.Where(e => e.Status is InvoiceStatus.Settled or InvoiceStatus.Processing).ToArray();
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
                        entities.Sum(entity => entity.PaidAmount.Net));
            }

            var perks = AppService.Parse(settings.PerksTemplate, false);
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
            var formUrl = settings.FormId != null
                ? _linkGenerator.GetPathByAction(nameof(UICrowdfundController.CrowdfundForm), "UICrowdfund",
                    new { appId = appData.Id }, _options.Value.RootPath)
                : null;
            var vm =  new ViewCrowdfundViewModel
            {
                Title = settings.Title,
                Tagline = settings.Tagline,
                HtmlLang = settings.HtmlLang,
                HtmlMetaTags= settings.HtmlMetaTags,
                Description = settings.Description,
                StoreName = store.StoreName,
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
                ResetEvery = Enum.GetName(typeof(Services.Apps.CrowdfundResetEvery), settings.ResetEvery),
                DisplayPerksRanking = settings.DisplayPerksRanking,
                PerkCount = perkCount,
                PerkValue = perkValue,
                NeverReset = settings.ResetEvery == Services.Apps.CrowdfundResetEvery.Never,
                FormUrl = formUrl,
                Sounds = settings.Sounds,
                AnimationColors = settings.AnimationColors,
                CurrencyData = _currencyNameTable.GetCurrencyData(settings.TargetCurrency, true),
                Info = new ViewCrowdfundViewModel.CrowdfundInfo
                {
                    TotalContributors = paidInvoices.Length,
                    ProgressPercentage = (currentPayments.TotalSettled / settings.TargetAmount) * 100,
                    PendingProgressPercentage = (currentPayments.TotalProcessing / settings.TargetAmount) * 100,
                    LastUpdated = DateTime.UtcNow,
                    PaymentStats = GetPaymentStats(currentPayments),
                    LastResetDate = lastResetDate,
                    NextResetDate = nextResetDate,
                    CurrentPendingAmount = currentPayments.TotalProcessing,
                    CurrentAmount = currentPayments.TotalSettled
                }
            };
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext != null && settings.MainImageUrl != null)
            {
                vm.MainImageUrl = await _uriResolver.Resolve(httpContext.Request.GetAbsoluteRootUri(), settings.MainImageUrl);
            }
            return vm;
        }

        private Dictionary<string, PaymentStat> GetPaymentStats(InvoiceStatistics stats)
        => stats.Total is 0.0m ? new Dictionary<string, PaymentStat>() : stats.ToDictionary(kv => kv.Key, kv =>
                {
                    var pmi = PaymentMethodId.Parse(kv.Key);
                    return new PaymentStat()
                    {
                        Label = _prettyNameProvider.PrettyName(pmi),
                        Percent = (kv.Value.CurrencyValue / stats.Total) * 100.0m,
                        // Note that the LNURL will have the same LN
                        IsLightning = pmi == PaymentTypes.LN.GetPaymentMethodId(kv.Value.Currency)
                    };
                });

        public override Task SetDefaultSettings(AppData appData, string defaultCurrency)
        {
            var emptyCrowdfund = new CrowdfundSettings { Title = appData.Name, TargetCurrency = defaultCurrency };
            appData.SetSettings(emptyCrowdfund);
            return Task.CompletedTask;
        }

        public override Task<string> ViewLink(AppData app)
        {
            return Task.FromResult(_linkGenerator.GetPathByAction(nameof(UICrowdfundController.ViewCrowdfund),
                "UICrowdfund", new { appId = app.Id }, _options.Value.RootPath)!);
        }
    }
}
