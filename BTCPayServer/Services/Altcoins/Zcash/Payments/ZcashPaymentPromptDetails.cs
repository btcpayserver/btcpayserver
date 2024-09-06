using BTCPayServer.Payments;

namespace BTCPayServer.Services.Altcoins.Zcash.Payments
{
    public class ZcashPaymentPromptDetails
    {
        public long AccountIndex { get; set; }
        public long AddressIndex { get; set; }
        public string DepositAddress { get; set; }
    }
}
