using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Models.NotificationViewModels;
using BTCPayServer.Services.Notifications;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanViewNotificationsForUser)]
    [Route("notifications/{action:lowercase=Index}")]
    public class UINotificationsController(
        StoreRepository storeRepo,
        NotificationManager notificationManager) : Controller
    {
        [HttpGet]
        public async Task<IActionResult> Index(NotificationIndexViewModel model = null)
        {
            model ??= new NotificationIndexViewModel { Skip = 0 };
            var timezoneOffset = model.TimezoneOffset ?? 0;
            model.Status ??= "Unread";
            ViewBag.Status = model.Status;
            if (User.GetIdOrNull() is not string userId)
                return RedirectToAction("Index", "UIHome");

            var searchTerm = string.IsNullOrEmpty(model.SearchText) ? model.SearchTerm : $"{model.SearchText},{model.SearchTerm}";
            var fs = new SearchString(searchTerm, timezoneOffset);
            var storeIds = fs.GetFilterArray("storeid");
            var stores = await storeRepo.GetStoresByUserId(userId);
            model.StoreFilterOptions = stores
                .Where(store => !store.Archived)
                .OrderBy(s => s.StoreName)
                .Select(s => new StoreFilterOption
                {
                    Selected = storeIds?.Contains(s.Id) is true,
                    Text = s.StoreName,
                    Value = s.Id
                })
                .ToList();

            model.Search = fs;

            var res = await notificationManager.GetNotifications(new NotificationsQuery
            {
                Skip = model.Skip,
                Take = model.Count,
                UserId = userId,
                SearchText = model.SearchText,
                Type = fs.GetFilterArray("type"),
                StoreIds = storeIds,
                Seen = model.Status == "Unread" ? false : null
            });
            model.Items = res.Items;

            return View(model);
        }

        [HttpPost]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanManageNotificationsForUser)]
        public async Task<IActionResult> FlipRead(string id)
        {
            if (User.GetIdOrNull() is string userId)
            {
                await notificationManager.ToggleSeen(new NotificationsQuery { Ids = [id], UserId = userId }, null);
                return RedirectToAction(nameof(Index));
            }

            return BadRequest();
        }

        [HttpGet]
        public async Task<IActionResult> NotificationPassThrough(string id)
        {
            if (User.GetIdOrNull() is string userId)
            {
                var items = await
                    notificationManager.ToggleSeen(new NotificationsQuery
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
            if (User.GetIdOrNull() is not string userId)
                return NotFound();

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
                        await notificationManager.Remove(new NotificationsQuery()
                        {
                            UserId = userId,
                            Ids = selectedItems
                        });

                        break;
                    case "mark-seen":
                        await notificationManager.ToggleSeen(new NotificationsQuery()
                        {
                            UserId = userId,
                            Ids = selectedItems,
                            Seen = false
                        }, true);

                        break;
                    case "mark-unseen":
                        await notificationManager.ToggleSeen(new NotificationsQuery()
                        {
                            UserId = userId,
                            Ids = selectedItems,
                            Seen = true
                        }, false);
                        break;
                }
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanManageNotificationsForUser)]
        public async Task<IActionResult> MarkAllAsSeen(string returnUrl)
        {
            if (User.GetIdOrNull() is not string userId)
                return NotFound();
            await notificationManager.ToggleSeen(new NotificationsQuery { Seen = false, UserId = userId }, true);
            return LocalRedirect(returnUrl);
        }
    }
}
