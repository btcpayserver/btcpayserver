using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace BTCPayServer.Security.Bitpay
{
    public class BitpayAuthorizationHandler : AuthorizationHandler<PolicyRequirement>
    {
        private readonly HttpContext _HttpContext;
        private readonly StoreRepository _storeRepository;
        private readonly TokenRepository _tokenRepository;

        public BitpayAuthorizationHandler(IHttpContextAccessor httpContextAccessor,
                                StoreRepository storeRepository,
                                TokenRepository tokenRepository)
        {
            _HttpContext = httpContextAccessor.HttpContext;
            _storeRepository = storeRepository;
            _tokenRepository = tokenRepository;
        }
        protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PolicyRequirement requirement)
        {
            string storeId = null;
            if (context.User.Identity.AuthenticationType == BitpayAuthenticationTypes.ApiKeyAuthentication)
            {
                storeId = context.User.Claims.Where(c => c.Type == BitpayClaims.ApiKeyStoreId).Select(c => c.Value).First();
            }
            else if (context.User.Identity.AuthenticationType == BitpayAuthenticationTypes.SinAuthentication)
            {
                var sin = context.User.Claims.Where(c => c.Type == BitpayClaims.SIN).Select(c => c.Value).First();
                var bitToken = (await _tokenRepository.GetTokens(sin)).FirstOrDefault();
                storeId = bitToken?.StoreId;
            }
            else if (context.User.Identity.AuthenticationType == BitpayAuthenticationTypes.Anonymous)
            {
                storeId = _HttpContext.GetImplicitStoreId();
            }
            if (storeId == null)
                return;
            var store = await _storeRepository.FindStore(storeId);
            if (store == null)
                return;
            var isAnonymous = context.User.Identity.AuthenticationType == BitpayAuthenticationTypes.Anonymous;
            var anyoneCanInvoice = store.GetStoreBlob().AnyoneCanInvoice;
            switch (requirement.Policy)
            {
                case Policies.CanCreateInvoice:
                    if (!isAnonymous || (isAnonymous && anyoneCanInvoice))
                    {
                        context.Succeed(requirement);
                        _HttpContext.SetStoreData(store);
                        return;
                    }
                    break;
                case ServerPolicies.CanGetRates.Key:
                    context.Succeed(requirement);
                    _HttpContext.SetStoreData(store);
                    return;
            }
        }
    }
}
