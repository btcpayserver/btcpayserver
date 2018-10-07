using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Changelly;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    public partial class StoresController
    {
        [HttpGet]
        [Route("{storeId}/changelly")]
        public IActionResult UpdateChangellySettings(string storeId)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();
            UpdateChangellySettingsViewModel vm = new UpdateChangellySettingsViewModel();
            SetExistingValues(store, vm);
            SetTargetPaymentMethods(store, vm);
            return View(vm);
        }

        private void SetTargetPaymentMethods(StoreData store, UpdateChangellySettingsViewModel vm)
        {
            var excludedPaymentMethods =
                store.GetStoreBlob().GetExcludedPaymentMethods();
            var btcLikePaymentMethods = store.GetSupportedPaymentMethods(_NetworkProvider).OfType<DerivationStrategy>()
                .Where(
                    strategy => !excludedPaymentMethods.Match(strategy.PaymentId));
            vm.AvailableTargetPaymentMethods = btcLikePaymentMethods;
        }

        private void SetExistingValues(StoreData store, UpdateChangellySettingsViewModel vm)
        {
            var existing = GetExistingThirdPartySupportedPaymentMethod(store);
            if (existing != null)
            {
                vm.ApiKey = existing.ApiKey;
                vm.ApiSecret = existing.ApiSecret;
                vm.ApiUrl = existing.ApiUrl;
                vm.TargetCryptoCode = existing.Target?.CryptoCode;
            }

            vm.Enabled = !store.GetStoreBlob()
                .IsExcluded(ChangellySupportedPaymentMethod.ChangellySupportedPaymentMethodId);
        }

        private ChangellySupportedPaymentMethod GetExistingThirdPartySupportedPaymentMethod(StoreData store)
        {
            var existing = store.GetSupportedPaymentMethods(_NetworkProvider)
                .OfType<ChangellySupportedPaymentMethod>()
                .FirstOrDefault(d => d.PaymentId == ChangellySupportedPaymentMethod.ChangellySupportedPaymentMethodId);
            return existing;
        }

        [HttpPost]
        [Route("{storeId}/changelly")]
        public async Task<IActionResult> UpdateChangellySettings(string storeId, UpdateChangellySettingsViewModel vm,
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

            var paymentMethod = new ChangellySupportedPaymentMethod();
            paymentMethod.ApiKey = vm.ApiKey;
            paymentMethod.ApiSecret = vm.ApiSecret;
            paymentMethod.ApiUrl = vm.ApiUrl;
            paymentMethod.Target = new PaymentMethodId(vm.TargetCryptoCode, PaymentTypes.BTCLike);
            SetTargetPaymentMethods(store, vm);
            switch (command)
            {
                case "save":
                    var storeBlob = store.GetStoreBlob();
                    storeBlob.SetExcluded(ChangellySupportedPaymentMethod.ChangellySupportedPaymentMethodId,
                        !vm.Enabled);
                    store.SetStoreBlob(storeBlob);
                    store.SetSupportedPaymentMethod(ChangellySupportedPaymentMethod.ChangellySupportedPaymentMethodId,
                        paymentMethod);
                    await _Repo.UpdateStore(store);
                    StatusMessage = "Changelly settings modified";
                    return RedirectToAction(nameof(UpdateStore), new {
                        storeId});
                case "test":
                    try
                    {
                        var client = new Changelly.Changelly(paymentMethod.ApiKey, paymentMethod.ApiSecret,
                            paymentMethod.ApiUrl);
                        var result = client.GetCurrenciesFull();
                        vm.StatusMessage = string.IsNullOrEmpty(result.Error)
                            ? $"Error: {result.Error}"
                            : "Test Successful";
                        return View(vm);
                    }
                    catch (Exception ex)
                    {
                        vm.StatusMessage = $"Error: {ex.Message}";
                        return View(vm);
                    }

                    break;
                default:
                    return View(vm);
            }
        }
    }
}
