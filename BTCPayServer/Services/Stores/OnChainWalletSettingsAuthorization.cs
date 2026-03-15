using System;
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

        var walletTypePolicy = cryptoCode.Equals("BTC", StringComparison.OrdinalIgnoreCase)
            ? Policies.CanModifyBitcoinOnchain
            : Policies.CanModifyOtherWallets;
        foreach (var policy in new[] { walletTypePolicy, Policies.CanManageWalletSettings })
        {
            if (!(await authorizationService.AuthorizeAsync(user, storeId, policy)).Succeeded)
                return false;
        }

        return true;
    }

    public async Task<bool> AuthorizeOnChainWalletSettings(HttpContext httpContext, ClaimsPrincipal user, string storeId, string cryptoCode)
    {
        if (!await CanManageOnChainWalletSettings(user, storeId, cryptoCode))
            return false;

        await EnsureStoreContext(httpContext, storeId);
        return true;
    }

    public async Task EnsureStoreContext(HttpContext httpContext, string storeId)
    {
        var currentStore = httpContext.GetStoreDataOrNull();
        if (currentStore?.Id == storeId)
            return;

        var store = await storeRepository.FindStore(storeId);
        if (store is not null)
            httpContext.SetStoreData(store);
    }
}
