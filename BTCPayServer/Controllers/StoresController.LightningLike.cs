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

        [HttpGet]
        [Route("{storeId}/lightning/{cryptoCode}")]
        public IActionResult AddLightningNode(string storeId, string cryptoCode)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();
            LightningNodeViewModel vm = new LightningNodeViewModel
            {
                CryptoCode = cryptoCode,
                StoreId = storeId
            };
            SetExistingValues(store, vm);
            return View(vm);
        }

        private void SetExistingValues(StoreData store, LightningNodeViewModel vm)
        {
            if (GetExistingLightningSupportedPaymentMethod(vm.CryptoCode, store) is LightningSupportedPaymentMethod paymentMethod)
            {
                vm.ConnectionString = paymentMethod.GetDisplayableConnectionString();
            }
            vm.Enabled = !store.GetStoreBlob().IsExcluded(new PaymentMethodId(vm.CryptoCode, PaymentTypes.LightningLike));
            vm.CanUseInternalNode = CanUseInternalLightning();
        }
        private LightningSupportedPaymentMethod GetExistingLightningSupportedPaymentMethod(string cryptoCode, StoreData store)
        {
            var id = new PaymentMethodId(cryptoCode, PaymentTypes.LightningLike);
            var existing = store.GetSupportedPaymentMethods(_NetworkProvider)
                .OfType<LightningSupportedPaymentMethod>()
                .FirstOrDefault(d => d.PaymentId == id);
            return existing;
        }
        [HttpPost]
        [Route("{storeId}/lightning/{cryptoCode}")]
        public async Task<IActionResult> AddLightningNode(string storeId, LightningNodeViewModel vm, string command, string cryptoCode)
        {
            vm.CryptoCode = cryptoCode;
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();
            var network = vm.CryptoCode == null ? null : _ExplorerProvider.GetNetwork(vm.CryptoCode);

            if (network == null)
            {
                ModelState.AddModelError(nameof(vm.CryptoCode), "Invalid network");
                return View(vm);
            }

            PaymentMethodId paymentMethodId = new PaymentMethodId(network.CryptoCode, PaymentTypes.LightningLike);
            Payments.Lightning.LightningSupportedPaymentMethod paymentMethod = null;
            if (vm.ConnectionString == LightningSupportedPaymentMethod.InternalNode)
            {
                if (!CanUseInternalLightning())
                {
                    ModelState.AddModelError(nameof(vm.ConnectionString), $"You are not authorized to use the internal lightning node");
                    return View(vm);
                }
                paymentMethod = new Payments.Lightning.LightningSupportedPaymentMethod()
                {
                    CryptoCode = paymentMethodId.CryptoCode
                };
                paymentMethod.SetInternalNode();
            }
            else if (!string.IsNullOrEmpty(vm.ConnectionString))
            {
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
                    ModelState.AddModelError(nameof(vm.ConnectionString), $"You are not a server admin, so the connection string should not contain 'cookiefilepath', 'macaroondirectorypath', 'macaroonfilepath', and should not point to a local ip or to a dns name ending with '.internal', '.local', '.lan' or '.'.");
                    return View(vm);
                }

                paymentMethod = new Payments.Lightning.LightningSupportedPaymentMethod()
                {
                    CryptoCode = paymentMethodId.CryptoCode
                };
                paymentMethod.SetLightningUrl(connectionString);
            }

            switch (command)
            {
                case "save":
                    var storeBlob = store.GetStoreBlob();
                    storeBlob.SetExcluded(paymentMethodId, !vm.Enabled);
                    storeBlob.Hints.Lightning = false;
                    store.SetStoreBlob(storeBlob);
                    store.SetSupportedPaymentMethod(paymentMethodId, paymentMethod);
                    await _Repo.UpdateStore(store);
                    TempData[WellKnownTempData.SuccessMessage] = $"Lightning node modified ({network.CryptoCode})";
                    return RedirectToAction(nameof(UpdateStore), new { storeId = storeId });
                case "test" when paymentMethod == null:
                    ModelState.AddModelError(nameof(vm.ConnectionString), "Missing url parameter");
                    return View(vm);
                case "test":
                    var handler = _ServiceProvider.GetRequiredService<LightningLikePaymentHandler>();
                    try
                    {
                        var info = await handler.GetNodeInfo(this.Request.IsOnion(), paymentMethod, network);
                        if (!vm.SkipPortTest)
                        {
                            using (CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
                            {
                                await handler.TestConnection(info, cts.Token);
                            }
                        }
                        TempData[WellKnownTempData.SuccessMessage] = $"Connection to the lightning node succeeded. Your node address: {info}";
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
        private bool CanUseInternalLightning()
        {
            return User.IsInRole(Roles.ServerAdmin) || _CssThemeManager.AllowLightningInternalNodeForAll;
        }
    }
}
