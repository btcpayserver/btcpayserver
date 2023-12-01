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

        [HttpGet("{storeId}/lightning/{cryptoCode}")]
        public IActionResult Lightning(string storeId, string cryptoCode)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();

            var vm = new LightningViewModel
            {
                CryptoCode = cryptoCode,
                StoreId = storeId
            };
            SetExistingValues(store, vm);

            if (vm.LightningNodeType == LightningNodeType.Internal)
            {
                var services = _externalServiceOptions.Value.ExternalServices.ToList()
                    .Where(service => ExternalServices.LightningServiceTypes.Contains(service.Type))
                    .Select(async service =>
                    {
                        var model = new AdditionalServiceViewModel
                        {
                            DisplayName = service.DisplayName,
                            ServiceName = service.ServiceName,
                            CryptoCode = service.CryptoCode,
                            Type = service.Type.ToString()
                        };
                        try
                        {
                            model.Link = await service.GetLink(Request.GetAbsoluteUriNoPathBase(), _BtcpayServerOptions.NetworkType);
                        }
                        catch (Exception exception)
                        {
                            model.Error = exception.Message;
                        }
                        return model;
                    })
                    .Select(t => t.Result)
                    .ToList();

                // other services
                foreach ((string key, Uri value) in _externalServiceOptions.Value.OtherExternalServices)
                {
                    if (ExternalServices.LightningServiceNames.Contains(key))
                    {
                        services.Add(new AdditionalServiceViewModel
                        {
                            DisplayName = key,
                            ServiceName = key,
                            Type = key.Replace(" ", ""),
                            Link = Request.GetAbsoluteUriNoPathBase(value).AbsoluteUri
                        });
                    }
                }

                vm.Services = services;
            }

            return View(vm);
        }

        [HttpGet("{storeId}/lightning/{cryptoCode}/setup")]
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

        [HttpPost("{storeId}/lightning/{cryptoCode}/setup")]
        public async Task<IActionResult> SetupLightningNode(string storeId, LightningNodeViewModel vm, string command, string cryptoCode)
        {
            vm.CryptoCode = cryptoCode;
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();

            vm.CanUseInternalNode = CanUseInternalLightning(vm.CryptoCode);

            if (vm.CryptoCode == null)
            {
                ModelState.AddModelError(nameof(vm.CryptoCode), "Invalid network");
                return View(vm);
            }

            var network = _ExplorerProvider.GetNetwork(vm.CryptoCode);
            var paymentMethodId = new PaymentMethodId(network.CryptoCode, PaymentTypes.LightningLike);

            LightningSupportedPaymentMethod? paymentMethod = null;
            if (vm.LightningNodeType == LightningNodeType.Internal)
            {
                if (!CanUseInternalLightning(network.CryptoCode))
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

                ILightningClient? lightningClient = null;
                try
                {
                    lightningClient = _lightningClientFactoryService.Create(vm.ConnectionString, network);
                }
                catch (Exception e)
                {
                    ModelState.AddModelError(nameof(vm.ConnectionString), $"Invalid URL ({e.Message})");
                    return View(vm);
                }
                if (!User.IsInRole(Roles.ServerAdmin) && !lightningClient.IsSafe())
                {
                    ModelState.AddModelError(nameof(vm.ConnectionString), "You are not a server admin, so the connection string should not contain 'cookiefilepath', 'macaroondirectorypath', 'macaroonfilepath', and should not point to a local ip or to a dns name ending with '.internal', '.local', '.lan' or '.'.");
                    return View(vm);
                }

                paymentMethod = new LightningSupportedPaymentMethod
                {
                    CryptoCode = paymentMethodId.CryptoCode
                };

                try
                {
                    paymentMethod.SetLightningUrl(lightningClient);
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError(nameof(vm.ConnectionString), ex.Message);
                    return View(vm);
                }
            }

            switch (command)
            {
                case "save":
                    var lnurl = new PaymentMethodId(vm.CryptoCode, PaymentTypes.LNURLPay);
                    store.SetSupportedPaymentMethod(paymentMethodId, paymentMethod);
                    store.SetSupportedPaymentMethod(lnurl, new LNURLPaySupportedPaymentMethod()
                    {
                        CryptoCode = vm.CryptoCode,
                        UseBech32Scheme = true,
                        LUD12Enabled = false
                    });

                    await _Repo.UpdateStore(store);
                    TempData[WellKnownTempData.SuccessMessage] = $"{network.CryptoCode} Lightning node updated.";
                    return RedirectToAction(nameof(LightningSettings), new { storeId, cryptoCode });

                case "test":
                    var handler = _ServiceProvider.GetRequiredService<LightningLikePaymentHandler>();
                    try
                    {
                        var info = await handler.GetNodeInfo(paymentMethod, network, new InvoiceLogs(), Request.IsOnion(), true);
                        var hasPublicAddress = info.Any();
                        if (!vm.SkipPortTest && hasPublicAddress)
                        {
                            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                            await handler.TestConnection(info.First(), cts.Token);
                        }
                        TempData[WellKnownTempData.SuccessMessage] = "Connection to the Lightning node successful" + (hasPublicAddress
                            ? $". Your node address: {info.First()}"
                            : ", but no public address has been configured");
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
        public IActionResult LightningSettings(string storeId, string cryptoCode)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();

            var storeBlob = store.GetStoreBlob();
            var excludeFilters = storeBlob.GetExcludedPaymentMethods();
            var lightning = GetExistingLightningSupportedPaymentMethod(cryptoCode, store);
            if (lightning == null)
            {
                TempData[WellKnownTempData.ErrorMessage] = "You need to connect to a Lightning node before adjusting its settings.";

                return RedirectToAction(nameof(SetupLightningNode), new { storeId, cryptoCode });
            }

            var vm = new LightningSettingsViewModel
            {
                CryptoCode = cryptoCode,
                StoreId = storeId,
                Enabled = !excludeFilters.Match(lightning.PaymentId),
                LightningDescriptionTemplate = storeBlob.LightningDescriptionTemplate,
                LightningAmountInSatoshi = storeBlob.LightningAmountInSatoshi,
                LightningPrivateRouteHints = storeBlob.LightningPrivateRouteHints,
                OnChainWithLnInvoiceFallback = storeBlob.OnChainWithLnInvoiceFallback
            };
            SetExistingValues(store, vm);

            var lnurl = GetExistingLNURLSupportedPaymentMethod(vm.CryptoCode, store);
            if (lnurl != null)
            {
                vm.LNURLEnabled = !store.GetStoreBlob().GetExcludedPaymentMethods().Match(lnurl.PaymentId);
                vm.LNURLBech32Mode = lnurl.UseBech32Scheme;
                vm.LUD12Enabled = lnurl.LUD12Enabled;
            }

            return View(vm);
        }

        [HttpPost("{storeId}/lightning/{cryptoCode}/settings")]
        public async Task<IActionResult> LightningSettings(LightningSettingsViewModel vm)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();

            if (vm.CryptoCode == null)
            {
                ModelState.AddModelError(nameof(vm.CryptoCode), "Invalid network");
                return View(vm);
            }

            var network = _ExplorerProvider.GetNetwork(vm.CryptoCode);
            var needUpdate = false;
            var blob = store.GetStoreBlob();
            blob.LightningDescriptionTemplate = vm.LightningDescriptionTemplate ?? string.Empty;
            blob.LightningAmountInSatoshi = vm.LightningAmountInSatoshi;
            blob.LightningPrivateRouteHints = vm.LightningPrivateRouteHints;
            blob.OnChainWithLnInvoiceFallback = vm.OnChainWithLnInvoiceFallback;
            var lnurlId = new PaymentMethodId(vm.CryptoCode, PaymentTypes.LNURLPay);
            blob.SetExcluded(lnurlId, !vm.LNURLEnabled);

            var lnurl = GetExistingLNURLSupportedPaymentMethod(vm.CryptoCode, store);
            if (lnurl is null || (
                lnurl.UseBech32Scheme != vm.LNURLBech32Mode ||
                lnurl.LUD12Enabled != vm.LUD12Enabled))
            {
                needUpdate = true;
            }

            store.SetSupportedPaymentMethod(new LNURLPaySupportedPaymentMethod
            {
                CryptoCode = vm.CryptoCode,
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

                TempData[WellKnownTempData.SuccessMessage] = $"{network.CryptoCode} Lightning settings successfully updated.";
            }

            return RedirectToAction(nameof(LightningSettings), new { vm.StoreId, vm.CryptoCode });
        }

        [HttpPost("{storeId}/lightning/{cryptoCode}/status")]
        public async Task<IActionResult> SetLightningNodeEnabled(string storeId, string cryptoCode, bool enabled)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();

            if (cryptoCode == null)
                return NotFound();

            var network = _ExplorerProvider.GetNetwork(cryptoCode);
            var lightning = GetExistingLightningSupportedPaymentMethod(cryptoCode, store);
            if (lightning == null)
                return NotFound();

            var paymentMethodId = new PaymentMethodId(network.CryptoCode, PaymentTypes.LightningLike);
            var storeBlob = store.GetStoreBlob();
            storeBlob.SetExcluded(paymentMethodId, !enabled);
            if (!enabled)
            {
                storeBlob.SetExcluded(new PaymentMethodId(network.CryptoCode, PaymentTypes.LNURLPay), true);
            }
            store.SetStoreBlob(storeBlob);
            await _Repo.UpdateStore(store);
            TempData[WellKnownTempData.SuccessMessage] = $"{network.CryptoCode} Lightning payments are now {(enabled ? "enabled" : "disabled")} for this store.";

            return RedirectToAction(nameof(LightningSettings), new { storeId, cryptoCode });
        }

        private bool CanUseInternalLightning(string cryptoCode)
        {
            return LightningNetworkOptions.InternalLightningByCryptoCode.ContainsKey(cryptoCode.ToUpperInvariant()) && (User.IsInRole(Roles.ServerAdmin) || _policiesSettings.AllowLightningInternalNodeForAll);
        }

        private void SetExistingValues(StoreData store, LightningNodeViewModel vm)
        {
            vm.CanUseInternalNode = CanUseInternalLightning(vm.CryptoCode);
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

        private LightningSupportedPaymentMethod? GetExistingLightningSupportedPaymentMethod(string cryptoCode, StoreData store)
        {
            var id = new PaymentMethodId(cryptoCode, PaymentTypes.LightningLike);
            var existing = store.GetSupportedPaymentMethods(_NetworkProvider)
                .OfType<LightningSupportedPaymentMethod>()
                .FirstOrDefault(d => d.PaymentId == id);
            return existing;
        }

        private LNURLPaySupportedPaymentMethod? GetExistingLNURLSupportedPaymentMethod(string cryptoCode, StoreData store)
        {
            var id = new PaymentMethodId(cryptoCode, PaymentTypes.LNURLPay);
            var existing = store.GetSupportedPaymentMethods(_NetworkProvider)
                .OfType<LNURLPaySupportedPaymentMethod>()
                .FirstOrDefault(d => d.PaymentId == id);
            return existing;
        }
    }
}
