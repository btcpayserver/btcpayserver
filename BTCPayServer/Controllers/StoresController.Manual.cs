using System;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments;
using Microsoft.AspNetCore.Mvc;
using BTCPayServer.Payments.Lightning;
using System.Net;
using BTCPayServer.Data;
using System.Threading;
using BTCPayServer.Lightning;
using BTCPayServer.Payments.Bitcoin;

namespace BTCPayServer.Controllers
{
    public class UpdateManualPaymentSettings
    {
        public bool Enabled { get; set; }
        public string StatusMessage { get; set; }
        public ManualPaymentSettings ToSettings()
        {
            return new ManualPaymentSettings();
        }
    }
    
    public partial class StoresController
    {
        [HttpGet]
        [Route("{storeId}/manual")]
        public IActionResult UpdateManualSettings(string storeId)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();
            var vm = new UpdateManualPaymentSettings();
            SetExistingValues(store, vm);
            return View(vm);
        }

        private void SetExistingValues(StoreData store, UpdateManualPaymentSettings vm)
                 {
                     var existing = GetExistingManualPaymentSettings(store);
                     vm.Enabled = existing != null && !store.GetStoreBlob().IsExcluded(ManualPaymentSettings.StaticPaymentId);
                 }
        private ManualPaymentSettings GetExistingManualPaymentSettings(StoreData store)
        {
            return store.GetSupportedPaymentMethods(_NetworkProvider)
                .OfType<ManualPaymentSettings>()
                .FirstOrDefault();
        }

        [HttpPost]
        [Route("{storeId}/manual")]
        public async Task<IActionResult> UpdateManualSettings(string storeId, UpdateManualPaymentSettings vm)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();


            if (!ModelState.IsValid)
            {
                return View(vm);
            }
            var storeBlob = store.GetStoreBlob();
            storeBlob.SetExcluded(ManualPaymentSettings.StaticPaymentId, !vm.Enabled);
            store.SetStoreBlob(storeBlob);
            store.SetSupportedPaymentMethod(ManualPaymentSettings.StaticPaymentId, vm.ToSettings());
            await _Repo.UpdateStore(store);
            StatusMessage = $"Manual payment settings modified";
            return RedirectToAction(nameof(UpdateStore), new { storeId = storeId });
        }
    }
}
