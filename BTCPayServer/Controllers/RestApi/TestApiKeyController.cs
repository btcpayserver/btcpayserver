using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Security;
using BTCPayServer.Security.APIKeys;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers.RestApi
{
    /// <summary>
    /// this controller serves as a testing endpoint for our api key unit tests
    /// </summary>
    [Route("api/test/apikey")]
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.ApiKey)]
    public class TestApiKeyController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly StoreRepository _storeRepository;

        public TestApiKeyController(UserManager<ApplicationUser> userManager, StoreRepository storeRepository)
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
        [Authorize(Policy = Policies.CanModifyServerSettings.Key, AuthenticationSchemes = AuthenticationSchemes.ApiKey)]
        public bool AmIAnAdmin()
        {
            return true;
        }

        [HttpGet("me/stores")]
        [Authorize(Policy = Policies.CanListStoreSettings.Key,
            AuthenticationSchemes = AuthenticationSchemes.ApiKey)]
        public async Task<StoreData[]> GetCurrentUserStores()
        {
            return await  User.GetStores(_userManager, _storeRepository);
        }
        
        [HttpGet("me/stores/actions")]
        [Authorize(Policy = Policies.CanModifyStoreSettings.Key,
            AuthenticationSchemes = AuthenticationSchemes.ApiKey)]
        public bool CanDoNonImplicitStoreActions()
        {
            return true;
        }


        [HttpGet("me/stores/{storeId}/can-edit")]
        [Authorize(Policy = Policies.CanModifyStoreSettings.Key,
            AuthenticationSchemes = AuthenticationSchemes.ApiKey)]
        public bool CanEdit(string storeId)
        {
            return true;
        }
    }
}
