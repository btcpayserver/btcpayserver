using System;
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Client.Models;
using BTCPayServer.Payments;

namespace BTCPayServer.Models.WalletViewModels
{
    public class PayoutsModel : BasePagingViewModel
    {
        public string PullPaymentId { get; set; }
        public string Command { get; set; }
        public Dictionary<PayoutState, int> PayoutStateCount { get; set; }
        public Dictionary<string, int> PaymentMethodCount { get; set; }
        public string PaymentMethodId { get; set; }

        public List<PayoutModel> Payouts { get; set; }
        public override int CurrentPageCount => Payouts.Count;
        public IEnumerable<PaymentMethodId> PaymentMethods { get; set; }
        public PayoutState PayoutState { get; set; }
        public string PullPaymentName { get; set; }

        public class PayoutModel
        {
            public string PayoutId { get; set; }
            public bool Selected { get; set; }
            public DateTimeOffset Date { get; set; }
            public string PullPaymentId { get; set; }
            public string Source { get; set; }
            public string SourceLink { get; set; }
            public string Destination { get; set; }
            public string Amount { get; set; }
            public string ProofLink { get; set; }
        }

        public string[] GetSelectedPayouts(PayoutState state)
        {
            return Payouts.Where(model => model.Selected).Select(model => model.PayoutId)
                .ToArray();
        }
    }
}
