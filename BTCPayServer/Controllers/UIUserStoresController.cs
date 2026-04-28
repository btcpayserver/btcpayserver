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
    public class UIUserStoresController : Controller
    {
        private readonly StoreRepository _repo;
        private readonly IStringLocalizer StringLocalizer;
        private readonly DefaultRulesCollection _defaultRules;
        private readonly RateFetcher _rateFactory;
        private readonly PoliciesSettings _policiesSettings;
        private readonly UserManager<ApplicationUser> _userManager;
        public string CreatedStoreId { get; set; }

        public UIUserStoresController(
			DefaultRulesCollection defaultRules,
            StoreRepository storeRepository,
            IStringLocalizer stringLocalizer,
            RateFetcher rateFactory,
            PoliciesSettings policiesSettings,
            UserManager<ApplicationUser> userManager)
        {
            _repo = storeRepository;
            StringLocalizer = stringLocalizer;
            _defaultRules = defaultRules;
            _rateFactory = rateFactory;
            _policiesSettings = policiesSettings;
            _userManager = userManager;
        }

        [HttpGet]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanViewStoreSettings)]
        public IActionResult ListStores(bool archived = false)
        {
            var stores = HttpContext.GetStoresData();
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
            var userId = User.GetId();
            if (!User.IsInRole(Roles.ServerAdmin))
            {
                var limit = await GetEffectiveStoreLimitAsync();
                if (limit.HasValue)
                {
                    var count = await _repo.CountStoresByUserId(userId);
                    if (count >= limit.Value)
                    {
                        TempData.SetStatusMessageModel(new StatusMessageModel
                        {
                            Severity = StatusMessageModel.StatusSeverity.Error,
                            Message = limit.Value == 0
                                ? StringLocalizer["Store creation is not allowed on this server."].Value
                                : StringLocalizer["You have reached the maximum number of stores allowed ({0}).", limit.Value].Value
                        });
                        return RedirectToAction(nameof(ListStores));
                    }
                }
            }

            var stores = await _repo.GetStoresByUserId(userId);
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
            var userId = User.GetId();
            if (!User.IsInRole(Roles.ServerAdmin))
            {
                var limit = await GetEffectiveStoreLimitAsync();
                if (limit.HasValue)
                {
                    var count = await _repo.CountStoresByUserId(userId);
                    if (count >= limit.Value)
                    {
                        ModelState.AddModelError(string.Empty, limit.Value == 0
                            ? StringLocalizer["Store creation is not allowed on this server."].Value
                            : StringLocalizer["You have reached the maximum number of stores allowed ({0}).", limit.Value].Value);
                    }
                }
            }

            if (!ModelState.IsValid)
            {
                var stores = await _repo.GetStoresByUserId(userId);
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
            await _repo.CreateStore(userId, store);
            CreatedStoreId = store.Id;
            TempData.SetStatusSuccess(StringLocalizer["Store successfully created"]);
            return RedirectToAction(nameof(UIStoresController.Index), "UIStores", new
            {
                storeId = store.Id
            });
        }

        private async Task<int?> GetEffectiveStoreLimitAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            var blob = user?.GetBlob();
            return blob?.StoreQuota ?? _policiesSettings.NonAdminMaxStores;
        }

        [HttpGet("{storeId}/me/delete")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanModifyStoreSettings)]
        public IActionResult DeleteStore(string storeId)
        {
            var store = HttpContext.GetStoreDataOrNull();
            if (store == null)
                return NotFound();
            return View("Confirm", new ConfirmModel(StringLocalizer["Delete store {0}", store.StoreName], StringLocalizer["This store will still be accessible to users sharing it"], StringLocalizer["Delete"]));
        }

        [HttpPost("{storeId}/me/delete")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanModifyStoreSettings)]
        public async Task<IActionResult> DeleteStorePost(string storeId)
        {
            var store = HttpContext.GetStoreDataOrNull();
            if (store == null)
                return NotFound();
            await _repo.RemoveStore(storeId, User.GetId());
            TempData.SetStatusSuccess(StringLocalizer["Store removed successfully"]);
            return RedirectToAction(nameof(UIHomeController.Index), "UIHome");
        }

		internal SelectList GetExchangesSelectList(string defaultCurrency, StoreBlob.RateSettings rateSettings)
		{
			if (rateSettings is null)
                rateSettings = new ();
			var defaultExchange = _defaultRules.GetRecommendedExchange(defaultCurrency);
			var exchanges = _rateFactory.RateProviderFactory
				.AvailableRateProviders
				.OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
				.ToList();
			var exchange = exchanges.FirstOrDefault(e => e.Id == defaultExchange);
			exchanges.Insert(0, new(null, StringLocalizer["Recommendation ({0})", exchange?.DisplayName ?? ""], ""));
			var chosen = exchanges.FirstOrDefault(f => f.Id == rateSettings.PreferredExchange) ?? exchanges.First();
			return new SelectList(exchanges, nameof(chosen.Id), nameof(chosen.DisplayName), chosen.Id);
		}
	}
}
