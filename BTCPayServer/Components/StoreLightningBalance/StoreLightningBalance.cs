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

namespace BTCPayServer.Components.StoreLightningBalance;

public class StoreLightningBalance : ViewComponent
{
    private string _cryptoCode;
    private readonly StoreRepository _storeRepo;
    private readonly BTCPayServerOptions _btcpayServerOptions;
    private readonly BTCPayNetworkProvider _networkProvider;
    private readonly LightningClientFactoryService _lightningClientFactory;
    private readonly IOptions<LightningNetworkOptions> _lightningNetworkOptions;
    private readonly IOptions<ExternalServicesOptions> _externalServiceOptions;

    public StoreLightningBalance(
        StoreRepository storeRepo,
        BTCPayNetworkProvider networkProvider,
        BTCPayServerOptions btcpayServerOptions,
        LightningClientFactoryService lightningClientFactory,
        IOptions<LightningNetworkOptions> lightningNetworkOptions,
        IOptions<ExternalServicesOptions> externalServiceOptions)
    {
        _storeRepo = storeRepo;
        _networkProvider = networkProvider;
        _btcpayServerOptions = btcpayServerOptions;
        _externalServiceOptions = externalServiceOptions;
        _lightningClientFactory = lightningClientFactory;
        _lightningNetworkOptions = lightningNetworkOptions;
        _cryptoCode = _networkProvider.DefaultNetwork.CryptoCode;
    }

    public async Task<IViewComponentResult> InvokeAsync(StoreData store)
    {
        var walletId = new WalletId(store.Id, _cryptoCode);
        var lightningClient = GetLightningClient(store);
        var vm = new StoreLightningBalanceViewModel
        {
            Store = store,
            CryptoCode = _cryptoCode,
            WalletId = walletId
        };
        
        if (lightningClient != null)
        {
            try
            {
                var balance = await lightningClient.GetBalance();
                vm.Balance = balance;
                vm.TotalOnchain = balance.OnchainBalance != null
                    ? (balance.OnchainBalance.Confirmed?? 0) + (balance.OnchainBalance.Reserved ?? 0) +
                      (balance.OnchainBalance.Unconfirmed ?? 0)
                    : null;
                vm.TotalOffchain = balance.OffchainBalance != null
                    ? (balance.OffchainBalance.Opening?? 0) + (balance.OffchainBalance.Local?? 0) +
                      (balance.OffchainBalance.Closing?? 0)
                    : null;
            }
            catch (NotSupportedException)
            {
                // not all implementations support balance fetching
                vm.ProblemDescription = "Your node does not support balance fetching.";
            }
            catch
            {
                // general error
                vm.ProblemDescription = "Could not fetch Lightning balance.";
            }
        }
        else
        {
            vm.ProblemDescription = "Cannot instantiate Lightning client.";
        }

        return View(vm);
    }
    
    private ILightningClient GetLightningClient(StoreData store)
    {
        var network = _networkProvider.GetNetwork<BTCPayNetwork>(_cryptoCode);
        var id = new PaymentMethodId(_cryptoCode, PaymentTypes.LightningLike);
        var existing = store.GetSupportedPaymentMethods(_networkProvider)
            .OfType<LightningSupportedPaymentMethod>()
            .FirstOrDefault(d => d.PaymentId == id);
        if (existing == null) return null;
        
        if (existing.GetExternalLightningUrl() is {} connectionString)
        {
            return _lightningClientFactory.Create(connectionString, network);
        }
        if (existing.IsInternalNode && _lightningNetworkOptions.Value.InternalLightningByCryptoCode.TryGetValue(_cryptoCode, out var internalLightningNode))
        {
            return _lightningClientFactory.Create(internalLightningNode, network);
        }

        return null;
    }
}
