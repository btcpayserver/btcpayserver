#nullable enable
using BTCPayServer.BIP78.Sender;
using BTCPayServer.Payments.PayJoin;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Payments.Bitcoin
{
    public class BitcoinPaymentLinkExtension : IPaymentLinkExtension
    {

        public BitcoinPaymentLinkExtension(PaymentMethodId paymentMethodId, BTCPayNetwork network, PaymentMethodHandlerDictionary handlers)
        {
            PaymentMethodId = paymentMethodId;
            Handlers = handlers;
            Network = network;
        }
        public PaymentMethodId PaymentMethodId { get; }
        public PaymentMethodHandlerDictionary Handlers { get; }

        public BTCPayNetwork Network { get; }

        public string? GetPaymentLink(PaymentPrompt prompt, IUrlHelper? urlHelper)
        {
            var due = prompt.Calculate().Due;
            var details = (BitcoinPaymentPromptDetails)Handlers[PaymentMethodId].ParsePaymentPromptDetails(prompt.Details);
            var bip21 = Network.GenerateBIP21(prompt.Destination, due);
            if (details.PayjoinEnabled)
            {
                var link = urlHelper?.ActionLink(nameof(PayJoinEndpointController.Submit), "PayJoinEndpoint", new { cryptoCode = Network.CryptoCode });
                if (link is not null)
                    bip21.QueryParams.Add(PayjoinClient.BIP21EndpointKey, link);
            }
            return bip21.ToString();
        }
    }
}
