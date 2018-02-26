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
            return View(vm);
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
            if (network == null || network.CLightningNetworkName == null)
            {
                ModelState.AddModelError(nameof(vm.CryptoCurrency), "Invalid network");
                return View(vm);
            }

            PaymentMethodId paymentMethodId = new PaymentMethodId(network.CryptoCode, PaymentTypes.LightningLike);
            Payments.Lightning.LightningSupportedPaymentMethod paymentMethod = null;
            if (!string.IsNullOrEmpty(vm.Url))
            {
                Uri uri;
                if (!Uri.TryCreate(vm.Url, UriKind.Absolute, out uri))
                {
                    ModelState.AddModelError(nameof(vm.Url), "Invalid URL");
                    return View(vm);
                }
                if (_NetworkProvider.ChainType == NBXplorer.ChainType.Main)
                {
                    if (uri.Scheme != "https")
                    {
                        ModelState.AddModelError(nameof(vm.Url), "The url must be HTTPS");
                        return View(vm);
                    }
                }
                if (string.IsNullOrEmpty(uri.UserInfo) || uri.UserInfo.Split(':').Length != 2)
                {
                    ModelState.AddModelError(nameof(vm.Url), "The url is missing user and password");
                    return View(vm);
                }

                paymentMethod = new Payments.Lightning.LightningSupportedPaymentMethod()
                {
                    CryptoCode = paymentMethodId.CryptoCode
                };
                paymentMethod.SetLightningChargeUrl(uri);
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
    }
}
