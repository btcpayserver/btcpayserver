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
using Microsoft.AspNetCore.Authorization;
using BTCPayServer.Authentication;

namespace BTCPayServer.Controllers
{
    [Authorize(AuthenticationSchemes = Security.Policies.BitpayAuthentication)]
    [AllowAnonymous]
    public class RateController : Controller
    {
        RateFetcher _RateProviderFactory;
        BTCPayNetworkProvider _NetworkProvider;
        CurrencyNameTable _CurrencyNameTable;
        StoreRepository _StoreRepo;

        public TokenRepository TokenRepository { get; }

        public RateController(
            RateFetcher rateProviderFactory,
            BTCPayNetworkProvider networkProvider,
            TokenRepository tokenRepository,
            StoreRepository storeRepo,
            CurrencyNameTable currencyNameTable)
        {
            _RateProviderFactory = rateProviderFactory ?? throw new ArgumentNullException(nameof(rateProviderFactory));
            _NetworkProvider = networkProvider;
            TokenRepository = tokenRepository;
            _StoreRepo = storeRepo;
            _CurrencyNameTable = currencyNameTable ?? throw new ArgumentNullException(nameof(currencyNameTable));
        }

        [Route("rates/{baseCurrency}")]
        [HttpGet]
        [BitpayAPIConstraint]
        public async Task<IActionResult> GetBaseCurrencyRates(string baseCurrency, string storeId)
        {
            storeId = await GetStoreId(storeId);
            var store = this.HttpContext.GetStoreData();
            if (store == null || store.Id != storeId)
                store = await _StoreRepo.FindStore(storeId);
            if (store == null)
            {
                var err = Json(new BitpayErrorsModel() { Error = "Store not found" });
                err.StatusCode = 404;
                return err;
            }
            var supportedMethods = store.GetSupportedPaymentMethods(_NetworkProvider);

            var currencyCodes = supportedMethods.Where(method => !string.IsNullOrEmpty(method.PaymentId.CryptoCode))
                .Select(method => method.PaymentId.CryptoCode).Distinct();

            var currencypairs = BuildCurrencyPairs(currencyCodes, baseCurrency);
            
            var result = await GetRates2(currencypairs, store.Id);
            var rates = (result as JsonResult)?.Value as Rate[];
            if (rates == null)
                return result;
            return Json(new DataWrapper<Rate[]>(rates));
        }


        [Route("rates/{baseCurrency}/{currency}")]
        [HttpGet]
        [BitpayAPIConstraint]
        public async Task<IActionResult> GetCurrencyPairRate(string baseCurrency, string currency, string storeId)
        {
            storeId = await GetStoreId(storeId);
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
            storeId = await GetStoreId(storeId);
            var result = await GetRates2(currencyPairs, storeId);
            var rates = (result as JsonResult)?.Value as Rate[];
            if (rates == null)
                return result;
            return Json(new DataWrapper<Rate[]>(rates));
        }

        private async Task<string> GetStoreId(string storeId)
        {
            if (storeId != null && this.HttpContext.GetStoreData()?.Id == storeId)
                return storeId;
            if(storeId == null)
            {
                var tokens = await this.TokenRepository.GetTokens(this.User.GetSIN());
                storeId = tokens.Select(s => s.StoreId).Where(s => s != null).FirstOrDefault();
            }
            if (storeId == null)
                return null;
            var store = await _StoreRepo.FindStore(storeId);
            if (store == null)
                return null;
            this.HttpContext.SetStoreData(store);
            return storeId;
        }

        [Route("api/rates")]
        [HttpGet]
        public async Task<IActionResult> GetRates2(string currencyPairs, string storeId)
        {
            storeId = await GetStoreId(storeId);
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
                var supportedMethods = store.GetSupportedPaymentMethods(_NetworkProvider);
                var currencyCodes = supportedMethods.Select(method => method.PaymentId.CryptoCode).Distinct();
                var defaultCrypto = store.GetDefaultCrypto(_NetworkProvider);

                currencyPairs = BuildCurrencyPairs(currencyCodes, defaultCrypto);

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
                            .Select(r => (Pair: r, Value: fetching[r].GetAwaiter().GetResult().BidAsk?.Bid))
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

        private static string BuildCurrencyPairs(IEnumerable<string> currencyCodes, string baseCrypto)
        {
            StringBuilder currencyPairsBuilder = new StringBuilder();
            bool first = true;
            foreach (var currencyCode in currencyCodes)
            {
                if(!first)
                    currencyPairsBuilder.Append(",");
                first = false;
                currencyPairsBuilder.Append($"{baseCrypto}_{currencyCode}");
            }
            return currencyPairsBuilder.ToString();
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
