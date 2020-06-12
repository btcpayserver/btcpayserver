using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTCPayServer.Events.Notifications
{
    internal class NotificationEvent
    {
        internal string[] ApplicationUserIds { get; set; }
        internal NotificationBase Notification { get; set; }
    }
}
