using System.Threading.Tasks;
using BTCPayServer.Client;

namespace BTCPayServer.Controllers;

public partial class UIStoresController
{
    private async Task<bool> AuthorizeLightningWalletSettingsAsync(string storeId)
    {
        if ((await _authorizationService.AuthorizeAsync(User, storeId, Policies.CanManageWallets)).Succeeded)
        {
            if (HttpContext.GetStoreData() is null)
            {
                var store = await _storeRepo.FindStore(storeId);
                if (store != null)
                {
                    HttpContext.SetStoreData(store);
                }
            }
            return true;
        }
        foreach (var policy in new[] { Policies.CanModifyBitcoinLightning, Policies.CanManageWalletSettings })
        {
            if (!(await _authorizationService.AuthorizeAsync(User, storeId, policy)).Succeeded)
                return false;
        }
        if (HttpContext.GetStoreData() is null)
        {
            var store = await _storeRepo.FindStore(storeId);
            if (store != null)
            {
                HttpContext.SetStoreData(store);
            }
        }
        return true;
    }
}
