using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Security;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BTCPayServer.Plugins.Bitpay.Security;

public class BitpayAuthorizationHandler(
    IHttpContextAccessor httpContextAccessor,
    StoreRepository storeRepository,
    TokenRepository tokenRepository)
    : AuthorizationHandler<PolicyRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PolicyRequirement requirement)
    {
        if (httpContextAccessor.HttpContext is null)
            return;
        var httpContext = httpContextAccessor.HttpContext;
        string storeId = null;
        if (context.User.Identity is { AuthenticationType: BitpayAuthenticationTypes.ApiKeyAuthentication })
        {
            storeId = context.User.Claims.Where(c => c.Type == BitpayClaims.ApiKeyStoreId).Select(c => c.Value).First();
        }
        else if (context.User.Identity is { AuthenticationType: BitpayAuthenticationTypes.SinAuthentication })
        {
            var sin = context.User.Claims.Where(c => c.Type == BitpayClaims.SIN).Select(c => c.Value).First();
            var bitToken = (await tokenRepository.GetTokens(sin)).FirstOrDefault();
            storeId = bitToken?.StoreId;
        }
        else if (context.User.Identity is { AuthenticationType: BitpayAuthenticationTypes.Anonymous })
        {
            if (httpContext.GetRouteData().Values.TryGetValue("storeId", out var v))
                storeId = v as string;

            if (storeId == null)
            {
                if (httpContext.Request.Query.TryGetValue("storeId", out var sv))
                {
                    storeId = sv.FirstOrDefault();
                }
            }
        }

        if (storeId == null)
            return;
        var store = await storeRepository.FindStore(storeId);
        if (store == null)
            return;
        var isAnonymous = context.User.Identity.AuthenticationType == BitpayAuthenticationTypes.Anonymous;
        var anyoneCanInvoice = store.GetStoreBlob().AnyoneCanInvoice;
        switch (requirement.Policy)
        {
            case Policies.CanCreateInvoice:
                if (!isAnonymous || anyoneCanInvoice)
                {
                    context.Succeed(requirement);
                    httpContextAccessor.HttpContext.SetStoreData(store);
                }

                break;
            case ServerPolicies.CanGetRates.Key:
                context.Succeed(requirement);
                httpContextAccessor.HttpContext.SetStoreData(store);
                break;
        }
    }
}
