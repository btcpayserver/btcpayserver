using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Components.StoreSelector
{
    public class StoreSelector : ViewComponent
    {
        private readonly StoreRepository _storeRepo;
        private readonly UriResolver _uriResolver;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly PaymentMethodHandlerDictionary _handlers;

        public StoreSelector(
            StoreRepository storeRepo,
            UriResolver uriResolver,
            UserManager<ApplicationUser> userManager,
            PaymentMethodHandlerDictionary handlers)
        {
            _storeRepo = storeRepo;
            _uriResolver = uriResolver;
            _userManager = userManager;
            _handlers = handlers;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var userId = _userManager.GetUserId(UserClaimsPrincipal);
            var stores = await _storeRepo.GetStoresByUserId(userId);
            var archivedCount = stores.Count(s => s.Archived);
            var selectableStores = stores
                .Where(store => !store.Archived)
                .Where(store => IsStoreSelectable(store, userId))
                .OrderBy(store => store.StoreName)
                .ToList();

            var currentStore = ViewContext.HttpContext.GetStoreData();
            if (currentStore is not null && selectableStores.All(s => s.Id != currentStore.Id))
            {
                currentStore = selectableStores.FirstOrDefault();
            }

            var options = selectableStores
                .Select(store => new StoreSelectorOption
                {
                    Text = store.StoreName,
                    Value = store.Id,
                    Selected = store.Id == currentStore?.Id
                })
                .ToList();

            var blob = currentStore?.GetStoreBlob();

            var vm = new StoreSelectorViewModel
            {
                Options = options,
                CurrentStoreId = currentStore?.Id,
                CurrentDisplayName = currentStore?.StoreName,
                CurrentStoreLogoUrl = await _uriResolver.Resolve(Request.GetAbsoluteRootUri(), blob?.LogoUrl),
                ArchivedCount = archivedCount
            };

            return View(vm);
        }

        private bool IsStoreSelectable(StoreData store, string userId)
        {
            var permissionSet = store.GetPermissionSet(userId);
            if (permissionSet.Contains(Policies.CanModifyStoreSettings, store.Id))
            {
                return true;
            }

            if (permissionSet.Contains(Policies.CanViewInvoices, store.Id))
            {
                return true;
            }

            if (!permissionSet.Contains(Policies.CanViewWallet, store.Id))
            {
                return false;
            }

            // For wallet-only roles, avoid showing stores that do not yet have a wallet configured.
            return store.GetPaymentMethodConfigs<DerivationSchemeSettings>(_handlers, true).Any();
        }
    }
}
