using System;
using System.Collections.Generic;
using BTCPayServer.Services.Wallets;
using NBitcoin;

namespace BTCPayServer.Payments.PayJoin
{
    public class PayJoinStateRecordedItem
    {
        public Transaction Transaction { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public uint256 ProposedTransactionHash { get; set; }
        public List<ReceivedCoin> CoinsExposed { get; set; }
        public decimal TotalOutputAmount { get; set; }
        public decimal ContributedAmount { get; set; }
        public uint256 OriginalTransactionHash { get; set; }

        public string InvoiceId { get; set; }

        public override string ToString()
        {
            return $"{InvoiceId}_{OriginalTransactionHash}";
        }
    }
}
