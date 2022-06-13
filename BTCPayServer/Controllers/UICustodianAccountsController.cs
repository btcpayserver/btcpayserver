using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Custodians;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Form;
using BTCPayServer.Client;
using BTCPayServer.Controllers.Greenfield;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Models.CustodianAccountViewModels;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Custodian.Client;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Controllers
{
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [AutoValidateAntiforgeryToken]
    public class UICustodianAccountsController : Controller
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

        public string CreatedCustodianAccountId { get; set; }

        [HttpGet("/stores/{storeId}/custodian-accounts/{accountId}")]
        public async Task<IActionResult> ViewCustodianAccount(string storeId, string accountId)
        {
            var vm = new ViewCustodianAccountViewModel();
            var custodianAccount = await _custodianAccountRepository.FindById(storeId, accountId);

            if (custodianAccount == null)
            {
                return NotFound();
            }

            var custodian = _custodianRegistry.GetCustodianByCode(custodianAccount.CustodianCode);

            vm.CustodianAccount = custodianAccount;
            try
            {
                vm.AssetBalances =
                    await custodian.GetAssetBalancesAsync(custodianAccount.GetBlob(), cancellationToken: default);
            }
            catch (Exception e)
            {
                vm.GetAssetBalanceException = e;
            }

            return View(vm);
        }

        [HttpGet("/stores/{storeId}/custodian-accounts/{accountId}/edit")]
        public async Task<IActionResult> EditCustodianAccount(string storeId, string accountId)
        {
            var custodianAccount = await _custodianAccountRepository.FindById(storeId, accountId);
            if (custodianAccount == null)
            {
                return NotFound();
            }

            var custodian = _custodianRegistry.GetCustodianByCode(custodianAccount.CustodianCode);
            var configForm = await custodian.GetConfigForm(custodianAccount.GetBlob(), "en-US");
            
            var vm = new EditCustodianAccountViewModel();
            vm.CustodianAccount = custodianAccount;
            vm.ConfigForm = configForm;
            return View(vm);
        }
        
        [HttpPost("/stores/{storeId}/custodian-accounts/{accountId}/edit")]
        public async Task<IActionResult> EditCustodianAccount(string storeId, string accountId, EditCustodianAccountViewModel vm)
        {
            var custodianAccount = await _custodianAccountRepository.FindById(storeId, accountId);
            if (custodianAccount == null)
            {
                return NotFound();
            }

            var custodian = _custodianRegistry.GetCustodianByCode(custodianAccount.CustodianCode);
            var configForm = await custodian.GetConfigForm(custodianAccount.GetBlob(), "en-US");

            var newData = new JObject();
            foreach (var pair in Request.Form)
            {
                newData.Add(pair.Key, pair.Value.ToString());
            }
            
            var filteredConfigData = RemoveUnusedFieldsFromConfig(custodianAccount.GetBlob(), newData, configForm);
            custodianAccount.SetBlob(filteredConfigData);

            custodianAccount = await _custodianAccountRepository.CreateOrUpdate(custodianAccount);
            
            vm.CustodianAccount = custodianAccount;
            vm.ConfigForm = configForm;
            return View(vm);
        }
        
        

        [HttpGet("/stores/{storeId}/custodian-accounts/create")]
        public IActionResult CreateCustodianAccount(string storeId)
        {
            var vm = new CreateCustodianAccountViewModel();
            vm.StoreId = storeId;
            vm.SetCustodianRegistry(_custodianRegistry);
            return View(vm);
        }

        [HttpPost("/stores/{storeId}/custodian-accounts/create")]
        public async Task<IActionResult> CreateCustodianAccount(string storeId, CreateCustodianAccountViewModel vm)
        {
            var store = GetCurrentStore();
            vm.StoreId = store.Id;
            vm.SetCustodianRegistry(_custodianRegistry);

            var custodian = _custodianRegistry.GetCustodianByCode(vm.SelectedCustodian);
            if (custodian == null)
            {
                ModelState.AddModelError(nameof(vm.SelectedCustodian), "Invalid Custodian");
            }
            else
            {
                if (string.IsNullOrEmpty(vm.Name))
                {
                    vm.Name = custodian.Name;
                }

                var custodianAccountData = new CustodianAccountData
                {
                    CustodianCode = vm.SelectedCustodian, StoreId = vm.StoreId, Name = custodian.Name
                };


                var configData = new JObject();
                foreach (var pair in Request.Form)
                {
                    configData.Add(pair.Key, pair.Value.ToString());
                }

                var configForm = await custodian.GetConfigForm(configData, "en-US");
                if (configForm.IsValid())
                {
                    // configForm.removeUnusedKeys();
                    custodianAccountData.SetBlob(configData);
                    custodianAccountData = await _custodianAccountRepository.CreateOrUpdate(custodianAccountData);
                    TempData[WellKnownTempData.SuccessMessage] = "Custodian account successfully created";
                    CreatedCustodianAccountId = custodianAccountData.Id;

                    return RedirectToAction(nameof(ViewCustodianAccount),
                        new { storeId = custodianAccountData.StoreId, accountId = custodianAccountData.Id });
                }

                // Ask for more data
                vm.ConfigForm = configForm;
            }
            return View(vm);
        }

        // The JObject may contain too much data because we used ALL post values and this may be more than we needed.
        // Because we don't know the form fields beforehand, we will filter out the superfluous data afterwards.
        // We will keep all the old keys + merge the new keys as per the current form.
        // Since the form can differ by circumstances, we will never remove any keys that were previously stored. We just limit what we add.
        private JObject RemoveUnusedFieldsFromConfig(JObject storedData, JObject newData, Form form)
        {
            JObject filteredData = new JObject();
            var storedKeys = new List<string>();
            foreach (var item in storedData)
            {
                storedKeys.Add(item.Key);
            }

            var formKeys = form.GetAllNames();

            foreach (var item in newData)
            {
                if (storedKeys.Contains(item.Key) || formKeys.Contains(item.Key) )
                {
                    filteredData[item.Key] = item.Value;
                }
            }
            
            return filteredData;
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
        private string GetUserId() => _userManager.GetUserId(User);

        private StoreData GetCurrentStore() => HttpContext.GetStoreData();

        // private AppData GetCurrentApp() => HttpContext.GetAppData();
    }
}
