using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Custodians;
using BTCPayServer.Client;
using BTCPayServer.Controllers.Greenfield;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Models.CustodianAccountViewModels;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Custodian.Client;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    [Authorize(Policy = Policies.CanManageCustodianAccounts, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [AutoValidateAntiforgeryToken]
    [Route("custodian-accounts")]
    public partial class UICustodianAccountsController : Controller
    {
        public UICustodianAccountsController(
            UserManager<ApplicationUser> userManager,
            // EventAggregator eventAggregator,
            // CurrencyNameTable currencies,
            StoreRepository storeRepository,
            CustodianAccountRepository custodianAccountRepository,
            IEnumerable<ICustodian> custodianRegistry,
            BTCPayServerClient btcPayServerClient
            )
        {
            _userManager = userManager;
            // _eventAggregator = eventAggregator;
            // _currencies = currencies;
            _storeRepository = storeRepository;
            _custodianAccountRepository = custodianAccountRepository;
            _custodianRegistry = custodianRegistry;
            _btcPayServerClient = btcPayServerClient;
        }

        private readonly IEnumerable<ICustodian> _custodianRegistry;
        private readonly UserManager<ApplicationUser> _userManager;
        // private readonly EventAggregator _eventAggregator;
        // private readonly CurrencyNameTable _currencies;
        private readonly StoreRepository _storeRepository;
        private readonly CustodianAccountRepository _custodianAccountRepository;
        private readonly BTCPayServerClient _btcPayServerClient;

        // public string CreatedAppId { get; set; }

        [HttpGet("/stores/{storeId}/custodian-accounts/{accountId}")]
        public async Task<IActionResult> ViewCustodianAccount(string storeId, string accountId)
        {
            return View(new ViewCustodianAccountViewModel());
        }

        [HttpGet("/stores/{storeId}/custodian-accounts/create")]
        public IActionResult CreateCustodianAccount(string storeId)
        {
            return View(new CreateCustodianAccountViewModel(storeId, _custodianRegistry));
        }

        [HttpPost("/stores/{storeId}/custodian-accounts/create")]
        public async Task<IActionResult> CreateCustodianAccount(string storeId, CreateCustodianAccountViewModel vm)
        {
            var store = GetCurrentStore();
            vm.StoreId = store.Id;

            // TODO check if the custodian exists => add error if it does not
            if (!Enum.TryParse(vm.SelectedCustodian, out AppType appType))
                ModelState.AddModelError(nameof(vm.SelectedCustodian), "Invalid Custodian");

            
            return View(vm);
            
            // if (!ModelState.IsValid)
            // {
            //     return View(vm);
            // }

            // var custodianAccountData = new CustodianAccountData
            // {
            //     CustodianCode = custodian,
            //     Name = vm.AppName,
            //     AppType = appType.ToString()
            // };
            //
            // await _custodianAccountRepository.CreateOrUpdate(appData);
            // TempData[WellKnownTempData.SuccessMessage] = "App successfully created";
            // CreatedAppId = appData.Id;
            //
            // switch (appType)
            // {
            //     case AppType.PointOfSale:
            //         return RedirectToAction(nameof(UpdatePointOfSale), new { appId = appData.Id });
            //     case AppType.Crowdfund:
            //         return RedirectToAction(nameof(UpdateCrowdfund), new { appId = appData.Id });
            //     default:
            //         return RedirectToAction(nameof(ListApps), new { storeId = appData.StoreDataId });
            // }
        }
        //
        // [HttpGet("{appId}/delete")]
        // public IActionResult DeleteApp(string appId)
        // {
        //     var app = GetCurrentApp();
        //     if (app == null)
        //         return NotFound();
        //
        //     return View("Confirm", new ConfirmModel("Delete app", $"The app <strong>{app.Name}</strong> and its settings will be permanently deleted. Are you sure?", "Delete"));
        // }
        //
        // [HttpPost("{appId}/delete")]
        // public async Task<IActionResult> DeleteAppPost(string appId)
        // {
        //     var app = GetCurrentApp();
        //     if (app == null)
        //         return NotFound();
        //
        //     if (await _appService.DeleteApp(app))
        //         TempData[WellKnownTempData.SuccessMessage] = "App deleted successfully.";
        //
        //     return RedirectToAction(nameof(ListApps), new { storeId = app.StoreDataId });
        // }
        //
        // async Task<string> GetStoreDefaultCurrentIfEmpty(string storeId, string currency)
        // {
        //     if (string.IsNullOrWhiteSpace(currency))
        //     {
        //         currency = (await _storeRepository.FindStore(storeId)).GetStoreBlob().DefaultCurrency;
        //     }
        //     return currency.Trim().ToUpperInvariant();
        // }
        //
        // private string GetUserId() => _userManager.GetUserId(User);

        private StoreData GetCurrentStore() => HttpContext.GetStoreData();

        // private AppData GetCurrentApp() => HttpContext.GetAppData();
    }
}
