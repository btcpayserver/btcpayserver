using System;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Models.StoreViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.CoinSwitch
{
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Route("plugins/{storeId}/coinswitch")]
    public class CoinSwitchController : Controller
    {
        private readonly BTCPayServerClient _btcPayServerClient;

        public CoinSwitchController(BTCPayServerClient btcPayServerClient)
        {
            _btcPayServerClient = btcPayServerClient;
        }

        [HttpGet("")]
        public async Task<IActionResult> UpdateCoinSwitchSettings(string storeId)
        {
            var store = await _btcPayServerClient.GetStore(storeId);

            UpdateCoinSwitchSettingsViewModel vm = new UpdateCoinSwitchSettingsViewModel();
            vm.StoreName = store.Name;
            CoinSwitchSettings coinswitch = null;
            try
            {
                coinswitch = (await _btcPayServerClient.GetStoreAdditionalDataKey(storeId, CoinSwitchPlugin.StoreBlobKey))
                    .ToObject<CoinSwitchSettings>();
            }
            catch (Exception e)
            {
                // ignored
            }

            SetExistingValues(coinswitch, vm);
            return View(vm);
        }

        private void SetExistingValues(CoinSwitchSettings existing, UpdateCoinSwitchSettingsViewModel vm)
        {
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
            var store = await _btcPayServerClient.GetStore(storeId);
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
                    await _btcPayServerClient.UpdateStoreAdditionalDataKey(storeId, CoinSwitchPlugin.StoreBlobKey,
                        JObject.FromObject(coinSwitchSettings));
                    TempData["SuccessMessage"] = "CoinSwitch settings modified";
                    return RedirectToAction(nameof(UpdateCoinSwitchSettings), new {storeId});

                default:
                    return View(vm);
            }
        }
    }
}
