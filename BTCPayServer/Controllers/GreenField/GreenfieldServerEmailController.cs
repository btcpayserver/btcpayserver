#nullable enable
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Models.ServerViewModels;
using BTCPayServer.Services;
using BTCPayServer.Services.Mails;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

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
        public async Task<IActionResult> ServerEmailSettings()
        {
            var email = await _emailSenderFactory.GetSettings() ?? new EmailSettings();
            var model = new ServerEmailSettingsData
            {
                EnableStoresToUseServerEmailSettings = !_policiesSettings.DisableStoresToUseServerEmailSettings,
                From = email.From,
                Server = email.Server,
                Port = email.Port,
                Login = email.Login,
                DisableCertificateCheck = email.DisableCertificateCheck,
                // Password is not returned
                Password = null
            };
            return Ok(model);
        }

        [Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPut("~/api/v1/server/email")]
        public async Task<IActionResult> ServerEmailSettings(ServerEmailSettingsData request)
        {
            if (_policiesSettings.DisableStoresToUseServerEmailSettings == request.EnableStoresToUseServerEmailSettings)
            {
                _policiesSettings.DisableStoresToUseServerEmailSettings = !request.EnableStoresToUseServerEmailSettings;
                await _settingsRepository.UpdateSetting(_policiesSettings);
            }
            
            // save
            if (request.From is not null && !MailboxAddressValidator.IsMailboxAddress(request.From))
            {
                 request.AddModelError(e => e.From,
                     "Invalid email address", this);
                 return this.CreateValidationError(ModelState);
            }
            
            var oldSettings = await _emailSenderFactory.GetSettings() ?? new EmailSettings();
            // retaining the password if it exists and was not provided in request
            if (string.IsNullOrEmpty(request.Password) &&
                !string.IsNullOrEmpty(oldSettings?.Password))
                request.Password = oldSettings.Password;
            
            // important to save as EmailSettings otherwise it won't be able to be fetched
            await _settingsRepository.UpdateSetting(new EmailSettings
            {
                Server = request.Server,
                Port = request.Port,
                Login = request.Login,
                Password = request.Password,
                From = request.From,
                DisableCertificateCheck = request.DisableCertificateCheck
            });
            
            return Ok(true);
        }
    }
}
