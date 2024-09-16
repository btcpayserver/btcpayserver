using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Services.Altcoins.Monero.Payments
{
    public class MoneroPaymentModelExtension : IPaymentModelExtension
    {
        private readonly BTCPayNetworkBase _network;
        private readonly IPaymentLinkExtension paymentLinkExtension;

        public MoneroPaymentModelExtension(
            PaymentMethodId paymentMethodId,
            IEnumerable<IPaymentLinkExtension> paymentLinkExtensions,
            BTCPayNetworkBase network)
        {
            PaymentMethodId = paymentMethodId;
            _network = network;
            paymentLinkExtension = paymentLinkExtensions.Single(p => p.PaymentMethodId == PaymentMethodId);
        }
        public PaymentMethodId PaymentMethodId { get; }

        public string DisplayName => _network.DisplayName;

        public string Image => _network.CryptoImagePath;
        public string Badge => "";

        public void ModifyPaymentModel(PaymentModelContext context)
        {
            if (context.Model.Activated)
            {
                context.Model.InvoiceBitcoinUrl = paymentLinkExtension.GetPaymentLink(context.Prompt, context.UrlHelper);
                context.Model.InvoiceBitcoinUrlQR = context.Model.InvoiceBitcoinUrl;
            }
            else
            {
                context.Model.InvoiceBitcoinUrl = "";
                context.Model.InvoiceBitcoinUrlQR = "";
            }
        }
    }
}
