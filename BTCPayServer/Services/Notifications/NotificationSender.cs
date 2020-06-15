using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Services.Notifications.Blobs;
using Microsoft.AspNetCore.Identity;
using Newtonsoft.Json;

namespace BTCPayServer.Services.Notifications
{
    public class NotificationSender
    {
        private readonly ApplicationDbContextFactory _contextFactory;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly EventAggregator _eventAggregator;

        public NotificationSender(ApplicationDbContextFactory contextFactory, UserManager<ApplicationUser> userManager, EventAggregator eventAggregator)
        {
            _contextFactory = contextFactory;
            _userManager = userManager;
            _eventAggregator = eventAggregator;
        }

        public async Task SendNotification(NotificationScope scope, BaseNotification notification)
        {
            var users = await GetUsers(scope);
            using (var db = _contextFactory.CreateContext())
            {
                foreach (var uid in users)
                {
                    var obj = JsonConvert.SerializeObject(this);
                    var data = new NotificationData
                    {
                        Id = Guid.NewGuid().ToString(),
                        Created = DateTimeOffset.UtcNow,
                        ApplicationUserId = uid,
                        NotificationType = notification.NotificationType,
                        Blob = ZipUtils.Zip(obj),
                        Seen = false
                    };
                    db.Notifications.Add(data);
                }

                await db.SaveChangesAsync();
            }
        }

        private async Task<string[]> GetUsers(NotificationScope scope)
        {
            if (scope is AdminScope)
            {
                var admins = await _userManager.GetUsersInRoleAsync(Roles.ServerAdmin);
                return admins.Select(a => a.Id).ToArray();
            }
            throw new NotSupportedException("Notification scope not supported");
        }
    }
}
