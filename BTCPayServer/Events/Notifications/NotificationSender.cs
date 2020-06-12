using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using Microsoft.AspNetCore.Identity;

namespace BTCPayServer.Events.Notifications
{
    public class NotificationSender
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly EventAggregator _eventAggregator;

        public NotificationSender(UserManager<ApplicationUser> userManager, EventAggregator eventAggregator)
        {
            _userManager = userManager;
            _eventAggregator = eventAggregator;
        }

        internal async Task NoticeNewVersionAsync(string version)
        {
            var admins = await _userManager.GetUsersInRoleAsync(Roles.ServerAdmin);
            var adminUids = admins.Select(a => a.Id).ToArray();
            var evt = new NotificationEvent
            {
                ApplicationUserIds = adminUids,
                Notification = new NewVersionNotification
                {
                    Version = version
                }
            };

            _eventAggregator.Publish(evt);
        }
    }
}
