using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Models;
using BTCPayServer.Payments;
using BTCPayServer.Rating;
using BTCPayServer.Security;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace BTCPayServer.Controllers
{
    [EnableCors(CorsPolicies.All)]
    [Authorize(Policy = ServerPolicies.CanGetRates.Key, AuthenticationSchemes = AuthenticationSchemes.Bitpay)]
    public class BitpayRateController : Controller
    {

        readonly RateFetcher _rateProviderFactory;
        readonly CurrencyNameTable _currencyNameTable;
        private readonly DefaultRulesCollection _defaultRules;
        private readonly PaymentMethodHandlerDictionary _handlers;
        readonly StoreRepository _storeRepo;
        private readonly InvoiceRepository _invoiceRepository;

        private StoreData CurrentStore => HttpContext.GetStoreData();

        public BitpayRateController(
            RateFetcher rateProviderFactory,
            StoreRepository storeRepo,
            InvoiceRepository invoiceRepository,
            CurrencyNameTable currencyNameTable,
            DefaultRulesCollection defaultRules,
            PaymentMethodHandlerDictionary handlers)
        {
            _rateProviderFactory = rateProviderFactory ?? throw new ArgumentNullException(nameof(rateProviderFactory));
            _storeRepo = storeRepo;
            _invoiceRepository = invoiceRepository;
            _currencyNameTable = currencyNameTable ?? throw new ArgumentNullException(nameof(currencyNameTable));
            _defaultRules = defaultRules;
            _handlers = handlers;
        }

        [Route("rates/{baseCurrency}")]
        [HttpGet]
        [BitpayAPIConstraint]
        public async Task<IActionResult> GetBaseCurrencyRates(string baseCurrency, string cryptoCode = null, CancellationToken cancellationToken = default)
        {
            var inv = _invoiceRepository.CreateNewInvoice(CurrentStore.Id);
            inv.Currency = baseCurrency;
            var ctx = new InvoiceCreationContext(CurrentStore, CurrentStore.GetStoreBlob(), inv, new Logging.InvoiceLogs(), _handlers, null);
            ctx.SetLazyActivation(true);
            await ctx.BeforeFetchingRates();
            var currencyCodes = ctx
                                .PaymentMethodContexts
                                .SelectMany(c => c.Value.RequiredRates)
                                .Where(c => c.Left.Equals(baseCurrency, StringComparison.OrdinalIgnoreCase))
                                .Select(c => c.Right)
                                .ToHashSet();
            var currencypairs = BuildCurrencyPairs(currencyCodes, baseCurrency);

            var result = await GetRates2(currencypairs, null, cryptoCode, cancellationToken);
            var rates = (result as JsonResult)?.Value as Rate[];
            return rates == null ? result : Json(new DataWrapper<Rate[]>(rates));
        }

        [HttpGet("rates/{baseCurrency}/{currency}")]
        [BitpayAPIConstraint]
        public async Task<IActionResult> GetCurrencyPairRate(string baseCurrency, string currency, string cryptoCode = null, CancellationToken cancellationToken = default)
        {
            var result = await GetRates2($"{baseCurrency}_{currency}", null, cryptoCode, cancellationToken);
            return (result as JsonResult)?.Value is not Rate[] rates
                ? result
                : Json(new DataWrapper<Rate>(rates.First()));
        }

        [HttpGet("rates")]
        [BitpayAPIConstraint]
        public async Task<IActionResult> GetRates(string currencyPairs, string storeId = null, string cryptoCode = null, CancellationToken cancellationToken = default)
        {
            var result = await GetRates2(currencyPairs, storeId, cryptoCode, cancellationToken);
            return (result as JsonResult)?.Value is not Rate[] rates
                ? result
                : Json(new DataWrapper<Rate[]>(rates));
        }

        [AllowAnonymous]
        [HttpGet("api/rates")]
        public async Task<IActionResult> GetRates2(string currencyPairs, string storeId, string cryptoCode = null, CancellationToken cancellationToken = default)
        {
            var store = CurrentStore ?? await _storeRepo.FindStore(storeId);
            if (store == null)
            {
                var err = Json(new BitpayErrorsModel { Error = "Store not found" });
                err.StatusCode = 404;
                return err;
            }
            if (currencyPairs == null)
            {
                var blob = store.GetStoreBlob();
                currencyPairs = blob.GetDefaultCurrencyPairString();
                if (string.IsNullOrEmpty(currencyPairs) && !string.IsNullOrWhiteSpace(cryptoCode))
                {
                    currencyPairs = $"{blob.DefaultCurrency}_{cryptoCode}".ToUpperInvariant();
                }
                if (string.IsNullOrEmpty(currencyPairs))
                {
                    var result = Json(new BitpayErrorsModel() { Error = "You need to setup the default currency pairs in 'Store Settings / Rates' or specify 'currencyPairs' query parameter (eg. BTC_USD,LTC_CAD)." });
                    result.StatusCode = 400;
                    return result;
                }
            }

            var rules = store.GetStoreBlob().GetRateRules(_defaultRules);
            var pairs = new HashSet<CurrencyPair>();
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

            var fetching = _rateProviderFactory.FetchRates(pairs, rules, new StoreIdRateContext(storeId), cancellationToken);
            await Task.WhenAll(fetching.Select(f => f.Value).ToArray());
            return Json(pairs
                            .Select(r => (Pair: r, Value: fetching[r].GetAwaiter().GetResult().BidAsk?.Bid))
                            .Where(r => r.Value.HasValue)
                            .Select(r =>
                            new Rate
                            {
                                CryptoCode = r.Pair.Left,
                                Code = r.Pair.Right,
                                CurrencyPair = r.Pair.ToString(),
                                Name = _currencyNameTable.GetCurrencyData(r.Pair.Right, true).Name,
                                Value = r.Value.Value
                            }).Where(n => n.Name != null).ToArray());
        }

        private static string BuildCurrencyPairs(IEnumerable<string> currencyCodes, string baseCrypto)
        {
            var currencyPairsBuilder = new StringBuilder();
            bool first = true;
            foreach (var currencyCode in currencyCodes)
            {
                if (!first)
                    currencyPairsBuilder.Append(',');
                first = false;
                currencyPairsBuilder.Append(CultureInfo.InvariantCulture, $"{baseCrypto}_{currencyCode}");
            }
            return currencyPairsBuilder.ToString();
        }

        public class Rate
        {

            [JsonProperty(PropertyName = "name")]
            public string Name { get; set; }

            [JsonProperty(PropertyName = "cryptoCode")]
            public string CryptoCode { get; set; }

            [JsonProperty(PropertyName = "currencyPair")]
            public string CurrencyPair { get; set; }

            [JsonProperty(PropertyName = "code")]
            public string Code { get; set; }

            [JsonProperty(PropertyName = "rate")]
            public decimal Value { get; set; }
        }
    }
}
