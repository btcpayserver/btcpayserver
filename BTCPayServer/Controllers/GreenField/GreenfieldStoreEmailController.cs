#nullable enable
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Plugins.Emails.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers.GreenField
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [EnableCors(CorsPolicies.All)]
    public class GreenfieldStoreEmailController : ControllerBase
    {
        private readonly EmailSenderFactory _emailSenderFactory;
        private readonly StoreRepository _storeRepository;

        public GreenfieldStoreEmailController(EmailSenderFactory emailSenderFactory, StoreRepository storeRepository)
        {
            _emailSenderFactory = emailSenderFactory;
            _storeRepository = storeRepository;
        }

        [Authorize(Policy = Policies.CanSendStoreEmail, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/stores/{storeId}/email/send")]
        public async Task<IActionResult> SendEmailFromStore(string storeId,
            [FromBody] SendEmailRequest request)
        {
            var store = HttpContext.GetStoreData();
            if (!MailboxAddressValidator.TryParse(request.Email, out var to))
            {
                ModelState.AddModelError(nameof(request.Email), "Invalid email");
                return this.CreateValidationError(ModelState);
            }
            var emailSender = await _emailSenderFactory.GetEmailSender(storeId);
            emailSender.SendEmail(to, request.Subject, request.Body);
            return Ok();
        }


        private EmailSettingsData ToApiModel(Data.StoreData data)
        {
            var storeEmailSettings = data.GetStoreBlob().EmailSettings ?? new();
            return storeEmailSettings.ToData<EmailSettingsData>();
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/email")]
        public IActionResult GetStoreEmailSettings()
        => Ok(ToApiModel(HttpContext.GetStoreData()));

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPut("~/api/v1/stores/{storeId}/email")]
        public async Task<IActionResult> UpdateStoreEmailSettings(string storeId, EmailSettingsData request)
        {
            if (!string.IsNullOrWhiteSpace(request.From) && !MailboxAddressValidator.IsMailboxAddress(request.From))
                ModelState.AddModelError(nameof(request.From), "Invalid email address");

            if (!ModelState.IsValid)
                return this.CreateValidationError(ModelState);

            var store = HttpContext.GetStoreData();
            var blob = store.GetStoreBlob();
            var settings = EmailSettings.FromData(request, blob.EmailSettings?.Password);
            blob.EmailSettings = settings;
            if (store.SetStoreBlob(blob))
                await _storeRepository.UpdateStore(store);

            return Ok(ToApiModel(store));
        }
    }
}
