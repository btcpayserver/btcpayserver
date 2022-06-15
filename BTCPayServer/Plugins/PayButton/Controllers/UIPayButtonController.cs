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
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Plugins.PayButton.Controllers
{
    [Route("stores")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [AutoValidateAntiforgeryToken]
    public class UIPayButtonController : Controller
    {
        public UIPayButtonController(
            StoreRepository repo,
            UIStoresController storesController,
            UserManager<ApplicationUser> userManager,
            AppService appService)
        {
            _repo = repo;
            _userManager = userManager;
            _appService = appService;
            _storesController = storesController;
        }

        readonly StoreRepository _repo;
        readonly UserManager<ApplicationUser> _userManager;
        private readonly AppService _appService;
        private readonly UIStoresController _storesController;

        [HttpPost("{storeId}/disable-anyone-can-pay")]
        public async Task<IActionResult> DisableAnyoneCanCreateInvoice(string storeId)
        {
            var blob = GetCurrentStore.GetStoreBlob();
            blob.AnyoneCanInvoice = false;
            GetCurrentStore.SetStoreBlob(blob);
            TempData[WellKnownTempData.SuccessMessage] = "Feature disabled";
            await _repo.UpdateStore(GetCurrentStore);
            return RedirectToAction(nameof(PayButton), new { storeId });
        }

        [HttpGet("{storeId}/paybutton")]
        public async Task<IActionResult> PayButton()
        {
            var store = GetCurrentStore;
            var storeBlob = store.GetStoreBlob();
            if (!storeBlob.AnyoneCanInvoice)
            {
                return View("PayButton/Enable", null);
            }

            var apps = await _appService.GetAllApps(_userManager.GetUserId(User), false, store.Id);
            var appUrl = HttpContext.Request.GetAbsoluteRoot().WithTrailingSlash();
            var model = new PayButtonViewModel
            {
                Price = null,
                Currency = storeBlob.DefaultCurrency,
                DefaultPaymentMethod = string.Empty,
                PaymentMethods = _storesController.GetEnabledPaymentMethodChoices(store),
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
            return View("PayButton/PayButton", model);
        }

        [HttpPost("{storeId}/paybutton")]
        public async Task<IActionResult> PayButton(bool enableStore)
        {
            var blob = GetCurrentStore.GetStoreBlob();
            blob.AnyoneCanInvoice = enableStore;
            if (GetCurrentStore.SetStoreBlob(blob))
            {
                await _repo.UpdateStore(GetCurrentStore);
                TempData[WellKnownTempData.SuccessMessage] = "Store successfully updated";
            }

            return RedirectToAction(nameof(PayButton), new
            {
                storeId = GetCurrentStore.Id
            });
        }

        private StoreData GetCurrentStore => HttpContext.GetStoreData();
    }
}
