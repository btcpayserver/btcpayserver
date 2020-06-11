using System;
using BTCPayServer.Data;
using BTCPayServer.Models.NotificationViewModels;
using ExchangeSharp;
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
                Blob = obj.ToBytesUTF8(),
                Seen = false
            };
            return data;
        }

        public abstract NotificationViewModel ToViewModel(NotificationData data);
    }
}
