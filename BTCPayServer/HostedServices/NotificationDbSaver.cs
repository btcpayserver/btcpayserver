using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
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

    public class NotificationManager
    {
        private readonly ApplicationDbContext _db;

        public NotificationManager(ApplicationDbContext db)
        {
            _db = db;
        }

        public int GetNotificationCount(ClaimsPrincipal user)
        {
            var claimWithId = user.Claims.SingleOrDefault(a => a.Type == ClaimTypes.NameIdentifier);

            // TODO: Soft caching in order not to pound database too much
            var count = _db.Notifications
                .Where(a => a.ApplicationUserId == claimWithId.Value && !a.Seen)
                .Count();
            return count;
        }
    }
}
