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
        public async Task<IActionResult> GetCurrencyList(string storeId)
        {
            storeId = storeId ?? this.HttpContext.GetStoreData()?.Id;
            var store = this.HttpContext.GetStoreData();
            if (store == null || store.Id != storeId)
                store = await _storeRepo.FindStore(storeId);
            if (store == null)
            {
                var err = Json(new BitpayErrorsModel() { Error = "Store not found" });
                err.StatusCode = 404;
                return err;
            }

            var blob = store.GetStoreBlob();
            if (blob.IsExcluded(ChangellySupportedPaymentMethod.ChangellySupportedPaymentMethodId))
            {
                return BadRequest();
            }

            var paymentMethod = store.GetSupportedPaymentMethods(_networkProvider).SingleOrDefault(method =>
                method.PaymentId == ChangellySupportedPaymentMethod.ChangellySupportedPaymentMethodId);

            if (paymentMethod == null)
            {
                return BadRequest();
            }

            var changellyPaymentMethod = paymentMethod as ChangellySupportedPaymentMethod;
            var client = new Changelly.Changelly(changellyPaymentMethod.ApiKey, changellyPaymentMethod.ApiSecret,
                changellyPaymentMethod.ApiUrl);

            var result = client.GetCurrenciesFull();
            if (result.Success)
            {
                return Ok(result);
            }

            return BadRequest(result);
        }
        [HttpGet]
        [Route("getExchangeAmount")]
        public async Task<IActionResult> GetExchangeAmount(string storeId, string fromCurrency, string toCurrency, double amount)
        {
            storeId = storeId ?? this.HttpContext.GetStoreData()?.Id;
            var store = this.HttpContext.GetStoreData();
            if (store == null || store.Id != storeId)
                store = await _storeRepo.FindStore(storeId);
            if (store == null)
            {
                var err = Json(new BitpayErrorsModel() { Error = "Store not found" });
                err.StatusCode = 404;
                return err;
            }

            var blob = store.GetStoreBlob();
            if (blob.IsExcluded(ChangellySupportedPaymentMethod.ChangellySupportedPaymentMethodId))
            {
                return BadRequest();
            }

            var paymentMethod = store.GetSupportedPaymentMethods(_networkProvider).SingleOrDefault(method =>
                method.PaymentId == ChangellySupportedPaymentMethod.ChangellySupportedPaymentMethodId);

            if (paymentMethod == null)
            {
                return BadRequest();
            }

            var changellyPaymentMethod = paymentMethod as ChangellySupportedPaymentMethod;
            var client = new Changelly.Changelly(changellyPaymentMethod.ApiKey, changellyPaymentMethod.ApiSecret,
                changellyPaymentMethod.ApiUrl);

            var result = client.GetExchangeAmount(fromCurrency, toCurrency, amount);
            if (result.Success)
            {
                return Ok(result);
            }

            return BadRequest(result);
        }
        
        [HttpPost]
        [Route("createTransaction")]
        public async Task<IActionResult> CreateTransaction(string storeId, [FromBody] CreateChangellyTransaction request)
        {
            storeId = storeId ?? this.HttpContext.GetStoreData()?.Id;
            var store = this.HttpContext.GetStoreData();
            if (store == null || store.Id != storeId)
                store = await _storeRepo.FindStore(storeId);
            if (store == null)
            {
                var err = Json(new BitpayErrorsModel() { Error = "Store not found" });
                err.StatusCode = 404;
                return err;
            }

            var blob = store.GetStoreBlob();
            if (blob.IsExcluded(ChangellySupportedPaymentMethod.ChangellySupportedPaymentMethodId))
            {
                return BadRequest();
            }

            var paymentMethod = store.GetSupportedPaymentMethods(_networkProvider).SingleOrDefault(method =>
                method.PaymentId == ChangellySupportedPaymentMethod.ChangellySupportedPaymentMethodId);

            if (paymentMethod == null)
            {
                return BadRequest();
            }

            var changellyPaymentMethod = paymentMethod as ChangellySupportedPaymentMethod;
            var client = new Changelly.Changelly(changellyPaymentMethod.ApiKey, changellyPaymentMethod.ApiSecret,
                changellyPaymentMethod.ApiUrl);

            var result = client.CreateTransaction(request.FromCurrency, request.ToCurrency, request.Address, request.Amount);
            if (result.Success)
            {
                return Ok(result);
            }

            return BadRequest(result);
        }

        public class CreateChangellyTransaction
        {
            public string FromCurrency { get; set; }
            public string ToCurrency { get; set; }
            public double Amount { get; set; }
            public string Address { get; set; }
        }
    }
}
