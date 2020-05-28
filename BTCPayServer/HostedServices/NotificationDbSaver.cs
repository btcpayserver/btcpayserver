using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Events.Notifications;
using Microsoft.AspNetCore.Identity;

namespace BTCPayServer.HostedServices
{
    public class NotificationDbSaver : EventHostedServiceBase
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public NotificationDbSaver(UserManager<ApplicationUser> userManager,
            EventAggregator eventAggregator) : base(eventAggregator) {
            _userManager = userManager;
        }

        public static List<NotificationData> Notif = new List<NotificationData>();

        protected override void SubscribeToEvents()
        {
            Subscribe<NewVersionNotification>();
            base.SubscribeToEvents();
        }

        protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
        {
            if (evt is NewVersionNotification)
            {
                var data = (evt as NewVersionNotification).ToData();

                var admins = await _userManager.GetUsersInRoleAsync(Roles.ServerAdmin);
                foreach (var admin in admins)
                {
                    data.Id = Guid.NewGuid().ToString();
                    data.ApplicationUserId = admin.Id;

                    Notif.Add(data);
                }
            }
        }
    }
}
