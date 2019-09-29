using System.Threading.Tasks;
using BTCPayServer.Authentication;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Security;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Validation;

namespace BTCPayServer.Controllers.RestApi
{
    /// <summary>
    /// this controller serves as a testing endpoint for our OpenId unit tests
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(AuthenticationSchemes = OpenIddictValidationDefaults.AuthenticationScheme)]
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
        public bool AmIAnAdmin()
        {
            return User.IsInRole(Roles.ServerAdmin);
        }

        [HttpGet("me/stores")]
        public async Task<StoreData[]> GetCurrentUserStores()
        {
            return await _storeRepository.GetStoresByUserId(_userManager.GetUserId(User));
        }


        [HttpGet("me/stores/{storeId}/can-edit")]
        [Authorize(Policy = Policies.CanModifyStoreSettings.Key,
            AuthenticationSchemes = OpenIddictValidationDefaults.AuthenticationScheme)]
        public bool CanEdit(string storeId)
        {
            return true;
        }


        #region scopes tests

        [Authorize(Policy = RestAPIPolicies.CanViewStores,
            AuthenticationSchemes = OpenIddictValidationDefaults.AuthenticationScheme)]
        [HttpGet(nameof(ScopeCanViewStores))]
        public bool ScopeCanViewStores() { return true; }

        [Authorize(Policy = RestAPIPolicies.CanManageStores,
            AuthenticationSchemes = OpenIddictValidationDefaults.AuthenticationScheme)]
        [HttpGet(nameof(ScopeCanManageStores))]
        public bool ScopeCanManageStores() { return true; }

        [Authorize(Policy = RestAPIPolicies.CanViewInvoices,
            AuthenticationSchemes = OpenIddictValidationDefaults.AuthenticationScheme)]
        [HttpGet(nameof(ScopeCanViewInvoices))]
        public bool ScopeCanViewInvoices() { return true; }

        [Authorize(Policy = RestAPIPolicies.CanCreateInvoices,
            AuthenticationSchemes = OpenIddictValidationDefaults.AuthenticationScheme)]
        [HttpGet(nameof(ScopeCanCreateInvoices))]
        public bool ScopeCanCreateInvoices() { return true; }

        [Authorize(Policy = RestAPIPolicies.CanManageInvoices,
            AuthenticationSchemes = OpenIddictValidationDefaults.AuthenticationScheme)]
        [HttpGet(nameof(ScopeCanManageInvoices))]
        public bool ScopeCanManageInvoices() { return true; }

        [Authorize(Policy = RestAPIPolicies.CanManageApps,
            AuthenticationSchemes = OpenIddictValidationDefaults.AuthenticationScheme)]
        [HttpGet(nameof(ScopeCanManageApps))]
        public bool ScopeCanManageApps() { return true; }

        [Authorize(Policy = RestAPIPolicies.CanViewApps,
            AuthenticationSchemes = OpenIddictValidationDefaults.AuthenticationScheme)]
        
        [HttpGet(nameof(ScopeCanViewApps))]
        public bool ScopeCanViewApps() { return true; }

        [Authorize(Policy = RestAPIPolicies.CanManageWallet,
            AuthenticationSchemes = OpenIddictValidationDefaults.AuthenticationScheme)]
        [HttpGet(nameof(ScopeCanManageWallet))]
        public bool ScopeCanManageWallet() { return true; }

        [Authorize(Policy = RestAPIPolicies.CanViewProfile,
            AuthenticationSchemes = OpenIddictValidationDefaults.AuthenticationScheme)]
        
        [HttpGet(nameof(ScopeCanViewProfile))]
        public bool ScopeCanViewProfile() { return true; }

        #endregion
    }
}
