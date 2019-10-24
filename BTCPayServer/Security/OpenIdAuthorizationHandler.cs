using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using BTCPayServer.Data;
using BTCPayServer.Services.Stores;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Primitives;
using static BTCPayServer.Security.OpenId.RestAPIPolicies;
using OpenIddict.Abstractions;
using BTCPayServer.Security.OpenId;

namespace BTCPayServer.Security
{
    public class OpenIdAuthorizationHandler : AuthorizationHandler<PolicyRequirement>
    {
        private readonly HttpContext _HttpContext;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly StoreRepository _storeRepository;

        public OpenIdAuthorizationHandler(IHttpContextAccessor httpContextAccessor,
                                UserManager<ApplicationUser> userManager,
                                StoreRepository storeRepository)
        {
            _HttpContext = httpContextAccessor.HttpContext;
            _userManager = userManager;
            _storeRepository = storeRepository;
        }
        protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PolicyRequirement requirement)
        {
            if (context.User.Identity.AuthenticationType != "AuthenticationTypes.Federation")
                return;

            bool success = false;
            switch (requirement.Policy)
            {
                case Policies.CanModifyStoreSettings.Key:
                    if (!context.HasScopes(BTCPayScopes.StoreManagement))
                        break;
                    // TODO: It should be possible to grant permission to a specific store
                    // we can do this by adding saving a claim with the specific store id
                    // to the access_token
                    string storeId = _HttpContext.GetImplicitStoreId();
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
                    if (!context.HasScopes(BTCPayScopes.ServerManagement))
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
