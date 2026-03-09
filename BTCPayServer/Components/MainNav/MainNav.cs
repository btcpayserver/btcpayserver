#nullable enable
using System;
using System.Linq;
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
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using NBitcoin;

namespace BTCPayServer.Components.MainNav
{
    public class MainNav(
        AppService appService,
        UIStoresController storesController,
        UserManager<ApplicationUser> userManager,
        PaymentMethodHandlerDictionary paymentMethodHandlerDictionary,
        SettingsRepository settingsRepository,
        IMemoryCache cache,
        UriResolver uriResolver,
        PoliciesSettings policiesSettings)
        : ViewComponent
    {
        public PoliciesSettings PoliciesSettings { get; } = policiesSettings;

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var navStore = HttpContext.GetNavStoreData();

            var serverSettings = await settingsRepository.GetSettingAsync<ServerSettings>() ?? new ServerSettings();
            var vm = new MainNavViewModel
            {
                Store = navStore,
                ContactUrl = serverSettings.ContactUrl

            };
            if (navStore != null)
            {
                var storeBlob = navStore.GetStoreBlob();

                // Wallets
                storesController.AddPaymentMethods(navStore, storeBlob,
                    out var derivationSchemes, out var lightningNodes);

                foreach (var lnNode in lightningNodes)
                {
                    var pmi = PaymentTypes.LN.GetPaymentMethodId(lnNode.CryptoCode);
                    if (paymentMethodHandlerDictionary.TryGet(pmi) is not LightningLikePaymentHandler handler)
                        continue;

                    if (lnNode.CacheKey is not null)
                    {
						using var cts = new CancellationTokenSource(5000);
						try
						{
							lnNode.Available = await cache.GetOrCreateAsync(lnNode.CacheKey, async entry =>
							{
								entry.SetAbsoluteExpiration(TimeSpan.FromMinutes(5));
								try
								{
									var paymentMethodDetails = navStore.GetPaymentMethodConfig<LightningPaymentMethodConfig>(pmi, paymentMethodHandlerDictionary);
									await handler.GetNodeInfo(paymentMethodDetails!, null, throws: true);
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
                var apps = await appService.GetAllApps(HttpContext.User.GetIdOrNull(), false, navStore.Id, true);
                vm.Apps = apps
                    .Where(a => !a.Archived)
                    .Select(a => new StoreApp
                    {
                        Id = a.Id,
                        AppName = a.AppName,
                        AppType = a.AppType,
                        Data = a.App
                    }).ToList();

                vm.ArchivedAppsCount = apps.Count(a => a.Archived);
            }

            var user = await userManager.GetUserAsync(HttpContext.User);
            if (user != null)
            {
                var blob = user.GetBlob();
                vm.UserName = blob?.Name;
                vm.UserImageUrl = string.IsNullOrEmpty(blob?.ImageUrl)
                    ? null
                    : await uriResolver.Resolve(Request.GetAbsoluteRootUri(), UnresolvedUri.Create(blob.ImageUrl));
            }

            return View(vm);
        }
    }
}
