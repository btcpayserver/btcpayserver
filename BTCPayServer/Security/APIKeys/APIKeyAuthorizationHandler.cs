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
                case Policies.CanModifyProfile.Key:
                    success = context.HasPermissions(Permissions.ProfileManagement);
                    break;
                case Policies.CanListStoreSettings.Key:
                    var selectiveStorePermissions =
                        Permissions.ExtractStorePermissionsIds(context.GetPermissions());
                    success = context.HasPermissions(Permissions.StoreManagement) ||
                              selectiveStorePermissions.Any();
                    break;
                case Policies.CanModifyStoreSettings.Key:
                    string storeId = _HttpContext.GetImplicitStoreId();
                    if (!context.HasPermissions(Permissions.StoreManagement) &&
                        !context.HasPermissions(Permissions.GetStorePermission(storeId)))
                        break;

                    if (storeId == null)
                    {
                        success = true;
                    }
                    else
                    {
                        var userid = _userManager.GetUserId(context.User);
                        if (string.IsNullOrEmpty(userid))
                            break;
                        var store = await _storeRepository.FindStore((string)storeId, userid);
                        if (store == null)
                            break;
                        success = true;
                        _HttpContext.SetStoreData(store);
                    }

                    break;
                case Policies.CanCreateUser.Key:
                case Policies.CanModifyServerSettings.Key:
                    if (!context.HasPermissions(Permissions.ServerManagement))
                        break;
                    // For this authorization, we still check in database because it is super sensitive.
                    success = await IsUserAdmin(context.User);
                    break;
            }

            //if you do not have the specific permissions, BUT you have server management, we enable god mode 
            if (!success && context.HasPermissions(Permissions.ServerManagement) &&
                requirement.Policy != Policies.CanModifyServerSettings.Key)
            {
                success = await IsUserAdmin(context.User);
            }

            if (success)
            {
                context.Succeed(requirement);
            }
        }

        private async Task<bool> IsUserAdmin(ClaimsPrincipal contextUser)
        {
            var user = await _userManager.GetUserAsync(contextUser);
            if (user == null)
                return false;
            if (!await _userManager.IsInRoleAsync(user, Roles.ServerAdmin))
                return false;
            return true;
        }
    }
}
