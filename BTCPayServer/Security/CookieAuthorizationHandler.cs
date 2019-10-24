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

namespace BTCPayServer.Security
{
    public class CookieAuthorizationHandler : AuthorizationHandler<PolicyRequirement>
    {
        private readonly HttpContext _HttpContext;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly StoreRepository _storeRepository;

        public CookieAuthorizationHandler(IHttpContextAccessor httpContextAccessor,
                                UserManager<ApplicationUser> userManager,
                                StoreRepository storeRepository)
        {
            _HttpContext = httpContextAccessor.HttpContext;
            _userManager = userManager;
            _storeRepository = storeRepository;
        }
        protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PolicyRequirement requirement)
        {
            if (context.User.Identity.AuthenticationType != AuthenticationSchemes.Cookie)
                return;

            var isAdmin = context.User.IsInRole(Roles.ServerAdmin);
            switch (requirement.Policy)
            {
                case Policies.CanModifyServerSettings.Key:
                    if (isAdmin)
                        context.Succeed(requirement);
                    return;
            }

            string storeId = _HttpContext.GetImplicitStoreId();
            if (storeId == null)
                return;

            var userid = _userManager.GetUserId(context.User);
            if (string.IsNullOrEmpty(userid))
                return;


            var store = await _storeRepository.FindStore((string)storeId, userid);
            if (store == null)
                return;
            bool success = false;
            switch (requirement.Policy)
            {
                case Policies.CanModifyStoreSettings.Key:
                    if (store.Role == StoreRoles.Owner || isAdmin)
                        success = true;
                    break;
                case Policies.CanCreateInvoice.Key:
                    if (store.Role == StoreRoles.Owner ||
                        store.Role == StoreRoles.Guest ||
                        isAdmin ||
                        store.GetStoreBlob().AnyoneCanInvoice)
                        success = true;
                    break;
            }

            if (success)
            {
                context.Succeed(requirement);
                _HttpContext.SetStoreData(store);
                return;
            }
        }
    }
}
