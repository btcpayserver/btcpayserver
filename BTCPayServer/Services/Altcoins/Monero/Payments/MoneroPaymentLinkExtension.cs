#nullable enable
using System.Globalization;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Altcoins;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Services.Altcoins.Monero.Payments
{
    public class MoneroPaymentLinkExtension : IPaymentLinkExtension
    {
        private readonly MoneroLikeSpecificBtcPayNetwork _network;

        public MoneroPaymentLinkExtension(PaymentMethodId paymentMethodId, MoneroLikeSpecificBtcPayNetwork network)
        {
            PaymentMethodId = paymentMethodId;
            _network = network;
        }
        public PaymentMethodId PaymentMethodId { get; }

        public string? GetPaymentLink(PaymentPrompt prompt, IUrlHelper? urlHelper)
        {
            var due = prompt.Calculate().Due;
            return $"{_network.UriScheme}:{prompt.Destination}?tx_amount={due.ToString(CultureInfo.InvariantCulture)}";
        }
    }
}
