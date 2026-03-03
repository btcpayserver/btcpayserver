using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Security;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace BTCPayServer.Plugins.Bitpay.Security;

public class BitpayAuthorizationHandler(
    IHttpContextAccessor httpContextAccessor,
    StoreRepository storeRepository,
    TokenRepository tokenRepository)
    : AuthorizationHandler<PolicyRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PolicyRequirement requirement)
    {
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
            storeId = httpContextAccessor.HttpContext.GetImplicitStoreId();
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
