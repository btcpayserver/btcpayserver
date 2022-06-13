using System.Collections.Generic;
using BTCPayServer.Abstractions.Contracts;

namespace BTCPayServer.Components.Notifications
{
    public class NotificationsViewModel
    {
        public string ReturnUrl { get; set; }
        public int UnseenCount { get; set; }
        public List<NotificationViewModel> Last5 { get; set; }
    }
}
