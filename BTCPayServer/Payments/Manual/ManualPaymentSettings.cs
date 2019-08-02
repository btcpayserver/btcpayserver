using System;

namespace BTCPayServer.Payments.Bitcoin
{
    public class ManualPaymentSettings : ISupportedPaymentMethod
    {
        public PaymentMethodId PaymentId { get; } = StaticPaymentId;
        public static PaymentMethodId StaticPaymentId { get; } = new PaymentMethodId(BTCPayNetworkProvider.ManualCryptoCode, PaymentTypes.Manual);

        public string DisplayText { get; set; } = string.Empty;
        public bool AllowCustomerToMarkPaid { get; set; } = false;
        public bool AllowPartialPaymentInput { get; set; } = false;
        public bool AllowPaymentNote { get; set; } = false;
        public bool SetPaymentAsConfirmed { get; set; } = true;
    }
}
