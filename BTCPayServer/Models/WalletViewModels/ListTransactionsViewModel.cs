using System;
using System.Collections.Generic;
using BTCPayServer.Services.Labels;

namespace BTCPayServer.Models.WalletViewModels
{
    public class ListTransactionsViewModel : BasePagingViewModel
    {
        public class TransactionViewModel
        {
            public DateTimeOffset Timestamp { get; set; }
            public bool IsConfirmed { get; set; }
            public string Comment { get; set; }
            public string Id { get; set; }
            public string Link { get; set; }
            public bool Positive { get; set; }
            public string Balance { get; set; }
            public HashSet<TransactionTagModel> Tags { get; set; } = new HashSet<TransactionTagModel>();
        }
        public HashSet<(string Text, string Color, string TextColor)> Labels { get; set; } = new HashSet<(string Text, string Color, string TextColor)>();
        public List<TransactionViewModel> Transactions { get; set; } = new List<TransactionViewModel>();
        public override int CurrentPageCount => Transactions.Count;
        public string CryptoCode { get; set; }
    }
}
