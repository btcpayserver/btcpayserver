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
using BTCPayServer.Models.NotificationViewModels;
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
            SubscribeAllChildrenOfNotificationEventBase();
            base.SubscribeToEvents();
        }

        // subscribe all children of NotificationEventBase
        public void SubscribeAllChildrenOfNotificationEventBase()
        {
            var method = this.GetType().GetMethod(nameof(SubscribeHelper));
            var notificationTypes = this.GetType().Assembly.GetTypes().Where(a => typeof(NotificationEventBase).IsAssignableFrom(a));
            foreach (var notif in notificationTypes)
            {
                var generic = method.MakeGenericMethod(notif);
                generic.Invoke(this, null);
            }
        }

        // we need publicly accessible method for reflection invoke
        public void SubscribeHelper<T>() => base.Subscribe<T>();

        protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
        {
            if (evt is NotificationEventBase)
            {
                var data = (evt as NotificationEventBase).ToData();

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

        public NotificationSummaryViewModel GetSummaryNotifications(ClaimsPrincipal user)
        {
            var resp = new NotificationSummaryViewModel();
            var claimWithId = user.Claims.SingleOrDefault(a => a.Type == ClaimTypes.NameIdentifier);

            // TODO: Soft caching in order not to pound database too much
            resp.UnseenCount = _db.Notifications
                .Where(a => a.ApplicationUserId == claimWithId.Value && !a.Seen)
                .Count();

            if (resp.UnseenCount > 0)
            {
                resp.Last5 = _db.Notifications
                    .Where(a => a.ApplicationUserId == claimWithId.Value && !a.Seen)
                    .OrderByDescending(a => a.Created)
                    .Take(5)
                    .Select(a => a.ViewModel())
                    .ToList();
            }
            else
            {
                resp.Last5 = new List<NotificationViewModel>();
            }

            return resp;
        }
    }

    public class NotificationSummaryViewModel
    {
        public int UnseenCount { get; set; }
        public List<NotificationViewModel> Last5 { get; set; }
    }
}
