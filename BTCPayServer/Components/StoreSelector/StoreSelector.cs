using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client;
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
            var archivedCount = stores.Count(s => s.Archived);
            var options = stores
                .Where(store => !store.Archived)
                .Select(store =>
                {
                    var cryptoCode = store
                        .GetSupportedPaymentMethods(_networkProvider)
                        .OfType<DerivationSchemeSettings>()
                        .FirstOrDefault()?
                        .Network.CryptoCode;
                    var walletId = cryptoCode != null ? new WalletId(store.Id, cryptoCode) : null;
                    var role = store.GetStoreRoleOfUser(userId);
                    return new StoreSelectorOption
                    {
                        Text = store.StoreName,
                        Value = store.Id,
                        Selected = store.Id == currentStore?.Id,
                        WalletId = walletId,
                        Store = store,
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
                CurrentStoreLogoFileId = blob?.LogoFileId,
                ArchivedCount = archivedCount
            };

            return View(vm);
        }
    }
}
