using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data.Data;
using BTCPayServer.Events;
using BTCPayServer.Events.Notifications;

namespace BTCPayServer.HostedServices
{
    public class NotificationDbSaver : EventHostedServiceBase
    {
        public NotificationDbSaver(EventAggregator eventAggregator) : base(eventAggregator) { }

        protected override void SubscribeToEvents()
        {
            Subscribe<NewVersionNotification>();
            base.SubscribeToEvents();
        }

        public static List<NotificationData> Notif = new List<NotificationData>();

        protected override Task ProcessEvent(object evt, CancellationToken cancellationToken)
        {
            if (evt is NewVersionNotification)
            {
                var data = (evt as NewVersionNotification).ToData();

                //var userIds = new[] { "rockstar", "nicolas", "kukkie", "pavel" };
                //foreach (var uid in userIds)
                data.Id = Guid.NewGuid().ToString();
                Notif.Add(data);
            }

            return Task.CompletedTask;
        }
    }
}
