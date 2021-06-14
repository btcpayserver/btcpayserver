using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Components.NotificationsDropdown;
using BTCPayServer.Data;
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

        public NotificationManager(ApplicationDbContextFactory factory, UserManager<ApplicationUser> userManager,
            IMemoryCache memoryCache, IEnumerable<INotificationHandler> handlers, EventAggregator eventAggregator)
        {
            _factory = factory;
            _userManager = userManager;
            _memoryCache = memoryCache;
            _eventAggregator = eventAggregator;
            _handlersByNotificationType = handlers.ToDictionary(h => h.NotificationType);
        }

        private const int _cacheExpiryMs = 5000;

        public async Task<NotificationSummaryViewModel> GetSummaryNotifications(ClaimsPrincipal user)
        {
            var userId = _userManager.GetUserId(user);
            var cacheKey = GetNotificationsCacheId(userId);

            return await _memoryCache.GetOrCreateAsync(cacheKey, async entry =>
            {
                var resp = await GetNotifications(new NotificationsQuery()
                {
                    Seen = false, Skip = 0, Take = 5, UserId = userId
                });
                entry.SetAbsoluteExpiration(TimeSpan.FromMilliseconds(_cacheExpiryMs));
                var res = new NotificationSummaryViewModel() {Last5 = resp.Items, UnseenCount = resp.Count};
                entry.Value = res;
                return res;
            });
        }
        
        public void InvalidateNotificationCache(params string[] userIds)
        {
            foreach (var userId in userIds)
            {
                _memoryCache.Remove(GetNotificationsCacheId(userId));
                _eventAggregator.Publish(new UserNotificationsUpdatedEvent() {UserId = userId});
            }
        }

        private static string GetNotificationsCacheId(string userId)
        {
            return $"notifications-{userId}";
        }

        public async Task<(List<NotificationViewModel> Items, int Count)> GetNotifications(NotificationsQuery query)
        {
            await using var dbContext = _factory.CreateContext();

            var queryables = GetNotificationsQueryable(dbContext, query);

            return (Items: (await queryables.withPaging.ToListAsync()).Select(ToViewModel).Where(model => model != null).ToList(),
                Count: await queryables.withoutPaging.CountAsync());
        }

        private ( IQueryable<NotificationData> withoutPaging, IQueryable<NotificationData> withPaging)
            GetNotificationsQueryable(ApplicationDbContext dbContext, NotificationsQuery query)
        {
            var queryable = dbContext.Notifications.AsQueryable();
            if (query.Ids?.Any() is true)
            {
                queryable = queryable.Where(data => query.Ids.Contains(data.Id));
            }

            if (!string.IsNullOrEmpty(query.UserId))
            {
                queryable = queryable.Where(data => data.ApplicationUserId == query.UserId);
            }

            if (query.Seen.HasValue)
            {
                queryable = queryable.Where(data => data.Seen == query.Seen);
            }

            queryable = queryable.OrderByDescending(a => a.Created);

            var queryable2 = queryable;
            if (query.Skip.HasValue)
            {
                queryable2 = queryable.Skip(query.Skip.Value);
            }

            if (query.Take.HasValue)
            {
                queryable2 = queryable.Take(query.Take.Value);
            }

            return (queryable, queryable2);
        }


        public async Task<List<NotificationViewModel>> ToggleSeen(NotificationsQuery notificationsQuery, bool? setSeen)
        {
            await using var dbContext = _factory.CreateContext();

            var queryables = GetNotificationsQueryable(dbContext, notificationsQuery);
            var items = await queryables.withPaging.ToListAsync();
            var userIds = items.Select(data => data.ApplicationUserId).Distinct();
            foreach (var notificationData in items)
            {
                notificationData.Seen = setSeen.GetValueOrDefault(!notificationData.Seen);
            }

            await dbContext.SaveChangesAsync();
            InvalidateNotificationCache(userIds.ToArray());
            return items.Select(ToViewModel).Where(model => model != null).ToList();
        }

        public async Task Remove(NotificationsQuery notificationsQuery)
        {
            await using var dbContext = _factory.CreateContext();

            var queryables = GetNotificationsQueryable(dbContext, notificationsQuery);
            dbContext.RemoveRange(queryables.withPaging);
            await dbContext.SaveChangesAsync();

            if (!string.IsNullOrEmpty(notificationsQuery.UserId))
                InvalidateNotificationCache(notificationsQuery.UserId);
        }


        private NotificationViewModel ToViewModel(NotificationData data)
        {
            var handler = GetHandler(data.NotificationType);
            if (handler is null)
                return null;
            var notification = JsonConvert.DeserializeObject(ZipUtils.Unzip(data.Blob), handler.NotificationBlobType);
            var obj = new NotificationViewModel {Id = data.Id, Created = data.Created, Seen = data.Seen};
            handler.FillViewModel(notification, obj);
            return obj;
        }

        public INotificationHandler GetHandler(string notificationId)
        {
            _handlersByNotificationType.TryGetValue(notificationId, out var h);

            return h;
            // throw new InvalidOperationException($"No INotificationHandler found for {notificationId}");
        }
    }

    public class NotificationsQuery
    {
        public string[] Ids { get; set; }
        public string UserId { get; set; }
        public int? Skip { get; set; }
        public int? Take { get; set; }
        public bool? Seen { get; set; }
    }
}
