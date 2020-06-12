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
        private readonly ApplicationDbContextFactory _ContextFactory;

        public NotificationDbSaver(ApplicationDbContextFactory contextFactory,
                    EventAggregator eventAggregator) : base(eventAggregator)
        {
            _ContextFactory = contextFactory;
        }

        protected override void SubscribeToEvents()
        {
            Subscribe<NotificationEvent>();
            base.SubscribeToEvents();
        }

        protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
        {
            var casted = evt as NotificationEvent;
            using (var db = _ContextFactory.CreateContext())
            {
                foreach (var uid in casted.ApplicationUserIds)
                {
                    var data = casted.Notification.ToData(uid);
                    db.Notifications.Add(data);
                }

                await db.SaveChangesAsync();
            }
        }
    }

    public class NotificationManager
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public NotificationManager(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        public NotificationSummaryViewModel GetSummaryNotifications(ClaimsPrincipal user)
        {
            var resp = new NotificationSummaryViewModel();
            var userId = _userManager.GetUserId(user);

            // TODO: Soft caching in order not to pound database too much
            resp.UnseenCount = _db.Notifications
                .Where(a => a.ApplicationUserId == userId && !a.Seen)
                .Count();

            if (resp.UnseenCount > 0)
            {
                try
                {
                    resp.Last5 = _db.Notifications
                        .Where(a => a.ApplicationUserId == userId && !a.Seen)
                        .OrderByDescending(a => a.Created)
                        .Take(5)
                        .Select(a => a.ViewModel())
                        .ToList();
                }
                catch (System.IO.InvalidDataException iex)
                {
                    // invalid notifications that are not pkuzipable, burn them all
                    var notif = _db.Notifications.Where(a => a.ApplicationUserId == userId);
                    _db.Notifications.RemoveRange(notif);
                    _db.SaveChanges();

                    resp.UnseenCount = 0;
                    resp.Last5 = new List<NotificationViewModel>();
                }
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
