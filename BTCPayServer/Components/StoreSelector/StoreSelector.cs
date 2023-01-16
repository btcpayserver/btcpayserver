using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Components.StoreSelector
{
    public class StoreSelector : ViewComponent
    {
        private readonly StoreRepository _storeRepo;
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly UserManager<ApplicationUser> _userManager;

        public StoreSelector(
            StoreRepository storeRepo,
            BTCPayNetworkProvider networkProvider,
            UserManager<ApplicationUser> userManager)
        {
            _storeRepo = storeRepo;
            _userManager = userManager;
            _networkProvider = networkProvider;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var userId = _userManager.GetUserId(UserClaimsPrincipal);
            var stores = await _storeRepo.GetStoresByUserId(userId);
            var currentStore = ViewContext.HttpContext.GetStoreData();
            var options = stores
                .Select(store =>
                {
                    var cryptoCode = store
                        .GetSupportedPaymentMethods(_networkProvider)
                        .OfType<DerivationSchemeSettings>()
                        .FirstOrDefault()?
                        .Network.CryptoCode;
                    var walletId = cryptoCode != null ? new WalletId(store.Id, cryptoCode) : null;
                    return new StoreSelectorOption
                    {
                        Text = store.StoreName,
                        Value = store.Id,
                        Selected = store.Id == currentStore?.Id,
                        IsOwner = store.Role == StoreRoles.Owner,
                        WalletId = walletId
                    };
                })
                .OrderBy(s => s.Text)
                .ToList();

            var blob = currentStore?.GetStoreBlob();

            var vm = new StoreSelectorViewModel
            {
                Options = options,
                CurrentStoreId = currentStore?.Id,
                CurrentDisplayName = currentStore?.StoreName,
                CurrentStoreIsOwner = currentStore?.Role == StoreRoles.Owner,
                CurrentStoreLogoFileId = blob?.LogoFileId
            };

            return View(vm);
        }
    }
}
