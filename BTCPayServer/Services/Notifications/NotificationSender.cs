using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Services.Notifications.Blobs;
using Microsoft.AspNetCore.Identity;

namespace BTCPayServer.Services.Notifications
{
    public class NotificationSender
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly EventAggregator _eventAggregator;
        private readonly ApplicationDbContextFactory _ContextFactory;

        public NotificationSender(UserManager<ApplicationUser> userManager, EventAggregator eventAggregator)
        {
            _userManager = userManager;
            _eventAggregator = eventAggregator;
        }

        internal async Task NoticeNewVersionAsync(string version)
        {
            var admins = await _userManager.GetUsersInRoleAsync(Roles.ServerAdmin);
            var adminUids = admins.Select(a => a.Id).ToArray();

            var notif = new NewVersionNotification
            {
                Version = version
            };
            using (var db = _ContextFactory.CreateContext())
            {
                foreach (var uid in adminUids)
                {
                    var data = notif.ToData(uid);
                    db.Notifications.Add(data);
                }

                await db.SaveChangesAsync();
            }

            // propagate notification
            _eventAggregator.Publish(new NotificationEvent
            {
                ApplicationUserIds = adminUids,
                Notification = notif
            });
        }
    }
}
