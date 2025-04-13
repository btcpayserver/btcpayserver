using System;
using System.Collections.Generic;
using BTCPayServer.Data;

namespace BTCPayServer.Models.WalletViewModels
{
    public class ListTransactionsViewModel : BasePagingViewModel
    {
        public class TransactionViewModel
        {
            public DateTimeOffset Timestamp { get; set; }
            public bool IsConfirmed { get; set; }
            public bool CanBumpFee { get; set; }
            public string Comment { get; set; }
            public string Id { get; set; }
            public string Link { get; set; }
            public bool Positive { get; set; }
            public string Balance { get; set; }
            public HashSet<TransactionTagModel> Tags { get; set; } = new();
        }
        public HashSet<(string Text, string Color, string TextColor)> Labels { get; set; } = new();
        public List<TransactionViewModel> Transactions { get; set; } = new();
        public override int CurrentPageCount => Transactions.Count;
        public string CryptoCode { get; set; }
        public PendingTransaction[] PendingTransactions { get; set; }
    }
}
