#nullable enable
using System.Globalization;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Altcoins;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Services.Altcoins.Zcash.Payments
{
    public class ZcashPaymentLinkExtension : IPaymentLinkExtension
    {
        private readonly ZcashLikeSpecificBtcPayNetwork _network;

        public ZcashPaymentLinkExtension(PaymentMethodId paymentMethodId, ZcashLikeSpecificBtcPayNetwork network)
        {
            PaymentMethodId = paymentMethodId;
            _network = network;
        }
        public PaymentMethodId PaymentMethodId { get; }

        public string? GetPaymentLink(PaymentPrompt prompt, IUrlHelper? urlHelper)
        {
            var due = prompt.Calculate().Due;
            return $"{_network.UriScheme}:{prompt.Destination}?amount={due.ToString(CultureInfo.InvariantCulture)}";
        }
    }
}
