using System;
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Payments;

namespace BTCPayServer.Models.WalletViewModels
{
    public class PayoutsModel
    {
        public string PullPaymentId { get; set; }
        public string Command { get; set; }
        public List<PayoutStateSet> PayoutStateSets{ get; set; } 
        public PaymentMethodId PaymentMethodId { get; set; }

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

        public class PayoutStateSet
        {
            public PayoutState State { get; set; }
            public List<PayoutModel> Payouts { get; set; }
        }

        public string[] GetSelectedPayouts(PayoutState state)
        {
            return PayoutStateSets.Where(set => set.State == state)
                .SelectMany(set => set.Payouts.Where(model => model.Selected).Select(model => model.PayoutId))
                .ToArray();
        }
    }
}
