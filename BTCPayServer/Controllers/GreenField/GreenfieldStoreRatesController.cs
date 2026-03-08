#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Rating;
using BTCPayServer.Services.Rates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers.GreenField
{
    [ApiController]
    [Route("api/v1/stores/{storeId}/rates")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [EnableCors(CorsPolicies.All)]
    public class GreenfieldStoreRatesController : ControllerBase
    {
        private readonly RateFetcher _rateProviderFactory;
        private readonly DefaultRulesCollection _defaultRules;

        public GreenfieldStoreRatesController(
            RateFetcher rateProviderFactory,
            DefaultRulesCollection defaultRules)
        {
            _rateProviderFactory = rateProviderFactory;
            _defaultRules = defaultRules;
        }

        [HttpGet("")]
        [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> GetStoreRates([FromQuery] string[]? currencyPair)
        {
            var data = HttpContext.GetStoreData();
            var blob = data.GetStoreBlob();
            var parsedCurrencyPairs = new HashSet<CurrencyPair>();

            if (currencyPair?.Any() is true)
            {
                foreach (var pair in currencyPair)
                {
                    if (!CurrencyPair.TryParse(pair, out var currencyPairParsed))
                    {
                        ModelState.AddModelError(nameof(currencyPair),
                            $"Invalid currency pair '{pair}' (it should be formatted like BTC_USD,BTC_CAD)");
                        break;
                    }

                    parsedCurrencyPairs.Add(currencyPairParsed);
                }
            }
            else
            {
                parsedCurrencyPairs = blob.DefaultCurrencyPairs.ToHashSet();
            }


            var rules = blob.GetRateRules(_defaultRules);


            var rateTasks = _rateProviderFactory.FetchRates(parsedCurrencyPairs, rules, new StoreIdRateContext(data.Id), CancellationToken.None);
            await Task.WhenAll(rateTasks.Values);
            var result = new List<StoreRateResult>();
            foreach (var rateTask in rateTasks)
            {
                var rateTaskResult = await rateTask.Value;

                result.Add(new StoreRateResult()
                {
                    CurrencyPair = rateTask.Key.ToString(),
                    Errors = rateTaskResult.Errors.Select(errors => errors.ToString()).ToList(),
                    Rate = rateTaskResult.Errors.Any() ? null : rateTaskResult.BidAsk.Bid
                });
            }

            return Ok(result);
        }

    }
}
