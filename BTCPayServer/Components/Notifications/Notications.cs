using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Services.Notifications;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Components.Notifications
{
    public class Notifications : ViewComponent
    {
        private readonly NotificationManager _notificationManager;

        private static readonly string[] _views = { "List", "Dropdown", "Recent" };

        public Notifications(NotificationManager notificationManager)
        {
            _notificationManager = notificationManager;
        }

        public async Task<IViewComponentResult> InvokeAsync(string appearance, string returnUrl)
        {
            var vm = await _notificationManager.GetSummaryNotifications(UserClaimsPrincipal);
            vm.ReturnUrl = returnUrl;
            var viewName = _views.Contains(appearance) ? appearance : _views[0];
            return View(viewName, vm);
        }
    }
}
