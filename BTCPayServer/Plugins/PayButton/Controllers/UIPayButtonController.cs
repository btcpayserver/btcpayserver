#nullable enable
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Plugins.PayButton.Models;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Plugins.PayButton.Controllers
{
    [Route("stores")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [AutoValidateAntiforgeryToken]
    [Area(PayButtonPlugin.Area)]
    public class UIPayButtonController(
        StoreRepository repo,
        UIStoresController storesController,
        UserManager<ApplicationUser> userManager,
        IStringLocalizer stringLocalizer,
        AppService appService)
        : Controller
    {
        public IStringLocalizer StringLocalizer { get; } = stringLocalizer;

        [HttpPost("{storeId}/disable-anyone-can-pay")]
        public async Task<IActionResult> DisableAnyoneCanCreateInvoice(string storeId)
        {
            var blob = GetCurrentStore.GetStoreBlob();
            blob.AnyoneCanInvoice = false;
            GetCurrentStore.SetStoreBlob(blob);
            TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Feature disabled"].Value;
            await repo.UpdateStore(GetCurrentStore);
            return RedirectToAction(nameof(PayButton), new { storeId });
        }

        [HttpGet("{storeId}/paybutton")]
        public async Task<IActionResult> PayButton()
        {
            var store = GetCurrentStore;
            var storeBlob = store.GetStoreBlob();
            if (!storeBlob.AnyoneCanInvoice)
            {
                return View("Enable", null);
            }

            var apps = await appService.GetAllApps(userManager.GetUserId(User), false, store.Id);
            // unset app store data, because we don't need it and inclusion leads to circular references when serializing to JSON
            foreach (var app in apps)
            {
                app.App.StoreData = null;
            }
            var appUrl = HttpContext.Request.GetAbsoluteRoot().WithTrailingSlash();
            var model = new PayButtonViewModel
            {
                Price = null,
                Currency = storeBlob.DefaultCurrency,
                DefaultPaymentMethod = string.Empty,
                PaymentMethods = storesController.GetEnabledPaymentMethodChoices(store),
                ButtonSize = 2,
                UrlRoot = appUrl,
                PayButtonImageUrl = appUrl + "img/paybutton/pay.svg",
                StoreId = store.Id,
                ButtonType = 0,
                Min = 1,
                Max = 20,
                Step = "1",
                Apps = apps
            };
            return View("PayButton", model);
        }

        [HttpPost("{storeId}/paybutton")]
        public async Task<IActionResult> PayButton(bool enableStore)
        {
            var blob = GetCurrentStore.GetStoreBlob();
            blob.AnyoneCanInvoice = enableStore;
            if (GetCurrentStore.SetStoreBlob(blob))
            {
                await repo.UpdateStore(GetCurrentStore);
                TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Store successfully updated"].Value;
            }

            return RedirectToAction(nameof(PayButton), new
            {
                storeId = GetCurrentStore.Id
            });
        }

        private StoreData GetCurrentStore => HttpContext.GetStoreData();
    }
}
