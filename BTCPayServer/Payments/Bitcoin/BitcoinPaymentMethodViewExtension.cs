namespace BTCPayServer.Payments.Bitcoin
{
    public class BitcoinPaymentMethodViewExtension : IPaymentMethodViewExtension
    {
        public BitcoinPaymentMethodViewExtension(PaymentMethodId paymentMethodId)
        {
            PaymentMethodId = paymentMethodId;
        }
        public PaymentMethodId PaymentMethodId { get; }

        public void RegisterViews(PaymentMethodViewContext context)
        {
            context.RegisterCheckoutUI(new CheckoutUIPaymentMethodSettings
            {
                ExtensionPartial = "Bitcoin/BitcoinLikeMethodCheckout",
                CheckoutBodyVueComponentName = "BitcoinLikeMethodCheckout",
                CheckoutHeaderVueComponentName = "BitcoinLikeMethodCheckoutHeader",
                NoScriptPartialName = "Bitcoin/BitcoinLikeMethodCheckoutNoScript"
            });
        }
    }
}
