using System;
using BTCPayServer.Data;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
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
        
        [HttpGet("/stores/{storeId}/apps")]
        public async Task<IActionResult> ListApps(
            string storeId, 
            string sortOrder = null,
            string sortOrderColumn = null
        )
        {
            var apps = await _appService.GetAllApps(GetUserId(), false, CurrentStore.Id);

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
                StoreId = CurrentStore.Id
            });
        }

        [HttpPost("/stores/{storeId}/apps/create")]
        public async Task<IActionResult> CreateApp(string storeId, CreateAppViewModel vm)
        {
            vm.StoreId = CurrentStore.Id;

            if (!Enum.TryParse(vm.SelectedAppType, out AppType appType))
                ModelState.AddModelError(nameof(vm.SelectedAppType), "Invalid App Type");

            if (!ModelState.IsValid)
            {
                return View(vm);
            }

            var appData = new AppData
            {
                StoreDataId = CurrentStore.Id,
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
            }

            await _appService.UpdateOrCreateApp(appData);
            TempData[WellKnownTempData.SuccessMessage] = "App successfully created";
            CreatedAppId = appData.Id;

            switch (appType)
            {
                case AppType.PointOfSale:
                    return RedirectToAction(nameof(UpdatePointOfSale), new { appId = appData.Id });
                case AppType.Crowdfund:
                    return RedirectToAction(nameof(UpdateCrowdfund), new { appId = appData.Id });
                default:
                    return RedirectToAction(nameof(ListApps), new { storeId = appData.StoreDataId });
            }
        }

        [HttpGet("{appId}/delete")]
        public async Task<IActionResult> DeleteApp(string appId)
        {
            var appData = await GetOwnedApp(appId);
            if (appData == null)
                return NotFound();
            return View("Confirm", new ConfirmModel("Delete app", $"The app <strong>{appData.Name}</strong> and its settings will be permanently deleted. Are you sure?", "Delete"));
        }

        [HttpPost("{appId}/delete")]
        public async Task<IActionResult> DeleteAppPost(string appId)
        {
            var appData = await GetOwnedApp(appId);
            if (appData == null)
                return NotFound();
            if (await _appService.DeleteApp(appData))
                TempData[WellKnownTempData.SuccessMessage] = "App deleted successfully.";
            return RedirectToAction(nameof(ListApps), new { storeId = appData.StoreDataId });
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

        private StoreData CurrentStore
        {
            get => HttpContext.GetStoreData();
        }
    }
}
