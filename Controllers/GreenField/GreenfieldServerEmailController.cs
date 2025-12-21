#nullable enable
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Services;
using BTCPayServer.Plugins.Emails.Services;
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

        private ServerEmailSettingsData ToApiModel(EmailSettings email)
        {
            var data = email.ToData<ServerEmailSettingsData>();
            data.EnableStoresToUseServerEmailSettings = !_policiesSettings.DisableStoresToUseServerEmailSettings;
            return data;
        }

        [Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/server/email")]
        public async Task<IActionResult> ServerEmailSettings()
        {
            var email = await _emailSenderFactory.GetSettings() ?? new EmailSettings();
            return Ok(ToApiModel(email));
        }

        [Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPut("~/api/v1/server/email")]
        public async Task<IActionResult> ServerEmailSettings(ServerEmailSettingsData request)
        {
            if (!string.IsNullOrWhiteSpace(request.From) && !MailboxAddressValidator.IsMailboxAddress(request.From))
                ModelState.AddModelError(nameof(request.From), "Invalid email address");

            if (!ModelState.IsValid)
                return this.CreateValidationError(ModelState);

            if (_policiesSettings.DisableStoresToUseServerEmailSettings == request.EnableStoresToUseServerEmailSettings)
            {
                _policiesSettings.DisableStoresToUseServerEmailSettings = !request.EnableStoresToUseServerEmailSettings;
                await _settingsRepository.UpdateSetting(_policiesSettings);
            }

            var settings = await _emailSenderFactory.GetSettings();
            settings = EmailSettings.FromData(request, settings?.Password);

            // important to save as EmailSettings otherwise it won't be able to be fetched
            await _settingsRepository.UpdateSetting(settings);
            return Ok(ToApiModel(settings));
        }
    }
}
