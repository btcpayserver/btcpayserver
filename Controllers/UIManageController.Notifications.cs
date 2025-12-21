using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Controllers
{
    public partial class UIManageController
    {
        [HttpGet("/notifications/settings")]
        public async Task<IActionResult> NotificationSettings([FromServices] IEnumerable<INotificationHandler> notificationHandlers)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user.DisabledNotifications == "all")
            {
                return View(new NotificationSettingsViewModel { All = true });
            }
            var disabledNotifications =
                user.DisabledNotifications?.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList() ??
                new List<string>();
            var notifications = notificationHandlers.SelectMany(handler => handler.Meta.Select(tuple =>
                    new SelectListItem(tuple.name, tuple.identifier,
                        !disabledNotifications.Contains(tuple.identifier, StringComparer.InvariantCultureIgnoreCase))))
                .ToList();

            return View(new NotificationSettingsViewModel { DisabledNotifications = notifications });
        }

        [HttpPost("/notifications/settings")]
        public async Task<IActionResult> NotificationSettings(NotificationSettingsViewModel vm, string command)
        {
            var user = await _userManager.GetUserAsync(User);
            if (command == "disable-all")
            {
                user.DisabledNotifications = "all";
            }
            else if (command == "enable-all")
            {
                user.DisabledNotifications = "";
            }
            else if (command == "update")
            {
                var disabled = vm.DisabledNotifications.Where(item => !item.Selected).Select(item => item.Value)
                    .ToArray();
                user.DisabledNotifications = disabled.Any()
                    ? string.Join(';', disabled) + ";"
                    : string.Empty;
            }

            await _userManager.UpdateAsync(user);
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = StringLocalizer["Updated successfully."].Value,
                Severity = StatusMessageModel.StatusSeverity.Success
            });
            return RedirectToAction("NotificationSettings");
        }

        public class NotificationSettingsViewModel
        {
            public bool All { get; set; }
            public List<SelectListItem> DisabledNotifications { get; set; }
        }
    }
}
