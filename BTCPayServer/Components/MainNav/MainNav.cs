using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using NBitcoin;

namespace BTCPayServer.Components.MainNav
{
    public class MainNav : ViewComponent
    {
        private readonly AppService _appService;
        private readonly StoreRepository _storeRepo;
        private readonly UIStoresController _storesController;
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly PaymentMethodHandlerDictionary _paymentMethodHandlerDictionary;
        private readonly SettingsRepository _settingsRepository;
        private readonly UriResolver _uriResolver;
        private readonly IMemoryCache _cache;

        public PoliciesSettings PoliciesSettings { get; }

        public MainNav(
            AppService appService,
            StoreRepository storeRepo,
            UIStoresController storesController,
            BTCPayNetworkProvider networkProvider,
            UserManager<ApplicationUser> userManager,
            PaymentMethodHandlerDictionary paymentMethodHandlerDictionary,
            SettingsRepository settingsRepository,
            IMemoryCache cache,
            UriResolver uriResolver,
            PoliciesSettings policiesSettings)
        {
            _storeRepo = storeRepo;
            _appService = appService;
            _userManager = userManager;
            _networkProvider = networkProvider;
            _storesController = storesController;
            _paymentMethodHandlerDictionary = paymentMethodHandlerDictionary;
            _settingsRepository = settingsRepository;
            _uriResolver = uriResolver;
            _cache = cache;
            PoliciesSettings = policiesSettings;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var store = ViewContext.HttpContext.GetStoreData();
            var serverSettings = await _settingsRepository.GetSettingAsync<ServerSettings>() ?? new ServerSettings();
            var vm = new MainNavViewModel
            {
                Store = store,
                ContactUrl = serverSettings.ContactUrl
            };
            if (store != null)
            {
                var storeBlob = store.GetStoreBlob();

                // Wallets
                _storesController.AddPaymentMethods(store, storeBlob,
                    out var derivationSchemes, out var lightningNodes);

                foreach (var lnNode in lightningNodes)
                {
                    var pmi = PaymentTypes.LN.GetPaymentMethodId(lnNode.CryptoCode);
                    if (_paymentMethodHandlerDictionary.TryGet(pmi) is not LightningLikePaymentHandler handler)
                        continue;

                    if (lnNode.CacheKey is not null)
                    {
						using var cts = new CancellationTokenSource(5000);
						try
						{
							lnNode.Available = await _cache.GetOrCreateAsync(lnNode.CacheKey, async entry =>
							{
								entry.SetAbsoluteExpiration(TimeSpan.FromMinutes(5));
								try
								{
									var paymentMethodDetails = store.GetPaymentMethodConfig<LightningPaymentMethodConfig>(pmi, _paymentMethodHandlerDictionary);
									await handler.GetNodeInfo(paymentMethodDetails, null, throws: true);
									// if we came here without exception, this means the node is available
									return true;
								}
								catch (Exception)
								{
									return false;
								}
							}).WithCancellation(cts.Token);
						}
						catch when (cts.IsCancellationRequested) { }
                    }
                }
                
                vm.DerivationSchemes = derivationSchemes;
                vm.LightningNodes = lightningNodes;

                // Apps
                var apps = await _appService.GetAllApps(UserId, false, store.Id, true);
                vm.Apps = apps
                    .Where(a => !a.Archived)
                    .Select(a => new StoreApp
                    {
                        Id = a.Id,
                        AppName = a.AppName,
                        AppType = a.AppType
                    }).ToList();

                vm.ArchivedAppsCount = apps.Count(a => a.Archived);
            }
            
            var user = await _userManager.GetUserAsync(HttpContext.User);
            if (user != null)
            {
                var blob = user.GetBlob();
                vm.UserName = blob?.Name;
                vm.UserImageUrl = string.IsNullOrEmpty(blob?.ImageUrl)
                    ? null
                    : await _uriResolver.Resolve(Request.GetAbsoluteRootUri(), UnresolvedUri.Create(blob?.ImageUrl));
            }

            return View(vm);
        }

        private string UserId => _userManager.GetUserId(HttpContext.User);
    }
}
