using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Rating;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using NBitcoin;
using YamlDotNet.Core;

namespace BTCPayServer.Hubs
{
    public class 
        CrowdfundHubStreamer
    {
        public const string CrowdfundInvoiceOrderIdPrefix = "crowdfund-app_";
        private readonly EventAggregator _EventAggregator;
        private readonly IHubContext<CrowdfundHub> _HubContext;
        private readonly IMemoryCache _MemoryCache;
        private readonly AppsHelper _AppsHelper;
        private readonly RateFetcher _RateFetcher;
        private readonly BTCPayNetworkProvider _BtcPayNetworkProvider;
        private readonly InvoiceRepository _InvoiceRepository;
        private readonly ConcurrentDictionary<string,(string appId, bool useAllStoreInvoices,bool useInvoiceAmount)> _QuickAppInvoiceLookup = 
            new ConcurrentDictionary<string, (string appId, bool useAllStoreInvoices, bool useInvoiceAmount)>();
        
        public CrowdfundHubStreamer(EventAggregator eventAggregator, 
            IHubContext<CrowdfundHub> hubContext, 
            IMemoryCache memoryCache,
            AppsHelper appsHelper, 
            RateFetcher rateFetcher,
            BTCPayNetworkProvider btcPayNetworkProvider,
            InvoiceRepository invoiceRepository)
        {
            _EventAggregator = eventAggregator;
            _HubContext = hubContext;
            _MemoryCache = memoryCache;
            _AppsHelper = appsHelper;
            _RateFetcher = rateFetcher;
            _BtcPayNetworkProvider = btcPayNetworkProvider;
            _InvoiceRepository = invoiceRepository;
#pragma warning disable 4014
            InitLookup();
#pragma warning restore 4014
            SubscribeToEvents();
        }

        private async Task InitLookup()
        {
          var apps =  await _AppsHelper.GetAllApps(null, true);
          apps = apps.Where(model => Enum.Parse<AppType>(model.AppType) == AppType.Crowdfund).ToArray();
          var tasks = new List<Task>();
          tasks.AddRange(apps.Select(app => Task.Run(async () =>
          {
              var fullApp = await _AppsHelper.GetApp(app.Id, AppType.Crowdfund, false);
              var settings = fullApp.GetSettings<AppsController.CrowdfundSettings>();
              UpdateLookup(app.Id, app.StoreId, settings);
          })));
          await Task.WhenAll(tasks);
        }

        private void UpdateLookup(string appId, string storeId, AppsController.CrowdfundSettings settings)
        {
            _QuickAppInvoiceLookup.AddOrReplace(storeId,
                (
                    appId: appId,
                    useAllStoreInvoices: settings?.UseAllStoreInvoices ?? false,
                    useInvoiceAmount: settings?.UseInvoiceAmount ?? false
                ));
        }

        public Task<ViewCrowdfundViewModel> GetCrowdfundInfo(string appId)
        {
            return _MemoryCache.GetOrCreateAsync(GetCacheKey(appId), async entry =>
            {           
                var app = await _AppsHelper.GetApp(appId, AppType.Crowdfund, true);
                var result = await GetInfo(app);
                entry.SetValue(result);
                
                TimeSpan? expire = null;

                if (result.StartDate.HasValue && result.StartDate < DateTime.Now)
                {
                    expire = result.StartDate.Value.Subtract(DateTime.Now);
                }
                else if (result.EndDate.HasValue && result.EndDate > DateTime.Now)
                {
                    expire = result.EndDate.Value.Subtract(DateTime.Now);
                }
                if(!expire.HasValue || expire?.TotalMinutes > 5 ||  expire?.TotalMilliseconds <= 0)
                {
                    expire = TimeSpan.FromMinutes(5);
                }

                entry.AbsoluteExpirationRelativeToNow = expire;
                return result;
            });
        }

        private void SubscribeToEvents()
        {
            
            _EventAggregator.Subscribe<InvoiceEvent>(OnInvoiceEvent);
            _EventAggregator.Subscribe<AppsController.CrowdfundAppUpdated>(updated =>
            {
                UpdateLookup(updated.AppId, updated.StoreId, updated.Settings);
                InvalidateCacheForApp(updated.AppId);
            });
        }

        private string GetCacheKey(string appId)
        {
            return $"{CrowdfundInvoiceOrderIdPrefix}:{appId}";
        }

        private void OnInvoiceEvent(InvoiceEvent invoiceEvent)
        {
            if (!_QuickAppInvoiceLookup.TryGetValue(invoiceEvent.Invoice.StoreId, out var quickLookup) ||
                (!quickLookup.useAllStoreInvoices && 
                 !string.IsNullOrEmpty(invoiceEvent.Invoice.OrderId) &&
                !invoiceEvent.Invoice.OrderId.Equals($"{CrowdfundInvoiceOrderIdPrefix}{quickLookup.appId}", StringComparison.InvariantCulture)
                ))
            {
                return;
            }
            
            switch (invoiceEvent.Name)
            {
                case InvoiceEvent.ReceivedPayment:
                    var data = invoiceEvent.Payment.GetCryptoPaymentData();
                    _HubContext.Clients.Group(quickLookup.appId).SendCoreAsync(CrowdfundHub.PaymentReceived, new object[]
                    {
                        data.GetValue(), 
                        invoiceEvent.Payment.GetCryptoCode(), 
                        Enum.GetName(typeof(PaymentTypes), 
                            invoiceEvent.Payment.GetPaymentMethodId().PaymentType)
                    } );
                    
                    InvalidateCacheForApp(quickLookup.appId);
                    break;
                case InvoiceEvent.Created:
                case InvoiceEvent.MarkedInvalid:
                case InvoiceEvent.MarkedCompleted:
                    if (quickLookup.useInvoiceAmount)
                    {
                        InvalidateCacheForApp(quickLookup.appId);
                    }
                    break;
                case InvoiceEvent.Completed:
                    InvalidateCacheForApp(quickLookup.appId);
                    break;
            }
        }

        private void InvalidateCacheForApp(string appId)
        {
            _MemoryCache.Remove(GetCacheKey(appId));

            GetCrowdfundInfo(appId).ContinueWith(task =>
            {
                _HubContext.Clients.Group(appId).SendCoreAsync(CrowdfundHub.InfoUpdated, new object[]{ task.Result} );
            }, TaskScheduler.Current);
            
        }
        
        private static async Task<decimal> GetCurrentContributionAmount(Dictionary<string, decimal> stats, string primaryCurrency,
            RateFetcher rateFetcher, RateRules rateRules)
        {
            decimal result = 0;

            var ratesTask = rateFetcher.FetchRates(
                stats.Keys
                    .Select((x) => new CurrencyPair( primaryCurrency, PaymentMethodId.Parse(x).CryptoCode))
                    .ToHashSet(), 
                rateRules);

            var finalTasks = new List<Task>();
            foreach (var rateTask in ratesTask)
            {
                finalTasks.Add(Task.Run(async () =>
                {
                    var tResult = await rateTask.Value;
                    var rate = tResult.BidAsk?.Bid;
                    if (rate == null) return;
                    var currencyGroup = stats[rateTask.Key.Right];
                    result += (1m / rate.Value) * currencyGroup;
                }));
            }

            await Task.WhenAll(finalTasks);

            return result;
        }
        
        private static Dictionary<string, decimal> GetCurrentContributionAmountStats(InvoiceEntity[] invoices, bool usePaymentData = true)
        {
            if(usePaymentData){
            var payments = invoices.SelectMany(entity => entity.GetPayments());

            var groupedByMethod = payments.GroupBy(entity => entity.GetPaymentMethodId());

            return groupedByMethod.ToDictionary(entities => entities.Key.ToString(),
                entities => entities.Sum(entity => entity.GetCryptoPaymentData().GetValue()));
            }
            else
            {
                return invoices
                    .GroupBy(entity => entity.ProductInformation.Currency)
                    .ToDictionary(
                        entities => entities.Key,
                        entities => entities.Sum(entity => entity.ProductInformation.Price));
            }
        }
        private async Task<ViewCrowdfundViewModel> GetInfo(AppData appData, string statusMessage= null)
        {
            var settings = appData.GetSettings<AppsController.CrowdfundSettings>();

            var resetEvery = settings.StartDate.HasValue? settings.ResetEvery : CrowdfundResetEvery.Never;
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
            
            var invoices = await GetInvoicesForApp(settings.UseAllStoreInvoices? null : appData.Id, lastResetDate);
            var completeInvoices = invoices.Where(entity => entity.Status == InvoiceStatus.Complete).ToArray();
            var pendingInvoices = invoices.Where(entity => entity.Status != InvoiceStatus.Complete).ToArray();
            
            var rateRules = appData.StoreData.GetStoreBlob().GetRateRules(_BtcPayNetworkProvider);
            
            var pendingPaymentStats = GetCurrentContributionAmountStats(pendingInvoices, !settings.UseInvoiceAmount);
            var paymentStats = GetCurrentContributionAmountStats(completeInvoices, !settings.UseInvoiceAmount); 
            
            var currentAmount = await GetCurrentContributionAmount(
                paymentStats,
                settings.TargetCurrency, _RateFetcher, rateRules); 
            var currentPendingAmount =  await GetCurrentContributionAmount(
                pendingPaymentStats,
                settings.TargetCurrency, _RateFetcher, rateRules);

            
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
                Perks = _AppsHelper.Parse(settings.PerksTemplate, settings.TargetCurrency),
                DisqusEnabled = settings.DisqusEnabled,
                SoundsEnabled = settings.SoundsEnabled,
                DisqusShortname = settings.DisqusShortname,
                AnimationsEnabled = settings.AnimationsEnabled,
                ResetEveryAmount = settings.ResetEveryAmount,
                ResetEvery = Enum.GetName(typeof(CrowdfundResetEvery),settings.ResetEvery),
                Info = new ViewCrowdfundViewModel.CrowdfundInfo()
                {
                    TotalContributors = invoices.Length,
                    CurrentPendingAmount = currentPendingAmount,
                    CurrentAmount = currentAmount,
                    ProgressPercentage =   (currentAmount/ settings.TargetAmount) * 100,
                    PendingProgressPercentage =  ( currentPendingAmount/ settings.TargetAmount) * 100,
                    LastUpdated = DateTime.Now,
                    PaymentStats = paymentStats,
                    PendingPaymentStats = pendingPaymentStats,
                    LastResetDate = lastResetDate,
                    NextResetDate = nextResetDate
                }
            };
        }

        private async Task<InvoiceEntity[]> GetInvoicesForApp(string appId, DateTime? startDate = null)
        {
            return await  _InvoiceRepository.GetInvoices(new InvoiceQuery()
            {
                OrderId = appId == null? null : new []{$"{CrowdfundInvoiceOrderIdPrefix}{appId}"},
                Status = new string[]{
                    InvoiceState.ToString(InvoiceStatus.New),
                    InvoiceState.ToString(InvoiceStatus.Paid), 
                    InvoiceState.ToString(InvoiceStatus.Confirmed), 
                    InvoiceState.ToString(InvoiceStatus.Complete)},
                StartDate = startDate
            });
        }
    }
}
