namespace BTCPayServer.Payments.Monero
{
    public class MoneroSupportedPaymentMethod : ISupportedPaymentMethod
    {

        public string CryptoCode { get; set; }
        public long AccountIndex { get; set; }
        public PaymentMethodId PaymentId => new PaymentMethodId(CryptoCode, MoneroPaymentType.Instance);
    }
}
