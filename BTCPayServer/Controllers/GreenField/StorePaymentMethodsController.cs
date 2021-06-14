#nullable enable
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
        public ActionResult<LightningNetworkPaymentMethodData> GetPaymentMethods(
            [FromQuery] bool enabledOnly = false
        )
        {
            var storeBlob = Store.GetStoreBlob();
            var excludedPaymentMethods = storeBlob.GetExcludedPaymentMethods();

            return Ok(new {
                onchain = StoreOnChainPaymentMethodsController.GetOnChainPaymentMethods(Store, _btcPayNetworkProvider, enabledOnly),
                lightning = StoreLightningNetworkPaymentMethodsController.GetLightningPaymentMethods(Store, _btcPayNetworkProvider, enabledOnly)
            });
        }
    }
}
