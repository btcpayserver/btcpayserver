using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Payments;
using BTCPayServer.Payouts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.PayoutProcessors.OnChain;

public class OnChainAutomatedPayoutSenderFactory : EventHostedServiceBase, IPayoutProcessorFactory
{
    private readonly PayoutMethodHandlerDictionary _handlers;
    private readonly IServiceProvider _serviceProvider;
    private readonly LinkGenerator _linkGenerator;
    private readonly PayoutMethodId[] _supportedPayoutMethods;
    private IStringLocalizer StringLocalizer { get; }

    public string FriendlyName => StringLocalizer["Automated Bitcoin Sender"];
    public OnChainAutomatedPayoutSenderFactory(
        PayoutMethodHandlerDictionary handlers,
        EventAggregator eventAggregator,
        ILogger<OnChainAutomatedPayoutSenderFactory> logger,
        IStringLocalizer stringLocalizer,
        IServiceProvider serviceProvider, LinkGenerator linkGenerator) : base(eventAggregator, logger)
    {
        _handlers = handlers;
        _serviceProvider = serviceProvider;
        _linkGenerator = linkGenerator;
        _supportedPayoutMethods = _handlers.OfType<BitcoinLikePayoutHandler>().Select(c => c.PayoutMethodId).ToArray();
        StringLocalizer = stringLocalizer;
    }

    public string Processor => ProcessorName;
    public static string ProcessorName => nameof(OnChainAutomatedPayoutSenderFactory);

    public string ConfigureLink(string storeId, PayoutMethodId payoutMethodId, HttpRequest request)
    {
        var network = _handlers.GetNetwork(payoutMethodId);
        return _linkGenerator.GetUriByAction("Configure",
            "UIOnChainAutomatedPayoutProcessors", new
            {
                storeId,
                cryptoCode = network.CryptoCode
            }, request.Scheme, request.Host, request.PathBase);
    }

    public IEnumerable<PayoutMethodId> GetSupportedPayoutMethods() => _supportedPayoutMethods;

    public Task<IHostedService> ConstructProcessor(PayoutProcessorData settings)
    {
        if (settings.Processor != Processor)
        {
            throw new NotSupportedException("This processor cannot handle the provided requirements");
        }
        var payoutMethodId = settings.GetPayoutMethodId();
        return Task.FromResult<IHostedService>(ActivatorUtilities.CreateInstance<OnChainAutomatedPayoutProcessor>(_serviceProvider, settings, payoutMethodId));
    }
}
