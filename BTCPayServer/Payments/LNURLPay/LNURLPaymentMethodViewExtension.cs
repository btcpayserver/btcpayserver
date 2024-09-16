namespace BTCPayServer.Payments.LNURLPay
{
    public class LNURLPaymentMethodViewExtension : IPaymentMethodViewExtension
    {
        public LNURLPaymentMethodViewExtension(PaymentMethodId paymentMethodId)
        {
            PaymentMethodId = paymentMethodId;
        }
        public PaymentMethodId PaymentMethodId { get; }

        public void RegisterViews(PaymentMethodViewContext context)
        {
            var details = context.Details;
            if (details is not LNURLPayPaymentMethodDetails d)
                return;
            context.RegisterPaymentMethodDetails("LNURL/AdditionalPaymentMethodDetails");
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
