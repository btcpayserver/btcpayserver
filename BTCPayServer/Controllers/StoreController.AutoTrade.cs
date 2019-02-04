using BTCPayServer.Data;
using BTCPayServer.Models.StoreViewModels;
using static BTCPayServer.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.Threading.Tasks;
using BTCPayServer.Payments.AutoTrade;

namespace BTCPayServer.Controllers
{
    public partial class StoresController
    {
        [HttpGet]
        [Route("(storeId/autotrade/exchangeName)")]
        public IActionResult UpdateAutoTradeSettings(string storeId, string exchangeName)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();
            UpdateAutoTradeSettingsViewModel vm = new UpdateAutoTradeSettingsViewModel();
            UpdateAutoTradeSettingsVMFromValues(store, vm, exchangeName);
            return View(vm);
        }

        private void UpdateAutoTradeSettingsVMFromValues(StoreData store, UpdateAutoTradeSettingsViewModel vm, string exchangeName)
        {
            var existing = store.GetStoreBlob().AutoTradeExchangeSettings;
            if (existing == null) return;

            vm.ApiKey = existing.ApiKey;
            vm.ApiSecret = existing.ApiSecret;
            vm.ApiUrl = existing.ApiUrl;
            vm.Enabled = existing.Enabled;
            vm.AmountMarkupPercentage = existing.AmountMarkupPercentage;
        }

        [HttpPost]
        [Route("{storeId}/autotrade/exchangeName")]
        public async Task<IActionResult> UpdateAutoTradeSettings(string storeId, UpdateAutoTradeSettingsViewModel vm, string exchangeName, string cmd)
        {
            var store = HttpContext.GetStoreData(); ;
            if (store == null)
                return NotFound();

            if (vm.Enabled)
            {
                if (!ModelState.IsValid)
                {
                    return View(vm);
                }
            }

            var settings = _autoTradeProvider.GetSettings(exchangeName);
            settings.ApiKey = vm.ApiKey;
            settings.ApiSecret = vm.ApiSecret;
            settings.ApiUrl = vm.ApiUrl;
            settings.Enabled = vm.Enabled;
            settings.AmountMarkupPercentage = vm.AmountMarkupPercentage;

            switch (cmd)
            {
                case "save":
                    var storeBlob = store.GetStoreBlob();
                    storeBlob.AutoTradeExchangeSettings = settings;
                    store.SetStoreBlob(storeBlob);
                    await _Repo.UpdateStore(store);
                    StatusMessage = "Auto trade settings modified";
                    return View(vm);
                case "test":
                default:
                    return View(vm);
            }
        }
    }
}
