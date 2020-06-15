using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.HostedServices;
using BTCPayServer.Models.NotificationViewModels;
using BTCPayServer.Security;
using BTCPayServer.Services;
using BTCPayServer.Services.Notifications;
using BTCPayServer.Services.Notifications.Blobs;
using Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    [BitpayAPIConstraint(false)]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public class NotificationsController : Controller
    {
        private readonly BTCPayServerEnvironment _env;
        private readonly ApplicationDbContext _db;
        private readonly NotificationSender _notificationSender;
        private readonly UserManager<ApplicationUser> _userManager;

        public NotificationsController(BTCPayServerEnvironment env, ApplicationDbContext db, NotificationSender notificationSender, UserManager<ApplicationUser> userManager)
        {
            _env = env;
            _db = db;
            _notificationSender = notificationSender;
            _userManager = userManager;
        }

        [HttpGet]
        public async Task<IActionResult> Index(int skip = 0, int count = 50, int timezoneOffset = 0)
        {
            if (!ValidUserClaim(out var userId))
                return RedirectToAction("Index", "Home");

            var model = new IndexViewModel()
            {
                Skip = skip,
                Count = count,
                Items = _db.Notifications
                    .OrderByDescending(a => a.Created)
                    .Skip(skip).Take(count)
                    .Where(a => a.ApplicationUserId == userId)
                    .Select(a => a.ToViewModel())
                    .ToList(),
                Total = _db.Notifications.Where(a => a.ApplicationUserId == userId).Count()
            };

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Generate(string version)
        {
            if (_env.NetworkType != NBitcoin.NetworkType.Regtest)
                return NotFound();
            await _notificationSender.SendNotification(new AdminScope(), new NewVersionNotification(version));
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> FlipRead(string id)
        {
            if (ValidUserClaim(out var userId))
            {
                var notif = _db.Notifications.Single(a => a.Id == id && a.ApplicationUserId == userId);
                notif.Seen = !notif.Seen;
                await _db.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> MassAction(string command, string[] selectedItems)
        {
            if (selectedItems != null)
            {
                if (command == "delete" && ValidUserClaim(out var userId))
                {
                    var toRemove = _db.Notifications.Where(a => a.ApplicationUserId == userId && selectedItems.Contains(a.Id));
                    _db.Notifications.RemoveRange(toRemove);
                    await _db.SaveChangesAsync();

                    return RedirectToAction(nameof(Index));
                }
            }

            return RedirectToAction(nameof(Index));
        }

        private bool ValidUserClaim(out string userId)
        {
            userId = _userManager.GetUserId(User);
            return userId != null;
        }
    }
}
