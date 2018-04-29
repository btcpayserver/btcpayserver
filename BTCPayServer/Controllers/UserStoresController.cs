using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Models;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Security;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NBXplorer.DerivationStrategy;

namespace BTCPayServer.Controllers
{
    [Route("stores")]
    [Authorize(AuthenticationSchemes = "Identity.Application")]
    [AutoValidateAntiforgeryToken]
    public partial class UserStoresController : Controller
    {
        private StoreRepository _Repo;
        private BTCPayNetworkProvider _NetworkProvider;
        private UserManager<ApplicationUser> _UserManager;
        private BTCPayWalletProvider _WalletProvider;

        public UserStoresController(
            UserManager<ApplicationUser> userManager,
            BTCPayNetworkProvider networkProvider,
            BTCPayWalletProvider walletProvider,
            StoreRepository storeRepository)
        {
            _Repo = storeRepository;
            _NetworkProvider = networkProvider;
            _UserManager = userManager;
            _WalletProvider = walletProvider;
        }
        [HttpGet]
        [Route("{storeId}/delete")]
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

        [HttpGet]
        [Route("create")]
        public IActionResult CreateStore()
        {
            return View();
        }

        public string CreatedStoreId
        {
            get; set;
        }

        [HttpPost]
        [Route("{storeId}/delete")]
        public async Task<IActionResult> DeleteStorePost(string storeId)
        {
            var userId = GetUserId();
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();
            await _Repo.RemoveStore(storeId, userId);
            StatusMessage = "Store removed successfully";
            return RedirectToAction(nameof(ListStores));
        }

        [TempData]
        public string StatusMessage { get; set; }

        [HttpGet]
        public async Task<IActionResult> ListStores()
        {
            StoresViewModel result = new StoresViewModel();
            var stores = await _Repo.GetStoresByUserId(GetUserId());

            var balances = stores
                                .Select(s => s.GetSupportedPaymentMethods(_NetworkProvider)
                                              .OfType<DerivationStrategy>()
                                              .Select(d => ((Wallet: _WalletProvider.GetWallet(d.Network),
                                                            DerivationStrategy: d.DerivationStrategyBase)))
                                              .Where(_ => _.Wallet != null)
                                              .Select(async _ => (await GetBalanceString(_)) + " " + _.Wallet.Network.CryptoCode))
                                .ToArray();

            await Task.WhenAll(balances.SelectMany(_ => _));
            for (int i = 0; i < stores.Length; i++)
            {
                var store = stores[i];
                result.Stores.Add(new StoresViewModel.StoreViewModel()
                {
                    Id = store.Id,
                    Name = store.StoreName,
                    WebSite = store.StoreWebsite,
                    IsOwner = store.HasClaim(Policies.CanModifyStoreSettings.Key),
                    Balances = store.HasClaim(Policies.CanModifyStoreSettings.Key) ? balances[i].Select(t => t.Result).ToArray() : Array.Empty<string>()
                });
            }
            return View(result);
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
            StatusMessage = "Store successfully created";
            return RedirectToAction(nameof(ListStores));
        }

        private static async Task<string> GetBalanceString((BTCPayWallet Wallet, DerivationStrategyBase DerivationStrategy) _)
        {
            using (CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                try
                {
                    return (await _.Wallet.GetBalance(_.DerivationStrategy, cts.Token)).ToString();
                }
                catch
                {
                    return "--";
                }
            }
        }


        private string GetUserId()
        {
            return _UserManager.GetUserId(User);
        }
    }
}
