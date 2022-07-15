using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Rating;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Controllers
{
    [Route("stores")]
    [AutoValidateAntiforgeryToken]
    public class UIUserStoresController : Controller
    {
        private readonly StoreRepository _repo;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RateFetcher _rateFactory;
        public string CreatedStoreId { get; set; }

        public UIUserStoresController(
            UserManager<ApplicationUser> userManager,
            StoreRepository storeRepository,
            RateFetcher rateFactory)
        {
            _repo = storeRepository;
            _userManager = userManager;
            _rateFactory = rateFactory;
        }

        [HttpGet("create")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanModifyStoreSettingsUnscoped)]
        public IActionResult CreateStore()
        {
            var vm = new CreateStoreViewModel
            {
                DefaultCurrency = StoreBlob.StandardDefaultCurrency,
                Exchanges = GetExchangesSelectList(CoinGeckoRateProvider.CoinGeckoName)
            };

            return View(vm);
        }

        [HttpPost("create")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanModifyStoreSettingsUnscoped)]
        public async Task<IActionResult> CreateStore(CreateStoreViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                vm.Exchanges = GetExchangesSelectList(vm.PreferredExchange);
                return View(vm);
            }
            
            var store = await _repo.CreateStore(GetUserId(), vm.Name, vm.DefaultCurrency, vm.PreferredExchange);
            CreatedStoreId = store.Id;
                
            TempData[WellKnownTempData.SuccessMessage] = "Store successfully created";
            return RedirectToAction(nameof(UIStoresController.Dashboard), "UIStores", new
            {
                storeId = store.Id
            });
        }

        [HttpGet("{storeId}/me/delete")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanModifyStoreSettings)]
        public IActionResult DeleteStore(string storeId)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();
            return View("Confirm", new ConfirmModel($"Delete store {store.StoreName}", "This store will still be accessible to users sharing it", "Delete"));
        }

        [HttpPost("{storeId}/me/delete")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanModifyStoreSettings)]
        public async Task<IActionResult> DeleteStorePost(string storeId)
        {
            var userId = GetUserId();
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();
            await _repo.RemoveStore(storeId, userId);
            TempData[WellKnownTempData.SuccessMessage] = "Store removed successfully";
            return RedirectToAction(nameof(UIHomeController.Index), "UIHome");
        }

        private string GetUserId() => _userManager.GetUserId(User);
        
        private SelectList GetExchangesSelectList(string selected) {
            var exchanges = _rateFactory.RateProviderFactory
                .GetSupportedExchanges()
                .Where(r => !string.IsNullOrWhiteSpace(r.Name))
                .OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase);
            var chosen = exchanges.FirstOrDefault(f => f.Id == selected) ?? exchanges.First();
            return new SelectList(exchanges, nameof(chosen.Id), nameof(chosen.Name), chosen.Id);
        }
    }
}
