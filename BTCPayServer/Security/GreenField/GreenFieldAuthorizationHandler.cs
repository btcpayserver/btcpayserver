using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace BTCPayServer.Security.GreenField
{
    public class GreenFieldAuthorizationHandler : AuthorizationHandler<PolicyRequirement>

    {
        private readonly HttpContext _HttpContext;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly StoreRepository _storeRepository;

        public GreenFieldAuthorizationHandler(IHttpContextAccessor httpContextAccessor,
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
            if (context.User.Identity.AuthenticationType != GreenFieldConstants.AuthenticationType)
                return;
            var userid = _userManager.GetUserId(context.User);
            bool success = false;
            var policy = requirement.Policy;
            var requiredUnscoped = false;
            if (policy.EndsWith(':'))
            {
                policy = policy.Substring(0, policy.Length - 1);
                requiredUnscoped = true;
            }

            switch (policy)
            {
                case { } when Policies.IsStorePolicy(policy):
                    var storeId = _HttpContext.GetImplicitStoreId();
                    // Specific store action
                    if (storeId != null)
                    {
                        if (context.HasPermission(Permission.Create(policy, storeId), requiredUnscoped))
                        {
                            if (string.IsNullOrEmpty(userid))
                                break;
                            var store = await _storeRepository.FindStore(storeId, userid);
                            if (store == null)
                                break;
                            if (Policies.IsStoreModifyPolicy(policy) || policy == Policies.CanUseLightningNodeInStore)
                            {
                                if (store.Role != StoreRoles.Owner)
                                    break;
                            }
                            success = true;
                            _HttpContext.SetStoreData(store);
                        }
                    }
                    else
                    {
                        if (requiredUnscoped && !context.HasPermission(Permission.Create(policy)))
                            break;
                        var stores = await _storeRepository.GetStoresByUserId(userid);
                        List<StoreData> permissionedStores = new List<StoreData>();
                        foreach (var store in stores)
                        {
                            if (context.HasPermission(Permission.Create(policy, store.Id), requiredUnscoped))
                                permissionedStores.Add(store);
                        }
                        _HttpContext.SetStoresData(permissionedStores.ToArray());
                        success = true;
                    }
                    break;
                case { } when Policies.IsServerPolicy(policy):
                    if (context.HasPermission(Permission.Create(policy)))
                    {
                        var user = await _userManager.GetUserAsync(context.User);
                        if (user == null)
                            break;
                        if (!await _userManager.IsInRoleAsync(user, Roles.ServerAdmin))
                            break;
                        success = true;
                    }
                    break;
                case Policies.CanModifyProfile:
                case Policies.CanViewProfile:
                case Policies.Unrestricted:
                    success = context.HasPermission(Permission.Create(policy), requiredUnscoped);
                    break;
            }

            if (success)
            {
                context.Succeed(requirement);
            }
        }
    }
}
