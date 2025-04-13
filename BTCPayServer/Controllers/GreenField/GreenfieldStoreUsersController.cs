using System;
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
using Newtonsoft.Json.Linq;
using static BTCPayServer.Services.Stores.StoreRepository;
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
        private readonly CallbackGenerator _callbackGenerator;
        private readonly UriResolver _uriResolver;

        public GreenfieldStoreUsersController(
            StoreRepository storeRepository,
            UserManager<ApplicationUser> userManager,
            CallbackGenerator callbackGenerator,
            UriResolver uriResolver)
        {
            _storeRepository = storeRepository;
            _userManager = userManager;
            _callbackGenerator = callbackGenerator;
            _uriResolver = uriResolver;
        }

        [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/users")]
        public async Task<IActionResult> GetStoreUsers()
        {
            var store = HttpContext.GetStoreData();
            return store == null ? StoreNotFound() : Ok(await ToAPI(store));
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpDelete("~/api/v1/stores/{storeId}/users/{idOrEmail}")]
        public async Task<IActionResult> RemoveStoreUser(string storeId, string idOrEmail)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return StoreNotFound();

            var user = await _userManager.FindByIdOrEmail(idOrEmail);
            if (user == null)
                return UserNotFound();

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
            if (store == null)
                return StoreNotFound();

            // Deprecated properties
            request.StoreRole ??= request.AdditionalData.TryGetValue("role", out var role) ? role.ToString() : null;
            request.Id ??= request.AdditionalData.TryGetValue("userId", out var userId) ? userId.ToString() : null;

            var user = await _userManager.FindByIdOrEmail(idOrEmail ?? request.Id);
            if (user == null)
                return UserNotFound();

            StoreRoleId roleId = null;
            if (request.StoreRole is not null)
            {
                roleId = await _storeRepository.ResolveStoreRoleId(storeId, request.StoreRole);
                if (roleId is null)
                    ModelState.AddModelError(nameof(request.StoreRole), "The role id provided does not exist");
            }

            if (!ModelState.IsValid)
                return this.CreateValidationError(ModelState);

            AddOrUpdateStoreUserResult res;
            if (string.IsNullOrEmpty(idOrEmail))
            {
                res = await _storeRepository.AddStoreUser(storeId, user.Id, roleId) ? new AddOrUpdateStoreUserResult.Success() : new AddOrUpdateStoreUserResult.DuplicateRole(roleId);
            }
            else
            {
                res = await _storeRepository.AddOrUpdateStoreUser(storeId, user.Id, roleId);
            }

            return res switch
            {
                AddOrUpdateStoreUserResult.Success => Ok(),
                AddOrUpdateStoreUserResult.DuplicateRole _ => this.CreateAPIError(409, "duplicate-store-user-role", "The user is already added to the store"),
                // AddOrUpdateStoreUserResult.InvalidRole
                // AddOrUpdateStoreUserResult.LastOwner
                _ => this.CreateAPIError(409, "store-user-role-orphaned", "Removing this user would result in the store having no owner."),
            };
        }

        private async Task<IEnumerable<StoreUserData>> ToAPI(StoreData store)
        {
            var storeUsers = new List<StoreUserData>();
            foreach (var storeUser in store.UserStores)
            {
                var user = await _userManager.FindByIdOrEmail(storeUser.ApplicationUserId);
                if (user == null)
                    continue;
                var data = await UserService.ForAPI<StoreUserData>(user, [], _callbackGenerator, _uriResolver, Request);
                data.StoreRole = storeUser.StoreRoleId;

                // Deprecated properties
                data.AdditionalData["userId"] = new JValue(storeUser.ApplicationUserId);
                data.AdditionalData["role"] = new JValue(storeUser.StoreRoleId);
                /////

                storeUsers.Add(data);
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
