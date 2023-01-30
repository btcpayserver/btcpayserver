using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Payments;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.PayoutProcessors.OnChain;

public class OnChainAutomatedPayoutSenderFactory : EventHostedServiceBase, IPayoutProcessorFactory
{
    private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
    private readonly IServiceProvider _serviceProvider;
    private readonly LinkGenerator _linkGenerator;

    public string FriendlyName { get; } = "Automated Bitcoin Sender";
    public OnChainAutomatedPayoutSenderFactory(EventAggregator eventAggregator,
        ILogger<OnChainAutomatedPayoutSenderFactory> logger,
        BTCPayNetworkProvider btcPayNetworkProvider,
        IServiceProvider serviceProvider, LinkGenerator linkGenerator) : base(eventAggregator, logger)
    {
        _btcPayNetworkProvider = btcPayNetworkProvider;
        _serviceProvider = serviceProvider;
        _linkGenerator = linkGenerator;
    }

    public string Processor => ProcessorName;
    public static string ProcessorName => nameof(OnChainAutomatedPayoutSenderFactory);

    public string ConfigureLink(string storeId, PaymentMethodId paymentMethodId, HttpRequest request)
    {
        return _linkGenerator.GetUriByAction("Configure",
            "UIOnChainAutomatedPayoutProcessors", new
            {
                storeId,
                cryptoCode = paymentMethodId.CryptoCode
            }, request.Scheme, request.Host, request.PathBase);
    }

    public IEnumerable<PaymentMethodId> GetSupportedPaymentMethods()
    {
        return _btcPayNetworkProvider.GetAll().OfType<BTCPayNetwork>()
            .Where(network => !network.ReadonlyWallet && network.WalletSupported)
            .Select(network =>
                new PaymentMethodId(network.CryptoCode, BitcoinPaymentType.Instance));
    }

    public Task<IHostedService> ConstructProcessor(PayoutProcessorData settings)
    {
        if (settings.Processor != Processor)
        {
            throw new NotSupportedException("This processor cannot handle the provided requirements");
        }

        return Task.FromResult<IHostedService>(ActivatorUtilities.CreateInstance<OnChainAutomatedPayoutProcessor>(_serviceProvider, settings));
    }
}
