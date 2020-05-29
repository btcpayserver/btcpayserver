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
        private readonly UserManager<ApplicationUser> _UserManager;
        private readonly ApplicationDbContextFactory _ContextFactory;

        public NotificationDbSaver(UserManager<ApplicationUser> userManager,
                    ApplicationDbContextFactory contextFactory,
                    EventAggregator eventAggregator) : base(eventAggregator)
        {
            _UserManager = userManager;
            _ContextFactory = contextFactory;
        }

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

                var admins = await _UserManager.GetUsersInRoleAsync(Roles.ServerAdmin);

                using (var db = _ContextFactory.CreateContext())
                {
                    foreach (var admin in admins)
                    {
                        data.Id = Guid.NewGuid().ToString();
                        data.ApplicationUserId = admin.Id;

                        db.Notifications.Add(data);
                    }

                    await db.SaveChangesAsync();
                }
            }
        }
    }
}
