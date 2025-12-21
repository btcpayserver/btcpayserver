#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Dom;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Lightning;
using BTCPayServer.Logging;
using BTCPayServer.Models;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NBitcoin;
using NBitpayClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Payments.Lightning
{
    public class LNURLPayPaymentHandler : IPaymentMethodHandler, ILightningPaymentHandler
    {
        private readonly BTCPayNetwork _network;
        public JsonSerializer Serializer { get; }
        private readonly LightningClientFactoryService _lightningClientFactoryService;
        private readonly IServiceProvider _serviceProvider;

        public LNURLPayPaymentHandler(
            PaymentMethodId paymentMethodId,
            BTCPayNetwork network,
            IOptions<LightningNetworkOptions> options,
            LightningClientFactoryService lightningClientFactoryService,
            IServiceProvider serviceProvider)
        {
            _network = network;
            Serializer = BlobSerializer.CreateSerializer(network.NBitcoinNetwork).Serializer;
            PaymentMethodId = paymentMethodId;
            _lightningClientFactoryService = lightningClientFactoryService;
            _serviceProvider = serviceProvider;
            Options = options;
        }

        public Task BeforeFetchingRates(PaymentMethodContext context)
        {
            // LNURL is already "Lazy" as the bolt11 is only created when needed
            context.Prompt.Inactive = false;
            context.Prompt.Currency = _network.CryptoCode;
            context.Prompt.Divisibility = 11;
            context.Prompt.RateDivisibility = 8;
            context.Prompt.PaymentMethodFee = 0.0m;
            return Task.CompletedTask;
        }

        public IOptions<LightningNetworkOptions> Options { get; }

        public PaymentMethodId PaymentMethodId { get; }

        public BTCPayNetwork Network => _network;

        public async Task ConfigurePrompt(PaymentMethodContext context)
        {
            var handlers = _serviceProvider.GetRequiredService<PaymentMethodHandlerDictionary>();
            var lightningHandler = (LightningLikePaymentHandler)handlers[PaymentTypes.LN.GetPaymentMethodId(_network.CryptoCode)];
            var store = context.Store;
            var lnPmi = PaymentTypes.LN.GetPaymentMethodId(_network.CryptoCode);
            var lnConfig = lightningHandler.ParsePaymentMethodConfig(store.GetPaymentMethodConfigs()[lnPmi]);
            if (lnConfig is null)
            {
                throw new PaymentMethodUnavailableException("LNURL requires a lightning node to be configured for the store.");
            }
            var preferOnion = Uri.TryCreate(context.InvoiceEntity.ServerUrl, UriKind.Absolute, out var u) && u.IsOnion();
            var nodeInfo = (await lightningHandler.GetNodeInfo(lnConfig, context.Logs, preferOnion)).FirstOrDefault();

            var lnUrlConfig = ParsePaymentMethodConfig(store.GetPaymentMethodConfigs()[PaymentMethodId]);
            context.Prompt.Details = JObject.FromObject(new LNURLPayPaymentMethodDetails()
            {
                Bech32Mode = lnUrlConfig.UseBech32Scheme,
                NodeInfo = nodeInfo?.ToString()
            }, Serializer);
        }

        public LNURLPaymentMethodConfig ParsePaymentMethodConfig(JToken config)
        {
            return config.ToObject<LNURLPaymentMethodConfig>(Serializer) ?? throw new FormatException($"Invalid {nameof(LNURLPaymentMethodConfig)}");
        }
        object IPaymentMethodHandler.ParsePaymentMethodConfig(JToken config)
        {
            return ParsePaymentMethodConfig(config);
        }
        object IPaymentMethodHandler.ParsePaymentPromptDetails(JToken details)
        {
            return ParsePaymentPromptDetails(details);
        }
        public LightningPaymentData ParsePaymentDetails(JToken details)
        {
            return details.ToObject<LightningPaymentData>(Serializer) ?? throw new FormatException($"Invalid {nameof(LightningPaymentData)}");
        }
        object IPaymentMethodHandler.ParsePaymentDetails(JToken details)
        {
            return ParsePaymentDetails(details);
        }
        public LNURLPayPaymentMethodDetails ParsePaymentPromptDetails(JToken details)
        {
            return details.ToObject<LNURLPayPaymentMethodDetails>(Serializer) ?? throw new FormatException($"Invalid {nameof(LNURLPayPaymentMethodDetails)}");
        }
    }
}
