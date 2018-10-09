using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Models;
using BTCPayServer.Payments.Changelly;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Mvc;
using NicolasDorier.RateLimits;

namespace BTCPayServer.Controllers
{
    [RateLimitsFilter(ZoneLimits.Changelly, Scope = RateLimitsScope.RemoteAddress)]
    [Route("[controller]/{storeId}")]
    public class ChangellyController : Controller
    {
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly StoreRepository _storeRepo;

        public ChangellyController(
            BTCPayNetworkProvider networkProvider,
            StoreRepository storeRepo)
        {
            _networkProvider = networkProvider;
            _storeRepo = storeRepo;
        }

        [HttpGet]
        [Route("currencies")]
        public async Task<IActionResult> GetCurrencyList(string storeId, bool enabledOnly = true)
        {
            if (!TryGetChangellyClient(storeId, out var actionResult, out var client))
            {
                return actionResult;
            }

            var result = client.GetCurrenciesFull();
            if (result.Success)
            {
                return Ok(result);
            }

            return BadRequest(result);
        }

        [HttpGet]
        [Route("getExchangeAmount")]
        public async Task<IActionResult> GetExchangeAmount(string storeId, string fromCurrency, string toCurrency,
            double amount)
        {
            if (!TryGetChangellyClient(storeId, out var actionResult, out var client))
            {
                return actionResult;
            }

            var result = client.GetExchangeAmount(fromCurrency, toCurrency, amount);
            if (result.Success)
            {
                return Ok(result);
            }

            return BadRequest(result);
        }
        
        private bool TryGetChangellyClient(string storeId, out IActionResult actionResult,
            out Changelly.Changelly changelly)
        {
            changelly = null;
            actionResult = null;
            storeId = storeId ?? this.HttpContext.GetStoreData()?.Id;

            var store = this.HttpContext.GetStoreData();
            if (store == null || store.Id != storeId)
                store = _storeRepo.FindStore(storeId).Result;
            if (store == null)
            {
                actionResult = NotFound(new BitpayErrorsModel() {Error = "Store not found"});
                return false;
            }

            var blob = store.GetStoreBlob();
            if (blob.IsExcluded(ChangellySupportedPaymentMethod.ChangellySupportedPaymentMethodId))
            {
                actionResult = BadRequest(new BitpayErrorsModel() {Error = "Changelly not enabled for this store"});
            }

            var paymentMethod = store.GetSupportedPaymentMethods(_networkProvider).SingleOrDefault(method =>
                method.PaymentId == ChangellySupportedPaymentMethod.ChangellySupportedPaymentMethodId);

            if (paymentMethod == null)
            {
                actionResult = BadRequest(new BitpayErrorsModel() {Error = "Changelly not configured for this store"});
                return false;
            }

            var changellyPaymentMethod = paymentMethod as ChangellySupportedPaymentMethod;
            changelly = new Changelly.Changelly(changellyPaymentMethod.ApiKey, changellyPaymentMethod.ApiSecret,
                changellyPaymentMethod.ApiUrl);
            return true;
        }
    }
}
