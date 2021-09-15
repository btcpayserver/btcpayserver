#nullable enable
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Controllers.GreenField
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    public class StorePaymentMethodsController : ControllerBase
    {
        private StoreData Store => HttpContext.GetStoreData();
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;

        public StorePaymentMethodsController(BTCPayNetworkProvider btcPayNetworkProvider)
        {
            _btcPayNetworkProvider = btcPayNetworkProvider;
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/payment-methods")]
        public ActionResult<Dictionary<string, GenericPaymentMethodData>> GetStorePaymentMethods(
            string storeId,
            [FromQuery] bool? enabled)
        {
            var storeBlob = Store.GetStoreBlob();
            var excludedPaymentMethods = storeBlob.GetExcludedPaymentMethods();
            return Ok(Store.GetSupportedPaymentMethods(_btcPayNetworkProvider)
                .Where(method =>
                    enabled is null || (enabled is false && excludedPaymentMethods.Match(method.PaymentId)))
                .ToDictionary(
                    method => method.PaymentId.ToStringNormalized(),
                    method => new GenericPaymentMethodData()
                    {
                        CryptoCode = method.PaymentId.CryptoCode,
                        Enabled = enabled.GetValueOrDefault(!excludedPaymentMethods.Match(method.PaymentId)),
                        Data = method.PaymentId.PaymentType.GetGreenfieldData(method)
                    }));
        }
    }
}
