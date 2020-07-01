using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using Microsoft.AspNetCore.Identity;
using Newtonsoft.Json;

namespace BTCPayServer.Services.Notifications
{
    public class UserNotificationsUpdatedEvent
    {
        public string UserId { get; set; }
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

        public async Task SendNotification(NotificationScope scope, object notification)
        {
            if (scope == null)
                throw new ArgumentNullException(nameof(scope));
            if (notification == null)
                throw new ArgumentNullException(nameof(notification));
            var users = await GetUsers(scope);
            using (var db = _contextFactory.CreateContext())
            {
                foreach (var uid in users)
                {
                    var obj = JsonConvert.SerializeObject(notification);
                    var data = new NotificationData
                    {
                        Id = Guid.NewGuid().ToString(),
                        Created = DateTimeOffset.UtcNow,
                        ApplicationUserId = uid,
                        NotificationType = _notificationManager.GetHandler(notification.GetType()).NotificationType,
                        Blob = ZipUtils.Zip(obj),
                        Seen = false
                    };
                    db.Notifications.Add(data);
                }
                await db.SaveChangesAsync();
            }
            foreach (string user in users)
            {
                _notificationManager.InvalidateNotificationCache(user);
            }
        }

        private async Task<string[]> GetUsers(NotificationScope scope)
        {
            if (scope is AdminScope)
            {
                var admins = await _userManager.GetUsersInRoleAsync(Roles.ServerAdmin);
                return admins.Select(a => a.Id).ToArray();
            }
            if (scope is StoreScope s)
            {
                using var ctx = _contextFactory.CreateContext();
                return ctx.UserStore
                            .Where(u => u.StoreDataId == s.StoreId)
                            .Select(u => u.ApplicationUserId)
                            .ToArray();
            }
            throw new NotSupportedException("Notification scope not supported");
        }
    }
}
