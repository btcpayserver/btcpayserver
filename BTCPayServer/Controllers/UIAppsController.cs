using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [AutoValidateAntiforgeryToken]
    [Route("apps")]
    public partial class UIAppsController : Controller
    {
        public UIAppsController(
            UserManager<ApplicationUser> userManager,
            EventAggregator eventAggregator,
            CurrencyNameTable currencies,
            StoreRepository storeRepository,
            AppService appService)
        {
            _userManager = userManager;
            _eventAggregator = eventAggregator;
            _currencies = currencies;
            _storeRepository = storeRepository;
            _appService = appService;
        }

        private readonly UserManager<ApplicationUser> _userManager;
        private readonly EventAggregator _eventAggregator;
        private readonly CurrencyNameTable _currencies;
        private readonly StoreRepository _storeRepository;
        private readonly AppService _appService;

        public string CreatedAppId { get; set; }

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

        [HttpGet("/stores/{storeId}/apps/create")]
        public IActionResult CreateApp(string storeId)
        {
            return View(new CreateAppViewModel
            {
                StoreId = GetCurrentStore().Id
            });
        }

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
                AppType.PointOfSale => RedirectToAction(nameof(UpdatePointOfSale), new { appId = appData.Id }),
                AppType.Crowdfund => RedirectToAction(nameof(UpdateCrowdfund), new { appId = appData.Id }),
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        [HttpGet("{appId}/delete")]
        public IActionResult DeleteApp(string appId)
        {
            var app = GetCurrentApp();
            if (app == null)
                return NotFound();

            return View("Confirm", new ConfirmModel("Delete app", $"The app <strong>{app.Name}</strong> and its settings will be permanently deleted. Are you sure?", "Delete"));
        }

        [HttpPost("{appId}/delete")]
        public async Task<IActionResult> DeleteAppPost(string appId)
        {
            var app = GetCurrentApp();
            if (app == null)
                return NotFound();

            if (await _appService.DeleteApp(app))
                TempData[WellKnownTempData.SuccessMessage] = "App deleted successfully.";

            return RedirectToAction(nameof(ListApps), new { storeId = app.StoreDataId });
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
