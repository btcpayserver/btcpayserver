using System;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments;
using Microsoft.AspNetCore.Mvc;
using BTCPayServer.Payments.Lightning;
using System.Net;
using BTCPayServer.Data;
using System.Threading;
using BTCPayServer.Lightning;

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
                InternalLightningNode = GetInternalLighningNode(cryptoCode)?.ToString()
            };
            SetExistingValues(store, vm);
            return View(vm);
        }

        private void SetExistingValues(StoreData store, LightningNodeViewModel vm)
        {
            vm.ConnectionString = GetExistingLightningSupportedPaymentMethod(vm.CryptoCode, store)?.GetLightningUrl()?.ToString();
            vm.Enabled = !store.GetStoreBlob().IsExcluded(new PaymentMethodId(vm.CryptoCode, PaymentTypes.LightningLike));
        }
        private LightningSupportedPaymentMethod GetExistingLightningSupportedPaymentMethod(string cryptoCode, StoreData store)
        {
            var id = new PaymentMethodId(cryptoCode, PaymentTypes.LightningLike);
            var existing = store.GetSupportedPaymentMethods(_NetworkProvider)
                .OfType<LightningSupportedPaymentMethod>()
                .FirstOrDefault(d => d.PaymentId == id);
            return existing;
        }

        private LightningConnectionString GetInternalLighningNode(string cryptoCode)
        {
            if (_BtcpayServerOptions.InternalLightningByCryptoCode.TryGetValue(cryptoCode, out var connectionString))
            {
                return CanUseInternalLightning() ? connectionString : null;
            }
            return null;
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

            var internalLightning = GetInternalLighningNode(network.CryptoCode);
            vm.InternalLightningNode = internalLightning?.ToString();
            if (network == null)
            {
                ModelState.AddModelError(nameof(vm.CryptoCode), "Invalid network");
                return View(vm);
            }

            PaymentMethodId paymentMethodId = new PaymentMethodId(network.CryptoCode, PaymentTypes.LightningLike);
            Payments.Lightning.LightningSupportedPaymentMethod paymentMethod = null;
            if (!string.IsNullOrEmpty(vm.ConnectionString))
            {
                if (!LightningConnectionString.TryParse(vm.ConnectionString, false, out var connectionString, out var error))
                {
                    ModelState.AddModelError(nameof(vm.ConnectionString), $"Invalid URL ({error})");
                    return View(vm);
                }

                if(connectionString.ConnectionType == LightningConnectionType.LndGRPC)
                {
                    ModelState.AddModelError(nameof(vm.ConnectionString), $"BTCPay does not support gRPC connections");
                    return View(vm);
                }

                var internalDomain = internalLightning?.BaseUri?.DnsSafeHost;

                bool isInternalNode = connectionString.ConnectionType == LightningConnectionType.CLightning ||
                                      connectionString.BaseUri.DnsSafeHost == internalDomain ||
                                      (internalDomain == "127.0.0.1" || internalDomain == "localhost");

                if (connectionString.BaseUri.Scheme == "http")
                {
                    if (!isInternalNode)
                    {
                        ModelState.AddModelError(nameof(vm.ConnectionString), "The url must be HTTPS");
                        return View(vm);
                    }
                }

                if(connectionString.MacaroonFilePath != null)
                {
                    if(!CanUseInternalLightning())
                    {
                        ModelState.AddModelError(nameof(vm.ConnectionString), "You are not authorized to use macaroonfilepath");
                        return View(vm);
                    }
                    if(!System.IO.File.Exists(connectionString.MacaroonFilePath))
                    {
                        ModelState.AddModelError(nameof(vm.ConnectionString), "The macaroonfilepath file does not exist");
                        return View(vm);
                    }
                    if(!System.IO.Path.IsPathRooted(connectionString.MacaroonFilePath))
                    {
                        ModelState.AddModelError(nameof(vm.ConnectionString), "The macaroonfilepath should be fully rooted");
                        return View(vm);
                    }
                }

                if (isInternalNode && !CanUseInternalLightning())
                {
                    ModelState.AddModelError(nameof(vm.ConnectionString), "Unauthorized url");
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
                    store.SetStoreBlob(storeBlob);
                    store.SetSupportedPaymentMethod(paymentMethodId, paymentMethod);
                    await _Repo.UpdateStore(store);
                    StatusMessage = $"Lightning node modified ({network.CryptoCode})";
                    return RedirectToAction(nameof(UpdateStore), new { storeId = storeId });
                case "test" when paymentMethod == null:
                    ModelState.AddModelError(nameof(vm.ConnectionString), "Missing url parameter");
                    return View(vm);
                case "test":
                    var handler = (LightningLikePaymentHandler)_ServiceProvider.GetRequiredService<IPaymentMethodHandler<Payments.Lightning.LightningSupportedPaymentMethod>>();
                    try
                    {
                        var info = await handler.Test(paymentMethod, network);
                        if (!vm.SkipPortTest)
                        {
                            using (CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
                            {
                                await handler.TestConnection(info, cts.Token);
                            }
                        }
                        vm.StatusMessage = $"Connection to the lightning node succeeded ({info})";
                    }
                    catch (Exception ex)
                    {
                        vm.StatusMessage = $"Error: {ex.Message}";
                        return View(vm);
                    }
                    return View(vm);
                default:
                    return View(vm);
            }
        }

        private bool CanUseInternalLightning()
        {
            return (_BTCPayEnv.IsDevelopping || User.IsInRole(Roles.ServerAdmin));
        }
    }
}
