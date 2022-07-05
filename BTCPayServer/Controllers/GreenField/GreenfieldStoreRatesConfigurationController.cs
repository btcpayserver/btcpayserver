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
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RateSource = BTCPayServer.Client.Models.RateSource;

namespace BTCPayServer.Controllers.GreenField
{
    [ApiController]
    [Route("api/v1/stores/{storeId}/rates/configuration")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    public class GreenfieldStoreRateConfigurationController : ControllerBase
    {
        private readonly RateFetcher _rateProviderFactory;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly StoreRepository _storeRepository;

        public GreenfieldStoreRateConfigurationController(
            RateFetcher rateProviderFactory,
            BTCPayNetworkProvider btcPayNetworkProvider,
            StoreRepository storeRepository)
        {
            _rateProviderFactory = rateProviderFactory;
            _btcPayNetworkProvider = btcPayNetworkProvider;
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
                Script = blob.RateScript,
                Spread = blob.Spread,
                UseScript = blob.RateScripting,
                PreferredSource = blob.PreferredExchange
            });
        }

        [HttpGet("~/api/v1/rates/sources")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public ActionResult<List<RateSource>> GetRateSources()
        {
            return Ok(_rateProviderFactory.RateProviderFactory.GetSupportedExchanges().Select(provider =>
                new RateSource() {Id = provider.Id, Name = provider.DisplayName}));
        }

        [HttpPut("")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> UpdateStoreRateConfiguration(
            StoreRateConfiguration configuration)
        {
            var storeData = HttpContext.GetStoreData();
            var blob = storeData.GetStoreBlob();
            if (!ValidateAndSanitizeConfiguration(configuration, blob))
            {
                return this.CreateValidationError(ModelState);
            }

            PopulateBlob(configuration, blob);

            storeData.SetStoreBlob(blob);

            await _storeRepository.UpdateStore(storeData);


            return GetStoreRateConfiguration();
        }

        [HttpPost("preview")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> PreviewUpdateStoreRateConfiguration(
            StoreRateConfiguration configuration, [FromQuery] string[] currencyPair)
        {
            var data = HttpContext.GetStoreData();
            var blob = data.GetStoreBlob();
            var parsedCurrencyPairs = new HashSet<CurrencyPair>();


            foreach (var pair in currencyPair)
            {
                if (!CurrencyPair.TryParse(pair, out var currencyPairParsed))
                {
                    ModelState.AddModelError(nameof(currencyPair),
                        $"Invalid currency pair '{pair}' (it should be formatted like BTC_USD,BTC_CAD)");
                    continue;
                }

                parsedCurrencyPairs.Add(currencyPairParsed);
            }

            if (!ValidateAndSanitizeConfiguration(configuration, blob))
            {
                return this.CreateValidationError(ModelState);
            }

            PopulateBlob(configuration, blob);

            var rules = blob.GetRateRules(_btcPayNetworkProvider);


            var rateTasks = _rateProviderFactory.FetchRates(parsedCurrencyPairs, rules, CancellationToken.None);
            await Task.WhenAll(rateTasks.Values);
            var result = new List<StoreRatePreviewResult>();
            foreach (var rateTask in rateTasks)
            {
                var rateTaskResult = rateTask.Value.Result;

                result.Add(new StoreRatePreviewResult()
                {
                    CurrencyPair = rateTask.Key.ToString(),
                    Errors = rateTaskResult.Errors.Select(errors => errors.ToString()).ToList(),
                    Rate = rateTaskResult.Errors.Any() ? (decimal?)null : rateTaskResult.BidAsk.Bid
                });
            }

            return Ok(result);
        }

        private bool ValidateAndSanitizeConfiguration(StoreRateConfiguration configuration, StoreBlob storeBlob)
        {
            if (configuration.Spread < 0 || configuration.Spread > 100)
            {
                ModelState.AddModelError(nameof(configuration.Spread),
                    $"Spread value must be between 0 and 100");
            }

            if (configuration.UseScript && string.IsNullOrEmpty(configuration.Script))
            {
                configuration.Script = storeBlob.GetDefaultRateRules(_btcPayNetworkProvider).ToString();
            }

            if (!string.IsNullOrEmpty(configuration.PreferredSource) &&
                !_rateProviderFactory
                    .RateProviderFactory
                    .GetSupportedExchanges()
                    .Any(s =>
                        s.Id.Equals(configuration.PreferredSource,
                            StringComparison.InvariantCultureIgnoreCase)))
            {
                ModelState.AddModelError(nameof(configuration.PreferredSource),
                    $"Unsupported source ({configuration.PreferredSource})");
            }

            if (!string.IsNullOrEmpty(configuration.Script) &&
                RateRules.TryParse(configuration.Script, out var rules, out _))
            {
                configuration.Script = rules.ToString();
            }
            else if(!string.IsNullOrEmpty(configuration.Script))
            {
                ModelState.AddModelError(nameof(configuration.Script),
                    $"Script syntax is invalid");
            }

            return ModelState.ErrorCount == 0;
        }

        private static void PopulateBlob(StoreRateConfiguration configuration, StoreBlob storeBlob)
        {
            storeBlob.PreferredExchange = configuration.PreferredSource;
            storeBlob.Spread = configuration.Spread;
            storeBlob.RateScripting = configuration.UseScript;
            storeBlob.RateScript = configuration.Script;
        }
    }
}
