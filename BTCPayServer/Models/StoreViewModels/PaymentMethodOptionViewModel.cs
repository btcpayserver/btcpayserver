using BTCPayServer.Payments;

namespace BTCPayServer.Models.StoreViewModels
{
    public class PaymentMethodOptionViewModel
    {
        public class Format
        {
            public string Name { get; set; }
            public string Value { get; set; }
            public PaymentMethodId PaymentId { get; set; }
        }
    }
}
