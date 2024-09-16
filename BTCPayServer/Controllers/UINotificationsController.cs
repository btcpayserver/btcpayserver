using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Models.NotificationViewModels;
using BTCPayServer.Services.Notifications;
using BTCPayServer.Services.Stores;
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
        private readonly StoreRepository _storeRepo;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly NotificationManager _notificationManager;

        public UINotificationsController(
            StoreRepository storeRepo,
            UserManager<ApplicationUser> userManager,
            NotificationManager notificationManager)
        {
            _storeRepo = storeRepo;
            _userManager = userManager;
            _notificationManager = notificationManager;
        }

        [HttpGet]
        public async Task<IActionResult> Index(NotificationIndexViewModel model = null)
        {
            model ??= new NotificationIndexViewModel { Skip = 0 };
            var timezoneOffset = model.TimezoneOffset ?? 0;
            model.Status ??= "Unread";
            ViewBag.Status = model.Status;
            if (!ValidUserClaim(out var userId))
                return RedirectToAction("Index", "UIHome");

            var stores = await _storeRepo.GetStoresByUserId(userId);
            model.Stores = stores.Where(store => !store.Archived).OrderBy(s => s.StoreName).ToList();

            var searchTerm = string.IsNullOrEmpty(model.SearchText) ? model.SearchTerm : $"{model.SearchText},{model.SearchTerm}";
            var fs = new SearchString(searchTerm, timezoneOffset);
            model.Search = fs;

            var res = await _notificationManager.GetNotifications(new NotificationsQuery
            {
                Skip = model.Skip,
                Take = model.Count,
                UserId = userId,
                SearchText = model.SearchText,
                Type = fs.GetFilterArray("type"),
                StoreIds = fs.GetFilterArray("storeid"),
                Seen = model.Status == "Unread" ? false : null
            });
            model.Items = res.Items;

            return View(model);
        }

        [HttpPost]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanManageNotificationsForUser)]
        public async Task<IActionResult> FlipRead(string id)
        {
            if (ValidUserClaim(out var userId))
            {
                await _notificationManager.ToggleSeen(new NotificationsQuery { Ids = [id], UserId = userId }, null);
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
                    _notificationManager.ToggleSeen(new NotificationsQuery
                    {
                        Ids = [id],
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
            await _notificationManager.ToggleSeen(new NotificationsQuery { Seen = false, UserId = userId }, true);
            return LocalRedirect(returnUrl);
        }

        private bool ValidUserClaim(out string userId)
        {
            userId = _userManager.GetUserId(User);
            return userId != null;
        }
    }
}
