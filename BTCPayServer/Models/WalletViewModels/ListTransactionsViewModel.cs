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
            public HashSet<ColoredLabel> Labels { get; set; } = new HashSet<ColoredLabel>();
        }
        public HashSet<ColoredLabel> Labels { get; set; } = new HashSet<ColoredLabel>();
        public List<TransactionViewModel> Transactions { get; set; } = new List<TransactionViewModel>();
    }
}
