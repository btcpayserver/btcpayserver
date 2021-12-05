using System;
using BTCPayServer.Data;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
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
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [AutoValidateAntiforgeryToken]
    [Route("stores/{storeId}/apps")]
    public partial class AppsController : Controller
    {
        public AppsController(
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

        public async Task<IActionResult> ListApps(
            string storeId, 
            string sortOrder = null,
            string sortOrderColumn = null
        )
        {
            var store = await _storeRepository.FindStore(storeId, GetUserId());
            if (store == null)
            {
                return NotFound();
            }
            HttpContext.SetStoreData(store);
            
            var apps = await _appService.GetAllApps(GetUserId());

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

        [HttpGet("create")]
        public async Task<IActionResult> CreateApp(string storeId)
        {
            var store = await _storeRepository.FindStore(storeId, GetUserId());
            if (store == null)
            {
                return NotFound();
            }
            HttpContext.SetStoreData(store);
            
            var vm = new CreateAppViewModel();
            return View(vm);
        }

        [HttpPost("create")]
        public async Task<IActionResult> CreateApp(string storeId, CreateAppViewModel vm)
        {
            var store = await _storeRepository.FindStore(storeId, GetUserId());
            if (store == null)
            {
                return NotFound();
            }
            HttpContext.SetStoreData(store);
            
            vm.SelectedStore = store.Id;

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
                    var emptyCrowdfund = new CrowdfundSettings();
                    emptyCrowdfund.TargetCurrency = defaultCurrency;
                    appData.SetSettings(emptyCrowdfund);
                    break;
                case AppType.PointOfSale:
                    var empty = new PointOfSaleSettings();
                    empty.Currency = defaultCurrency;
                    appData.SetSettings(empty);
                    break;
            }

            await _appService.UpdateOrCreateApp(appData);
            TempData[WellKnownTempData.SuccessMessage] = "App successfully created";
            CreatedAppId = appData.Id;

            switch (appType)
            {
                case AppType.PointOfSale:
                    return RedirectToAction(nameof(UpdatePointOfSale), new { storeId = appData.StoreDataId, appId = appData.Id });
                case AppType.Crowdfund:
                    return RedirectToAction(nameof(UpdateCrowdfund), new { storeId = appData.StoreDataId, appId = appData.Id });
                default:
                    return RedirectToAction(nameof(ListApps), new { storeId = appData.StoreDataId });
            }
        }

        [HttpGet("{appId}/delete")]
        public async Task<IActionResult> DeleteApp(string storeId, string appId)
        {
            var store = await _storeRepository.FindStore(storeId, GetUserId());
            if (store == null)
            {
                return NotFound();
            }
            HttpContext.SetStoreData(store);
            
            var appData = await GetOwnedApp(appId);
            if (appData == null)
                return NotFound();
            return View("Confirm", new ConfirmModel("Delete app", $"The app <strong>{appData.Name}</strong> and its settings will be permanently deleted. Are you sure?", "Delete"));
        }

        [HttpPost("{appId}/delete")]
        public async Task<IActionResult> DeleteAppPost(string storeId, string appId)
        {
            var store = await _storeRepository.FindStore(storeId, GetUserId());
            if (store == null)
            {
                return NotFound();
            }
            HttpContext.SetStoreData(store);
            
            var appData = await GetOwnedApp(appId);
            if (appData == null)
                return NotFound();
            if (await _appService.DeleteApp(appData))
                TempData[WellKnownTempData.SuccessMessage] = "App deleted successfully.";
            return RedirectToAction(nameof(ListApps), new { storeId });
        }

        async Task<string> GetStoreDefaultCurrentIfEmpty(string storeId, string currency)
        {
            if (string.IsNullOrWhiteSpace(currency))
            {
                currency = (await _storeRepository.FindStore(storeId)).GetStoreBlob().DefaultCurrency;
            }
            return currency.Trim().ToUpperInvariant();
        }

        private Task<AppData> GetOwnedApp(string appId, AppType? type = null)
        {
            return _appService.GetAppDataIfOwner(GetUserId(), appId, type);
        }

        private string GetUserId()
        {
            return _userManager.GetUserId(User);
        }
    }
}
