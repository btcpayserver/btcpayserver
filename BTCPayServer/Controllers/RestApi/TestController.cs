using System.Threading.Tasks;
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
            return  User.IsInRole(Roles.ServerAdmin);
        }
        [HttpGet("me/stores")]
        public async Task<StoreData[]> GetCurrentUserStores()
        {
            return await _storeRepository.GetStoresByUserId(_userManager.GetUserId(User));
        }


        [HttpGet("me/stores/{storeId}/can-edit")]
        [Authorize(Policy = Policies.CanModifyStoreSettings.Key, AuthenticationSchemes = OpenIddictValidationDefaults.AuthenticationScheme)]
        public bool CanEdit(string storeId)
        {
            return true;
        }
    }
}
