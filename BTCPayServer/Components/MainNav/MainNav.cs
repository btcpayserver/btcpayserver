using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Models.ServerViewModels;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Options;
using NBitcoin;
using NBitcoin.Secp256k1;

namespace BTCPayServer.Components.MainNav
{
    public class MainNav : ViewComponent
    {
        private readonly AppService _appService;
        private readonly StoreRepository _storeRepo;
        private readonly StoresController _storesController;
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly PaymentMethodHandlerDictionary _paymentMethodHandlerDictionary;
        private readonly IOptions<ExternalServicesOptions> _externalServiceOptions;
        private readonly ExternalServiceTypes[] _externalServiceTypes =
        {
            ExternalServiceTypes.Spark,
            ExternalServiceTypes.RTL,
            ExternalServiceTypes.ThunderHub
        };
        private readonly string[] _externalServiceNames =
        {
            "Lightning Terminal", 
            "Tallycoin Connect"
        };

        public MainNav(
            AppService appService,
            StoreRepository storeRepo,
            StoresController storesController,
            BTCPayNetworkProvider networkProvider,
            UserManager<ApplicationUser> userManager,
            IOptions<ExternalServicesOptions> externalServiceOptions,
            PaymentMethodHandlerDictionary paymentMethodHandlerDictionary)
        {
            _storeRepo = storeRepo;
            _appService = appService;
            _userManager = userManager;
            _networkProvider = networkProvider;
            _storesController = storesController;
            _externalServiceOptions = externalServiceOptions;
            _paymentMethodHandlerDictionary = paymentMethodHandlerDictionary;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var store = ViewContext.HttpContext.GetStoreData();
            var services = _externalServiceOptions.Value.ExternalServices.ToList()
                .Where(service => _externalServiceTypes.Contains(service.Type))
                .Select(service => new AdditionalServiceViewModel
                {
                    DisplayName = service.DisplayName,
                    ServiceName = service.ServiceName,
                    CryptoCode = service.CryptoCode,
                    Type = service.Type.ToString()
                })
                .ToList();
            
            // other services
            foreach ((string key, Uri value) in _externalServiceOptions.Value.OtherExternalServices)
            {
                if (_externalServiceNames.Contains(key))
                {
                    services.Add(new AdditionalServiceViewModel
                    {
                        DisplayName = key,
                        ServiceName = key,
                        Type = key.Replace(" ", ""),
                        Link = Request.GetAbsoluteUriNoPathBase(value).AbsoluteUri
                    });
                }
            }
            
            var vm = new MainNavViewModel
            {
                Store = store,
                Services = services
            };
            
#if ALTCOINS
            vm.AltcoinsBuild = true;
#endif
            if (store != null)
            {
                var storeBlob = store.GetStoreBlob();

                // Wallets
                _storesController.AddPaymentMethods(store, storeBlob,
                    out var derivationSchemes, out var lightningNodes);
                vm.DerivationSchemes = derivationSchemes;
                vm.LightningNodes = lightningNodes;

                // Apps
                var apps = await _appService.GetAllApps(UserId, false, store.Id);
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

        private string UserId => _userManager.GetUserId(HttpContext.User);
    }
}
