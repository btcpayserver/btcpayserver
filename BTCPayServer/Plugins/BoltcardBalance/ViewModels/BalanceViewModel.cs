using System;
using System.Collections.Generic;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Plugins.BoltcardBalance.ViewModels
{
    public class BalanceViewModel
    {
        public class Transaction
        {
            public DateTimeOffset Date { get; set; }
            public bool Positive => Balance >= 0;
            public decimal Balance { get; set; }
            public PayoutState Status { get; internal set; }
        }
        public string Currency { get; set; }
        public decimal AmountDue { get; set; }
        public List<Transaction> Transactions { get; set; } = new List<Transaction>();
        public string LNUrlBech32 { get; set; }
        public string BoltcardKeysResetLink { get; set; }
        public string PullPaymentLink { get; set; }
        public string LNUrlPay { get; set; }

        public string WipeData{ get; set; }
    }
}
