using System.Security.Claims;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace BTCPayServer.Services.Stores;

public class OnChainWalletSettingsAuthorization(
    IAuthorizationService authorizationService,
    StoreRepository storeRepository)
{
    public async Task<bool> CanManageOnChainWalletSettings(ClaimsPrincipal user, string storeId, string cryptoCode)
    {
        if ((await authorizationService.AuthorizeAsync(user, storeId, Policies.CanManageWallets)).Succeeded)
            return true;

        return (await authorizationService.AuthorizeAsync(user, storeId, Policies.CanManageWalletSettings)).Succeeded;
    }

    public async Task<bool> AuthorizeOnChainWalletSettings(HttpContext httpContext, ClaimsPrincipal user, string storeId, string cryptoCode)
    {
        if (!await CanManageOnChainWalletSettings(user, storeId, cryptoCode))
            return false;

        await EnsureStoreContext(httpContext, user, storeId);
        return true;
    }

    public async Task EnsureStoreContext(HttpContext httpContext, ClaimsPrincipal user, string storeId)
    {
        var currentStore = httpContext.GetStoreDataOrNull();
        if (currentStore?.Id == storeId)
            return;

        var store = await storeRepository.FindStore(storeId, user, true);
        if (store is not null)
        {
            httpContext.SetStoreData(store);
            httpContext.SetNavStoreData(store);
        }
    }
}
