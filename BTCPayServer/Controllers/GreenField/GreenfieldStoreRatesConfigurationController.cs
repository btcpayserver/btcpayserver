#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Rating;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using RateSource = BTCPayServer.Client.Models.RateSource;

namespace BTCPayServer.Controllers.GreenField
{
    [ApiController]
    [Route("api/v1/stores/{storeId}/rates/configuration")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [EnableCors(CorsPolicies.All)]
    public class GreenfieldStoreRateConfigurationController : ControllerBase
    {
        private readonly RateFetcher _rateProviderFactory;
        private readonly DefaultRulesCollection _defaultRules;
        private readonly StoreRepository _storeRepository;

        public GreenfieldStoreRateConfigurationController(
            RateFetcher rateProviderFactory,
			DefaultRulesCollection defaultRules,
            StoreRepository storeRepository)
        {
            _rateProviderFactory = rateProviderFactory;
			_defaultRules = defaultRules;
            _storeRepository = storeRepository;
        }

        [HttpGet("")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public IActionResult GetStoreRateConfiguration()
        {
            var data = HttpContext.GetStoreData();
            var blob = data.GetStoreBlob();

            return Ok(new StoreRateConfiguration()
            {
                EffectiveScript = blob.GetRateRules(_defaultRules, out var preferredExchange).ToString(),
                Spread = blob.Spread * 100.0m,
                IsCustomScript = blob.RateScripting,
                PreferredSource = preferredExchange ? blob.GetPreferredExchange(_defaultRules) : null
            });
        }

        [HttpGet("/misc/rate-sources")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie + "," + AuthenticationSchemes.Greenfield)]
        public ActionResult<List<RateSource>> GetRateSources()
        {
            return Ok(_rateProviderFactory.RateProviderFactory.AvailableRateProviders.Select(provider =>
                new RateSource() { Id = provider.Id, Name = provider.DisplayName }));
        }

        [HttpPut("")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> UpdateStoreRateConfiguration(
            StoreRateConfiguration configuration)
        {
            var storeData = HttpContext.GetStoreData();
            var blob = storeData.GetStoreBlob();
            ValidateAndSanitizeConfiguration(configuration, blob);
            if (!ModelState.IsValid)
                return this.CreateValidationError(ModelState);

            PopulateBlob(configuration, blob);

            storeData.SetStoreBlob(blob);

            await _storeRepository.UpdateStore(storeData);


            return GetStoreRateConfiguration();
        }

        [HttpPost("preview")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> PreviewUpdateStoreRateConfiguration(
            StoreRateConfiguration configuration, [FromQuery] string[]? currencyPair)
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

            ValidateAndSanitizeConfiguration(configuration, blob);
            if (!ModelState.IsValid)
                return this.CreateValidationError(ModelState);
            PopulateBlob(configuration, blob);

            var rules = blob.GetRateRules(_defaultRules);


            var rateTasks = _rateProviderFactory.FetchRates(parsedCurrencyPairs, rules, new StoreIdRateContext(data.Id), CancellationToken.None);
            await Task.WhenAll(rateTasks.Values);
            var result = new List<StoreRateResult>();
            foreach (var rateTask in rateTasks)
            {
                var rateTaskResult = rateTask.Value.Result;

                result.Add(new StoreRateResult()
                {
                    CurrencyPair = rateTask.Key.ToString(),
                    Errors = rateTaskResult.Errors.Select(errors => errors.ToString()).ToList(),
                    Rate = rateTaskResult.Errors.Any() ? (decimal?)null : rateTaskResult.BidAsk.Bid
                });
            }

            return Ok(result);
        }

        private void ValidateAndSanitizeConfiguration(StoreRateConfiguration? configuration, StoreBlob storeBlob)
        {
            if (configuration is null)
            {
                ModelState.AddModelError("", "Body required");
                return;
            }
            if (configuration.Spread < 0 || configuration.Spread > 100)
            {
                ModelState.AddModelError(nameof(configuration.Spread),
                    $"Spread value must be in %, between 0 and 100");
            }

            if (configuration.IsCustomScript)
            {
                if (string.IsNullOrEmpty(configuration.EffectiveScript))
                {
                    configuration.EffectiveScript = storeBlob.GetDefaultRateRules(_defaultRules).ToString();
                }

                if (!RateRules.TryParse(configuration.EffectiveScript, out var r))
                {
                    ModelState.AddModelError(nameof(configuration.EffectiveScript),
                   $"Script syntax is invalid");
                }
                else
                {
                    configuration.EffectiveScript = r.ToString();
                }

                if (!string.IsNullOrEmpty(configuration.PreferredSource))
                {
                    ModelState.AddModelError(nameof(configuration.PreferredSource),
$"You can't set the preferredSource if you are using custom scripts");
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(configuration.EffectiveScript))
                {
                    ModelState.AddModelError(nameof(configuration.EffectiveScript),
                    $"You can't set the effectiveScript if you aren't using custom scripts");
                }
                if (!string.IsNullOrEmpty(configuration.PreferredSource))
                {
                    configuration.PreferredSource = _rateProviderFactory
                        .RateProviderFactory
                        .AvailableRateProviders
                        .FirstOrDefault(s =>
                            s.Id.Equals(configuration.PreferredSource,
                                StringComparison.InvariantCultureIgnoreCase))?.Id;

                    if (string.IsNullOrEmpty(configuration.PreferredSource))
                    {
                        ModelState.AddModelError(nameof(configuration.PreferredSource),
                        $"Unsupported source, please check /misc/rate-sources to see valid values ({configuration.PreferredSource})");
                    }
                }
            }
        }

        private static void PopulateBlob(StoreRateConfiguration configuration, StoreBlob storeBlob)
        {
            storeBlob.PreferredExchange = configuration.PreferredSource;
            storeBlob.Spread = configuration.Spread / 100.0m;
            storeBlob.RateScripting = configuration.IsCustomScript;
            storeBlob.RateScript = configuration.EffectiveScript;
        }
    }
}
