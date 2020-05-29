using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Events.Notifications;
using BTCPayServer.Filters;
using BTCPayServer.HostedServices;
using BTCPayServer.Models.NotificationViewModels;
using BTCPayServer.Security;
using Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    [BitpayAPIConstraint(false)]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public class NotificationsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly EventAggregator _eventAggregator;

        public NotificationsController(ApplicationDbContext db, EventAggregator eventAggregator)
        {
            _db = db;
            _eventAggregator = eventAggregator;
        }

        [HttpGet]
        public async Task<IActionResult> Index(int skip = 0, int count = 50, int timezoneOffset = 0)
        {
            // TODO: Refactor
            var claimWithId = User.Claims.SingleOrDefault(a => a.Type == ClaimTypes.NameIdentifier);
            if (claimWithId == null)
                return RedirectToAction("Index", "Home");

            var userId = claimWithId.Value;
            var model = new IndexViewModel()
            {
                Items = _db.Notifications
                    .OrderByDescending(a => a.Created)
                    .Skip(skip).Take(count)
                    .Where(a => a.ApplicationUserId == userId)
                    .Select(a => a.ViewModel())
                    .ToList()
            };

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

        [HttpGet]
        public async Task<IActionResult> Delete(string id)
        {
            // TODO: Refactor
            var claimWithId = User.Claims.SingleOrDefault(a => a.Type == ClaimTypes.NameIdentifier);
            if (claimWithId == null)
                return RedirectToAction("Index", "Home");

            var notif = _db.Notifications.SingleOrDefault(a => a.Id == id && a.ApplicationUserId == claimWithId.Value);
            _db.Notifications.Remove(notif);
            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> FlipRead(string id)
        {
            // TODO: Refactor
            var claimWithId = User.Claims.SingleOrDefault(a => a.Type == ClaimTypes.NameIdentifier);
            if (claimWithId == null)
                return RedirectToAction("Index", "Home");

            var notif = _db.Notifications.SingleOrDefault(a => a.Id == id && a.ApplicationUserId == claimWithId.Value);
            notif.Seen = !notif.Seen;
            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }
    }
}
