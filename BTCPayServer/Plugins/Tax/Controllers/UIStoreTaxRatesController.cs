#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.Tax.Controllers
{
    [Route("stores/{storeId}/tax-rates")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Area(TaxPlugin.Area)]
    public class UIStoreTaxRatesController(StoreRepository storeRepository) : Controller
    {
        private StoreData CurrentStore => HttpContext.GetStoreData();

        private static List<StoreTaxRate> SortForDisplay(List<StoreTaxRate>? rates) =>
            (rates ?? new()).OrderByDescending(r => r.IsDefault).ThenBy(r => r.Name).ToList();

        [HttpGet("")]
        public IActionResult TaxRates()
        {
            var blob = CurrentStore.GetStoreBlob();
            var model = new StoreTaxRatesViewModel { TaxRates = SortForDisplay(blob.TaxRates) };
            return View(model);
        }

        [HttpPost("")]
        public async Task<IActionResult> AddOrUpdateTaxRate(StoreTaxRateEditViewModel model)
        {
            var blob = CurrentStore.GetStoreBlob();

            if (!ModelState.IsValid)
            {
                return View(nameof(TaxRates), new StoreTaxRatesViewModel { TaxRates = SortForDisplay(blob.TaxRates) });
            }

            blob.TaxRates ??= new();
            var existing = string.IsNullOrEmpty(model.Id) ? null : blob.TaxRates.FirstOrDefault(r => r.Id == model.Id);

            if (existing != null)
            {
                existing.Name = model.Name!;
                existing.Rate = model.Rate;
            }
            else
            {
                blob.TaxRates.Add(new StoreTaxRate
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = model.Name!,
                    Rate = model.Rate,
                    IsDefault = blob.TaxRates.Count == 0
                });
            }

            CurrentStore.SetStoreBlob(blob);
            await storeRepository.UpdateStore(CurrentStore);
            TempData[WellKnownTempData.SuccessMessage] = "Tax rate saved";
            return RedirectToAction(nameof(TaxRates), new { storeId = CurrentStore.Id });
        }

        [HttpPost("{taxRateId}/set-default")]
        public async Task<IActionResult> SetDefault(string taxRateId)
        {
            var storeBlob = CurrentStore.GetStoreBlob();
            if (storeBlob.TaxRates != null)
            {
                foreach (var r in storeBlob.TaxRates)
                    r.IsDefault = r.Id == taxRateId;

                CurrentStore.SetStoreBlob(storeBlob);
                await storeRepository.UpdateStore(CurrentStore);
            }
            TempData[WellKnownTempData.SuccessMessage] = "Default tax rate updated";
            return RedirectToAction(nameof(TaxRates), new { storeId = CurrentStore.Id });
        }

        [HttpPost("{taxRateId}/delete")]
        public async Task<IActionResult> DeleteTaxRate(string taxRateId)
        {
            var storeBlob = CurrentStore.GetStoreBlob();
            storeBlob.TaxRates?.RemoveAll(r => r.Id == taxRateId);
            CurrentStore.SetStoreBlob(storeBlob);
            await storeRepository.UpdateStore(CurrentStore);
            TempData[WellKnownTempData.SuccessMessage] = "Tax rate deleted";
            return RedirectToAction(nameof(TaxRates), new { storeId = CurrentStore.Id });
        }
    }
}
