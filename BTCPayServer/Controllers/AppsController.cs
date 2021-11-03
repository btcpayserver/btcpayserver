using System;
using BTCPayServer.Data;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Models;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Security;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Mails;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [AutoValidateAntiforgeryToken]
    [Route("apps")]
    public partial class AppsController : Controller
    {
        public AppsController(
            UserManager<ApplicationUser> userManager,
            ApplicationDbContextFactory contextFactory,
            EventAggregator eventAggregator,
            BTCPayNetworkProvider networkProvider,
            CurrencyNameTable currencies,
            EmailSenderFactory emailSenderFactory,
            Services.Stores.StoreRepository storeRepository,
            AppService AppService)
        {
            _UserManager = userManager;
            _ContextFactory = contextFactory;
            _EventAggregator = eventAggregator;
            _NetworkProvider = networkProvider;
            _currencies = currencies;
            _emailSenderFactory = emailSenderFactory;
            _storeRepository = storeRepository;
            _AppService = AppService;
        }

        private readonly UserManager<ApplicationUser> _UserManager;
        private readonly ApplicationDbContextFactory _ContextFactory;
        private readonly EventAggregator _EventAggregator;
        private readonly BTCPayNetworkProvider _NetworkProvider;
        private readonly CurrencyNameTable _currencies;
        private readonly EmailSenderFactory _emailSenderFactory;
        private readonly StoreRepository _storeRepository;
        private readonly AppService _AppService;

        public string CreatedAppId { get; set; }

        public async Task<IActionResult> ListApps(
            string sortOrder = null,
            string sortOrderColumn = null
        )
        {
            var apps = await _AppService.GetAllApps(GetUserId());

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
            
            return View(new ListAppsViewModel()
            {
                Apps = apps
            });
        }

        [HttpPost]
        [Route("{appId}/delete")]
        public async Task<IActionResult> DeleteAppPost(string appId)
        {
            var appData = await GetOwnedApp(appId);
            if (appData == null)
                return NotFound();
            if (await _AppService.DeleteApp(appData))
                TempData[WellKnownTempData.SuccessMessage] = "App deleted successfully.";
            return RedirectToAction(nameof(ListApps));
        }

        [HttpGet]
        [Route("create")]
        public async Task<IActionResult> CreateApp()
        {
            var stores = await _AppService.GetOwnedStores(GetUserId());
            if (stores.Length == 0)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Html =
                        $"Error: You need to create at least one store. <a href='{(Url.Action("CreateStore", "UserStores"))}' class='alert-link'>Create store</a>",
                    Severity = StatusMessageModel.StatusSeverity.Error
                });
                return RedirectToAction(nameof(ListApps));
            }
            var vm = new CreateAppViewModel();
            vm.SetStores(stores);
            return View(vm);
        }

        [HttpPost]
        [Route("create")]
        public async Task<IActionResult> CreateApp(CreateAppViewModel vm)
        {
            var stores = await _AppService.GetOwnedStores(GetUserId());
            if (stores.Length == 0)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Html =
                        $"Error: You need to create at least one store. <a href='{(Url.Action("CreateStore", "UserStores"))}' class='alert-link'>Create store</a>",
                    Severity = StatusMessageModel.StatusSeverity.Error
                });
                return RedirectToAction(nameof(ListApps));
            }
            var selectedStore = vm.SelectedStore;
            vm.SetStores(stores);
            vm.SelectedStore = selectedStore;

            if (!Enum.TryParse<AppType>(vm.SelectedAppType, out AppType appType))
                ModelState.AddModelError(nameof(vm.SelectedAppType), "Invalid App Type");

            if (!ModelState.IsValid)
            {
                return View(vm);
            }

            if (!stores.Any(s => s.Id == selectedStore))
            {
                TempData[WellKnownTempData.ErrorMessage] = "You are not owner of this store";
                return RedirectToAction(nameof(ListApps));
            }
            var appData = new AppData
            {
                StoreDataId = selectedStore,
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

            await _AppService.UpdateOrCreateApp(appData);
            TempData[WellKnownTempData.SuccessMessage] = "App successfully created";
            CreatedAppId = appData.Id;

            switch (appType)
            {
                case AppType.PointOfSale:
                    return RedirectToAction(nameof(UpdatePointOfSale), new { appId = appData.Id });
                case AppType.Crowdfund:
                    return RedirectToAction(nameof(UpdateCrowdfund), new { appId = appData.Id });
                default:
                    return RedirectToAction(nameof(ListApps));
            }
        }

        async Task<string> GetStoreDefaultCurrentIfEmpty(string storeId, string currency)
        {
            if (String.IsNullOrWhiteSpace(currency))
            {
                currency = (await _storeRepository.FindStore(storeId)).GetStoreBlob().DefaultCurrency;
            }
            return currency.Trim().ToUpperInvariant();
        }

        [HttpGet("{appId}/delete")]
        public async Task<IActionResult> DeleteApp(string appId)
        {
            var appData = await GetOwnedApp(appId);
            if (appData == null)
                return NotFound();
            return View("Confirm", new ConfirmModel("Delete app", $"The app <strong>{appData.Name}</strong> and its settings will be permanently deleted. Are you sure?", "Delete"));
        }

        private Task<AppData> GetOwnedApp(string appId, AppType? type = null)
        {
            return _AppService.GetAppDataIfOwner(GetUserId(), appId, type);
        }

        private string GetUserId()
        {
            return _UserManager.GetUserId(User);
        }
    }
}
