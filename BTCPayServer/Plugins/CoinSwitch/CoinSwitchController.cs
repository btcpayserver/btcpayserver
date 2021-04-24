using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.CoinSwitch
{
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Route("plugins/{storeId}/coinswitch")]
    public class CoinSwitchController : Controller
    {
        private readonly StoreRepository _storeRepository;

        public CoinSwitchController(StoreRepository storeRepository)
        {
            _storeRepository = storeRepository;
        }

        [HttpGet("")]
        public IActionResult UpdateCoinSwitchSettings(string storeId)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();
            UpdateCoinSwitchSettingsViewModel vm = new UpdateCoinSwitchSettingsViewModel();
            SetExistingValues(store, vm);
            return View(vm);
        }

        private void SetExistingValues(StoreData store, UpdateCoinSwitchSettingsViewModel vm)
        {
            var existing = store.GetStoreBlob().GetCoinSwitchSettings();
            if (existing == null)
                return;
            vm.MerchantId = existing.MerchantId;
            vm.Enabled = existing.Enabled;
            vm.Mode = existing.Mode;
            vm.AmountMarkupPercentage = existing.AmountMarkupPercentage;
        }

        [HttpPost("")]
        public async Task<IActionResult> UpdateCoinSwitchSettings(string storeId, UpdateCoinSwitchSettingsViewModel vm,
            string command)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();
            if (vm.Enabled)
            {
                if (!ModelState.IsValid)
                {
                    return View(vm);
                }
            }

            var coinSwitchSettings = new CoinSwitchSettings()
            {
                MerchantId = vm.MerchantId,
                Enabled = vm.Enabled,
                Mode = vm.Mode,
                AmountMarkupPercentage = vm.AmountMarkupPercentage
            };

            switch (command)
            {
                case "save":
                    var storeBlob = store.GetStoreBlob();
                    storeBlob.SetCoinSwitchSettings(coinSwitchSettings);
                    store.SetStoreBlob(storeBlob);
                    await _storeRepository.UpdateStore(store);
                    TempData[WellKnownTempData.SuccessMessage] = "CoinSwitch settings modified";
                    return RedirectToAction(nameof(UpdateCoinSwitchSettings), new {storeId});

                default:
                    return View(vm);
            }
        }
    }
}
