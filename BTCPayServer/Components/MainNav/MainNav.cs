using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using NBitcoin;
using NBitcoin.Secp256k1;

namespace BTCPayServer.Components.MainNav
{
    public class MainNav : ViewComponent
    {
        private const string RootName = "Global";
        private readonly AppService _appService;
        private readonly StoreRepository _storeRepo;
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly PaymentMethodHandlerDictionary _paymentMethodHandlerDictionary;

        public MainNav(
            AppService appService,
            StoreRepository storeRepo,
            BTCPayNetworkProvider networkProvider, 
            UserManager<ApplicationUser> userManager,
            PaymentMethodHandlerDictionary paymentMethodHandlerDictionary)
        {
            _storeRepo = storeRepo;
            _appService = appService;
            _userManager = userManager;
            _networkProvider = networkProvider;
            _paymentMethodHandlerDictionary = paymentMethodHandlerDictionary;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var store = ViewContext.HttpContext.GetStoreData();
            var vm = new MainNavViewModel { Store = store };
/*#if ALTCOINS
            vm.AltcoinsBuild = true;
#endif*/
            if (store != null)
            {
                var storeBlob = store.GetStoreBlob();
                            
                // Wallets
                AddPaymentMethods(store, storeBlob, 
                    out var derivationSchemes, out var lightningNodes);
                vm.DerivationSchemes = derivationSchemes;
                vm.LightningNodes = lightningNodes;
                
                // Apps
                var apps = await _appService.GetAllApps(GetUserId(), false, store.Id);
                vm.Apps = apps.Select(a => new StoreApp
                {
                    Id = a.Id,
                    AppName = a.AppName,
                    AppType = a.AppType,
                    IsOwner = a.IsOwner
                }).ToList();
            }
            
            return View(vm);
        }
        
        // TODO: Refactor this to use shared code extracted from StoresController
        private void AddPaymentMethods(StoreData store, StoreBlob storeBlob, 
            out List<StoreDerivationScheme> derivationSchemes, out List<StoreLightningNode> lightningNodes)
        {
            var excludeFilters = storeBlob.GetExcludedPaymentMethods();
            var derivationByCryptoCode =
                store
                .GetSupportedPaymentMethods(_networkProvider)
                .OfType<DerivationSchemeSettings>()
                .ToDictionary(c => c.Network.CryptoCode.ToUpperInvariant());

            var lightningByCryptoCode = store
                .GetSupportedPaymentMethods(_networkProvider)
                .OfType<LightningSupportedPaymentMethod>()
                .Where(method => method.PaymentId.PaymentType == LightningPaymentType.Instance)
                .ToDictionary(c => c.CryptoCode.ToUpperInvariant());

            derivationSchemes = new List<StoreDerivationScheme>();
            lightningNodes = new List<StoreLightningNode>();

            foreach (var paymentMethodId in _paymentMethodHandlerDictionary.Distinct().SelectMany(handler => handler.GetSupportedPaymentMethods()))
            {
                switch (paymentMethodId.PaymentType)
                {
                    case BitcoinPaymentType _:
                        var strategy = derivationByCryptoCode.TryGet(paymentMethodId.CryptoCode);
                        var network = _networkProvider.GetNetwork<BTCPayNetwork>(paymentMethodId.CryptoCode);
                        var value = strategy?.ToPrettyString() ?? string.Empty;

                        derivationSchemes.Add(new StoreDerivationScheme
                        {
                            Crypto = paymentMethodId.CryptoCode,
                            WalletSupported = network.WalletSupported,
                            Value = value,
                            WalletId = new WalletId(store.Id, paymentMethodId.CryptoCode),
                            Enabled = !excludeFilters.Match(paymentMethodId) && strategy != null,
/*#if ALTCOINS
                            Collapsed = network is ElementsBTCPayNetwork elementsBTCPayNetwork && elementsBTCPayNetwork.NetworkCryptoCode != elementsBTCPayNetwork.CryptoCode && string.IsNullOrEmpty(value)
#endif*/
                        });
                        break;
                    
                    case LNURLPayPaymentType lnurlPayPaymentType:
                        break;
                    
                    case LightningPaymentType _:
                        var lightning = lightningByCryptoCode.TryGet(paymentMethodId.CryptoCode);
                        var isEnabled = !excludeFilters.Match(paymentMethodId) && lightning != null;
                        lightningNodes.Add(new StoreLightningNode
                        {
                            CryptoCode = paymentMethodId.CryptoCode,
                            Address = lightning?.GetDisplayableConnectionString(),
                            Enabled = isEnabled
                        });
                        break;
                }
            }
        }

        private string GetUserId()
        {
            return _userManager.GetUserId(HttpContext.User);
        }
    }
}
