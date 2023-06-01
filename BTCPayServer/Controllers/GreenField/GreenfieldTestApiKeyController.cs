using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Security;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers.Greenfield
{
    /// <summary>
    /// this controller serves as a testing endpoint for our api key unit tests
    /// </summary>
    [Route("api/test/apikey")]
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [EnableCors(CorsPolicies.All)]
    public class GreenfieldTestApiKeyController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly PaymentTypeRegistry _paymentTypeRegistry;

        public GreenfieldTestApiKeyController(UserManager<ApplicationUser> userManager, PaymentTypeRegistry paymentTypeRegistry)
        {
            _userManager = userManager;
            _paymentTypeRegistry = paymentTypeRegistry;
        }

        [HttpGet("me/id")]
        [Authorize(Policy = Policies.CanViewProfile, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public string GetCurrentUserId()
        {
            return _userManager.GetUserId(User);
        }

        [HttpGet("me")]
        [Authorize(Policy = Policies.CanViewProfile, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<ApplicationUser> GetCurrentUser()
        {
            return await _userManager.GetUserAsync(User);
        }

        [HttpGet("me/is-admin")]
        [Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public bool AmIAnAdmin()
        {
            return true;
        }

        [HttpGet("me/stores")]
        [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public BTCPayServer.Client.Models.StoreData[] GetCurrentUserStores()
        {
            return this.HttpContext.GetStoresData().Select(data => Greenfield.GreenfieldStoresController.FromModel(data, _paymentTypeRegistry)).ToArray();
        }

        [HttpGet("me/stores/{storeId}/can-view")]
        [Authorize(Policy = Policies.CanViewStoreSettings,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public bool CanViewStore(string storeId)
        {
            return true;
        }

        [HttpGet("me/stores/{storeId}/can-edit")]
        [Authorize(Policy = Policies.CanModifyStoreSettings,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public bool CanEditStore(string storeId)
        {
            return true;
        }
    }
}
