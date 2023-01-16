using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Lightning;
using BTCPayServer.Models;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Components.StoreLightningServices;

public class StoreLightningServices : ViewComponent
{
    private readonly BTCPayServerOptions _btcpayServerOptions;
    private readonly BTCPayNetworkProvider _networkProvider;
    private readonly IOptions<ExternalServicesOptions> _externalServiceOptions;

    public StoreLightningServices(
        BTCPayNetworkProvider networkProvider,
        BTCPayServerOptions btcpayServerOptions,
        IOptions<ExternalServicesOptions> externalServiceOptions)
    {
        _networkProvider = networkProvider;
        _btcpayServerOptions = btcpayServerOptions;
        _externalServiceOptions = externalServiceOptions;
    }

    public IViewComponentResult Invoke(StoreLightningServicesViewModel vm)
    {
        if (vm.Store == null)
            throw new ArgumentNullException(nameof(vm.Store));
        if (vm.CryptoCode == null)
            throw new ArgumentNullException(nameof(vm.CryptoCode));
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
