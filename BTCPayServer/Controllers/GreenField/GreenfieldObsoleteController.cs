using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers.GreenField
{
    [ApiController]
    [EnableCors(CorsPolicies.All)]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    public class GreenfieldObsoleteController : ControllerBase
    {
        [HttpGet("~/api/v1/stores/{storeId}/payment-methods/LNURL")]
        public IActionResult Obsolete1(string storeId)
        {
            return Obsolete();
        }
        [HttpGet("~/api/v1/stores/{storeId}/payment-methods/LNURLPay/{cryptoCode}")]
        [HttpDelete("~/api/v1/stores/{storeId}/payment-methods/LNURLPay/{cryptoCode}")]
        [HttpPut("~/api/v1/stores/{storeId}/payment-methods/LNURLPay/{cryptoCode}")]
        public IActionResult Obsolete2(string storeId, string cryptoCode)
        {
            return Obsolete();
        }
        [HttpGet("~/api/v1/stores/{storeId}/payment-methods/LightningNetwork")]
        public IActionResult Obsolete3(string storeId)
        {
            return Obsolete();
        }
        [HttpGet("~/api/v1/stores/{storeId}/payment-methods/LightningNetwork/{cryptoCode}")]
        [HttpDelete("~/api/v1/stores/{storeId}/payment-methods/LightningNetwork/{cryptoCode}")]
        [HttpPut("~/api/v1/stores/{storeId}/payment-methods/LightningNetwork/{cryptoCode}")]
        public IActionResult Obsolete4(string storeId, string cryptoCode)
        {
            return Obsolete();
        }
        private IActionResult Obsolete()
        {
            return this.CreateAPIError(410, "unsupported-in-v2", "This route isn't supported by BTCPay Server 2.0 and newer. Please update your integration.");
        }
    }
}
