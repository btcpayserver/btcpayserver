using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Data;
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
        private readonly GreenfieldStoresController _greenfieldStoresController;

        public GreenfieldTestApiKeyController(
            UserManager<ApplicationUser> userManager,
            GreenfieldStoresController greenfieldStoresController)
        {
            _userManager = userManager;
            _greenfieldStoresController = greenfieldStoresController;
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
        public async Task<BTCPayServer.Client.Models.StoreData[]> GetCurrentUserStores()
        {
            var storesData = HttpContext.GetStoresData();
            var stores = new List<Client.Models.StoreData>();
            foreach (var storeData in storesData)
            {
                stores.Add(await _greenfieldStoresController.FromModel(storeData));
            }
            return stores.ToArray();
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
