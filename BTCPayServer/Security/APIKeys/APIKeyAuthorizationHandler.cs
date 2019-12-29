using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Internal;

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
                case Policies.CanListStoreSettings.Key:
                    var selectiveStorePermissions =
                        APIKeyConstants.Permissions.ExtractStorePermissionsIds(context.GetPermissions());
                    if (context.HasPermissions(APIKeyConstants.Permissions.StoreManagement) || selectiveStorePermissions.Any())
                        success = true;
                    else
                    {
                        success = false;
                    }
                    break;
                case Policies.CanModifyStoreSettings.Key:
                    string storeId = _HttpContext.GetImplicitStoreId();
                    if (!context.HasPermissions(APIKeyConstants.Permissions.StoreManagement) &&
                        !context.HasPermissions(APIKeyConstants.Permissions.GetStorePermission(storeId)))
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
                case Policies.CanModifyServerSettings.Key:
                    if (!context.HasPermissions(APIKeyConstants.Permissions.ServerManagement))
                        break;
                    // For this authorization, we stil check in database because it is super sensitive.
                    var user = await _userManager.GetUserAsync(context.User);
                    if (user == null)
                        break;
                    if (!await _userManager.IsInRoleAsync(user, Roles.ServerAdmin))
                        break;
                    success = true;
                    break;
            }

            if (success)
            {
                context.Succeed(requirement);
            }
        }
    }
}
