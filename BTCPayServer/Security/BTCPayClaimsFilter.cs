using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using BTCPayServer.Models;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Security
{

    public class BTCPayClaimsFilter : IAsyncAuthorizationFilter, IConfigureOptions<MvcOptions>
    {
        UserManager<ApplicationUser> _UserManager;
        StoreRepository _StoreRepository;
        public BTCPayClaimsFilter(
            UserManager<ApplicationUser> userManager,
            StoreRepository storeRepository)
        {
            _UserManager = userManager;
            _StoreRepository = storeRepository;
        }

        void IConfigureOptions<MvcOptions>.Configure(MvcOptions options)
        {
            options.Filters.Add(typeof(BTCPayClaimsFilter));
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var principal = context.HttpContext.User;
            if (!context.HttpContext.GetIsBitpayAPI())
            {
                var identity = ((ClaimsIdentity)principal.Identity);
                if (principal.IsInRole(Roles.ServerAdmin))
                {
                    identity.AddClaim(new Claim(Policies.CanModifyServerSettings.Key, "true"));
                }
                if (context.RouteData.Values.TryGetValue("storeId", out var storeId))
                {
                    var claim = identity.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier);
                    if (claim != null)
                    {
                        var store = await _StoreRepository.FindStore((string)storeId, claim.Value);
                        if (store == null)
                            context.Result = new ChallengeResult(Policies.CookieAuthentication);
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
}
