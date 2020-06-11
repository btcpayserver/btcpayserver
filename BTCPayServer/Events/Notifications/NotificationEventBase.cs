using System;
using BTCPayServer.Data;
using BTCPayServer.Models.NotificationViewModels;
using Newtonsoft.Json;

namespace BTCPayServer.Events.Notifications
{
    // Make sure to keep all NotificationEventBase classes in same namespace
    // because of dependent initialization and parsing to view models logic
    // IndexViewModel.cs#32
    internal abstract class NotificationEventBase
    {
        internal virtual string NotificationType { get { return GetType().Name; } }

        public NotificationData ToData()
        {
            var obj = JsonConvert.SerializeObject(this);

            var data = new NotificationData
            {
                Created = DateTimeOffset.UtcNow,
                NotificationType = NotificationType,
                Blob = ZipUtils.Zip(obj),
                Seen = false
            };
            return data;
        }

        public abstract NotificationViewModel ToViewModel(NotificationData data);
    }
}
