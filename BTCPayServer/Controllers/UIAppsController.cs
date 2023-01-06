using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Plugins.Crowdfund.Controllers;
using BTCPayServer.Plugins.PointOfSale.Controllers;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    [AutoValidateAntiforgeryToken]
    [Route("apps")]
    public partial class UIAppsController : Controller
    {
        public UIAppsController(
            UserManager<ApplicationUser> userManager,
            StoreRepository storeRepository,
            AppService appService)
        {
            _userManager = userManager;
            _storeRepository = storeRepository;
            _appService = appService;
        }

        private readonly UserManager<ApplicationUser> _userManager;
        private readonly StoreRepository _storeRepository;
        private readonly AppService _appService;

        public string CreatedAppId { get; set; }

        public class AppUpdated
        {
            public string AppId { get; set; }
            public object Settings { get; set; }
            public string StoreId { get; set; }
            public override string ToString()
            {
                return string.Empty;
            }
        }

        [HttpGet("/apps/{appId}")]
        public async Task<IActionResult> RedirectToApp(string appId)
        {
            var app = await _appService.GetApp(appId, null);
            if (app is null)
                return NotFound();

            return app.AppType switch
            {
                nameof(AppType.Crowdfund) => RedirectToAction(nameof(UICrowdfundController.ViewCrowdfund), "UICrowdfund", new { appId }),
                nameof(AppType.PointOfSale) => RedirectToAction(nameof(UIPointOfSaleController.ViewPointOfSale), "UIPointOfSale", new { appId }),
                _ => NotFound()
            };
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [HttpGet("/stores/{storeId}/apps")]
        public async Task<IActionResult> ListApps(
            string storeId,
            string sortOrder = null,
            string sortOrderColumn = null
        )
        {
            var store = GetCurrentStore();
            var apps = await _appService.GetAllApps(GetUserId(), false, store.Id);

            if (sortOrder != null && sortOrderColumn != null)
            {
                apps = apps.OrderByDescending(app =>
                {
                    switch (sortOrderColumn)
                    {
                        case nameof(app.AppName):
                            return app.AppName;
                        case nameof(app.StoreName):
                            return app.StoreName;
                        case nameof(app.AppType):
                            return app.AppType;
                        default:
                            return app.Id;
                    }
                }).ToArray();

                switch (sortOrder)
                {
                    case "desc":
                        ViewData[$"{sortOrderColumn}SortOrder"] = "asc";
                        break;
                    case "asc":
                        apps = apps.Reverse().ToArray();
                        ViewData[$"{sortOrderColumn}SortOrder"] = "desc";
                        break;
                }
            }

            return View(new ListAppsViewModel
            {
                Apps = apps
            });
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [HttpGet("/stores/{storeId}/apps/create")]
        public IActionResult CreateApp(string storeId)
        {
            return View(new CreateAppViewModel
            {
                StoreId = GetCurrentStore().Id
            });
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [HttpPost("/stores/{storeId}/apps/create")]
        public async Task<IActionResult> CreateApp(string storeId, CreateAppViewModel vm)
        {
            var store = GetCurrentStore();
            vm.StoreId = store.Id;

            if (!Enum.TryParse(vm.SelectedAppType, out AppType appType))
                ModelState.AddModelError(nameof(vm.SelectedAppType), "Invalid App Type");

            if (!ModelState.IsValid)
            {
                return View(vm);
            }

            var appData = new AppData
            {
                StoreDataId = store.Id,
                Name = vm.AppName,
                AppType = appType.ToString()
            };

            var defaultCurrency = await GetStoreDefaultCurrentIfEmpty(appData.StoreDataId, null);
            switch (appType)
            {
                case AppType.Crowdfund:
                    var emptyCrowdfund = new CrowdfundSettings { TargetCurrency = defaultCurrency };
                    appData.SetSettings(emptyCrowdfund);
                    break;
                case AppType.PointOfSale:
                    var empty = new PointOfSaleSettings { Currency = defaultCurrency };
                    appData.SetSettings(empty);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            await _appService.UpdateOrCreateApp(appData);
            TempData[WellKnownTempData.SuccessMessage] = "App successfully created";
            CreatedAppId = appData.Id;

            return appType switch
            {
                AppType.PointOfSale => RedirectToAction(nameof(UIPointOfSaleController.UpdatePointOfSale), "UIPointOfSale", new { appId = appData.Id }),
                AppType.Crowdfund => RedirectToAction(nameof(UICrowdfundController.UpdateCrowdfund), "UICrowdfund", new { appId = appData.Id }),
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [HttpGet("{appId}/delete")]
        public IActionResult DeleteApp(string appId)
        {
            var app = GetCurrentApp();
            if (app == null)
                return NotFound();

            return View("Confirm", new ConfirmModel("Delete app", $"The app <strong>{app.Name}</strong> and its settings will be permanently deleted. Are you sure?", "Delete"));
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [HttpPost("{appId}/delete")]
        public async Task<IActionResult> DeleteAppPost(string appId)
        {
            var app = GetCurrentApp();
            if (app == null)
                return NotFound();

            if (await _appService.DeleteApp(app))
                TempData[WellKnownTempData.SuccessMessage] = "App deleted successfully.";

            return RedirectToAction(nameof(UIStoresController.Dashboard), "UIStores", new { storeId = app.StoreDataId });
        }

        async Task<string> GetStoreDefaultCurrentIfEmpty(string storeId, string currency)
        {
            if (string.IsNullOrWhiteSpace(currency))
            {
                currency = (await _storeRepository.FindStore(storeId)).GetStoreBlob().DefaultCurrency;
            }
            return currency.Trim().ToUpperInvariant();
        }

        private string GetUserId() => _userManager.GetUserId(User);

        private StoreData GetCurrentStore() => HttpContext.GetStoreData();

        private AppData GetCurrentApp() => HttpContext.GetAppData();
    }
}
