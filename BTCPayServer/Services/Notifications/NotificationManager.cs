using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using BTCPayServer.Components.NotificationsDropdown;
using BTCPayServer.Data;
using BTCPayServer.Models.NotificationViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;

namespace BTCPayServer.Services.Notifications
{
    public class NotificationManager
    {
        private readonly ApplicationDbContextFactory _factory;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IMemoryCache _memoryCache;
        private readonly EventAggregator _eventAggregator;
        private readonly Dictionary<string, INotificationHandler> _handlersByNotificationType;
        private readonly Dictionary<Type, INotificationHandler> _handlersByBlobType;

        public NotificationManager(ApplicationDbContextFactory factory, UserManager<ApplicationUser> userManager,
            IMemoryCache memoryCache, IEnumerable<INotificationHandler> handlers, EventAggregator eventAggregator)
        {
            _factory = factory;
            _userManager = userManager;
            _memoryCache = memoryCache;
            _eventAggregator = eventAggregator;
            _handlersByNotificationType = handlers.ToDictionary(h => h.NotificationType);
            _handlersByBlobType = handlers.ToDictionary(h => h.NotificationBlobType);
        }

        private const int _cacheExpiryMs = 5000;

        public async Task<NotificationSummaryViewModel> GetSummaryNotifications(ClaimsPrincipal user)
        {
            var userId = _userManager.GetUserId(user);
            var cacheKey = GetNotificationsCacheId(userId);
            if (_memoryCache.TryGetValue<NotificationSummaryViewModel>(cacheKey, out var obj))
                return obj;

            var resp = await FetchNotificationsFromDb(userId);
            _memoryCache.Set(cacheKey, resp,
                new MemoryCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromMilliseconds(_cacheExpiryMs)));

            return resp;
        }

        public void InvalidateNotificationCache(string userId)
        {
            _memoryCache.Remove(GetNotificationsCacheId(userId));

            _eventAggregator.Publish(new UserNotificationsUpdatedEvent() { UserId = userId });
        }

        private static string GetNotificationsCacheId(string userId)
        {
            return $"notifications-{userId}";
        }

        private async Task<NotificationSummaryViewModel> FetchNotificationsFromDb(string userId)
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
                        resp.Last5 = (await _db.Notifications
                                .Where(a => a.ApplicationUserId == userId && !a.Seen)
                                .OrderByDescending(a => a.Created)
                                .Take(5)
                                .ToListAsync())
                            .Select(a => ToViewModel(a))
                            .ToList();
                    }
                    catch (System.IO.InvalidDataException)
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

        public NotificationViewModel ToViewModel(NotificationData data)
        {
            var handler = GetHandler(data.NotificationType);
            var notification = JsonConvert.DeserializeObject(ZipUtils.Unzip(data.Blob), handler.NotificationBlobType);
            var obj = new NotificationViewModel { Id = data.Id, Created = data.Created, Seen = data.Seen };
            handler.FillViewModel(notification, obj);
            return obj;
        }

        public INotificationHandler GetHandler(string notificationId)
        {
            if (_handlersByNotificationType.TryGetValue(notificationId, out var h))
                return h;
            throw new InvalidOperationException($"No INotificationHandler found for {notificationId}");
        }

        public INotificationHandler GetHandler(Type blobType)
        {
            if (_handlersByBlobType.TryGetValue(blobType, out var h))
                return h;
            throw new InvalidOperationException($"No INotificationHandler found for {blobType.Name}");
        }
    }
}
