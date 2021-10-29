using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Lightning;
using BTCPayServer.Logging;
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
        public async Task<IActionResult> SetupLightningNode(string storeId, string cryptoCode)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();

            var vm = new LightningNodeViewModel
            {
                CryptoCode = cryptoCode,
                StoreId = storeId
            };
            await SetExistingValues(store, vm);
            return View(vm);
        }

        [HttpPost("{storeId}/lightning/{cryptoCode}")]
        public async Task<IActionResult> SetupLightningNode(string storeId, LightningNodeViewModel vm, string command, string cryptoCode)
        {
            vm.CryptoCode = cryptoCode;
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();

            vm.CanUseInternalNode = await CanUseInternalLightning();

            var network = vm.CryptoCode == null ? null : _ExplorerProvider.GetNetwork(vm.CryptoCode);
            if (network == null)
            {
                ModelState.AddModelError(nameof(vm.CryptoCode), "Invalid network");
                return View(vm);
            }

            var paymentMethodId = new PaymentMethodId(network.CryptoCode, PaymentTypes.LightningLike);

            LightningSupportedPaymentMethod paymentMethod = null;
            if (vm.LightningNodeType == LightningNodeType.Internal)
            {
                if (!await CanUseInternalLightning())
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

                    var lnurl = new PaymentMethodId(vm.CryptoCode, PaymentTypes.LNURLPay);
                    store.SetStoreBlob(storeBlob);
                    store.SetSupportedPaymentMethod(paymentMethodId, paymentMethod);

                    await _Repo.UpdateStore(store);
                    TempData[WellKnownTempData.SuccessMessage] = $"{network.CryptoCode} Lightning node updated.";
                    return RedirectToAction(nameof(PaymentMethods), new { storeId });

                case "test":
                    var handler = _ServiceProvider.GetRequiredService<LightningLikePaymentHandler>();
                    try
                    {
                        var info = await handler.GetNodeInfo(paymentMethod, network, new InvoiceLogs(), Request.IsOnion());
                        if (!vm.SkipPortTest)
                        {
                            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                            await handler.TestConnection(info.First(), cts.Token);
                        }
                        TempData[WellKnownTempData.SuccessMessage] = $"Connection to the Lightning node successful. Your node address: {info.First()}";
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
        
        [HttpGet("{storeId}/lightning/{cryptoCode}/settings")]
        public async Task<IActionResult> LightningSettings(string storeId, string cryptoCode)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();
            
            var storeBlob = store.GetStoreBlob();
            var vm = new LightningSettingsViewModel
            {
                CryptoCode = cryptoCode,
                StoreId = storeId,
                LightningDescriptionTemplate = storeBlob.LightningDescriptionTemplate,
                LightningAmountInSatoshi = storeBlob.LightningAmountInSatoshi,
                LightningPrivateRouteHints = storeBlob.LightningPrivateRouteHints,
                OnChainWithLnInvoiceFallback = storeBlob.OnChainWithLnInvoiceFallback
            };
            await SetExistingValues(store, vm);
            
            var lightning = GetExistingLightningSupportedPaymentMethod(vm.CryptoCode, store);
            var lnSet = lightning != null;
            if (lnSet)
            {
                vm.DisableBolt11PaymentMethod = lightning.DisableBOLT11PaymentOption;
            }

            var lnurl = GetExistingLNURLSupportedPaymentMethod(vm.CryptoCode, store);
            if (lnurl != null)
            {
                vm.LNURLEnabled = !store.GetStoreBlob().GetExcludedPaymentMethods().Match(lnurl.PaymentId);
                vm.LNURLBech32Mode = lnurl.UseBech32Scheme;
                vm.LNURLStandardInvoiceEnabled = lnurl.EnableForStandardInvoices;
                vm.LUD12Enabled = lnurl.LUD12Enabled;
                vm.DisableBolt11PaymentMethod =
                    vm.LNURLEnabled && vm.LNURLStandardInvoiceEnabled && vm.DisableBolt11PaymentMethod;
            }
            else
            {
                //disable by default for now
                //vm.LNURLEnabled = !lnSet;
                vm.DisableBolt11PaymentMethod = false;
            }
            
            return View(vm);
        }

        [HttpPost("{storeId}/lightning/{cryptoCode}/settings")]
        public async Task<IActionResult> LightningSettings(LightningSettingsViewModel vm)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();

            var network = vm.CryptoCode == null ? null : _ExplorerProvider.GetNetwork(vm.CryptoCode);
            if (network == null)
            {
                ModelState.AddModelError(nameof(vm.CryptoCode), "Invalid network");
                return View(vm);
            }
            
            var needUpdate = false;
            var blob = store.GetStoreBlob();
            blob.LightningDescriptionTemplate = vm.LightningDescriptionTemplate ?? string.Empty;
            blob.LightningAmountInSatoshi = vm.LightningAmountInSatoshi;
            blob.LightningPrivateRouteHints = vm.LightningPrivateRouteHints;
            blob.OnChainWithLnInvoiceFallback = vm.OnChainWithLnInvoiceFallback;
            var disableBolt11PaymentMethod =
                vm.LNURLEnabled && vm.LNURLStandardInvoiceEnabled && vm.DisableBolt11PaymentMethod;
            var lnurlId = new PaymentMethodId(vm.CryptoCode, PaymentTypes.LNURLPay);
            blob.SetExcluded(lnurlId, !vm.LNURLEnabled);
            var lightning = GetExistingLightningSupportedPaymentMethod(vm.CryptoCode, store);
            if (lightning.DisableBOLT11PaymentOption != disableBolt11PaymentMethod)
            {
                needUpdate = true;
                lightning.DisableBOLT11PaymentOption = disableBolt11PaymentMethod;
                store.SetSupportedPaymentMethod(lightning);
            }
            
            var lnurl = GetExistingLNURLSupportedPaymentMethod(vm.CryptoCode, store);
            if (lnurl is null || (
                lnurl.EnableForStandardInvoices != vm.LNURLStandardInvoiceEnabled || 
                lnurl.UseBech32Scheme != vm.LNURLBech32Mode || 
                lnurl.LUD12Enabled != vm.LUD12Enabled))
            {
                needUpdate = true;
            }
            
            store.SetSupportedPaymentMethod(new LNURLPaySupportedPaymentMethod
            {
                CryptoCode = vm.CryptoCode,
                EnableForStandardInvoices = vm.LNURLStandardInvoiceEnabled,
                UseBech32Scheme = vm.LNURLBech32Mode,
                LUD12Enabled = vm.LUD12Enabled
            });
            
            if (store.SetStoreBlob(blob))
            {
                needUpdate = true;
            }
            
            if (needUpdate)
            {
                await _Repo.UpdateStore(store);

                TempData[WellKnownTempData.SuccessMessage] = $"{network.CryptoCode} Lightning settings successfully updated";
            }

            return RedirectToAction(nameof(PaymentMethods), new { vm.StoreId });
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

            return RedirectToAction(nameof(PaymentMethods), new { storeId });
        }

        private async Task<bool> CanUseInternalLightning()
        {
            return User.IsInRole(Roles.ServerAdmin) || (await _settingsRepository.GetPolicies()).AllowLightningInternalNodeForAll;
        }

        private async Task SetExistingValues(StoreData store, LightningNodeViewModel vm)
        {
            vm.CanUseInternalNode = await CanUseInternalLightning();
            var lightning = GetExistingLightningSupportedPaymentMethod(vm.CryptoCode, store);

            var lnSet = lightning != null;
            if (lnSet)
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
        private LNURLPaySupportedPaymentMethod GetExistingLNURLSupportedPaymentMethod(string cryptoCode, StoreData store)
        {
            var id = new PaymentMethodId(cryptoCode, PaymentTypes.LNURLPay);
            var existing = store.GetSupportedPaymentMethods(_NetworkProvider)
                .OfType<LNURLPaySupportedPaymentMethod>()
                .FirstOrDefault(d => d.PaymentId == id);
            return existing;
        }
    }
}
