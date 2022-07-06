#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Components.StoreLightningBalance;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Lightning;
using BTCPayServer.Logging;
using BTCPayServer.Models;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

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
            var vm = new StoreDashboardViewModel
            {
                WalletEnabled = walletEnabled,
                LightningEnabled = lightningEnabled,
                StoreId = CurrentStore.Id,
                StoreName = CurrentStore.StoreName,
                IsSetUp = walletEnabled || lightningEnabled
            };
            
            // Widget data
            if (vm.WalletEnabled || vm.LightningEnabled)
            {
                var userId = GetUserId();
                var apps = await _appService.GetAllApps(userId, false, store.Id);
                vm.Apps = apps
                    .Select(a =>
                    {
                        var appData = _appService.GetAppDataIfOwner(userId, a.Id).Result;
                        appData.StoreData = store;
                        return appData;
                    })
                    .ToList();
            }
            
            return View(vm);
        }
        
        [HttpGet("{storeId}/lightning/{cryptoCode}/balance")]
        public IActionResult LightningBalance(string storeId, string cryptoCode)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();

            var vm = new StoreLightningBalanceViewModel { Store = store };
            return ViewComponent("StoreLightningBalance", new { vm });
        }
    }
}
