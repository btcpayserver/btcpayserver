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
        private readonly SettingsRepository _settingsRepository;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly DefaultRulesCollection _defaultRules;
        private readonly RateFetcher _rateFactory;
        public string CreatedStoreId { get; set; }

        public UIUserStoresController(
            UserManager<ApplicationUser> userManager,
			DefaultRulesCollection defaultRules,
            StoreRepository storeRepository,
            IStringLocalizer stringLocalizer,
            RateFetcher rateFactory,
            SettingsRepository settingsRepository)
        {
            _repo = storeRepository;
            StringLocalizer = stringLocalizer;
            _userManager = userManager;
            _defaultRules = defaultRules;
            _rateFactory = rateFactory;
            _settingsRepository = settingsRepository;
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
                DefaultCurrency = (await _settingsRepository.GetSettingAsync<PoliciesSettings>())?.DefaultCurrency ?? StoreBlob.StandardDefaultCurrency,
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
                vm.Exchanges = GetExchangesSelectList(null);
                return View(vm);
            }

            var store = new StoreData { StoreName = vm.Name };
            var blob = store.GetStoreBlob();
            blob.DefaultCurrency = vm.DefaultCurrency;
            blob.PreferredExchange = vm.PreferredExchange;
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
            return View("Confirm", new ConfirmModel(StringLocalizer["Delete store {0}", store.StoreName], StringLocalizer["This store will still be accessible to users sharing it"], "Delete"));
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

		internal SelectList GetExchangesSelectList(StoreBlob storeBlob)
		{
			if (storeBlob is null)
				storeBlob = new StoreBlob();
			var defaultExchange = _defaultRules.GetRecommendedExchange(storeBlob.DefaultCurrency);
			var exchanges = _rateFactory.RateProviderFactory
				.AvailableRateProviders
				.OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
				.ToList();
			var exchange = exchanges.First(e => e.Id == defaultExchange);
			exchanges.Insert(0, new(null, StringLocalizer["Recommendation ({0})", exchange.DisplayName], ""));
			var chosen = exchanges.FirstOrDefault(f => f.Id == storeBlob.PreferredExchange) ?? exchanges.First();
			return new SelectList(exchanges, nameof(chosen.Id), nameof(chosen.DisplayName), chosen.Id);
		}
	}
}
