using System.Collections.Generic;
using BTCPayServer.Abstractions.Contracts;

namespace BTCPayServer.Models.NotificationViewModels
{
    public class IndexViewModel : BasePagingViewModel
    {
        public List<NotificationViewModel> Items { get; set; }
        public string SearchText { get; set; }
        public string Status { get; set; }
        public SearchString Search { get; set; }
        public override int CurrentPageCount => Items.Count;
    }
}
