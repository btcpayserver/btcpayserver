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
    public class UserStoresController : Controller
    {
        private readonly StoreRepository _repo;
        private readonly UserManager<ApplicationUser> _userManager;

        public UserStoresController(
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
            return RedirectToAction(nameof(StoresController.Dashboard), "Stores", new
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
            return RedirectToAction(nameof(ListStores));
        }

        [HttpGet]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanModifyStoreSettingsUnscoped)]
        public async Task<IActionResult> ListStores(
            string sortOrder = null,
            string sortOrderColumn = null
        )
        {
            StoresViewModel result = new StoresViewModel();
            var stores = await _repo.GetStoresByUserId(GetUserId());
            if (sortOrder != null && sortOrderColumn != null)
            {
                stores = stores.OrderByDescending(store =>
                    {
                        switch (sortOrderColumn)
                        {
                            case nameof(store.StoreName):
                                return store.StoreName;
                            case nameof(store.StoreWebsite):
                                return store.StoreWebsite;
                            default:
                                return store.Id;
                        }
                    }).ToArray();

                switch (sortOrder)
                {
                    case "desc":
                        ViewData[$"{sortOrderColumn}SortOrder"] = "asc";
                        break;
                    case "asc":
                        stores = stores.Reverse().ToArray();
                        ViewData[$"{sortOrderColumn}SortOrder"] = "desc";
                        break;
                }
            }

            for (int i = 0; i < stores.Length; i++)
            {
                var store = stores[i];
                var blob = store.GetStoreBlob();
                result.Stores.Add(new StoresViewModel.StoreViewModel()
                {
                    Id = store.Id,

                    Name = store.StoreName,
                    WebSite = store.StoreWebsite,
                    IsOwner = store.Role == StoreRoles.Owner,
                    HintWalletWarning = blob.Hints.Wallet && blob.Hints.Lightning
                });
            }
            return View(result);
        }

        private string GetUserId() => _userManager.GetUserId(User);
    }
}
