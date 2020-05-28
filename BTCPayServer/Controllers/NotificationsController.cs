using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Events.Notifications;
using BTCPayServer.Filters;
using BTCPayServer.HostedServices;
using BTCPayServer.Models.NotificationViewModels;
using BTCPayServer.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    [BitpayAPIConstraint(false)]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public class NotificationsController : Controller
    {
        private readonly EventAggregator _eventAggregator;

        public NotificationsController(EventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
        }

        [HttpGet]
        public async Task<IActionResult> Index(int skip = 0, int count = 50, int timezoneOffset = 0)
        {
            var claimWithId = User.Claims.SingleOrDefault(a => a.Type == ClaimTypes.NameIdentifier);
            if (claimWithId == null)
                return RedirectToAction("Index", "Home");

            var userId = claimWithId.Value;
            var model = new IndexViewModel()
            {
                Items = NotificationDbSaver.Notif
                    .Skip(skip).Take(count)
                    .Where(a => a.ApplicationUserId == userId)
                    .Select(a => a.ViewModel())
                    .ToList()
            };
            model.Items = model.Items.OrderByDescending(a => a.Created).ToList();
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Generate()
        {
            _eventAggregator.NoticeNewVersion("1.0.4.4");
            // waiting for event handler to catch up
            await Task.Delay(1000);
            return RedirectToAction(nameof(Index));
        }
    }
}
