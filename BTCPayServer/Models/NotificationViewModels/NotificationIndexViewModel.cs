using System.Collections.Generic;
using BTCPayServer.Abstractions.Contracts;

namespace BTCPayServer.Models.NotificationViewModels
{
    public class NotificationIndexViewModel : BasePagingViewModel
    {
        public List<NotificationViewModel> Items { get; set; } = [];
        public override int CurrentPageCount => Items.Count;
        public List<StoreFilterOption> StoreFilterOptions { get; set; }
        protected override void AddUIFilters(SearchString search)
        {
            base.AddUIFilters(search);
            search.UIFilters.Add("type");
            search.UIFilters.Add("storeid");
            search.UIFilters.Add("all");
        }
    }

    public class StoreFilterOption
    {
        public bool Selected { get; set; }
        public string Text { get; set; }
        public string Value { get; set; }
    }
}
