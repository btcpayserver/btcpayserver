using System.Threading.Tasks;
using BTCPayServer.Authentication;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Security;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Validation.AspNetCore;

namespace BTCPayServer.Controllers.RestApi
{
    /// <summary>
    /// this controller serves as a testing endpoint for our OpenId unit tests
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.OpenId)]
    public class TestController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly StoreRepository _storeRepository;

        public TestController(UserManager<ApplicationUser> userManager, StoreRepository storeRepository)
        {
            _userManager = userManager;
            _storeRepository = storeRepository;
        }

        [HttpGet("me/id")]
        public string GetCurrentUserId()
        {
            return _userManager.GetUserId(User);
        }

        [HttpGet("me")]
        public async Task<ApplicationUser> GetCurrentUser()
        {
            return await _userManager.GetUserAsync(User);
        }

        [HttpGet("me/is-admin")]
        [Authorize(Policy = Policies.CanModifyServerSettings.Key, AuthenticationSchemes = AuthenticationSchemes.OpenId)]
        public bool AmIAnAdmin()
        {
            return true;
        }

        [HttpGet("me/stores")]
        public async Task<StoreData[]> GetCurrentUserStores()
        {
            return await _storeRepository.GetStoresByUserId(_userManager.GetUserId(User));
        }


        [HttpGet("me/stores/{storeId}/can-edit")]
        [Authorize(Policy = Policies.CanModifyStoreSettings.Key,
            AuthenticationSchemes = AuthenticationSchemes.OpenId)]
        public bool CanEdit(string storeId)
        {
            return true;
        }


#region scopes tests

        [Authorize(Policy = RestAPIPolicies.CanViewStores,
            AuthenticationSchemes = AuthenticationSchemes.OpenId)]
        [HttpGet(nameof(ScopeCanViewStores))]
        public bool ScopeCanViewStores() { return true; }

        [Authorize(Policy = RestAPIPolicies.CanManageStores,
            AuthenticationSchemes = AuthenticationSchemes.OpenId)]
        [HttpGet(nameof(ScopeCanManageStores))]
        public bool ScopeCanManageStores() { return true; }

        [Authorize(Policy = RestAPIPolicies.CanViewInvoices,
            AuthenticationSchemes = AuthenticationSchemes.OpenId)]
        [HttpGet(nameof(ScopeCanViewInvoices))]
        public bool ScopeCanViewInvoices() { return true; }

        [Authorize(Policy = RestAPIPolicies.CanCreateInvoices,
            AuthenticationSchemes = AuthenticationSchemes.OpenId)]
        [HttpGet(nameof(ScopeCanCreateInvoices))]
        public bool ScopeCanCreateInvoices() { return true; }

        [Authorize(Policy = RestAPIPolicies.CanManageInvoices,
            AuthenticationSchemes = AuthenticationSchemes.OpenId)]
        [HttpGet(nameof(ScopeCanManageInvoices))]
        public bool ScopeCanManageInvoices() { return true; }

        [Authorize(Policy = RestAPIPolicies.CanManageApps,
            AuthenticationSchemes = AuthenticationSchemes.OpenId)]
        [HttpGet(nameof(ScopeCanManageApps))]
        public bool ScopeCanManageApps() { return true; }

        [Authorize(Policy = RestAPIPolicies.CanViewApps,
            AuthenticationSchemes = AuthenticationSchemes.OpenId)]
        
        [HttpGet(nameof(ScopeCanViewApps))]
        public bool ScopeCanViewApps() { return true; }

        [Authorize(Policy = RestAPIPolicies.CanManageWallet,
            AuthenticationSchemes = AuthenticationSchemes.OpenId)]
        [HttpGet(nameof(ScopeCanManageWallet))]
        public bool ScopeCanManageWallet() { return true; }

        [Authorize(Policy = RestAPIPolicies.CanViewProfile,
            AuthenticationSchemes = AuthenticationSchemes.OpenId)]
        [HttpGet(nameof(ScopeCanViewProfile))]
        public bool ScopeCanViewProfile() { return true; }

#endregion
    }
}
