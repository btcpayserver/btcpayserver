using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Services.Notifications.Blobs
{
    [AttributeUsage(AttributeTargets.Class)]
    public class NotificationAttribute : Attribute
    {
        public NotificationAttribute(string notificationType)
        {
            NotificationType = notificationType;
        }
        public string NotificationType { get; }
    }
}
