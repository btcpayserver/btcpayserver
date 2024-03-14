using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers.Greenfield
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [EnableCors(CorsPolicies.All)]
    public class GreenfieldStoreUsersController : ControllerBase
    {
        private readonly StoreRepository _storeRepository;
        private readonly UserManager<ApplicationUser> _userManager;

        public GreenfieldStoreUsersController(StoreRepository storeRepository, UserManager<ApplicationUser> userManager)
        {
            _storeRepository = storeRepository;
            _userManager = userManager;
        }

        [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/users")]
        public IActionResult GetStoreUsers()
        {
            var store = HttpContext.GetStoreData();
            return store == null ? StoreNotFound() : Ok(FromModel(store));
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpDelete("~/api/v1/stores/{storeId}/users/{idOrEmail}")]
        public async Task<IActionResult> RemoveStoreUser(string storeId, string idOrEmail)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
            {
                return StoreNotFound();
            }

            var userId = await _userManager.FindByIdOrEmail(idOrEmail);
            if (userId != null && await _storeRepository.RemoveStoreUser(storeId, idOrEmail))
            {
                return Ok();
            }

            return this.CreateAPIError(409, "store-user-role-orphaned", "Removing this user would result in the store having no owner.");
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/stores/{storeId}/users")]
        public async Task<IActionResult> AddStoreUser(string storeId, StoreUserData request)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
            {
                return StoreNotFound();
            }
            StoreRoleId roleId = null;

            if (request.Role is not null)
            {
                roleId = await _storeRepository.ResolveStoreRoleId(storeId, request.Role);
                if (roleId is null)
                    ModelState.AddModelError(nameof(request.Role), "The role id provided does not exist");
            }
            
            if (!ModelState.IsValid)
                return this.CreateValidationError(ModelState);

            if (await _storeRepository.AddStoreUser(storeId, request.UserId, roleId))
            {
                return Ok();
            }

            return this.CreateAPIError(409, "duplicate-store-user-role", "The user is already added to the store");
        }

        private IEnumerable<StoreUserData> FromModel(Data.StoreData data)
        {
            return data.UserStores.Select(store => new StoreUserData() { UserId = store.ApplicationUserId, Role = store.StoreRoleId });
        }
        private IActionResult StoreNotFound()
        {
            return this.CreateAPIError(404, "store-not-found", "The store was not found");
        }
    }
}
