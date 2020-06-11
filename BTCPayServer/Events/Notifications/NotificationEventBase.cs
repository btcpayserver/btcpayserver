using System;
using BTCPayServer.Data;
using BTCPayServer.Models.NotificationViewModels;
using Newtonsoft.Json;

namespace BTCPayServer.Events.Notifications
{
    public abstract class NotificationEventBase
    {
        public virtual string NotificationType { get { return GetType().Name; } }

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
