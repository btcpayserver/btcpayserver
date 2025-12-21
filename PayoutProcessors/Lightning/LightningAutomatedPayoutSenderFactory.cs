using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Data.Payouts.LightningLike;
using BTCPayServer.Payments;
using BTCPayServer.Payouts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.PayoutProcessors.Lightning;

public class LightningAutomatedPayoutSenderFactory : IPayoutProcessorFactory
{
    private readonly PayoutMethodHandlerDictionary _handlers;
    private readonly IServiceProvider _serviceProvider;
    private readonly LinkGenerator _linkGenerator;
    private readonly PayoutMethodId[] _supportedPayoutMethods;
    private IStringLocalizer StringLocalizer { get; }

    public LightningAutomatedPayoutSenderFactory(
        PayoutMethodHandlerDictionary handlers,
        IServiceProvider serviceProvider,
        IStringLocalizer stringLocalizer,
        LinkGenerator linkGenerator)
    {
        _handlers = handlers;
        _serviceProvider = serviceProvider;
        _linkGenerator = linkGenerator;
        _supportedPayoutMethods = _handlers.OfType<LightningLikePayoutHandler>().Select(n => n.PayoutMethodId).ToArray();
        StringLocalizer = stringLocalizer;
    }

    public string FriendlyName => StringLocalizer["Automated Lightning Sender"];

    public string ConfigureLink(string storeId, PayoutMethodId payoutMethodId, HttpRequest request)
    {
        var network = _handlers.TryGetNetwork(payoutMethodId);
        return _linkGenerator.GetUriByAction("Configure",
            "UILightningAutomatedPayoutProcessors", new
            {
                storeId,
                cryptoCode = network.CryptoCode
            }, request.Scheme, request.Host, request.PathBase);
    }
    public string Processor => ProcessorName;
    public static string ProcessorName => nameof(LightningAutomatedPayoutSenderFactory);
    public IEnumerable<PayoutMethodId> GetSupportedPayoutMethods() => _supportedPayoutMethods;

    public LightningAutomatedPayoutProcessor ConstructProcessor(PayoutProcessorData settings)
    {
        if (settings.Processor != Processor)
        {
            throw new NotSupportedException("This processor cannot handle the provided requirements");
        }
        var payoutMethodId = settings.GetPayoutMethodId();
        return ActivatorUtilities.CreateInstance<LightningAutomatedPayoutProcessor>(_serviceProvider, settings, payoutMethodId);
    }
    Task<IHostedService> IPayoutProcessorFactory.ConstructProcessor(PayoutProcessorData settings)
    {
        return Task.FromResult<IHostedService>(ConstructProcessor(settings));
    }
}
