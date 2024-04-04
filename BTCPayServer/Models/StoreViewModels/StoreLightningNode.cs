using BTCPayServer.Payments;

namespace BTCPayServer.Models.StoreViewModels
{
    public class StoreLightningNode
    {
        public string CryptoCode { get; set; }
        public PaymentMethodId PaymentMethodId { get; set; }
        public string Address { get; set; }
        public bool Enabled { get; set; }
    }
}
