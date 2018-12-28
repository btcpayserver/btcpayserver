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
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace BTCPayServer.Hubs
{
    public class CrowdfundHub: Hub
    {
        private readonly AppsPublicController _AppsPublicController;

        public CrowdfundHub(AppsPublicController appsPublicController)
        {
            _AppsPublicController = appsPublicController;
        }
        public async Task ListenToCrowdfundApp(string appId)
        {
            if (Context.Items.ContainsKey("app"))
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, Context.Items["app"].ToString());
                Context.Items.Remove("app");
            }
            Context.Items.Add("app", appId);
            await Groups.AddToGroupAsync(Context.ConnectionId, appId);
        }


        public async Task CreateInvoice(ContributeToCrowdfund model)
        {
               model.RedirectToCheckout = false;
               _AppsPublicController.ControllerContext.HttpContext = Context.GetHttpContext();
               var result = await _AppsPublicController.ContributeToCrowdfund(Context.Items["app"].ToString(), model);
               await Clients.Caller.SendCoreAsync("InvoiceCreated", new[] {(result as OkObjectResult)?.Value.ToString()});
        }

    }

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

                var token = new CancellationTokenSource();
                _CacheTokens.Add(key, token);
                entry.AddExpirationToken(new CancellationChangeToken(token.Token));
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
                
                var app = await _AppsHelper.GetApp(appId, AppType.Crowdfund, true);
                var result = await GetInfo(app, _InvoiceRepository, _RateFetcher,
                    _BtcPayNetworkProvider);
                entry.SetValue(result);
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
                    
                    _HubContext.Clients.Group(appId).SendCoreAsync("PaymentReceived", new object[]
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
                _HubContext.Clients.Group(appId).SendCoreAsync("InfoUpdated", new object[]{ task.Result} );
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

        private static async Task<ViewCrowdfundViewModel> GetInfo(AppData appData, InvoiceRepository invoiceRepository,
            RateFetcher rateFetcher, BTCPayNetworkProvider btcPayNetworkProvider, string statusMessage= null)
        {
            var settings = appData.GetSettings<AppsController.CrowdfundSettings>();
            var invoices = await GetInvoicesForApp(appData, invoiceRepository);
          
            
            var rateRules = appData.StoreData.GetStoreBlob().GetRateRules(btcPayNetworkProvider);
            var currentAmount = await GetCurrentContributionAmount(
                invoices.Where(entity => entity.Status == InvoiceStatus.Complete).ToArray(),
                settings.TargetCurrency, rateFetcher, rateRules);
            var currentPendingAmount =  await GetCurrentContributionAmount(
                invoices.Where(entity => entity.Status != InvoiceStatus.Complete).ToArray(),
                settings.TargetCurrency, rateFetcher, rateRules);
            
            
            var active = (settings.StartDate == null || DateTime.Now >= settings.StartDate) &&
                         (settings.EndDate == null || DateTime.Now <= settings.EndDate) &&
                         (!settings.EnforceTargetAmount || settings.TargetAmount > currentAmount);

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
                Info = new ViewCrowdfundViewModel.CrowdfundInfo()
                {
                    TotalContributors = invoices.Length,
                    CurrentPendingAmount = currentPendingAmount,
                    CurrentAmount = currentAmount,
                    Active = active,
                    DaysLeft = settings.EndDate.HasValue? (settings.EndDate - DateTime.UtcNow).Value.Days: (int?) null,
                    DaysLeftToStart = settings.StartDate.HasValue? (settings.StartDate - DateTime.UtcNow).Value.Days: (int?) null,
                    ShowProgress = settings.TargetAmount.HasValue,
                    ProgressPercentage =   (currentAmount/ settings.TargetAmount) * 100,
                    PendingProgressPercentage =  ( currentPendingAmount/ settings.TargetAmount) * 100
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
