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

        public GreenfieldStoreUsersController(StoreRepository storeRepository, UserManager<ApplicationUser> userManager)
        {
            _storeRepository = storeRepository;
        }
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/users")]
        public IActionResult GetStoreUsers()
        {

            var store = HttpContext.GetStoreData();
            return store == null ? StoreNotFound() : Ok(FromModel(store));
        }
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpDelete("~/api/v1/stores/{storeId}/users/{userId}")]
        public async Task<IActionResult> RemoveStoreUser(string storeId, string userId)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
            {
                return StoreNotFound();
            }

            if (await _storeRepository.RemoveStoreUser(storeId, userId))
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
            //we do not need to validate the role string as any value other than `StoreRoles.Owner` is currently treated like a guest
            if (await _storeRepository.AddStoreUser(storeId, request.UserId, request.Role))
            {
                return Ok();
            }

            return this.CreateAPIError(409, "duplicate-store-user-role", "The user is already added to the store");
        }

        private IEnumerable<StoreUserData> FromModel(Data.StoreData data)
        {
            return data.UserStores.Select(store => new StoreUserData() { UserId = store.ApplicationUserId, Role = store.Role });
        }
        private IActionResult StoreNotFound()
        {
            return this.CreateAPIError(404, "store-not-found", "The store was not found");
        }
    }
}
