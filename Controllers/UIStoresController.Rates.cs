#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Rating;
using BTCPayServer.Services.Rates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers;

public partial class UIStoresController
{
    [HttpGet("{storeId}/rates")]
    public async Task<IActionResult> Rates()
    {
        var storeBlob = CurrentStore.GetStoreBlob();
        var vm = new RatesViewModel();
        await SetViewModel(vm, storeBlob);
        return View(vm);
    }

    [HttpPost("{storeId}/rates")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Rates(RatesViewModel model, string? command = null, string? storeId = null, CancellationToken cancellationToken = default)
    {
        model.StoreId = storeId ?? model.StoreId;

        var storeBlob = CurrentStore.GetStoreBlob();
        try
        {
            var currencyPairs = model.DefaultCurrencyPairs?
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => CurrencyPair.Parse(p))
                .ToArray();
            storeBlob.DefaultCurrencyPairs = currencyPairs;
        }
        catch
        {
            ModelState.AddModelError(nameof(model.DefaultCurrencyPairs), StringLocalizer["Invalid currency pairs (should be for example: {0})", "BTC_USD,BTC_CAD,BTC_JPY"]);
        }
        storeBlob.Spread = (decimal)model.Spread / 100.0m;

        var primarySettings = storeBlob.PrimaryRateSettings ??= new();
        FillToStore(primarySettings, model.PrimarySource);
        if (model.HasFallback)
        {
            storeBlob.FallbackRateSettings = new();
            FillToStore(storeBlob.FallbackRateSettings, model.FallbackSource);
        }
        else
        {
            storeBlob.FallbackRateSettings = null;
        }

        if (!ModelState.IsValid)
        {
            await SetViewModel(model, storeBlob);
            return View(model);
        }

        if (command is "scripting-toggle-fallback" or "scripting-toggle-primary")
        {
            var isFallback = command is "scripting-toggle-fallback";
            var rateSettings = storeBlob.GetOrCreateRateSettings(isFallback);

            if (!rateSettings.RateScripting)
            {
                rateSettings.RateScript = rateSettings.GetDefaultRateRules(_defaultRules, storeBlob.Spread).ToString();
                rateSettings.RateScripting = true;
            }
            else
            {
                rateSettings.RateScripting = false;
                rateSettings.RateScript = null;
            }

            CurrentStore.SetStoreBlob(storeBlob);
            await _storeRepo.UpdateStore(CurrentStore);
            if (rateSettings.RateScripting)
            {
                TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Rate rules scripting activated"].Value;
            }
            else
            {
                TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Rate rules scripting deactivated"].Value;
            }

            return RedirectToAction(nameof(Rates), null, new { storeId = CurrentStore.Id });
        }
        else if (command == "Test")
        {
            await SetViewModel(model, storeBlob);
            if (string.IsNullOrWhiteSpace(model.ScriptTest))
            {
                ModelState.AddModelError(nameof(model.ScriptTest), StringLocalizer["Fill out currency pair to test for (like {0})", "BTC_USD,BTC_CAD"]);
                return View(model);
            }
            var splitted = model.ScriptTest.Split(',', StringSplitOptions.RemoveEmptyEntries);

            var pairs = new List<CurrencyPair>();
            foreach (var pair in splitted)
            {
                if (!CurrencyPair.TryParse(pair, out var currencyPair))
                {
                    ModelState.AddModelError(nameof(model.ScriptTest), StringLocalizer["Invalid currency pair '{0}' (it should be formatted like {1})", pair, "BTC_USD,BTC_CAD"]);
                    return View(model);
                }
                pairs.Add(currencyPair);
            }
            var testResults = new List<RatesViewModel.TestResultViewModel>();
            foreach (var isFallback in new[]{ false, true })
            {
                var blob = storeBlob.GetRateSettings(isFallback);
                if (blob is null)
                    continue;
                var rules = blob.GetRateRules(_defaultRules, storeBlob.Spread);
                var fetchs = _rateFactory.FetchRates(pairs.ToHashSet(), rules, new StoreIdRateContext(model.StoreId), cancellationToken);
                foreach (var fetch in fetchs)
                {
                    var testResult = await (fetch.Value);
                    testResults.Add(new RatesViewModel.TestResultViewModel
                    {
                        CurrencyPair = isFallback ? $"{fetch.Key} (fallback)" : fetch.Key.ToString(),
                        Error = testResult.Errors.Count != 0,
                        Rule = testResult.Errors.Count == 0
                            ? testResult.Rule + " = " + testResult.BidAsk.Bid.ToString(CultureInfo.InvariantCulture)
                            : testResult.EvaluatedRule
                    });
                }
            }
            model.TestRateRules = testResults.OrderBy(o => o.CurrencyPair).ToList();
            model.Hash = "#TestResult";
            return View(model);
        }

        // command == Save
        if (CurrentStore.SetStoreBlob(storeBlob))
        {
            await _storeRepo.UpdateStore(CurrentStore);
            TempData[WellKnownTempData.SuccessMessage] = "Rate settings updated";
        }
        return RedirectToAction(nameof(Rates), new
        {
            storeId = CurrentStore.Id
        });
    }

