using System.Threading.Tasks;
using BTCPayServer.Client;

namespace BTCPayServer.Controllers;

public partial class UIStoresController
{
    private async Task<bool> AuthorizeLightningWalletSettingsAsync(string storeId)
    {
        if ((await _authorizationService.AuthorizeAsync(User, storeId, Policies.CanManageWallets)).Succeeded)
        {
            await EnsureStoreContextAsync(storeId);
            return true;
        }
        foreach (var policy in new[] { Policies.CanModifyBitcoinLightning, Policies.CanManageWalletSettings })
        {
            if (!(await _authorizationService.AuthorizeAsync(User, storeId, policy)).Succeeded)
                return false;
        }
        await EnsureStoreContextAsync(storeId);
        return true;
    }

    private async Task EnsureStoreContextAsync(string storeId)
    {
        var currentStore = HttpContext.GetStoreDataOrNull();
        if (currentStore?.Id == storeId)
            return;

        var store = await _storeRepo.FindStore(storeId);
        if (store != null)
            HttpContext.SetStoreData(store);
    }
}
