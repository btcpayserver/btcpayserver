using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Services.Altcoins.Monero.Services;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Services.Altcoins.Monero.Payments
{
    public class MoneroCheckoutModelExtension : ICheckoutModelExtension
    {
        private readonly BTCPayNetworkBase _network;
        private readonly PaymentMethodHandlerDictionary _handlers;
        private readonly IPaymentLinkExtension paymentLinkExtension;

        public MoneroCheckoutModelExtension(
            PaymentMethodId paymentMethodId,
            IEnumerable<IPaymentLinkExtension> paymentLinkExtensions,
            BTCPayNetworkBase network,
            PaymentMethodHandlerDictionary handlers)
        {
            PaymentMethodId = paymentMethodId;
            _network = network;
            _handlers = handlers;
            paymentLinkExtension = paymentLinkExtensions.Single(p => p.PaymentMethodId == PaymentMethodId);
        }
        public PaymentMethodId PaymentMethodId { get; }

        public string DisplayName => _network.DisplayName;

        public string Image => _network.CryptoImagePath;
        public string Badge => "";

        public void ModifyCheckoutModel(CheckoutModelContext context)
        {
            if (context is not { Handler: MoneroLikePaymentMethodHandler handler })
                return;
            context.Model.CheckoutBodyComponentName = BitcoinCheckoutModelExtension.CheckoutBodyComponentName;
            var details = context.InvoiceEntity.GetPayments(true)
                    .Select(p => p.GetDetails<MoneroLikePaymentData>(handler))
                    .Where(p => p is not null)
                    .FirstOrDefault();
            if (details is not null)
            {
                context.Model.ReceivedConfirmations = details.ConfirmationCount;
                context.Model.RequiredConfirmations = (int)MoneroListener.ConfirmationsRequired(details, context.InvoiceEntity.SpeedPolicy);
            }

            context.Model.InvoiceBitcoinUrl = paymentLinkExtension.GetPaymentLink(context.Prompt, context.UrlHelper);
            context.Model.InvoiceBitcoinUrlQR = context.Model.InvoiceBitcoinUrl;
        }
    }
}
