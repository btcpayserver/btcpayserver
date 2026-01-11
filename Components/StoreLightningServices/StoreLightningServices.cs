using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Security;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Components.StoreLightningServices;

public class StoreLightningServices : ViewComponent
{
    private readonly BTCPayServerOptions _btcpayServerOptions;
    private readonly BTCPayNetworkProvider _networkProvider;
    private readonly IAuthorizationService _authorizationService;
    private readonly PaymentMethodHandlerDictionary _handlers;
    private readonly IOptions<LightningNetworkOptions> _lightningNetworkOptions;
    private readonly IOptions<ExternalServicesOptions> _externalServiceOptions;

    public StoreLightningServices(
        BTCPayNetworkProvider networkProvider,
        BTCPayServerOptions btcpayServerOptions,
        IAuthorizationService authorizationService,
        PaymentMethodHandlerDictionary handlers,
        IOptions<LightningNetworkOptions> lightningNetworkOptions,
        IOptions<ExternalServicesOptions> externalServiceOptions)
    {
        _networkProvider = networkProvider;
        _btcpayServerOptions = btcpayServerOptions;
        _lightningNetworkOptions = lightningNetworkOptions;
        _externalServiceOptions = externalServiceOptions;
        _authorizationService = authorizationService;
        _handlers = handlers;
    }

    public async Task<IViewComponentResult> InvokeAsync(StoreData store, string cryptoCode)
    {
        var vm = new StoreLightningServicesViewModel { StoreId = store.Id, CryptoCode = cryptoCode };
        var id = PaymentTypes.LN.GetPaymentMethodId(cryptoCode);
        var existing = store.GetPaymentMethodConfig<LightningPaymentMethodConfig>(id, _handlers);
        if (existing?.IsInternalNode is true && _lightningNetworkOptions.Value.InternalLightningByCryptoCode.TryGetValue(cryptoCode, out _))
        {
            var result = await _authorizationService.AuthorizeAsync(HttpContext.User, null, new PolicyRequirement(Policies.CanUseInternalLightningNode));
            vm.LightningNodeType = result.Succeeded ? LightningNodeType.Internal : null;
        }

        if (vm.LightningNodeType != LightningNodeType.Internal)
            return View(vm);
        if (!User.IsInRole(Roles.ServerAdmin))
            return View(vm);

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

        return View(vm);
    }
}
