using System;
using System.Collections.Generic;
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
            public string Rate { get; set; }
            public List<string> Rates { get; set; } = new();
            public RateBook WalletRateBook { get; set; }
            public RateBook InvoiceRateBook { get; set; }
            public string InvoiceId { get; set; }
            public TransactionHistoryLine HistoryLine { get; set; }
        }
        public HashSet<(string Text, string Color, string TextColor, long UsageCount)> Labels { get; set; } = new();
        public List<(string Text, string Color, string TextColor, long UsageCount)> PopularLabels { get; set; } = new();
        public List<TransactionViewModel> Transactions { get; set; } = new();
        public override int CurrentPageCount => Transactions.Count;
        public string CryptoCode { get; set; }
        public PendingTransaction[] PendingTransactions { get; set; }
        public List<string> Rates { get; set; } = new();
        public string SearchInputText { get; set; }
        public bool HasFilters { get; set; }

        protected override void AddUIFilters(SearchString search)
        {
            base.AddUIFilters(search);
            foreach (var filter in new[]{"label", "nolabel", "direction"})
                search.UIFilters.Add(filter);
        }

        protected override void RunFilterCommand(SearchString search)
        {
            base.RunFilterCommand(search);
            if (FilterCommand is "alllabels")
            {
                search.Filters.Remove("label");
                search.Filters.Remove("nolabel");
            }
            else if (FilterCommand is "nolabel")
            {
                search.SetFilter("nolabel", "true");
                search.Filters.Remove("label");
            }
            else if (FilterCommand.StartsWith("addlabel:"))
            {
                search.Filters.Remove("nolabel");
                search.SetFilter("label", FilterCommand.Substring("addlabel:".Length));
            }
        }
    }
}
