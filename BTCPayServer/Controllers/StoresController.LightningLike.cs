using System;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning.CLightning;
using Microsoft.AspNetCore.Mvc;
using BTCPayServer.Payments.Lightning;
using System.Net;
using BTCPayServer.Data;
using System.Threading;

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
            LightningNodeViewModel vm = new LightningNodeViewModel();
            vm.CryptoCode = cryptoCode;
            vm.InternalLightningNode = GetInternalLighningNode(cryptoCode)?.UriWithCreds?.AbsoluteUri;
            vm.Url = GetExistingLightningSupportedPaymentMethod(vm.CryptoCode, store)?.GetLightningUrl()?.ToFullEditString();
            return View(vm);
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
            vm.InternalLightningNode = internalLightning?.UriWithCreds?.AbsoluteUri;
            if (network == null)
            {
                ModelState.AddModelError(nameof(vm.CryptoCode), "Invalid network");
                return View(vm);
            }

            PaymentMethodId paymentMethodId = new PaymentMethodId(network.CryptoCode, PaymentTypes.LightningLike);
            Payments.Lightning.LightningSupportedPaymentMethod paymentMethod = null;
            if (!string.IsNullOrEmpty(vm.Url))
            {
                if (!LightningConnectionString.TryParse(vm.Url, out var connectionString, out var error))
                {
                    ModelState.AddModelError(nameof(vm.Url), $"Invalid URL ({error})");
                    return View(vm);
                }

                var internalDomain = internalLightning?.UriPlain?.DnsSafeHost;
                bool isLocal = (internalDomain == "127.0.0.1" || internalDomain == "localhost");

                bool isInternalNode = connectionString.ConnectionType == LightningConnectionType.CLightning ||
                                      connectionString.BaseUri.DnsSafeHost == internalDomain ||
                                      isLocal;

                if (connectionString.BaseUri.Scheme == "http" && !isLocal)
                {
                    if (!isInternalNode || (isInternalNode && !CanUseInternalLightning()))
                    {
                        ModelState.AddModelError(nameof(vm.Url), "The url must be HTTPS");
                        return View(vm);
                    }
                }

                if (isInternalNode && !CanUseInternalLightning())
                {
                    ModelState.AddModelError(nameof(vm.Url), "Unauthorized url");
                    return View(vm);
                }

                paymentMethod = new Payments.Lightning.LightningSupportedPaymentMethod()
                {
                    CryptoCode = paymentMethodId.CryptoCode
                };
                paymentMethod.SetLightningUrl(connectionString);
            }

            if (command == "save")
            {
                store.SetSupportedPaymentMethod(paymentMethodId, paymentMethod);
                await _Repo.UpdateStore(store);
                StatusMessage = $"Lightning node modified ({network.CryptoCode})";
                return RedirectToAction(nameof(UpdateStore), new { storeId = storeId });
            }
            else // if(command == "test")
            {
                if (paymentMethod == null)
                {
                    ModelState.AddModelError(nameof(vm.Url), "Missing url parameter");
                    return View(vm);
                }
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
            }
        }

        private bool CanUseInternalLightning()
        {
            return (_BTCPayEnv.IsDevelopping || User.IsInRole(Roles.ServerAdmin));
        }
    }
}
