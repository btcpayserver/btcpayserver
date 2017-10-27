using BTCPayServer.Models;
using BTCPayServer.Models.ServerViewModels;
using BTCPayServer.Services;
using BTCPayServer.Services.Mails;
using BTCPayServer.Validations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;

namespace BTCPayServer.Controllers
{
    [Authorize(Roles = Roles.ServerAdmin)]
    public class ServerController : Controller
    {
        private UserManager<ApplicationUser> _UserManager;
        SettingsRepository _SettingsRepository;

        public ServerController(UserManager<ApplicationUser> userManager, SettingsRepository settingsRepository)
        {
            _UserManager = userManager;
            _SettingsRepository = settingsRepository;
        }

        [Route("server/users")]
        public IActionResult ListUsers()
        {
            var users = new UsersViewModel();
            users.Users
                = _UserManager.Users.Select(u => new UsersViewModel.UserViewModel()
                {
                    Name = u.UserName,
                    Email = u.Email
                }).ToList();
            return View(users);
        }

        [Route("server/emails")]
        public async Task<IActionResult> Emails()
        {
            var data = (await _SettingsRepository.GetSettingAsync<EmailSettings>()) ?? new EmailSettings();
            return View(new EmailsViewModel() { Settings = data });
        }

        [Route("server/policies")]
        public async Task<IActionResult> Policies()
        {
            var data = (await _SettingsRepository.GetSettingAsync<PoliciesSettings>()) ?? new PoliciesSettings();
            return View(data);
        }
        [Route("server/policies")]
        [HttpPost]
        public async Task<IActionResult> Policies(PoliciesSettings settings)
        {
            await _SettingsRepository.UpdateSetting(settings);
            TempData["StatusMessage"] = "Policies upadated successfully";
            return View(settings);
        }

        [Route("server/emails")]
        [HttpPost]
        public async Task<IActionResult> Emails(EmailsViewModel model, string command)
        {
            if (command == "Test")
            {
                if (!ModelState.IsValid)
                    return View(model);
                try
                {
                    var client = model.Settings.CreateSmtpClient();
                    await client.SendMailAsync(model.Settings.From, model.TestEmail, "BTCPay test", "BTCPay test");
                    model.StatusMessage = "Email sent to " + model.TestEmail + ", please, verify you received it";
                }
                catch (Exception ex)
                {
                    model.StatusMessage = "Error: " + ex.Message;
                }
                return View(model);
            }
            else
            {
                ModelState.Remove(nameof(model.TestEmail));
                if (!ModelState.IsValid)
                    return View(model);
                await _SettingsRepository.UpdateSetting(model.Settings);
                model.StatusMessage = "Email settings saved";
                return View(model);
            }
        }
    }
}
