using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
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
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [AutoValidateAntiforgeryToken]
    public partial class UserStoresController : Controller
    {
        private readonly StoreRepository _Repo;
        private readonly BTCPayNetworkProvider _NetworkProvider;
        private readonly UserManager<ApplicationUser> _UserManager;

        public UserStoresController(
            UserManager<ApplicationUser> userManager,
            BTCPayNetworkProvider networkProvider,
            StoreRepository storeRepository)
        {
            _Repo = storeRepository;
            _NetworkProvider = networkProvider;
            _UserManager = userManager;
        }

        [HttpGet]
        [Route("create")]
        public IActionResult CreateStore()
        {
            return View();
        }

        [HttpPost]
        [Route("create")]
        public async Task<IActionResult> CreateStore(CreateStoreViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                return View(vm);
            }
            var store = await _Repo.CreateStore(GetUserId(), vm.Name);
            CreatedStoreId = store.Id;
            TempData[WellKnownTempData.SuccessMessage] = "Store successfully created";
            return RedirectToAction(nameof(StoresController.UpdateStore), "Stores", new
            {
                storeId = store.Id
            });
        }

        public string CreatedStoreId
        {
            get; set;
        }

        [HttpGet]
        [Route("{storeId}/me/delete")]
        public IActionResult DeleteStore(string storeId)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();
            return View("Confirm", new ConfirmModel()
            {
                Title = "Delete store " + store.StoreName,
                Description = "This store will still be accessible to users sharing it",
                Action = "Delete"
            });
        }

        [HttpPost]
        [Route("{storeId}/me/delete")]
        public async Task<IActionResult> DeleteStorePost(string storeId)
        {
            var userId = GetUserId();
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();
            await _Repo.RemoveStore(storeId, userId);
            TempData[WellKnownTempData.SuccessMessage] = "Store removed successfully";
            return RedirectToAction(nameof(ListStores));
        }

        [HttpGet]
        public async Task<IActionResult> ListStores(
            string sortOrder = null,
            string sortOrderColumn = null
        )
        {
            StoresViewModel result = new StoresViewModel();
            var stores = await _Repo.GetStoresByUserId(GetUserId());
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
                    HintWalletWarning = blob.Hints.Wallet
                });
            }
            return View(result);
        }

        private string GetUserId()
        {
            return _UserManager.GetUserId(User);
        }
    }
}
