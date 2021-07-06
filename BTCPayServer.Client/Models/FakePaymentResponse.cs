using System;

namespace BTCPayServer.Client.Models
{
    public class FakePaymentResponse
    {
        public Decimal amountRemaining { get; set; }
        public String txid { get; set; }
        public String successMessage { get; set; }
        public String errorMessage { get; set; }
    }
}
