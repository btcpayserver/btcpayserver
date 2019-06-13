using System.Security.Claims;
using System.Threading.Tasks;
using BTCPayServer.Models;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Security
{

    public class BTCPayClaimsFilter : IAsyncAuthorizationFilter, IConfigureOptions<MvcOptions>
    {
        UserManager<ApplicationUser> _userManager;
        StoreRepository _StoreRepository;
        public BTCPayClaimsFilter(
            UserManager<ApplicationUser> userManager,
            StoreRepository storeRepository)
        {
            _userManager = userManager;
            _StoreRepository = storeRepository;
        }

        void IConfigureOptions<MvcOptions>.Configure(MvcOptions options)
        {
            options.Filters.Add(typeof(BTCPayClaimsFilter));
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            if (context.HttpContext.User?.Identity?.AuthenticationType != Policies.CookieAuthentication)
                return;
            var principal = context.HttpContext.User;
            var identity = ((ClaimsIdentity)principal.Identity);
            if (principal.IsInRole(Roles.ServerAdmin))
            {
                identity.AddClaim(new Claim(Policies.CanModifyServerSettings.Key, "true"));
            }

            if (context.RouteData.Values.TryGetValue("storeId", out var storeId))
            {
                var userid = _userManager.GetUserId(principal);

                if (!string.IsNullOrEmpty(userid))
                {
                    var store = await _StoreRepository.FindStore((string)storeId, userid);
                    if (store == null)
                    {
                        context.Result = new ChallengeResult();
                    }
                    else
                    {
                        context.HttpContext.SetStoreData(store);
                        if (store != null)
                        {
                            identity.AddClaims(store.GetClaims());
                        }
                    }
                }
            }
        }
    }
}
