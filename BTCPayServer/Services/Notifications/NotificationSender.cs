using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Services.Notifications
{
    public class UserNotificationsUpdatedEvent
    {
        public string UserId { get; set; }
        public override string ToString()
        {
            return string.Empty;
        }
    }
    public class NotificationSender
    {
        private readonly ApplicationDbContextFactory _contextFactory;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly NotificationManager _notificationManager;

        public NotificationSender(ApplicationDbContextFactory contextFactory, UserManager<ApplicationUser> userManager, NotificationManager notificationManager)
        {
            _contextFactory = contextFactory;
            _userManager = userManager;
            _notificationManager = notificationManager;
        }

        public async Task SendNotification(INotificationScope scope, BaseNotification notification)
        {
            ArgumentNullException.ThrowIfNull(scope);
            ArgumentNullException.ThrowIfNull(notification);
            var users = await GetUsers(scope, notification.Identifier);
            await using (var db = _contextFactory.CreateContext())
            {
                foreach (var uid in users)
                {
                    var data = new NotificationData
                    {
                        Id = Guid.NewGuid().ToString(),
                        Created = DateTimeOffset.UtcNow,
                        ApplicationUserId = uid,
                        NotificationType = notification.NotificationType,
                        Seen = false
                    };
                    data.HasTypedBlob<BaseNotification>().SetBlob(notification);
                    await db.Notifications.AddAsync(data);
                }
                await db.SaveChangesAsync();
            }
            foreach (string user in users)
            {
                _notificationManager.InvalidateNotificationCache(user);
            }
        }

        public BaseNotification GetBaseNotification(NotificationData notificationData)
        {
            return notificationData.HasTypedBlob<BaseNotification>().GetBlob();
        }

        private async Task<string[]> GetUsers(INotificationScope scope, string notificationIdentifier)
        {
            await using var ctx = _contextFactory.CreateContext();

            var split = notificationIdentifier.Split('_', StringSplitOptions.None);
            var terms = new List<string>();
            foreach (var t in split)
            {
                terms.Add(terms.Any() ? $"{terms.Last().TrimEnd(';')}_{t};" : $"{t};");
            }
            IQueryable<ApplicationUser> query;
            switch (scope)
            {
                case AdminScope _:
                    {
                        query = _userManager.GetUsersInRoleAsync(Roles.ServerAdmin).Result.AsQueryable();

                        break;
                    }
                case StoreScope s:
                    var roles = s.Roles?.Select(role => role.Id);
                    query = ctx.UserStore
                        .Include(store => store.ApplicationUser)
                        .Where(u => u.StoreDataId == s.StoreId && (roles == null || roles.Contains(u.StoreRoleId)))
                        .Select(u => u.ApplicationUser);
                    break;
                case UserScope userScope:
                    query = ctx.Users
                        .Where(user => user.Id == userScope.UserId);
                    break;
                default:
                    throw new NotSupportedException("Notification scope not supported");


            }
            query = query.Where(store => store.DisabledNotifications != "all");
            foreach (string term in terms)
            {
                // Cannot specify StringComparison as EF core does not support it and would attempt client-side evaluation 
                // ReSharper disable once CA1307
#pragma warning disable CA1307 // Specify StringComparison
                query = query.Where(user => user.DisabledNotifications == null || !user.DisabledNotifications.Contains(term));
#pragma warning restore CA1307 // Specify StringComparison
            }

            return query.Select(user => user.Id).ToArray();
        }
    }
}
