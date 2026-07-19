using System;
using System.Collections.Generic;
using BTCPayServer.Components.LabelSelector;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Wallets;

namespace BTCPayServer.Plugins.Wallets.Views.ViewModels
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
            public TransactionHistoryLine HistoryLine { get; set; }
        }
        public HashSet<LabelSelectorItemViewModel> Labels { get; set; } = new();
        public List<TransactionViewModel> Transactions { get; set; } = new();
        public override int CurrentPageCount => Transactions.Count;
        public string CryptoCode { get; set; }
        public PendingTransaction[] PendingTransactions { get; set; }
        public bool HasFilters { get; set; }

        protected override void AddUIFilters(SearchString search)
        {
            base.AddUIFilters(search);
            search.UIFilterTypes.Add("direction");
            LabelSelector.AddUIFilters(search);
        }

        protected override void RunFilterCommand(SearchString search)
        {
            base.RunFilterCommand(search);
            LabelSelector.RunFilterCommand(search, FilterCommand);
        }
    }
}
