namespace BTCPayServer.Payments.Lightning
{
    public class LightningPaymentMethodViewExtension : IPaymentMethodViewExtension
    {
        public LightningPaymentMethodViewExtension(PaymentMethodId paymentMethodId)
        {
            PaymentMethodId = paymentMethodId;
        }
        public PaymentMethodId PaymentMethodId { get; }

        public void RegisterViews(PaymentMethodViewContext context)
        {
            context.RegisterCheckoutUI(new CheckoutUIPaymentMethodSettings()
            {
                ExtensionPartial = "Lightning/LightningLikeMethodCheckout",
                CheckoutBodyVueComponentName = "LightningLikeMethodCheckout",
                CheckoutHeaderVueComponentName = "LightningLikeMethodCheckoutHeader",
                NoScriptPartialName = "Lightning/LightningLikeMethodCheckoutNoScript"
            });
        }
    }
}
