using System;

namespace BTCPayServer.Client.Models
{
    public class FakePaymentResponse
    {
        public Decimal AmountRemaining { get; set; }
        public String Txid { get; set; }
        public String SuccessMessage { get; set; }
        public String ErrorMessage { get; set; }
    }
}
