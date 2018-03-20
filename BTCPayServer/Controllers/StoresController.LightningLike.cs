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

namespace BTCPayServer.Controllers
{
    public partial class StoresController
    {
        [HttpGet]
        [Route("{storeId}/lightning")]
        public async Task<IActionResult> AddLightningNode(string storeId, string selectedCrypto = null)
        {
            selectedCrypto = selectedCrypto ?? "BTC";
            var store = await _Repo.FindStore(storeId, GetUserId());
            if (store == null)
                return NotFound();
            LightningNodeViewModel vm = new LightningNodeViewModel();
            vm.SetCryptoCurrencies(_NetworkProvider, selectedCrypto);
            vm.InternalLightningNode = GetInternalLighningNode(selectedCrypto)?.ToUri(true)?.AbsoluteUri;
            return View(vm);
        }

        private LightningConnectionString GetInternalLighningNode(string selectedCrypto)
        {
            selectedCrypto = "BTC";
            if (_BtcpayServerOptions.InternalLightningByCryptoCode.TryGetValue(selectedCrypto, out var connectionString))
            {
                return CanUseInternalLightning() ? connectionString : null;
            }
            return null;
        }

        [HttpPost]
        [Route("{storeId}/lightning")]
        public async Task<IActionResult> AddLightningNode(string storeId, LightningNodeViewModel vm, string command)
        {
            var store = await _Repo.FindStore(storeId, GetUserId());
            if (store == null)
                return NotFound();
            var network = vm.CryptoCurrency == null ? null : _ExplorerProvider.GetNetwork(vm.CryptoCurrency);
            vm.SetCryptoCurrencies(_NetworkProvider, vm.CryptoCurrency);

            var internalLightning = GetInternalLighningNode(network.CryptoCode);
            vm.InternalLightningNode = internalLightning?.ToUri(true)?.AbsoluteUri;
            if (network == null)
            {
                ModelState.AddModelError(nameof(vm.CryptoCurrency), "Invalid network");
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

                var internalDomain = internalLightning?.ToUri(false)?.DnsSafeHost;
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
                    await handler.Test(paymentMethod, network);
                }
                catch (Exception ex)
                {
                    vm.StatusMessage = $"Error: {ex.Message}";
                    return View(vm);
                }
                vm.StatusMessage = "Connection to the lightning node succeed";
                return View(vm);
            }
        }

        private bool CanUseInternalLightning()
        {
            return (_BTCPayEnv.IsDevelopping || User.IsInRole(Roles.ServerAdmin));
        }
    }
}
