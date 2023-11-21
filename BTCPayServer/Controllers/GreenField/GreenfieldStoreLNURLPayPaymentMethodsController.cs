#nullable enable
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Lightning;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Security;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Controllers.Greenfield
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [EnableCors(CorsPolicies.All)]
    public class GreenfieldStoreLNURLPayPaymentMethodsController : ControllerBase
    {
        private StoreData Store => HttpContext.GetStoreData();
        private readonly StoreRepository _storeRepository;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;

        public GreenfieldStoreLNURLPayPaymentMethodsController(
            StoreRepository storeRepository,
            BTCPayNetworkProvider btcPayNetworkProvider)
        {
            _storeRepository = storeRepository;
            _btcPayNetworkProvider = btcPayNetworkProvider;
        }

        public static IEnumerable<LNURLPayPaymentMethodData> GetLNURLPayPaymentMethods(StoreData store,
            BTCPayNetworkProvider networkProvider, bool? enabled)
        {
            var blob = store.GetStoreBlob();
            var excludedPaymentMethods = blob.GetExcludedPaymentMethods();

            return store.GetSupportedPaymentMethods(networkProvider)
                .Where((method) => method.PaymentId.PaymentType == PaymentTypes.LNURLPay)
                .OfType<LNURLPaySupportedPaymentMethod>()
                .Select(paymentMethod =>
                    new LNURLPayPaymentMethodData(
                        paymentMethod.PaymentId.CryptoCode,
                        !excludedPaymentMethods.Match(paymentMethod.PaymentId),
                        paymentMethod.UseBech32Scheme,
                        paymentMethod.LUD12Enabled
                    )
                )
                .Where((result) => enabled is null || enabled == result.Enabled)
                .ToList();
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/payment-methods/LNURLPay")]
        public ActionResult<IEnumerable<LNURLPayPaymentMethodData>> GetLNURLPayPaymentMethods(
            string storeId,
            [FromQuery] bool? enabled)
        {
            return Ok(GetLNURLPayPaymentMethods(Store, _btcPayNetworkProvider, enabled));
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/payment-methods/LNURLPay/{cryptoCode}")]
        public IActionResult GetLNURLPayPaymentMethod(string storeId, string cryptoCode)
        {
            AssertCryptoCodeWallet(cryptoCode, out _);
            var method = GetExistingLNURLPayPaymentMethod(cryptoCode);
            if (method is null)
            {
                return this.CreateAPIError(404, "paymentmethod-not-found", "The LNURL Payment Method isn't activated");
            }

            return Ok(method);
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpDelete("~/api/v1/stores/{storeId}/payment-methods/LNURLPay/{cryptoCode}")]
        public async Task<IActionResult> RemoveLNURLPayPaymentMethod(
            string storeId,
            string cryptoCode)
        {

            AssertCryptoCodeWallet(cryptoCode, out _);

            var id = new PaymentMethodId(cryptoCode, PaymentTypes.LNURLPay);
            var store = Store;
            store.SetSupportedPaymentMethod(id, null);
            await _storeRepository.UpdateStore(store);
            return Ok();
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPut("~/api/v1/stores/{storeId}/payment-methods/LNURLPay/{cryptoCode}")]
        public async Task<IActionResult> UpdateLNURLPayPaymentMethod(string storeId, string cryptoCode,
            [FromBody] LNURLPayPaymentMethodData paymentMethodData)
        {
            var paymentMethodId = new PaymentMethodId(cryptoCode, PaymentTypes.LNURLPay);

            AssertCryptoCodeWallet(cryptoCode, out _);

            var lnMethod = GreenfieldStoreLightningNetworkPaymentMethodsController.GetExistingLightningLikePaymentMethod(_btcPayNetworkProvider,
                cryptoCode, Store);

            if ((lnMethod is null || lnMethod.Enabled is false) && paymentMethodData.Enabled)
            {
                ModelState.AddModelError(nameof(LNURLPayPaymentMethodData.Enabled),
                    "LNURL Pay cannot be enabled unless the lightning payment method is configured and enabled on this store");
            }

            if (!ModelState.IsValid)
                return this.CreateValidationError(ModelState);

            var paymentMethod = new LNURLPaySupportedPaymentMethod
            {
                CryptoCode = cryptoCode,
                UseBech32Scheme = paymentMethodData.UseBech32Scheme,
                LUD12Enabled = paymentMethodData.LUD12Enabled
            };

            var store = Store;
            var storeBlob = store.GetStoreBlob();
            store.SetSupportedPaymentMethod(paymentMethodId, paymentMethod);
            storeBlob.SetExcluded(paymentMethodId, !paymentMethodData.Enabled);
            store.SetStoreBlob(storeBlob);
            await _storeRepository.UpdateStore(store);
            return Ok(GetExistingLNURLPayPaymentMethod(cryptoCode, store));
        }

        private LNURLPayPaymentMethodData? GetExistingLNURLPayPaymentMethod(string cryptoCode,
            StoreData? store = null)
        {
            store ??= Store;
            var storeBlob = store.GetStoreBlob();
            var id = new PaymentMethodId(cryptoCode, PaymentTypes.LNURLPay);
            var paymentMethod = store
                .GetSupportedPaymentMethods(_btcPayNetworkProvider)
                .OfType<LNURLPaySupportedPaymentMethod>()
                .FirstOrDefault(method => method.PaymentId == id);

            var excluded = storeBlob.IsExcluded(id);
            return paymentMethod is null
                ? null
                : new LNURLPayPaymentMethodData(
                    paymentMethod.PaymentId.CryptoCode,
                    !excluded,
                    paymentMethod.UseBech32Scheme,
                    paymentMethod.LUD12Enabled
                );
        }
        private void AssertCryptoCodeWallet(string cryptoCode, out BTCPayNetwork network)
        {
            network = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
            if (network is null)
                throw new JsonHttpException(this.CreateAPIError(404, "unknown-cryptocode", "This crypto code isn't set up in this BTCPay Server instance"));
        }

    }
}
