#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Controllers;

public partial class UIStoresController
{
    [HttpGet("{storeId}/lightning/{cryptoCode}")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
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
                        model.Link = await service.GetLink(Request.GetAbsoluteUriNoPathBase(), _btcpayServerOptions.NetworkType);
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
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
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
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> SetupLightningNode(string storeId, LightningNodeViewModel vm, string command, string cryptoCode)
    {
        vm.CryptoCode = cryptoCode;
        var store = HttpContext.GetStoreData();
        if (store == null)
            return NotFound();

        var network = _explorerProvider.GetNetwork(vm.CryptoCode);
        var oldConf = _handlers.GetLightningConfig(store, network);

        vm.CanUseInternalNode = CanUseInternalLightning(vm.CryptoCode);

        if (vm.CryptoCode == null)
        {
            ModelState.AddModelError(nameof(vm.CryptoCode), "Invalid network");
            return View(vm);
        }

            
        var paymentMethodId = PaymentTypes.LN.GetPaymentMethodId(network.CryptoCode);

        LightningPaymentMethodConfig? paymentMethod;
        if (vm.LightningNodeType == LightningNodeType.Internal)
        {
            paymentMethod = new LightningPaymentMethodConfig();
            paymentMethod.SetInternalNode();
        }
        else
        {
            if (string.IsNullOrEmpty(vm.ConnectionString))
            {
                ModelState.AddModelError(nameof(vm.ConnectionString), "Please provide a connection string");
                return View(vm);
            }
            paymentMethod = new LightningPaymentMethodConfig { ConnectionString = vm.ConnectionString };
        }

        var handler = (LightningLikePaymentHandler)_handlers[paymentMethodId];
        var ctx = new PaymentMethodConfigValidationContext(_authorizationService, ModelState,
            JToken.FromObject(paymentMethod, handler.Serializer), User, oldConf is null ? null : JToken.FromObject(oldConf, handler.Serializer));
        await handler.ValidatePaymentMethodConfig(ctx);
        if (ctx.MissingPermission is not null)
            ModelState.AddModelError(nameof(vm.ConnectionString), "You do not have the permissions to change this settings");
        if (!ModelState.IsValid)
            return View(vm);

        switch (command)
        {
            case "save":
                var lnurl = PaymentTypes.LNURL.GetPaymentMethodId(vm.CryptoCode);
                store.SetPaymentMethodConfig(_handlers[paymentMethodId], paymentMethod);
                store.SetPaymentMethodConfig(_handlers[lnurl], new LNURLPaymentMethodConfig
                {
                    UseBech32Scheme = true,
                    LUD12Enabled = false
                });

                await _storeRepo.UpdateStore(store);
                TempData[WellKnownTempData.SuccessMessage] = $"{network.CryptoCode} Lightning node updated.";
                return RedirectToAction(nameof(LightningSettings), new { storeId, cryptoCode });

            case "test":
                try
                {
                    var info = await handler.GetNodeInfo(paymentMethod, null, Request.IsOnion(), true);
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
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public IActionResult LightningSettings(string storeId, string cryptoCode)
    {
        var store = HttpContext.GetStoreData();
        if (store == null)
            return NotFound();

        var storeBlob = store.GetStoreBlob();
        var excludeFilters = storeBlob.GetExcludedPaymentMethods();
        var lnId = PaymentTypes.LN.GetPaymentMethodId(cryptoCode);
        var lightning = GetConfig<LightningPaymentMethodConfig>(lnId, store);
        if (lightning == null)
        {
            TempData[WellKnownTempData.ErrorMessage] = "You need to connect to a Lightning node before adjusting its settings.";

            return RedirectToAction(nameof(SetupLightningNode), new { storeId, cryptoCode });
        }

        var vm = new LightningSettingsViewModel
        {
            CryptoCode = cryptoCode,
            StoreId = storeId,
            Enabled = !excludeFilters.Match(lnId),
            LightningDescriptionTemplate = storeBlob.LightningDescriptionTemplate,
            LightningAmountInSatoshi = storeBlob.LightningAmountInSatoshi,
            LightningPrivateRouteHints = storeBlob.LightningPrivateRouteHints,
            OnChainWithLnInvoiceFallback = storeBlob.OnChainWithLnInvoiceFallback
        };
        SetExistingValues(store, vm);

        var lnurlId = PaymentTypes.LNURL.GetPaymentMethodId(vm.CryptoCode);
        var lnurl = GetConfig<LNURLPaymentMethodConfig>(lnurlId, store);
        if (lnurl != null)
        {
            vm.LNURLEnabled = !store.GetStoreBlob().GetExcludedPaymentMethods().Match(lnurlId);
            vm.LNURLBech32Mode = lnurl.UseBech32Scheme;
            vm.LUD12Enabled = lnurl.LUD12Enabled;
        }

        return View(vm);
    }

    [HttpPost("{storeId}/lightning/{cryptoCode}/settings")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
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

        var network = _explorerProvider.GetNetwork(vm.CryptoCode);
        var lnId = PaymentTypes.LN.GetPaymentMethodId(network.CryptoCode);
        var lnurlId = PaymentTypes.LNURL.GetPaymentMethodId(network.CryptoCode);
        
        var lightning = GetConfig<LightningPaymentMethodConfig>(lnId, store);
        if (lightning == null)
            return NotFound();
        
        var needUpdate = false;
        var blob = store.GetStoreBlob();
        blob.LightningDescriptionTemplate = vm.LightningDescriptionTemplate ?? string.Empty;
        blob.LightningAmountInSatoshi = vm.LightningAmountInSatoshi;
        blob.LightningPrivateRouteHints = vm.LightningPrivateRouteHints;
        blob.OnChainWithLnInvoiceFallback = vm.OnChainWithLnInvoiceFallback;
        
        // Lightning
        blob.SetExcluded(lnId, !vm.Enabled);
        
        // LNURL
        blob.SetExcluded(lnurlId, !vm.LNURLEnabled || !vm.Enabled);

        var lnurl = GetConfig<LNURLPaymentMethodConfig>(lnurlId, store);
        if (lnurl is null || (
                lnurl.UseBech32Scheme != vm.LNURLBech32Mode ||
                lnurl.LUD12Enabled != vm.LUD12Enabled))
        {
            needUpdate = true;
        }

        store.SetPaymentMethodConfig(_handlers[lnurlId], new LNURLPaymentMethodConfig
        {
            UseBech32Scheme = vm.LNURLBech32Mode,
            LUD12Enabled = vm.LUD12Enabled
        });

        if (store.SetStoreBlob(blob))
        {
            needUpdate = true;
        }

        if (needUpdate)
        {
            await _storeRepo.UpdateStore(store);

            TempData[WellKnownTempData.SuccessMessage] = $"{network.CryptoCode} Lightning settings successfully updated.";
        }

        return RedirectToAction(nameof(LightningSettings), new { vm.StoreId, vm.CryptoCode });
    }

    private bool CanUseInternalLightning(string cryptoCode)
    {
        return _lightningNetworkOptions.InternalLightningByCryptoCode.ContainsKey(cryptoCode.ToUpperInvariant()) && (User.IsInRole(Roles.ServerAdmin) || _policiesSettings.AllowLightningInternalNodeForAll);
    }

    private void SetExistingValues(StoreData store, LightningNodeViewModel vm)
    {
        vm.CanUseInternalNode = CanUseInternalLightning(vm.CryptoCode);
        var lightning = GetConfig<LightningPaymentMethodConfig>(PaymentTypes.LN.GetPaymentMethodId(vm.CryptoCode), store);

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

    private T? GetConfig<T>(PaymentMethodId paymentMethodId, StoreData store) where T: class
    {
        return store.GetPaymentMethodConfig<T>(paymentMethodId, _handlers);
    }
}
