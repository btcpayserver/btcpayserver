using System;
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

namespace BTCPayServer.Hubs
{
    public class CrowdfundHubStreamer
    {
        public const string CrowdfundInvoiceOrderIdPrefix = "crowdfund-app:";
        private readonly EventAggregator _EventAggregator;
        private readonly IHubContext<CrowdfundHub> _HubContext;
        private readonly IMemoryCache _MemoryCache;
        private readonly AppsHelper _AppsHelper;
        private readonly RateFetcher _RateFetcher;
        private readonly BTCPayNetworkProvider _BtcPayNetworkProvider;
        private readonly InvoiceRepository _InvoiceRepository;
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
            SubscribeToEvents();
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
                InvalidateCacheForApp(updated.AppId);
            });
        }

        private string GetCacheKey(string appId)
        {
            return $"{CrowdfundInvoiceOrderIdPrefix}:{appId}";
        }

        private void OnInvoiceEvent(InvoiceEvent invoiceEvent)
        {
            if (!invoiceEvent.Invoice.OrderId.StartsWith(CrowdfundInvoiceOrderIdPrefix, StringComparison.InvariantCultureIgnoreCase))
            {
                return;
            }
            var appId = invoiceEvent.Invoice.OrderId.Replace(CrowdfundInvoiceOrderIdPrefix, "", StringComparison.InvariantCultureIgnoreCase);
            switch (invoiceEvent.Name)
            {
                case InvoiceEvent.ReceivedPayment:
                    var data = invoiceEvent.Payment.GetCryptoPaymentData();
                    _HubContext.Clients.Group(appId).SendCoreAsync(CrowdfundHub.PaymentReceived, new object[]
                    {
                        data.GetValue(), 
                        invoiceEvent.Payment.GetCryptoCode(), 
                        Enum.GetName(typeof(PaymentTypes), 
                            invoiceEvent.Payment.GetPaymentMethodId().PaymentType)
                    } );
                    
                    InvalidateCacheForApp(appId);
                    break;
                case InvoiceEvent.Completed:
                    InvalidateCacheForApp(appId);
                    break;
            }
        }

        private void InvalidateCacheForApp(string appId)
        {
            _MemoryCache.Remove(GetCacheKey(appId));

            GetCrowdfundInfo(appId).ContinueWith(task =>
            {
                _HubContext.Clients.Group(appId).SendCoreAsync(CrowdfundHub.InfoUpdated, new object[]{ task.Result} );
            }, TaskScheduler.Default);
            
        }
        
        private static async Task<decimal> GetCurrentContributionAmount(Dictionary<string, decimal> stats, string primaryCurrency,
            RateFetcher rateFetcher, RateRules rateRules)
        {
            decimal result = 0;

            var ratesTask = rateFetcher.FetchRates(
                stats.Keys
                    .Select((x) => new CurrencyPair(PaymentMethodId.Parse(x).CryptoCode, primaryCurrency))
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
                    var currencyGroup = stats[rateTask.Key.Left];
                    result += currencyGroup / rate.Value;
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
            var invoices = await GetInvoicesForApp(appData, _InvoiceRepository);

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
                Info = new ViewCrowdfundViewModel.CrowdfundInfo()
                {
                    TotalContributors = invoices.Length,
                    CurrentPendingAmount = currentPendingAmount,
                    CurrentAmount = currentAmount,
                    ProgressPercentage =   (currentAmount/ settings.TargetAmount) * 100,
                    PendingProgressPercentage =  ( currentPendingAmount/ settings.TargetAmount) * 100,
                    LastUpdated = DateTime.Now,
                    PaymentStats = paymentStats,
                    PendingPaymentStats = pendingPaymentStats
                }
            };
        }

        private static async Task<InvoiceEntity[]> GetInvoicesForApp(AppData appData, InvoiceRepository invoiceRepository)
        {
            return await  invoiceRepository.GetInvoices(new InvoiceQuery()
            {
                OrderId = $"{CrowdfundInvoiceOrderIdPrefix}{appData.Id}",
                Status = new string[]{
                    InvoiceState.ToString(InvoiceStatus.New),
                    InvoiceState.ToString(InvoiceStatus.Paid), 
                    InvoiceState.ToString(InvoiceStatus.Confirmed), 
                    InvoiceState.ToString(InvoiceStatus.Complete)}
            });
        }
        
        
    }
}
