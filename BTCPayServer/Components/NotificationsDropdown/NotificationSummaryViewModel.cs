using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Models.NotificationViewModels;

namespace BTCPayServer.Components.NotificationsDropdown
{
    public class NotificationSummaryViewModel
    {
        public int UnseenCount { get; set; }
        public List<NotificationViewModel> Last5 { get; set; }
    }
}
