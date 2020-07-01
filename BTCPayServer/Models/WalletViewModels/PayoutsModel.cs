using System;
using System.Collections.Generic;

namespace BTCPayServer.Models.WalletViewModels
{
    public class PayoutsModel
    {
        public string PullPaymentId { get; set; }
        public string Command { get; set; }
        public class PayoutModel
        {
            public string PayoutId { get; set; }
            public bool Selected { get; set; }
            public DateTimeOffset Date { get; set; }
            public string PullPaymentId { get; set; }
            public string PullPaymentName { get; set; }
            public string Destination { get; set; }
            public string Amount { get; set; }
            public string TransactionLink { get; set; }
        }
        public List<PayoutModel> WaitingForApproval { get; set; } = new List<PayoutModel>();
        public List<PayoutModel> Other { get; set; } = new List<PayoutModel>();
    }
}
