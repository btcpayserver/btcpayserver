using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Security.Greenfield
{
    public class GreenfieldAuthorizationHandler : AuthorizationHandler<PolicyRequirement>
    {
        private readonly HttpContext _httpContext;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly StoreRepository _storeRepository;
        private readonly IPluginHookService _pluginHookService;

        public GreenfieldAuthorizationHandler(IHttpContextAccessor httpContextAccessor,
            UserManager<ApplicationUser> userManager,
            StoreRepository storeRepository,
            IPluginHookService pluginHookService)
        {
            _httpContext = httpContextAccessor.HttpContext;
            _userManager = userManager;
            _storeRepository = storeRepository;
            _pluginHookService = pluginHookService;
        }

        protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context,
            PolicyRequirement requirement)
        {
            if (context.User.Identity?.AuthenticationType != GreenfieldConstants.AuthenticationType)
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
                    var storeId = requiredUnscoped ? null : (context.Resource as string ?? _httpContext.GetImplicitStoreId());
                    // Specific store action
                    if (storeId != null)
                    {
                        if (context.HasPermission(Permission.Create(policy, storeId)))
                        {
                            if (string.IsNullOrEmpty(userid))
                                break;
                            var store = await _storeRepository.FindStore(storeId, userid);
                            if (store == null)
                                break;
                            if (!store.HasPermission(userid, policy))
                                break;
                            success = true;
                            _httpContext.SetStoreData(store);
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
                            if (context.HasPermission(Permission.Create(policy, store.Id)))
                                permissionedStores.Add(store);
                        }
                        _httpContext.SetStoresData(permissionedStores.ToArray());
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
                case { } when Policies.IsPluginPolicy(requirement.Policy):
                    var handle = (AuthorizationFilterHandle)await _pluginHookService.ApplyFilter("handle-authorization-requirement",
                        new AuthorizationFilterHandle(context, requirement, _httpContext));
                    success = handle.Success;
                    break;
                case Policies.CanManageNotificationsForUser:
                case Policies.CanViewNotificationsForUser:
                case Policies.CanModifyProfile:
                case Policies.CanViewProfile:
                case Policies.CanDeleteUser:
                case Policies.Unrestricted:
                    success = context.HasPermission(Permission.Create(policy));
                    break;
            }

            if (success)
            {
                context.Succeed(requirement);
            }
            _httpContext.Items[RequestedPermissionKey] = policy;
        }
        public const string RequestedPermissionKey = nameof(RequestedPermissionKey);
    }
}
