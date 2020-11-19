using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

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
                case Policies.CanModifyServerSettings:
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


            var store = await _storeRepository.FindStore(storeId, userid);
            if (store == null)
                return;
            bool success = false;
            switch (requirement.Policy)
            {
                case Policies.CanModifyStoreSettings:
                    if (store.Role == StoreRoles.Owner || isAdmin)
                        success = true;
                    break;
                case Policies.CanCreateInvoice:
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
