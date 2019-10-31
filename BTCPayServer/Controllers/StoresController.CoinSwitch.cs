using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments.CoinSwitch;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    public partial class StoresController
    {
        [HttpGet]
        [Route("{storeId}/coinswitch")]
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

            var existing = store.GetStoreBlob().CoinSwitchSettings;
            if (existing == null) return;
            vm.MerchantId = existing.MerchantId;
            vm.Enabled = existing.Enabled;
            vm.Mode = existing.Mode;
            vm.AmountMarkupPercentage = existing.AmountMarkupPercentage;
        }

        [HttpPost]
        [Route("{storeId}/coinswitch")]
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
                MerchantId =  vm.MerchantId,
                Enabled = vm.Enabled,
                Mode = vm.Mode,
                AmountMarkupPercentage = vm.AmountMarkupPercentage
            };
            
            switch (command)
            {
                case "save":
                    var storeBlob = store.GetStoreBlob();
                    storeBlob.CoinSwitchSettings = coinSwitchSettings;
                    store.SetStoreBlob(storeBlob);
                    await _Repo.UpdateStore(store);
                    TempData[WellKnownTempData.SuccessMessage] = "CoinSwitch settings modified";
                    return RedirectToAction(nameof(UpdateStore), new {
                        storeId});

                default:
                    return View(vm);
            }
        }
    }
}
