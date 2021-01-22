using System.Security.Claims;
using System.Threading.Tasks;
using BTCPayServer.Client;
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
            var isAdmin = (await authorizationService.AuthorizeAsync(user, Policies.CanModifyServerSettings))
                .Succeeded;
            if (isAdmin)
                return (true, true);
            var policies = policiesSettings;
            var hotWallet = policies?.AllowHotWalletForAll is true;
            return (hotWallet, hotWallet && policies?.AllowHotWalletRPCImportForAll is true);
        }
    }
}
