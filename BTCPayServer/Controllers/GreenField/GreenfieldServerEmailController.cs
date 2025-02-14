#nullable enable
using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Models.ServerViewModels;
using BTCPayServer.Services;
using BTCPayServer.Services.Mails;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using MimeKit;

namespace BTCPayServer.Controllers.GreenField
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [EnableCors(CorsPolicies.All)]
    public class GreenfieldServerEmailController : Controller
    {
        private readonly EmailSenderFactory _emailSenderFactory;
        private readonly PoliciesSettings _policiesSettings;
        readonly SettingsRepository _settingsRepository;

        public GreenfieldServerEmailController(EmailSenderFactory emailSenderFactory, PoliciesSettings policiesSettings, SettingsRepository settingsRepository)
        {
            _emailSenderFactory = emailSenderFactory;
            _policiesSettings = policiesSettings;
            _settingsRepository = settingsRepository;
        }

        [Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/server/email")]
        public async Task<IActionResult> ServerEmail()
        {
            var email = await _emailSenderFactory.GetSettings() ?? new EmailSettings();
            var model = new ServerEmailSettingsData
            {
                EnableStoresToUseServerEmailSettings = !_policiesSettings.DisableStoresToUseServerEmailSettings,
                Settings = email
            };
            return Ok(model);
        }

        [Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/server/email")]
        public async Task<IActionResult> ServerEmail(ServerEmailSettingsData model)
        {
            if (_policiesSettings.DisableStoresToUseServerEmailSettings == model.EnableStoresToUseServerEmailSettings)
            {
                _policiesSettings.DisableStoresToUseServerEmailSettings = !model.EnableStoresToUseServerEmailSettings;
                await _settingsRepository.UpdateSetting(_policiesSettings);
            }
            
            // save
            if (model.Settings.From is not null && !MailboxAddressValidator.IsMailboxAddress(model.Settings.From))
            {
                 model.Settings.AddModelError(e => e.From,
                     "Invalid email address", this);
                 return this.CreateValidationError(ModelState);
            }
            
            var oldSettings = await _emailSenderFactory.GetSettings() ?? new EmailSettings();
            if (new ServerEmailsViewModel(oldSettings).PasswordSet)
            {
                model.Settings.Password = oldSettings.Password;
            }
            await _settingsRepository.UpdateSetting(model.Settings);
            
            return Ok();
        }
    }
}
