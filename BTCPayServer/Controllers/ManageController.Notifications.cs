using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Models;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    public partial class ManageController
    {
        [HttpGet("notifications")]
        public async Task<IActionResult> NotificationSettings()
        {
            var user = await _userManager.GetUserAsync(User);
            return View(new NotificationSettingsViewModel()
            {
                DisabledNotifications =
                    user.DisabledNotifications?.Split(';', StringSplitOptions.RemoveEmptyEntries)?.ToList() ??
                    new List<string>()
            });
        }

        [HttpPost("notifications")]
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
                user.DisabledNotifications = vm.DisabledNotifications?.Any() is true
                    ? string.Join(';', vm.DisabledNotifications) + ";"
                    : string.Empty;
            }

            await _userManager.UpdateAsync(user);
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Message = "Updated successfully.", Severity = StatusMessageModel.StatusSeverity.Success
            });
            return RedirectToAction("NotificationSettings");
        }

        public class NotificationSettingsViewModel
        {
            public List<string> DisabledNotifications { get; set; }
        }
    }
}
