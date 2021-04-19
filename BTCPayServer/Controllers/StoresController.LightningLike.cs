using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Lightning;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Controllers
{
    public partial class StoresController
    {
        [HttpGet("{storeId}/lightning/{cryptoCode}")]
        public IActionResult SetupLightningNode(string storeId, string cryptoCode)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();

            var vm = new LightningNodeViewModel
            {
                CryptoCode = cryptoCode,
                StoreId = storeId
            };
            SetExistingValues(store, vm);
            return View(vm);
        }

        [HttpPost("{storeId}/lightning/{cryptoCode}")]
        public async Task<IActionResult> SetupLightningNode(string storeId, LightningNodeViewModel vm, string command, string cryptoCode)
        {
            vm.CryptoCode = cryptoCode;
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();

            vm.CanUseInternalNode = CanUseInternalLightning();

            var network = vm.CryptoCode == null ? null : _ExplorerProvider.GetNetwork(vm.CryptoCode);
            if (network == null)
            {
                ModelState.AddModelError(nameof(vm.CryptoCode), "Invalid network");
                return View(vm);
            }

            var paymentMethodId = new PaymentMethodId(network.CryptoCode, PaymentTypes.LightningLike);
            var lightning = GetExistingLightningSupportedPaymentMethod(vm.CryptoCode, store);

            LightningSupportedPaymentMethod paymentMethod = null;
            if (vm.LightningNodeType == LightningNodeType.Internal)
            {
                if (!CanUseInternalLightning())
                {
                    ModelState.AddModelError(nameof(vm.ConnectionString), "You are not authorized to use the internal lightning node");
                    return View(vm);
                }
                paymentMethod = new LightningSupportedPaymentMethod
                {
                    CryptoCode = paymentMethodId.CryptoCode
                };
                paymentMethod.SetInternalNode();
            }
            else
            {
                if (string.IsNullOrEmpty(vm.ConnectionString))
                {
                    ModelState.AddModelError(nameof(vm.ConnectionString), "Please provide a connection string");
                    return View(vm);
                }
                if (!LightningConnectionString.TryParse(vm.ConnectionString, false, out var connectionString, out var error))
                {
                    ModelState.AddModelError(nameof(vm.ConnectionString), $"Invalid URL ({error})");
                    return View(vm);
                }
                if (connectionString.ConnectionType == LightningConnectionType.LndGRPC)
                {
                    ModelState.AddModelError(nameof(vm.ConnectionString), $"BTCPay does not support gRPC connections");
                    return View(vm);
                }
                if (!User.IsInRole(Roles.ServerAdmin) && !connectionString.IsSafe())
                {
                    ModelState.AddModelError(nameof(vm.ConnectionString), "You are not a server admin, so the connection string should not contain 'cookiefilepath', 'macaroondirectorypath', 'macaroonfilepath', and should not point to a local ip or to a dns name ending with '.internal', '.local', '.lan' or '.'.");
                    return View(vm);
                }

                paymentMethod = new LightningSupportedPaymentMethod
                {
                    CryptoCode = paymentMethodId.CryptoCode
                };
                paymentMethod.SetLightningUrl(connectionString);
            }

            switch (command)
            {
                case "save":
                    var storeBlob = store.GetStoreBlob();
                    storeBlob.Hints.Lightning = false;
                    store.SetStoreBlob(storeBlob);
                    store.SetSupportedPaymentMethod(paymentMethodId, paymentMethod);
                    await _Repo.UpdateStore(store);
                    TempData[WellKnownTempData.SuccessMessage] = $"{network.CryptoCode} Lightning node updated.";
                    return RedirectToAction(nameof(UpdateStore), new { storeId });

                case "test":
                    var handler = _ServiceProvider.GetRequiredService<LightningLikePaymentHandler>();
                    try
                    {
                        var info = await handler.GetNodeInfo(Request.IsOnion(), paymentMethod, network);
                        if (!vm.SkipPortTest)
                        {
                            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                            await handler.TestConnection(info, cts.Token);
                        }
                        TempData[WellKnownTempData.SuccessMessage] = $"Connection to the Lightning node successful. Your node address: {info}";
                    }
                    catch (Exception ex)
                    {
                        TempData[WellKnownTempData.ErrorMessage] = ex.Message;
                        return View(vm);
                    }
                    return View(vm);

                default:
                    return View(vm);
            }
        }

        [HttpPost("{storeId}/lightning/{cryptoCode}/status")]
        public async Task<IActionResult> SetLightningNodeEnabled(string storeId, string cryptoCode, bool enabled)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();

            var network = cryptoCode == null ? null : _ExplorerProvider.GetNetwork(cryptoCode);
            if (network == null)
                return NotFound();

            var lightning = GetExistingLightningSupportedPaymentMethod(cryptoCode, store);
            if (lightning == null)
                return NotFound();

            var paymentMethodId = new PaymentMethodId(network.CryptoCode, PaymentTypes.LightningLike);
            var storeBlob = store.GetStoreBlob();
            storeBlob.SetExcluded(paymentMethodId, !enabled);
            store.SetStoreBlob(storeBlob);
            await _Repo.UpdateStore(store);
            TempData[WellKnownTempData.SuccessMessage] = $"{network.CryptoCode} Lightning payments are now {(enabled ? "enabled" : "disabled")} for this store.";

            return RedirectToAction(nameof(UpdateStore), new { storeId });
        }

        private bool CanUseInternalLightning()
        {
            return User.IsInRole(Roles.ServerAdmin) || _CssThemeManager.AllowLightningInternalNodeForAll;
        }

        private void SetExistingValues(StoreData store, LightningNodeViewModel vm)
        {
            vm.CanUseInternalNode = CanUseInternalLightning();
            var lightning = GetExistingLightningSupportedPaymentMethod(vm.CryptoCode, store);
            if (lightning != null)
            {
                vm.LightningNodeType = lightning.IsInternalNode ? LightningNodeType.Internal : LightningNodeType.Custom;
                vm.ConnectionString = lightning.GetDisplayableConnectionString();
            }
            else
            {
                vm.LightningNodeType = vm.CanUseInternalNode ? LightningNodeType.Internal : LightningNodeType.Custom;
            }
        }

        private LightningSupportedPaymentMethod GetExistingLightningSupportedPaymentMethod(string cryptoCode, StoreData store)
        {
            var id = new PaymentMethodId(cryptoCode, PaymentTypes.LightningLike);
            var existing = store.GetSupportedPaymentMethods(_NetworkProvider)
                .OfType<LightningSupportedPaymentMethod>()
                .FirstOrDefault(d => d.PaymentId == id);
            return existing;
        }
    }
}
