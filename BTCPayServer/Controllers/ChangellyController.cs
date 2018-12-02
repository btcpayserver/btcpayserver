using System;
using System.Threading.Tasks;
using BTCPayServer.Models;
using BTCPayServer.Payments.Changelly;
using BTCPayServer.Rating;
using BTCPayServer.Services.Rates;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    [Route("[controller]/{storeId}")]
    public class ChangellyController : Controller
    {
        private readonly ChangellyClientProvider _changellyClientProvider;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly RateFetcher _RateProviderFactory;

        public ChangellyController(ChangellyClientProvider changellyClientProvider,
            BTCPayNetworkProvider btcPayNetworkProvider,
            RateFetcher rateProviderFactory)
        {
            _RateProviderFactory = rateProviderFactory ?? throw new ArgumentNullException(nameof(rateProviderFactory));

            _changellyClientProvider = changellyClientProvider;
            _btcPayNetworkProvider = btcPayNetworkProvider;
        }

        [HttpGet]
        [Route("currencies")]
        public async Task<IActionResult> GetCurrencyList(string storeId)
        {
            try
            {
                
                var client = await TryGetChangellyClient(storeId);
                
                return Ok(await client.GetCurrenciesFull());
            }
            catch (Exception e)
            {
                return BadRequest(new BitpayErrorModel()
                {
                    Error = e.Message
                });
            }
        }

        [HttpGet]
        [Route("calculate")]
        public async Task<IActionResult> CalculateAmount(string storeId, string fromCurrency, string toCurrency,
            decimal toCurrencyAmount)
        {
            try
            {
                var client = await TryGetChangellyClient(storeId);

                if (fromCurrency.Equals("usd", StringComparison.InvariantCultureIgnoreCase)
                    || fromCurrency.Equals("eur", StringComparison.InvariantCultureIgnoreCase))
                {
                    return await HandleCalculateFiatAmount(fromCurrency, toCurrency, toCurrencyAmount);
                }
                
                var callCounter = 0;
                var baseRate = await client.GetExchangeAmount(fromCurrency, toCurrency, 1);
                var currentAmount = ChangellyCalculationHelper.ComputeBaseAmount(baseRate, toCurrencyAmount);
                while (true)
                {
                    if (callCounter > 10)
                    {
                        BadRequest();
                    }

                    var computedAmount = await client.GetExchangeAmount(fromCurrency, toCurrency, currentAmount);
                    callCounter++;
                    if (computedAmount < toCurrencyAmount)
                    {
                        currentAmount =
                            ChangellyCalculationHelper.ComputeCorrectAmount(currentAmount, computedAmount,
                                toCurrencyAmount);
                    }
                    else
                    {
                        return Ok(currentAmount);
                    }
                }
            }
            catch (Exception e)
            {
                return BadRequest(new BitpayErrorModel()
                {
                    Error = e.Message
                });
            }
        }

        private async Task<Changelly> TryGetChangellyClient(string storeId)
        {
            var store = IsTest? null: HttpContext.GetStoreData();
            storeId = storeId ?? store?.Id;

            return await _changellyClientProvider.TryGetChangellyClient(storeId, store);
        }

        private async Task<IActionResult> HandleCalculateFiatAmount(string fromCurrency, string toCurrency,
            decimal toCurrencyAmount)
        {
            var store = HttpContext.GetStoreData();
            var rules = store.GetStoreBlob().GetRateRules(_btcPayNetworkProvider);
            var rate = await _RateProviderFactory.FetchRate(new CurrencyPair(toCurrency, fromCurrency), rules);
            if (rate.BidAsk == null) return BadRequest();
            var flatRate = rate.BidAsk.Center;
            return Ok(flatRate * toCurrencyAmount);
        }

        public bool IsTest { get; set; } = false;
    }
    
    
}