    private void FillToStore(StoreBlob.RateSettings blob, RatesViewModel.Source model)
    {
        if (model.PreferredExchange != null)
            model.PreferredExchange = model.PreferredExchange.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(model.PreferredExchange))
            model.PreferredExchange = null;

        blob.RateScripting = model.ShowScripting;
        if (model.ShowScripting)
        {
            RateRules? rules;
            if (!RateRules.TryParse(model.Script, out rules, out var errors))
            {
                errors ??= [];
                var errorString = string.Join(", ", errors.ToArray());
                ModelState.AddModelError(nameof(model.Script), StringLocalizer["Parsing error: {0}", errorString]);
                return;
            }

            blob.RateScript = rules.ToString();
            ModelState.Remove(nameof(model.Script));
        }
        else
        {
            blob.RateScript = null;
        }
        blob.PreferredExchange = model.PreferredExchange;
        if (model.PreferredExchange is not null && GetAvailableExchanges().All(a => a.Id != model.PreferredExchange))
        {
            ModelState.AddModelError(nameof(model.PreferredExchange), StringLocalizer["Unsupported exchange"]);
            return;
        }
    }

    private async Task SetViewModel(RatesViewModel.Source vm, StoreBlob.RateSettings? rateSettings, StoreBlob storeBlob)
    {
        if (rateSettings is null)
            return;
        var sources = GetAvailableExchanges();
        var exchange = rateSettings.GetPreferredExchange(_defaultRules, storeBlob.DefaultCurrency);
        var chosenSource = sources.First(r => r.Id == exchange);
        vm.Exchanges = _userStoresController.GetExchangesSelectList(storeBlob.DefaultCurrency, rateSettings);
        vm.PreferredExchange = vm.Exchanges.SelectedValue as string;
        vm.PreferredResolvedExchange = chosenSource.Id;
        vm.RateSource = chosenSource.Url;
        vm.Script = rateSettings.GetRateRules(_defaultRules, storeBlob.Spread).ToString();

        var defaultRateSettings = (await _storeRepo.GetDefaultStoreTemplate()).GetStoreBlob()?.GetRateSettings(false) ?? new();
        vm.DefaultScript =  defaultRateSettings.GetDefaultRateRules(_defaultRules, storeBlob.Spread).ToString();
        vm.ShowScripting = rateSettings.RateScripting;

        vm.ScriptingConfirm = new()
        {
            Title = StringLocalizer["Rate rule scripting"],
            Action = StringLocalizer["Continue"],
            GenerateForm = false
        };
        if (vm.ShowScripting)
        {
            vm.ScriptingConfirm.Description = StringLocalizer["This action will delete your rate script. Are you sure to turn off rate rules scripting?"];
            vm.ScriptingConfirm.ButtonClass = "btn-danger";
        }
        else
        {
            vm.ScriptingConfirm.Description = StringLocalizer["This action will modify your current rate sources. Are you sure to turn on rate rules scripting? (Advanced users)"];
            vm.ScriptingConfirm.ButtonClass = "btn-primary";
        }
    }

    private List<RateSourceInfo> GetAvailableExchanges()
    {
        return _rateFactory.RateProviderFactory.AvailableRateProviders
            .OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task SetViewModel(RatesViewModel vm, StoreBlob storeBlob)
    {
        vm.AvailableExchanges = GetAvailableExchanges();
        vm.PrimarySource = new();
        vm.FallbackSource = new() { IsFallback = true };
        await SetViewModel(vm.PrimarySource, storeBlob.GetRateSettings(false), storeBlob);
        if (storeBlob.GetRateSettings(true) is { } r)
        {
            vm.HasFallback = true;
            await SetViewModel(vm.FallbackSource, r, storeBlob);
        }
        else
        {
            await SetViewModel(vm.FallbackSource, new(), storeBlob);
        }

        vm.Spread = (double)(storeBlob.Spread * 100m);
        vm.StoreId = CurrentStore.Id;
        vm.DefaultCurrencyPairs = storeBlob.GetDefaultCurrencyPairString();
    }
}
