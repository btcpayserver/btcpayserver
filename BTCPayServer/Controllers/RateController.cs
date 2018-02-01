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

namespace BTCPayServer.Controllers
{
    public class RateController : Controller
    {
        IRateProviderFactory _RateProviderFactory;
        BTCPayNetworkProvider _NetworkProvider;
        CurrencyNameTable _CurrencyNameTable;
        StoreRepository _StoreRepo;
        public RateController(
            IRateProviderFactory rateProviderFactory, 
            BTCPayNetworkProvider networkProvider,
            StoreRepository storeRepo,
            CurrencyNameTable currencyNameTable)
        {
            _RateProviderFactory = rateProviderFactory ?? throw new ArgumentNullException(nameof(rateProviderFactory));
            _NetworkProvider = networkProvider;
            _StoreRepo = storeRepo;
            _CurrencyNameTable = currencyNameTable ?? throw new ArgumentNullException(nameof(currencyNameTable));
        }

        [Route("rates")]
        [HttpGet]
        [BitpayAPIConstraint]
        public async Task<IActionResult> GetRates(string cryptoCode = null, string storeId = null)
        {
            var result = await GetRates2(cryptoCode, storeId);
            var rates = (result as JsonResult)?.Value as NBitpayClient.Rate[];
            if(rates == null)
                return result;
            return Json(new DataWrapper<NBitpayClient.Rate[]>(rates)); 
        }

        [Route("api/rates")]
        [HttpGet]
        public async Task<IActionResult> GetRates2(string cryptoCode = null, string storeId = null)
        {
            cryptoCode = cryptoCode ?? "BTC";
            var network = _NetworkProvider.GetNetwork(cryptoCode);
            if (network == null)
                return NotFound();
            var rateProvider = _RateProviderFactory.GetRateProvider(network, true);
            if (rateProvider == null)
                return NotFound();

            if (storeId != null)
            {
                var store = await _StoreRepo.FindStore(storeId);
                if (store == null)
                    return NotFound();
                rateProvider = store.GetStoreBlob().ApplyRateRules(network, rateProvider);
            }

            var allRates = (await rateProvider.GetRatesAsync());
            return Json(allRates.Select(r =>
                            new NBitpayClient.Rate()
                            {
                                Code = r.Currency,
                                Name = _CurrencyNameTable.GetCurrencyData(r.Currency)?.Name,
                                Value = r.Value
                            }).Where(n => n.Name != null).ToArray());
        }
    }
}
