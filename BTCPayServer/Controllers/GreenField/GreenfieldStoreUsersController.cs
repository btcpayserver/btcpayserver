using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Controllers.Greenfield
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [EnableCors(CorsPolicies.All)]
    public class GreenfieldStoreUsersController : ControllerBase
    {
        private readonly StoreRepository _storeRepository;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly UriResolver _uriResolver;

        public GreenfieldStoreUsersController(
            StoreRepository storeRepository,
            UserManager<ApplicationUser> userManager,
            UriResolver uriResolver)
        {
            _storeRepository = storeRepository;
            _userManager = userManager;
            _uriResolver = uriResolver;
        }

        [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/users")]
        public async Task<IActionResult> GetStoreUsers()
        {
            var store = HttpContext.GetStoreData();
            return store == null ? StoreNotFound() : Ok(await FromModel(store));
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpDelete("~/api/v1/stores/{storeId}/users/{idOrEmail}")]
        public async Task<IActionResult> RemoveStoreUser(string storeId, string idOrEmail)
        {
            var store = HttpContext.GetStoreData();
            if (store == null) return StoreNotFound();

            var user = await _userManager.FindByIdOrEmail(idOrEmail);
            if (user == null) return UserNotFound();
            
            return await _storeRepository.RemoveStoreUser(storeId, user.Id)
                ? Ok()
                : this.CreateAPIError(409, "store-user-role-orphaned", "Removing this user would result in the store having no owner.");
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/stores/{storeId}/users")]
        [HttpPut("~/api/v1/stores/{storeId}/users/{idOrEmail?}")]
        public async Task<IActionResult> AddOrUpdateStoreUser(string storeId, StoreUserData request, string idOrEmail = null)
        {
            var store = HttpContext.GetStoreData();
            if (store == null) return StoreNotFound();

            var user = await _userManager.FindByIdOrEmail(idOrEmail ?? request.UserId);
            if (user == null) return UserNotFound();
            
            StoreRoleId roleId = null;
            if (request.Role is not null)
            {
                roleId = await _storeRepository.ResolveStoreRoleId(storeId, request.Role);
                if (roleId is null)
                    ModelState.AddModelError(nameof(request.Role), "The role id provided does not exist");
            }
            
            if (!ModelState.IsValid)
                return this.CreateValidationError(ModelState);

            var result = string.IsNullOrEmpty(idOrEmail)
                ? await _storeRepository.AddStoreUser(storeId, user.Id, roleId)
                : await _storeRepository.AddOrUpdateStoreUser(storeId, user.Id, roleId);
            return result
                ? Ok()
                : this.CreateAPIError(409, "duplicate-store-user-role", "The user is already added to the store");
        }

        private async Task<IEnumerable<StoreUserData>> FromModel(StoreData data)
        {
            var storeUsers = new List<StoreUserData>();
            foreach (var storeUser in data.UserStores)
            {
                var user = await _userManager.FindByIdOrEmail(storeUser.ApplicationUserId);
                var blob = user?.GetBlob();
                storeUsers.Add(new StoreUserData
                {
                    UserId = storeUser.ApplicationUserId,
                    Role = storeUser.StoreRoleId,
                    Email = user?.Email,
                    Name = blob?.Name,
                    ImageUrl = blob?.ImageUrl == null ? null : await _uriResolver.Resolve(Request.GetAbsoluteRootUri(), UnresolvedUri.Create(blob.ImageUrl))
                });
            }
            return storeUsers;
        }

        private IActionResult StoreNotFound()
        {
            return this.CreateAPIError(404, "store-not-found", "The store was not found");
        }

        private IActionResult UserNotFound()
        {
            return this.CreateAPIError(404, "user-not-found", "The user was not found");
        }
    }
}
