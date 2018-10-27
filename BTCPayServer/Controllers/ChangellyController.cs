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
            if (!TryGetChangellyClient(storeId, out var actionResult, out var client))
            {
                return actionResult;
            }

            try
            {
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
            if (!TryGetChangellyClient(storeId, out var actionResult, out var client))
            {
                return actionResult;
            }


            if (fromCurrency.Equals("usd", StringComparison.InvariantCultureIgnoreCase)
                || fromCurrency.Equals("eur", StringComparison.InvariantCultureIgnoreCase))
            {
                var store = HttpContext.GetStoreData();
                var rules = store.GetStoreBlob().GetRateRules(_btcPayNetworkProvider);
                var rate = await _RateProviderFactory.FetchRate(new CurrencyPair(toCurrency, fromCurrency), rules);
                if (rate.BidAsk == null) return BadRequest();
                var flatRate = rate.BidAsk.Center;
                return Ok(flatRate * toCurrencyAmount);
            }


            try
            {
                var callCounter = 0;
                var response1 = await client.GetExchangeAmount(fromCurrency, toCurrency, 1);
                var currentAmount = response1;
                while (true)
                {
                    if (callCounter > 10)
                    {
                        BadRequest();
                    }

                    var response2 = await client.GetExchangeAmount(fromCurrency, toCurrency, currentAmount);
                    callCounter++;
                    if (response2 < toCurrencyAmount)
                    {
                        var newCurrentAmount = ((toCurrencyAmount / response2) * 1m) * currentAmount;

                        currentAmount = newCurrentAmount;
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

        private bool TryGetChangellyClient(string storeId, out IActionResult actionResult,
            out Changelly changelly)
        {
            changelly = null;
            actionResult = null;
            storeId = storeId ?? HttpContext.GetStoreData()?.Id;

            if (_changellyClientProvider.TryGetChangellyClient(storeId, out var error, out changelly)) 
                return true;
            actionResult = BadRequest(new BitpayErrorModel()
            {
                Error = error
            });
            return false;

        }
    }
}
