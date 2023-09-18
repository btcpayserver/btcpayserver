using System;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Models.NotificationViewModels;
using BTCPayServer.Security;
using BTCPayServer.Services;
using BTCPayServer.Services.Notifications;
using BTCPayServer.Services.Notifications.Blobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    [BitpayAPIConstraint(false)]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanViewNotificationsForUser)]
    [Route("notifications/{action:lowercase=Index}")]
    public class UINotificationsController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly NotificationManager _notificationManager;

        public UINotificationsController(
            UserManager<ApplicationUser> userManager,
            NotificationManager notificationManager)
        {
            _userManager = userManager;
            _notificationManager = notificationManager;
        }

        [HttpGet]
        public async Task<IActionResult> Index(int skip = 0, int count = 50, int timezoneOffset = 0)
        {
            if (!ValidUserClaim(out var userId))
                return RedirectToAction("Index", "UIHome");

            var res = await _notificationManager.GetNotifications(new NotificationsQuery()
            {
                Skip = skip,
                Take = count,
                UserId = userId
            });

            var model = new IndexViewModel() { Skip = skip, Count = count, Items = res.Items, Total = res.Count };

            return View(model);
        }

        [HttpPost]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanManageNotificationsForUser)]
        public async Task<IActionResult> FlipRead(string id)
        {
            if (ValidUserClaim(out var userId))
            {
                await _notificationManager.ToggleSeen(new NotificationsQuery() { Ids = new[] { id }, UserId = userId }, null);
                return RedirectToAction(nameof(Index));
            }

            return BadRequest();
        }

        [HttpGet]
        public async Task<IActionResult> NotificationPassThrough(string id)
        {
            if (ValidUserClaim(out var userId))
            {
                var items = await
                    _notificationManager.ToggleSeen(new NotificationsQuery()
                    {
                        Ids = new[] { id },
                        UserId = userId
                    }, true);

                var link = items.FirstOrDefault()?.ActionLink ?? "";
                if (string.IsNullOrEmpty(link))
                {
                    return RedirectToAction(nameof(Index));
                }

                return Redirect(link);
            }

            return NotFound();
        }

        [HttpPost]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanManageNotificationsForUser)]
        public async Task<IActionResult> MassAction(string command, string[] selectedItems)
        {
            if (!ValidUserClaim(out var userId))
            {
                return NotFound();
            }

            if (command.StartsWith("flip-individual", StringComparison.InvariantCulture))
            {
                var id = command.Split(":")[1];
                return await FlipRead(id);
            }

            if (selectedItems != null)
            {
                switch (command)
                {
                    case "delete":
                        await _notificationManager.Remove(new NotificationsQuery()
                        {
                            UserId = userId,
                            Ids = selectedItems
                        });

                        break;
                    case "mark-seen":
                        await _notificationManager.ToggleSeen(new NotificationsQuery()
                        {
                            UserId = userId,
                            Ids = selectedItems,
                            Seen = false
                        }, true);

                        break;
                    case "mark-unseen":
                        await _notificationManager.ToggleSeen(new NotificationsQuery()
                        {
                            UserId = userId,
                            Ids = selectedItems,
                            Seen = true
                        }, false);
                        break;
                }
                return RedirectToAction(nameof(Index));
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanManageNotificationsForUser)]
        public async Task<IActionResult> MarkAllAsSeen(string returnUrl)
        {
            if (!ValidUserClaim(out var userId))
            {
                return NotFound();
            }
            await _notificationManager.ToggleSeen(new NotificationsQuery() { Seen = false, UserId = userId }, true);
            return LocalRedirect(returnUrl);
        }

        private bool ValidUserClaim(out string userId)
        {
            userId = _userManager.GetUserId(User);
            return userId != null;
        }
    }
}
