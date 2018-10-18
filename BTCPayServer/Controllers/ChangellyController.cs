using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Models;
using BTCPayServer.Payments.Changelly;
using BTCPayServer.Rating;
using BTCPayServer.Services.Rates;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Controllers
{
    [Route("[controller]/{storeId}")]
    public class ChangellyController : Controller
    {
        private readonly ChangellyClientProvider _changellyClientProvider;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly ILogger<ChangellyController> _logger;
        private readonly RateFetcher _RateProviderFactory;

        public ChangellyController(ChangellyClientProvider changellyClientProvider,
            BTCPayNetworkProvider btcPayNetworkProvider,
            RateFetcher rateProviderFactory,
            ILogger<ChangellyController> logger)
        {
            _RateProviderFactory = rateProviderFactory ?? throw new ArgumentNullException(nameof(rateProviderFactory));

            _changellyClientProvider = changellyClientProvider;
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _logger = logger;
        }

        [HttpGet]
        [Route("currencies")]
        public async Task<IActionResult> GetCurrencyList(string storeId)
        {
            if (!TryGetChangellyClient(storeId, out var actionResult, out var client))
            {
                return actionResult;
            }

            var result = await client.GetCurrenciesFull();
            if (result.Success)
            {
                return Ok(result);
            }

            return BadRequest(result);
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

            decimal? currentAmount = null;
            var callCounter = 0;

            var response1 = await client.GetExchangeAmount(fromCurrency, toCurrency, 1);
            if (!response1.Success) return BadRequest(response1);
            currentAmount = response1.Amount;

            while (true)
            {
                if (callCounter > 10)
                {
                    BadRequest();
                }

                var response2 = await client.GetExchangeAmount(fromCurrency, toCurrency, currentAmount.Value);
                callCounter++;
                if (!response2.Success) return BadRequest(response2);
                if (response2.Amount < toCurrencyAmount)
                {
                    var newCurrentAmount = ((toCurrencyAmount / response2.Amount) * 1m) * currentAmount.Value;
                    _logger.LogInformation(
                        "attempt {0} for {1} to {2}[{3}] {4} = {5}, trying {6} now ]",
                        callCounter, fromCurrency, toCurrency, toCurrencyAmount, currentAmount.Value, response2.Amount,
                        newCurrentAmount);

                    currentAmount = newCurrentAmount;
                }
                else
                {
                    return Ok(currentAmount.Value);
                }
            }
        }

        private bool TryGetChangellyClient(string storeId, out IActionResult actionResult,
            out Changelly changelly)
        {
            changelly = null;
            actionResult = null;
            storeId = storeId ?? HttpContext.GetStoreData()?.Id;

            if (!_changellyClientProvider.TryGetChangellyClient(storeId, out var error, out changelly))
            {
                actionResult = BadRequest(new BitpayErrorModel()
                {
                    Error = error
                });
                return false;
            }

            return true;
        }
    }
}
