using System.Collections.Generic;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Components.StoreSelector;
using BTCPayServer.Data;

namespace BTCPayServer.Models.NotificationViewModels
{
    public class IndexViewModel : BasePagingViewModel
    {
        public List<NotificationViewModel> Items { get; set; } = new List<NotificationViewModel>();
        public string SearchText { get; set; }
        public string Status { get; set; }
        public SearchString Search { get; set; }
        public override int CurrentPageCount => Items.Count;
    }

    public class NotificationIndexViewModel : IndexViewModel
    {
        public List<StoreData> Stores { get; set; }
    }
}
