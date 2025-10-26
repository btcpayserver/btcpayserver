using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Services;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Controllers
{
    [Route("stores")]
    [AutoValidateAntiforgeryToken]
    public class UIUserStoresController : Controller
    {
        private readonly StoreRepository _repo;
        private readonly IStringLocalizer StringLocalizer;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly DefaultRulesCollection _defaultRules;
        private readonly RateFetcher _rateFactory;
        public string CreatedStoreId { get; set; }

        public UIUserStoresController(
            UserManager<ApplicationUser> userManager,
			DefaultRulesCollection defaultRules,
            StoreRepository storeRepository,
            IStringLocalizer stringLocalizer,
            RateFetcher rateFactory)
        {
            _repo = storeRepository;
            StringLocalizer = stringLocalizer;
            _userManager = userManager;
            _defaultRules = defaultRules;
            _rateFactory = rateFactory;
        }

        [HttpGet]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanModifyStoreSettingsUnscoped)]
        public async Task<IActionResult> ListStores(bool archived = false)
        {
            var stores = await _repo.GetStoresByUserId(GetUserId());
            var vm = new ListStoresViewModel
            {
                Stores = stores
                    .Where(s => s.Archived == archived)
                    .OrderBy(s => s.StoreName, StringComparer.InvariantCultureIgnoreCase)
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
            var defaultTemplate = await _repo.GetDefaultStoreTemplate();
            var blob = defaultTemplate.GetStoreBlob();
            var vm = new CreateStoreViewModel
            {
                Name = defaultTemplate.StoreName,
                IsFirstStore = !(stores.Any() || skipWizard),
                DefaultCurrency = blob.DefaultCurrency,
                Exchanges = GetExchangesSelectList(blob.DefaultCurrency, null),
                CanEditPreferredExchange = blob.GetRateSettings(false)?.RateScripting is not true,
                PreferredExchange = blob.GetRateSettings(false)?.PreferredExchange
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
                var template = await _repo.GetDefaultStoreTemplate();
                var defaultCurrency = template.GetStoreBlob().DefaultCurrency ?? StoreBlob.StandardDefaultCurrency;
                vm.Exchanges = GetExchangesSelectList(defaultCurrency, null);
                return View(vm);
            }

            var store = await _repo.GetDefaultStoreTemplate();
            store.StoreName = vm.Name;
            var blob = store.GetStoreBlob();
            blob.DefaultCurrency = vm.DefaultCurrency;
            if (vm.CanEditPreferredExchange)
            {
                var rate = blob.GetOrCreateRateSettings(false);
                rate.PreferredExchange = vm.PreferredExchange;
                rate.RateScripting = false;
            }
            store.SetStoreBlob(blob);
            await _repo.CreateStore(GetUserId(), store);
            CreatedStoreId = store.Id;
            TempData.SetStatusSuccess(StringLocalizer["Store successfully created"]);
            return RedirectToAction(nameof(UIStoresController.Index), "UIStores", new
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
            return View("Confirm", new ConfirmModel(StringLocalizer["Delete store {0}", store.StoreName], StringLocalizer["This store will still be accessible to users sharing it"], StringLocalizer["Delete"]));
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
            TempData.SetStatusSuccess(StringLocalizer["Store removed successfully"]);
            return RedirectToAction(nameof(UIHomeController.Index), "UIHome");
        }

        private string GetUserId() => _userManager.GetUserId(User);

		internal SelectList GetExchangesSelectList(string defaultCurrency, StoreBlob.RateSettings rateSettings)
		{
			if (rateSettings is null)
                rateSettings = new ();
			var defaultExchange = _defaultRules.GetRecommendedExchange(defaultCurrency);
			var exchanges = _rateFactory.RateProviderFactory
				.AvailableRateProviders
				.OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
				.ToList();
			var exchange = exchanges.First(e => e.Id == defaultExchange);
			exchanges.Insert(0, new(null, StringLocalizer["Recommendation ({0})", exchange.DisplayName], ""));
			var chosen = exchanges.FirstOrDefault(f => f.Id == rateSettings.PreferredExchange) ?? exchanges.First();
			return new SelectList(exchanges, nameof(chosen.Id), nameof(chosen.DisplayName), chosen.Id);
		}
	}
}
