using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Security;
using BTCPayServer.Services.Stores;
using ExchangeSharp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    [Route("stores")]
    [AutoValidateAntiforgeryToken]
    public class UIUserStoresController : Controller
    {
        private readonly StoreRepository _repo;
        private readonly UserManager<ApplicationUser> _userManager;

        public UIUserStoresController(
            UserManager<ApplicationUser> userManager,
            StoreRepository storeRepository)
        {
            _repo = storeRepository;
            _userManager = userManager;
        }

        [HttpGet("create")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanModifyStoreSettingsUnscoped)]
        public IActionResult CreateStore()
        {
            return View();
        }

        [HttpPost("create")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanModifyStoreSettingsUnscoped)]
        public async Task<IActionResult> CreateStore(CreateStoreViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                return View(vm);
            }
            var store = await _repo.CreateStore(GetUserId(), vm.Name);
            CreatedStoreId = store.Id;
            TempData[WellKnownTempData.SuccessMessage] = "Store successfully created";
            return RedirectToAction(nameof(UIStoresController.Dashboard), "UIStores", new
            {
                storeId = store.Id
            });
        }

        public string CreatedStoreId
        {
            get; set;
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
    }
}
