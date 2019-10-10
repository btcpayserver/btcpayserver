using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using Microsoft.AspNetCore.Http;
using BTCPayServer.Data;
using BTCPayServer.Services.Stores;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Authentication;
using BTCPayServer.Authentication;

namespace BTCPayServer.Security
{
    public class ClaimTransformer : IClaimsTransformation
    {
        private readonly HttpContext _HttpContext;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly StoreRepository _storeRepository;

        public ClaimTransformer(IHttpContextAccessor httpContextAccessor,
                                UserManager<ApplicationUser> userManager,
                                StoreRepository storeRepository)
        {
            _HttpContext = httpContextAccessor.HttpContext;
            _userManager = userManager;
            _storeRepository = storeRepository;
        }
        public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            var routeData = _HttpContext.GetRouteData();
            if (routeData == null)
                return principal;
            var identity = ((ClaimsIdentity)principal.Identity);
            // A ClaimTransform can be called several time, we prevent dups by removing all the
            // claims this transform might add.
            var claims = new[] { RestAPIPolicies.CanEditStore };
            foreach (var claim in identity.Claims.Where(c => claims.Contains(c.Type)).ToList())
            {
                identity.RemoveClaim(claim);
            }

            if (!routeData.Values.TryGetValue("storeId", out var storeId))
            {
                return principal;
            }
            var userid = _userManager.GetUserId(principal);
            if (!string.IsNullOrEmpty(userid))
            {
                var store = await _storeRepository.FindStore((string)storeId, userid);
                if (store != null)
                {
                    _HttpContext.SetStoreData(store);
                    foreach (var claim in store.GetClaims())
                    {
                        if (claim.Type.Equals(Policies.CanModifyStoreSettings.Key, System.StringComparison.OrdinalIgnoreCase))
                        {
                            identity.AddClaim(new Claim(RestAPIPolicies.CanEditStore, store.Id));
                        }
                    }
                }
            }
            return principal;
        }
    }
}
