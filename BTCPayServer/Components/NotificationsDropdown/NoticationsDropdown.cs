using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Services.Notifications;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Components.NotificationsDropdown
{
    public class NotificationsDropdown : ViewComponent
    {
        private readonly NotificationManager _notificationManager;

        public NotificationsDropdown(NotificationManager notificationManager)
        {
            _notificationManager = notificationManager;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            return View(await _notificationManager.GetSummaryNotifications(UserClaimsPrincipal));
        }
    }
}
