using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Services;

namespace BTCPayServer.Models.WalletViewModels
{
    public class ListTransactionsViewModel
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
            public HashSet<Label> Labels { get; set; } = new HashSet<Label>();
        }
        public HashSet<Label> Labels { get; set; } = new HashSet<Label>();
        public List<TransactionViewModel> Transactions { get; set; } = new List<TransactionViewModel>();
    }
}
