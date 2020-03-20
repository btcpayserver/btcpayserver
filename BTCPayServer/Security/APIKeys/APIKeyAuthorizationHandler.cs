using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace BTCPayServer.Security.APIKeys
{
    public class APIKeyAuthorizationHandler : AuthorizationHandler<PolicyRequirement>

    {
        private readonly HttpContext _HttpContext;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly StoreRepository _storeRepository;

        public APIKeyAuthorizationHandler(IHttpContextAccessor httpContextAccessor,
            UserManager<ApplicationUser> userManager,
            StoreRepository storeRepository)
        {
            _HttpContext = httpContextAccessor.HttpContext;
            _userManager = userManager;
            _storeRepository = storeRepository;
        }

        protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context,
            PolicyRequirement requirement)
        {
            if (context.User.Identity.AuthenticationType != APIKeyConstants.AuthenticationType)
                return;

            bool success = false;
            switch (requirement.Policy)
            {
                case Policies.CanModifyProfile:
                case Policies.CanViewProfile:
                    success = context.HasPermission(Permission.Create(requirement.Policy));
                    break;

                case Policies.CanViewStoreSettings:
                case Policies.CanModifyStoreSettings:
                    var storeId = _HttpContext.GetImplicitStoreId();
                    var userid = _userManager.GetUserId(context.User);
                    // Specific store action
                    if (storeId != null)
                    {
                        if (context.HasPermission(Permission.Create(requirement.Policy, storeId)))
                        {
                            if (string.IsNullOrEmpty(userid))
                                break;
                            var store = await _storeRepository.FindStore((string)storeId, userid);
                            if (store == null)
                                break;
                            success = true;
                            _HttpContext.SetStoreData(store);
                        }
                    }
                    else
                    {
                        var stores = await _storeRepository.GetStoresByUserId(userid);
                        List<StoreData> permissionedStores = new List<StoreData>();
                        foreach (var store in stores)
                        {
                            if (context.HasPermission(Permission.Create(requirement.Policy, store.Id)))
                                permissionedStores.Add(store);
                        }
                        _HttpContext.SetStoresData(stores.ToArray());
                        success = true;
                    }
                    break;
                case Policies.CanCreateUser:
                case Policies.CanModifyServerSettings:
                    if (context.HasPermission(Permission.Create(requirement.Policy)))
                    {
                        var user = await _userManager.GetUserAsync(context.User);
                        if (user == null)
                            break;
                        if (!await _userManager.IsInRoleAsync(user, Roles.ServerAdmin))
                            break;
                        success = true;
                    }
                    break;
            }

            if (success)
            {
                context.Succeed(requirement);
            }
        }
    }
}
