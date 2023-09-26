using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
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

        [HttpGet()]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanModifyStoreSettingsUnscoped)]
        public async Task<IActionResult> ListStores(bool archived = false)
        {
            var stores = await _repo.GetStoresByUserId(GetUserId());
            var vm = new ListStoresViewModel
            {
                Stores = stores
                    .Where(s => s.Archived == archived)
                    .Select(s => new ListStoresViewModel.StoreViewModel
                    {
                        StoreId = s.Id,
                        StoreName = s.StoreName,
                        Archived = s.Archived
                    }).ToList(),
                Archived = archived
            };
            return View(vm);
        }

        [HttpGet("create")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanModifyStoreSettingsUnscoped)]
        public async Task<IActionResult> CreateStore(bool skipWizard)
        {
            var stores = await _repo.GetStoresByUserId(GetUserId());
            var vm = new CreateStoreViewModel
            {
                IsFirstStore = !(stores.Any() || skipWizard),
                DefaultCurrency = StoreBlob.StandardDefaultCurrency,
                Exchanges = GetExchangesSelectList(null)
            };

            return View(vm);
        }

        [HttpPost("create")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanModifyStoreSettingsUnscoped)]
        public async Task<IActionResult> CreateStore(CreateStoreViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                var stores = await _repo.GetStoresByUserId(GetUserId());
                vm.IsFirstStore = !stores.Any();
                vm.Exchanges = GetExchangesSelectList(vm.PreferredExchange);
                return View(vm);
            }

            var store = new StoreData { StoreName = vm.Name };
            var blob = store.GetStoreBlob();
            blob.DefaultCurrency = vm.DefaultCurrency;
            blob.PreferredExchange = vm.PreferredExchange;
            store.SetStoreBlob(blob);
            await _repo.CreateStore(GetUserId(), store);
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

        private SelectList GetExchangesSelectList(string selected)
        {
            var exchanges = _rateFactory.RateProviderFactory
                .AvailableRateProviders
                .OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
            exchanges.Insert(0, new (null, "Recommended", ""));
            var chosen = exchanges.FirstOrDefault(f => f.Id == selected) ?? exchanges.First();
            return new SelectList(exchanges, nameof(chosen.Id), nameof(chosen.DisplayName), chosen.Id);
        }
    }
}
