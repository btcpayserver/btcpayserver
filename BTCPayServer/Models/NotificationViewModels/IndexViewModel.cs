using System.Collections.Generic;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Data;

namespace BTCPayServer.Models.NotificationViewModels
{
    public class IndexViewModel : BasePagingViewModel
    {
        public List<NotificationViewModel> Items { get; set; } = [];
        public string SearchText { get; set; }
        public string Status { get; set; }
        public SearchString Search { get; set; }
        public override int CurrentPageCount => Items.Count;
    }

    public class NotificationIndexViewModel : IndexViewModel
    {
        public List<StoreFilterOption> StoreFilterOptions { get; set; }
    }

    public class StoreFilterOption
    {
        public bool Selected { get; set; }
        public string Text { get; set; }
        public string Value { get; set; }
    }
}
