using System.Collections.Generic;
using BTCPayServer.Abstractions.Contracts;

namespace BTCPayServer.Models.NotificationViewModels
{
    public class IndexViewModel
    {
        public int Skip { get; set; }
        public int Count { get; set; }
        public int Total { get; set; }
        public List<NotificationViewModel> Items { get; set; }
    }


}
