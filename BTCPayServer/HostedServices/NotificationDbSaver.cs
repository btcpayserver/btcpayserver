using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Events.Notifications;
using BTCPayServer.Models.NotificationViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;

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
        private readonly ApplicationDbContextFactory _factory;
        private readonly UserManager<ApplicationUser> _userManager;

        public NotificationManager(ApplicationDbContextFactory factory, UserManager<ApplicationUser> userManager)
        {
            _factory = factory;
            _userManager = userManager;
        }

        private static MemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
        private const int _cacheExpiryMs = 5000;
        public NotificationSummaryViewModel GetSummaryNotifications(ClaimsPrincipal user)
        {
            var userId = _userManager.GetUserId(user);

            if (_cache.TryGetValue<NotificationSummaryViewModel>(userId, out var obj))
                return obj;
            
            var resp = fetchNotificationsFromDb(userId);
            _cache.Set(userId, resp, new MemoryCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromMilliseconds(_cacheExpiryMs)));

            return resp;
        }

        private NotificationSummaryViewModel fetchNotificationsFromDb(string userId)
        {
            var resp = new NotificationSummaryViewModel();
            using (var _db = _factory.CreateContext())
            {
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
