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

        private Dictionary<string, CancellationTokenSource> _CacheTokens = new Dictionary<string, CancellationTokenSource>();
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
            var key = GetCacheKey(appId);
            return _MemoryCache.GetOrCreateAsync(key, async entry =>
            {
                if (_CacheTokens.ContainsKey(key))
                {
                    _CacheTokens.Remove(key);
                }               
                var app = await _AppsHelper.GetApp(appId, AppType.Crowdfund, true);
                var result = await GetInfo(app);
                entry.SetValue(result);
                
                var token = new CancellationTokenSource();
                _CacheTokens.Add(key, token);
                entry.AddExpirationToken(new CancellationChangeToken(token.Token));
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
            
            _EventAggregator.Subscribe<InvoiceEvent>(Subscription);
            _EventAggregator.Subscribe<AppsController.CrowdfundAppUpdated>(updated =>
            {
                InvalidateCacheForApp(updated.AppId);
            });
        }

        private string GetCacheKey(string appId)
        {
            return $"{CrowdfundInvoiceOrderIdPrefix}:{appId}";
        }

        private void Subscription(InvoiceEvent invoiceEvent)
        {
            if (!invoiceEvent.Invoice.OrderId.StartsWith(CrowdfundInvoiceOrderIdPrefix, StringComparison.InvariantCultureIgnoreCase))
            {
                return;
            }
            var appId = invoiceEvent.Invoice.OrderId.Replace(CrowdfundInvoiceOrderIdPrefix, "", StringComparison.InvariantCultureIgnoreCase);
            switch (invoiceEvent.Name)
            {
                case InvoiceEvent.ReceivedPayment:
                    
                    _HubContext.Clients.Group(appId).SendCoreAsync(CrowdfundHub.PaymentReceived, new object[]
                    {
                        invoiceEvent.Payment.GetCryptoPaymentData().GetValue(), 
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
            if (_CacheTokens.ContainsKey(appId))
            {
                _CacheTokens[appId].Cancel();
            }

            GetCrowdfundInfo(appId).ContinueWith(task =>
            {
                _HubContext.Clients.Group(appId).SendCoreAsync(CrowdfundHub.InfoUpdated, new object[]{ task.Result} );
            }, TaskScheduler.Default);
            
        }
        
        private static async Task<decimal> GetCurrentContributionAmount(InvoiceEntity[] invoices, string primaryCurrency,
            RateFetcher rateFetcher, RateRules rateRules)
        {
            decimal result = 0;
            
            var groupingByCurrency = invoices.GroupBy(entity => entity.ProductInformation.Currency);

            var ratesTask = rateFetcher.FetchRates(
                groupingByCurrency
                    .Select((entities) => new CurrencyPair(entities.Key, primaryCurrency))
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
                    var currencyGroup = groupingByCurrency.Single(entities => entities.Key == rateTask.Key.Left);
                    result += currencyGroup.Sum(entity => entity.ProductInformation.Price / rate.Value);
                }));
            }

            await Task.WhenAll(finalTasks);

            return result;
        }
        
        private static Dictionary<string, decimal> GetCurrentContributionAmountStats(InvoiceEntity[] invoices)
        {
            var payments = invoices.SelectMany(entity => entity.GetPayments());

            var groupedByMethod = payments.GroupBy(entity => entity.GetPaymentMethodId());

            return groupedByMethod.ToDictionary(entities => entities.Key.ToString(),
                entities => entities.Sum(entity => entity.GetCryptoPaymentData().GetValue()));
        }
        private async Task<ViewCrowdfundViewModel> GetInfo(AppData appData, string statusMessage= null)
        {
            var settings = appData.GetSettings<AppsController.CrowdfundSettings>();
            var invoices = await GetInvoicesForApp(appData, _InvoiceRepository);

            var completeInvoices = invoices.Where(entity => entity.Status == InvoiceStatus.Complete).ToArray();
            var pendingInvoices = invoices.Where(entity => entity.Status != InvoiceStatus.Complete).ToArray();
            
            var rateRules = appData.StoreData.GetStoreBlob().GetRateRules(_BtcPayNetworkProvider);
            
            var currentAmount = await GetCurrentContributionAmount(
                completeInvoices,
                settings.TargetCurrency, _RateFetcher, rateRules);
            var currentPendingAmount =  await GetCurrentContributionAmount(
                pendingInvoices,
                settings.TargetCurrency, _RateFetcher, rateRules);

            var pendingPaymentStats = GetCurrentContributionAmountStats(pendingInvoices);
            var paymentStats = GetCurrentContributionAmountStats(completeInvoices); 
            
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
                StartDate = settings.StartDate, 
                EndDate = settings.EndDate,
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
