using System.Security.Claims;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Security.Bitpay;
using BTCPayServer.Security.GreenField;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Authorization;

namespace BTCPayServer
{
    public static class AuthorizationExtensions
    {
        public static async Task<(bool HotWallet, bool RPCImport)> CanUseHotWallet(
            this IAuthorizationService authorizationService,
            PoliciesSettings policiesSettings,
            ClaimsPrincipal user)
        {
            return (await authorizationService.AuthorizeAsync(user, Policies.CanModifyServerSettings))
                .Succeeded ? (true, true) : (policiesSettings?.AllowHotWalletForAll is true, policiesSettings?.AllowHotWalletRPCImportForAll is true);
        }
    }
}
