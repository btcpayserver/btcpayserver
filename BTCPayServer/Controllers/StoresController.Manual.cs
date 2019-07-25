using System;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
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
using Org.BouncyCastle.Asn1.X509;

namespace BTCPayServer.Controllers
{
    public class UpdateManualPaymentSettings
    {
        public bool Enabled { get; set; }
        public string StatusMessage { get; set; }
        [Display(Name = "Display Text")]
        public string DisplayText { get; set; } = string.Empty;
        [Display(Name = "Allow Customer To Mark Paid (otherwise only store admin)")]
        public bool AllowCustomerToMarkPaid { get; set; } = false;
        [Display(Name = "Allow a partial payment to be registered")]
        public bool AllowPartialPaymentInput { get; set; } = false;
        
        [Display(Name = "Allow a note to be specified with the payment")]
        public bool AllowPaymentNote { get; set; } = false;
        
        [Display(Name = "Set payment to confirmed( instead of Paid")]
        public bool SetPaymentAsConfirmed { get; set; } = true;

        public ManualPaymentSettings ToSettings()
        {
            return new ManualPaymentSettings()
            {
                AllowCustomerToMarkPaid = AllowCustomerToMarkPaid,
                DisplayText = DisplayText,
                AllowPaymentNote = AllowPaymentNote,
                AllowPartialPaymentInput = AllowPartialPaymentInput,
                SetPaymentAsConfirmed = SetPaymentAsConfirmed
            };
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
            if (existing == null)
            {
                return;
            }
            vm.AllowCustomerToMarkPaid = existing.AllowCustomerToMarkPaid;
            vm.DisplayText = existing.DisplayText;
            vm.AllowPaymentNote = existing.AllowPaymentNote;
            vm.AllowPartialPaymentInput = existing.AllowPartialPaymentInput;
            vm.SetPaymentAsConfirmed = existing.SetPaymentAsConfirmed;
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
            return RedirectToAction(nameof(UpdateStore), new {storeId = storeId});
        }
    }
}
