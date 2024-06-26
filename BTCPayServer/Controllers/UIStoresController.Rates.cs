#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Rating;
using BTCPayServer.Services.Rates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using static BTCPayServer.Lightning.Eclair.Models.ChannelResponse;

namespace BTCPayServer.Controllers;

public partial class UIStoresController
{
    [HttpGet("{storeId}/rates")]
    public IActionResult Rates()
    {
        var storeBlob = CurrentStore.GetStoreBlob();
        var vm = new RatesViewModel();
        FillFromStore(vm, storeBlob);
        return View(vm);
    }

    [HttpPost("{storeId}/rates")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Rates(RatesViewModel model, string? command = null, string? storeId = null, CancellationToken cancellationToken = default)
    {
        if (command == "scripting-on")
        {
            return RedirectToAction(nameof(ShowRateRules), new { scripting = true, storeId = model.StoreId });
        }
        if (command == "scripting-off")
        {
            return RedirectToAction(nameof(ShowRateRules), new { scripting = false, storeId = model.StoreId });
        }
        model.StoreId = storeId ?? model.StoreId;
        CurrencyPair[]? currencyPairs = null;
        try
        {
            currencyPairs = model.DefaultCurrencyPairs?
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => CurrencyPair.Parse(p))
                .ToArray();
        }
        catch
        {
            ModelState.AddModelError(nameof(model.DefaultCurrencyPairs), "Invalid currency pairs (should be for example: BTC_USD,BTC_CAD,BTC_JPY)");
        }
        if (!ModelState.IsValid)
        {
            return View(model);
        }
        if (model.PreferredExchange != null)
            model.PreferredExchange = model.PreferredExchange.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(model.PreferredExchange))
            model.PreferredExchange = null;

        var blob = CurrentStore.GetStoreBlob();
        RateRules? rules;
        if (model.ShowScripting)
        {
            if (!RateRules.TryParse(model.Script, out rules, out var errors))
            {
                errors ??= [];
                var errorString = string.Join(", ", errors.ToArray());
                ModelState.AddModelError(nameof(model.Script), $"Parsing error ({errorString})");
                FillFromStore(model, blob);
                return View(model);
            }
            else
            {
                blob.RateScript = rules.ToString();
                ModelState.Remove(nameof(model.Script));
            }
        }
        blob.PreferredExchange = model.PreferredExchange;
        blob.Spread = (decimal)model.Spread / 100.0m;
        blob.DefaultCurrencyPairs = currencyPairs;
        FillFromStore(model, blob);
        rules = blob.GetRateRules(_defaultRules);
        if (command == "Test")
        {
            if (string.IsNullOrWhiteSpace(model.ScriptTest))
            {
                ModelState.AddModelError(nameof(model.ScriptTest), "Fill out currency pair to test for (like BTC_USD,BTC_CAD)");
                return View(model);
            }
            var splitted = model.ScriptTest.Split(',', StringSplitOptions.RemoveEmptyEntries);

            var pairs = new List<CurrencyPair>();
            foreach (var pair in splitted)
            {
                if (!CurrencyPair.TryParse(pair, out var currencyPair))
                {
                    ModelState.AddModelError(nameof(model.ScriptTest), $"Invalid currency pair '{pair}' (it should be formatted like BTC_USD,BTC_CAD)");
                    return View(model);
                }
                pairs.Add(currencyPair);
            }

            var fetchs = _rateFactory.FetchRates(pairs.ToHashSet(), rules, new StoreIdRateContext(model.StoreId), cancellationToken);
            var testResults = new List<RatesViewModel.TestResultViewModel>();
            foreach (var fetch in fetchs)
            {
                var testResult = await (fetch.Value);
                testResults.Add(new RatesViewModel.TestResultViewModel
                {
                    CurrencyPair = fetch.Key.ToString(),
                    Error = testResult.Errors.Count != 0,
                    Rule = testResult.Errors.Count == 0 ? testResult.Rule + " = " + testResult.BidAsk.Bid.ToString(CultureInfo.InvariantCulture)
                        : testResult.EvaluatedRule
                });
            }
            model.TestRateRules = testResults;
            return View(model);
        }

        if (model.PreferredExchange is not null && !model.AvailableExchanges.Any(a => a.Id == model.PreferredExchange))
        {
            ModelState.AddModelError(nameof(model.PreferredExchange), $"Unsupported exchange");
            return View(model);
        }

        // command == Save
        if (CurrentStore.SetStoreBlob(blob))
        {
            await _storeRepo.UpdateStore(CurrentStore);
            TempData[WellKnownTempData.SuccessMessage] = "Rate settings updated";
        }
        return RedirectToAction(nameof(Rates), new
        {
            storeId = CurrentStore.Id
        });
    }

    [HttpGet("{storeId}/rates/confirm")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public IActionResult ShowRateRules(bool scripting)
    {
        return View("Confirm", new ConfirmModel
        {
            Action = "Continue",
            Title = "Rate rule scripting",
            Description = scripting ?
                "This action will modify your current rate sources. Are you sure to turn on rate rules scripting? (Advanced users)"
                : "This action will delete your rate script. Are you sure to turn off rate rules scripting?",
            ButtonClass = scripting ? "btn-primary" : "btn-danger"
        });
    }

    [HttpPost("{storeId}/rates/confirm")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> ShowRateRulesPost(bool scripting)
    {
        var blob = CurrentStore.GetStoreBlob();
        blob.RateScripting = scripting;
        blob.RateScript = blob.GetDefaultRateRules(_defaultRules).ToString();
        CurrentStore.SetStoreBlob(blob);
        await _storeRepo.UpdateStore(CurrentStore);
        TempData[WellKnownTempData.SuccessMessage] = "Rate rules scripting " + (scripting ? "activated" : "deactivated");
        return RedirectToAction(nameof(Rates), new { storeId = CurrentStore.Id });
    }

    private void FillFromStore(RatesViewModel vm, StoreBlob storeBlob)
    {
        var sources = _rateFactory.RateProviderFactory.AvailableRateProviders
            .OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase);
        vm.AvailableExchanges = sources;
        var exchange = storeBlob.GetPreferredExchange(_defaultRules);
        var chosenSource = sources.First(r => r.Id == exchange);
        vm.Exchanges = UIUserStoresController.GetExchangesSelectList(_rateFactory, _defaultRules, storeBlob);
        vm.PreferredExchange = vm.Exchanges.SelectedValue as string;
        vm.PreferredResolvedExchange = chosenSource.Id;
        vm.RateSource = chosenSource.Url;
        vm.Spread = (double)(storeBlob.Spread * 100m);
        vm.StoreId = CurrentStore.Id;
        vm.Script = storeBlob.GetRateRules(_defaultRules).ToString();
        vm.DefaultScript = storeBlob.GetDefaultRateRules(_defaultRules).ToString();
        vm.DefaultCurrencyPairs = storeBlob.GetDefaultCurrencyPairString();
        vm.ShowScripting = storeBlob.RateScripting;
    }
}
