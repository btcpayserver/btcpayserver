using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
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
        private readonly IMemoryCache _memoryCache;
        private readonly EventAggregator _eventAggregator;
        private readonly Dictionary<string, INotificationHandler> _handlersByNotificationType;

        public NotificationManager(ApplicationDbContextFactory factory,
            IMemoryCache memoryCache, IEnumerable<INotificationHandler> handlers, EventAggregator eventAggregator)
        {
            _factory = factory;
            _memoryCache = memoryCache;
            _eventAggregator = eventAggregator;
            _handlersByNotificationType = handlers.ToDictionary(h => h.NotificationType);
        }


        public async Task<(List<NotificationViewModel> Items, int? Count)> GetSummaryNotifications(string userId, bool cachedOnly)
        {
            var cacheKey = GetNotificationsCacheId(userId);
            if (cachedOnly)
                return _memoryCache.Get<(List<NotificationViewModel> Items, int? Count)>(cacheKey);
            return await _memoryCache.GetOrCreateAsync(cacheKey, async entry =>
            {
                var res = await GetNotifications(new NotificationsQuery
                {
                    Seen = false,
                    Skip = 0,
                    Take = 5,
                    UserId = userId
                });
                entry.Value = res;
                return res;
            });
        }

        public void InvalidateNotificationCache(params string[] userIds)
        {
            foreach (var userId in userIds)
            {
                _memoryCache.Remove(GetNotificationsCacheId(userId));
                _eventAggregator.Publish(new UserNotificationsUpdatedEvent() { UserId = userId });
            }
        }

        private static string GetNotificationsCacheId(string userId)
        {
            return $"notifications-{userId}";
        }
        public const int MaxUnseen = 100;
        public async Task<(List<NotificationViewModel> Items, int? Count)> GetNotifications(NotificationsQuery query)
        {
            await using var dbContext = _factory.CreateContext();

            var queryables = GetNotificationsQueryable(dbContext, query);
            var items = (await queryables.withPaging.ToListAsync()).Select(ToViewModel).Where(model => model != null).ToList();

            int? count = null;
            if (query.Seen is false)
            {
                // Unseen notifications aren't likely to be too huge, so count should be fast
                count = await queryables.withoutPaging.CountAsync();
                if (count >= MaxUnseen)
                {
                    // If we have too much unseen notifications, we don't want to show the exact count
                    // because it would be too long to display, so we just show 99+
                    // Then cleanup a bit the database by removing the oldest notifications, as it would be expensive to fetch every time
                    if (count >= MaxUnseen + (MaxUnseen / 2))
                    {
                        nextBatch:
                        var seenToRemove = await queryables.withoutPaging.OrderByDescending(data => data.Created).Skip(MaxUnseen).Take(1000).ToListAsync();
                        if (seenToRemove.Count > 0)
                        {
                            foreach (var seen in seenToRemove)
                            {
                                seen.Seen = true;
                            }
                            await dbContext.SaveChangesAsync();
                            goto nextBatch;
                        }
                    }
                    count = MaxUnseen;
                }
            }
            return (Items: items, Count: count);
        }

        private (IQueryable<NotificationData> withoutPaging, IQueryable<NotificationData> withPaging)
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
            var notification = data.HasTypedBlob(handler.NotificationBlobType).GetBlob();
            var obj = new NotificationViewModel
            {
                Id = data.Id,
                Type = data.NotificationType,
                Created = data.Created,
                Seen = data.Seen
            };
            handler.FillViewModel(notification, obj);
            return obj;
        }

        public INotificationHandler GetHandler(string notificationId)
        {
            _handlersByNotificationType.TryGetValue(notificationId, out var h);
            return h;
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
