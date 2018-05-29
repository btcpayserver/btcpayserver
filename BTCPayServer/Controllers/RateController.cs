using BTCPayServer.Models;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Filters;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using BTCPayServer.Rating;
using Newtonsoft.Json;

namespace BTCPayServer.Controllers
{
    public class RateController : Controller
    {
        BTCPayRateProviderFactory _RateProviderFactory;
        BTCPayNetworkProvider _NetworkProvider;
        CurrencyNameTable _CurrencyNameTable;
        StoreRepository _StoreRepo;
        public RateController(
            BTCPayRateProviderFactory rateProviderFactory,
            BTCPayNetworkProvider networkProvider,
            StoreRepository storeRepo,
            CurrencyNameTable currencyNameTable)
        {
            _RateProviderFactory = rateProviderFactory ?? throw new ArgumentNullException(nameof(rateProviderFactory));
            _NetworkProvider = networkProvider;
            _StoreRepo = storeRepo;
            _CurrencyNameTable = currencyNameTable ?? throw new ArgumentNullException(nameof(currencyNameTable));
        }

        [Route("rates/{baseCurrency}")]
        [HttpGet]
        public async Task<IActionResult> GetBaseCurrencyRates(string baseCurrency, string storeId)
        {
            storeId = storeId ?? this.HttpContext.GetStoreData()?.Id;
            var store = this.HttpContext.GetStoreData();
            if (store == null || store.Id != storeId)
                store = await _StoreRepo.FindStore(storeId);
            if (store == null)
            {
                var err = Json(new BitpayErrorsModel() { Error = "Store not found" });
                err.StatusCode = 404;
                return err;
            }
            var currencypairs = "";
            var supportedMethods = store.GetSupportedPaymentMethods(_NetworkProvider);

            var currencyCodes = supportedMethods.Where(method => !string.IsNullOrEmpty(method.CryptoCode))
                .Select(method => method.CryptoCode).Distinct();


            foreach (var currencyCode in currencyCodes)
            {
                if (baseCurrency == currencyCode)
                {
                    continue;
                }
                if (!string.IsNullOrEmpty(currencypairs))
                {
                    currencypairs += ",";
                }
                currencypairs += baseCurrency + "_ " + currencyCode;
            }
            var result = await GetRates2(currencypairs, store.Id);
            var rates = (result as JsonResult)?.Value as Rate[];
            if (rates == null)
                return result;
            return Json(new DataWrapper<Rate[]>(rates));
        }


        [Route("rates/{baseCurrency}/{currency}")]
        [HttpGet]
        public async Task<IActionResult> GetCurrencyPairRate(string baseCurrency, string currency, string storeId)
        {
            storeId = storeId ?? this.HttpContext.GetStoreData()?.Id;
            var result = await GetRates2($"{baseCurrency}_{currency}", storeId);
            var rates = (result as JsonResult)?.Value as Rate[];
            if (rates == null)
                return result;
            return Json(new DataWrapper<Rate>(rates.First()));
        }

        [Route("rates")]
        [HttpGet]
        [BitpayAPIConstraint]
        public async Task<IActionResult> GetRates(string currencyPairs, string storeId)
        {
            storeId = storeId ?? this.HttpContext.GetStoreData()?.Id;
            var result = await GetRates2(currencyPairs, storeId);
            var rates = (result as JsonResult)?.Value as Rate[];
            if (rates == null)
                return result;
            return Json(new DataWrapper<Rate[]>(rates));
        }


        [Route("api/rates")]
        [HttpGet]
        public async Task<IActionResult> GetRates2(string currencyPairs, string storeId)
        {
            if (storeId == null)
            {
                var result = Json(new BitpayErrorsModel() { Error = "You need to specify storeId (in your store settings)" });
                result.StatusCode = 400;
                return result;
            }
            var store = this.HttpContext.GetStoreData();
            if (store == null || store.Id != storeId)
                store = await _StoreRepo.FindStore(storeId);
            if (store == null)
            {
                var result = Json(new BitpayErrorsModel() { Error = "Store not found" });
                result.StatusCode = 404;
                return result;
            }

            if (currencyPairs == null)
            {
                currencyPairs = "";
                var supportedMethods = store.GetSupportedPaymentMethods(_NetworkProvider);
                var currencyCodes = supportedMethods.Where(method => !string.IsNullOrEmpty(method.CryptoCode))
                    .Select(method => method.CryptoCode).Distinct();

                foreach (var currencyCode in currencyCodes)
                {
                    foreach (var currencyCode2 in currencyCodes)
                    {
                        if (currencyCode == currencyCode2)
                        {
                            continue;
                        }
                        if (!string.IsNullOrEmpty(currencyPairs))
                        {
                            currencyPairs += ",";
                        }
                        currencyPairs += $"{currencyCode}_{currencyCode2}";
                    }
                }

                if (string.IsNullOrEmpty(currencyPairs))
                {
                    var result = Json(new BitpayErrorsModel() { Error = "You need to specify currencyPairs (eg. BTC_USD,LTC_CAD)" });
                    result.StatusCode = 400;
                    return result;
                }
            }


            var rules = store.GetStoreBlob().GetRateRules(_NetworkProvider);

            HashSet<CurrencyPair> pairs = new HashSet<CurrencyPair>();
            foreach (var currency in currencyPairs.Split(','))
            {
                if (!CurrencyPair.TryParse(currency, out var pair))
                {
                    var result = Json(new BitpayErrorsModel() { Error = $"Currency pair {currency} uncorrectly formatted" });
                    result.StatusCode = 400;
                    return result;
                }
                pairs.Add(pair);
            }

            var fetching = _RateProviderFactory.FetchRates(pairs, rules);
            await Task.WhenAll(fetching.Select(f => f.Value).ToArray());
            return Json(pairs
                            .AsParallel()
                            .Select(r => (Pair: r, Value: fetching[r].GetAwaiter().GetResult().Value))
                            .Where(r => r.Value.HasValue)
                            .Select(r =>
                            new Rate()
                            {
                                CryptoCode = r.Pair.Left,
                                Code = r.Pair.Right,
                                CurrencyPair = r.Pair.ToString(),
                                Name = _CurrencyNameTable.GetCurrencyData(r.Pair.Right, true).Name,
                                Value = r.Value.Value
                            }).Where(n => n.Name != null).ToArray());
        }

        public class Rate
        {

            [JsonProperty(PropertyName = "name")]
            public string Name
            {
                get;
                set;
            }
            [JsonProperty(PropertyName = "cryptoCode")]
            public string CryptoCode
            {
                get;
                set;
            }

            [JsonProperty(PropertyName = "currencyPair")]
            public string CurrencyPair
            {
                get;
                set;
            }

            [JsonProperty(PropertyName = "code")]
            public string Code
            {
                get;
                set;
            }
            [JsonProperty(PropertyName = "rate")]
            public decimal Value
            {
                get;
                set;
            }
        }
    }
}
