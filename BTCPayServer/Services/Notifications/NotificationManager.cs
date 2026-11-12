using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

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
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
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
            var items = new List<NotificationViewModel>();
            var searchBody = !string.IsNullOrEmpty(query.SearchText);
            var skip = searchBody ? query.Skip.GetValueOrDefault() : 0;
            var take = query.Take ?? int.MaxValue;
            // Only body search needs the localized, handler-generated view model.
            // Otherwise filtering and paging both happen in the database.
            var itemQuery = searchBody ? queryables.withoutPaging : queryables.withPaging;
            if (take > 0)
            {
                await foreach (var data in itemQuery.AsNoTracking().AsAsyncEnumerable())
                {
                    var model = ToViewModel(data);
                    if (model is null || (searchBody && !model.Body.Contains(query.SearchText)))
                        continue;
                    if (skip > 0)
                    {
                        skip--;
                        continue;
                    }
                    items.Add(model);
                    if (items.Count >= take)
                        break;
                }
            }
            int? count = null;
            if (query.Seen is false)
            {
                count = await queryables.withoutPaging.Take(MaxUnseen).CountAsync();
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

            if (query.Type?.Length > 0)
            {
                var types = query.Type.Contains("userupdate")
                    ? new[] { "inviteaccepted", "userapproval" }
                    : query.Type.Select(type => type.ToLowerInvariant()).ToArray();
                queryable = queryable.Where(data => types.Contains(data.NotificationType.ToLower()));
            }

            if (query.StoreIds?.Length > 0)
            {
                var storeIds = query.StoreIds.Select(id => id.ToLowerInvariant()).ToArray();
                queryable = queryable.Where(data => storeIds.Contains(NotificationData.GetBlobProperty(data.Blob2, "storeId").ToLower()));
            }

            queryable = queryable.OrderByDescending(a => a.Created).ThenByDescending(a => a.Id);

            var queryable2 = queryable;
            if (query.Skip.HasValue)
            {
                queryable2 = queryable.Skip(query.Skip.Value);
            }

            if (query.Take.HasValue)
            {
                queryable2 = queryable2.Take(query.Take.Value);
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
        public string SearchText { get; set; }
        public string[] Type { get; set; }
        public string[] StoreIds { get; set; }
    }
}
