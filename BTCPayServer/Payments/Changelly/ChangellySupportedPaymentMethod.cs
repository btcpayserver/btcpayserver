namespace BTCPayServer.Payments.Changelly
{
    public class ChangellySupportedPaymentMethod : ISupportedPaymentMethod
    {
        public static readonly PaymentMethodId ChangellySupportedPaymentMethodId =
            new PaymentMethodId("Changelly", PaymentTypes.ThirdParty);

        public PaymentMethodId PaymentId { get; } = ChangellySupportedPaymentMethodId;

        public string ApiKey { get; set; }
        public string ApiSecret { get; set; }
        public string ApiUrl { get; set; }
        public bool Enabled { get; set; }
        public string ChangellyMerchantId { get; set; }

        public bool IsConfigured()
        {
            return
                !string.IsNullOrEmpty(ApiKey) ||
                !string.IsNullOrEmpty(ApiSecret) ||
                !string.IsNullOrEmpty(ApiUrl);
        }
    }
}
