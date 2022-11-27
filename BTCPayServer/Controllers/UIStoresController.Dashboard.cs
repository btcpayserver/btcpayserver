#nullable enable
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Components.StoreLightningBalance;
using BTCPayServer.Components.StoreNumbers;
using BTCPayServer.Components.StoreRecentInvoices;
using BTCPayServer.Components.StoreRecentTransactions;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Models.StoreViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Controllers
{
    public partial class UIStoresController
    {
        [HttpGet("{storeId}")]
        public async Task<IActionResult> Dashboard()
        {
            var store = CurrentStore;
            var storeBlob = store.GetStoreBlob();

            AddPaymentMethods(store, storeBlob,
                out var derivationSchemes, out var lightningNodes);

            var walletEnabled = derivationSchemes.Any(scheme => !string.IsNullOrEmpty(scheme.Value) && scheme.Enabled);
            var lightningEnabled = lightningNodes.Any(ln => !string.IsNullOrEmpty(ln.Address) && ln.Enabled);
            var cryptoCode = _NetworkProvider.DefaultNetwork.CryptoCode;
            var vm = new StoreDashboardViewModel
            {
                WalletEnabled = walletEnabled,
                LightningEnabled = lightningEnabled,
                LightningSupported = _NetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode)?.SupportLightning is true,
                StoreId = CurrentStore.Id,
                StoreName = CurrentStore.StoreName,
                CryptoCode = cryptoCode,
                Network =  _NetworkProvider.DefaultNetwork,
                IsSetUp = walletEnabled || lightningEnabled
            };
            
            // Widget data
            if (!vm.WalletEnabled && !vm.LightningEnabled) return View(vm);
            
            var userId = GetUserId();
            var apps = await _appService.GetAllApps(userId, false, store.Id);
            foreach (var app in apps)
            {
                var appData = await _appService.GetAppDataIfOwner(userId, app.Id);
                vm.Apps.Add(appData);
            }

            return View(vm);
        }

        [HttpGet("{storeId}/dashboard/{cryptoCode}/lightning/balance")]
        public IActionResult LightningBalance(string storeId, string cryptoCode) =>
            ViewComponent("StoreLightningBalance", new { vm = CreateViewModel<StoreLightningBalanceViewModel>(storeId, cryptoCode) });
        
        [HttpGet("{storeId}/dashboard/{cryptoCode}/numbers")]
        public IActionResult StoreNumbers(string storeId, string cryptoCode) =>
            ViewComponent("StoreNumbers", new { vm = CreateViewModel<StoreNumbersViewModel>(storeId, cryptoCode) });
        
        [HttpGet("{storeId}/dashboard/{cryptoCode}/recent-transactions")]
        public IActionResult RecentTransactions(string storeId, string cryptoCode) =>
            ViewComponent("StoreRecentTransactions", new { vm = CreateViewModel<StoreRecentTransactionsViewModel>(storeId, cryptoCode) });
        
        [HttpGet("{storeId}/dashboard/{cryptoCode}/recent-invoices")]
        public IActionResult RecentInvoices(string storeId, string cryptoCode) =>
            ViewComponent("StoreRecentInvoices", new { vm = CreateViewModel<StoreRecentInvoicesViewModel>(storeId, cryptoCode) });

        private T CreateViewModel<T>(string storeId, string cryptoCode) where T : class, IStoreViewModel
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();

            return (T)ActivatorUtilities.CreateInstance(_Services, typeof(T), new[] { store, cryptoCode });
        }
    }
}
